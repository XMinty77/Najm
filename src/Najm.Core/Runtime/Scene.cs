using System.Numerics;
using System.Runtime.ExceptionServices;
using Najm.Utils;

namespace Najm.Core;

/// <summary>Provides the engine-controlled lifecycle and layer roots for a portable scene.</summary>
/// <remarks>
/// The engine loads each instance once, invokes <see cref="OnStart"/> inside its
/// first tick, and then accepts only strictly increasing frame indices. Stop and
/// unload are engine commands; scene authors customize lifecycle through the
/// protected hooks.
/// </remarks>
public class Scene
{
    private static readonly Vector2 DefaultVirtualResolution = new(1920f, 1080f);

    private readonly SceneRuntime runtime;
    private readonly Scheduler scheduler;
    private readonly InputRouter router;
    private readonly Vector2 virtualResolution = DefaultVirtualResolution;
    private SceneEnvironment? environment;
    private List<IDisposable>? owned;
    private ICompositor? compositor;
    private SceneState state;
    private bool loadCompleted;
    private bool startCompleted;
    private bool stopAttempted;
    private bool unloadAttempted;
    private bool isRendering;
    private long lastFrame = -1;

    /// <summary>Creates an unloaded scene with an empty layer stack.</summary>
    public Scene()
    {
        Layers = new LayerStack(this);
        scheduler = new Scheduler(this);
        router = new InputRouter(this);
        runtime = new SceneRuntime(this, Layers, scheduler);
    }

    /// <summary>Gets this scene's controlled, add-ordered layer stack.</summary>
    public LayerStack Layers { get; }

    /// <summary>Gets this scene's input router: capture, focus, and picking (§9.2).</summary>
    /// <remarks>
    /// <para>
    /// The router runs itself during the Input phase of every tick; this property is the handle for
    /// the things that are asked <em>between</em> events — <see cref="InputRouter.Capture"/> from a
    /// press, <see cref="InputRouter.Focus"/> from a click on a text field,
    /// <see cref="InputRouter.Pick"/> from a tool that wants to know what is under a point.
    /// </para>
    /// <para>
    /// One router per scene instance, for the whole of its life. Capture and focus are therefore
    /// scene state, and a replay that constructs a fresh instance (§2.2) starts with neither — which
    /// is what keeps them out of the determinism story.
    /// </para>
    /// </remarks>
    public InputRouter Input => router;

    /// <summary>Gets the closed capability set this scene was loaded with.</summary>
    /// <remarks>
    /// <para>
    /// Valid only while the scene is loaded. The engine binds it before any author code runs, so
    /// <see cref="OnLoad"/> and everything after it can read it, and releases it once
    /// <see cref="OnUnload"/> has returned — an unloaded scene holds no reference to its host's
    /// capabilities, and neither does one whose load failed. Reading it outside that window is a
    /// lifecycle mistake and throws rather than handing back a half-formed environment.
    /// </para>
    /// <para>
    /// A scene reaches its host only through this property. There is no service registry and no
    /// ambient host singleton, so an embedder can hand a child scene a decorated environment and be
    /// certain the child cannot see around it.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The scene is not loaded.</exception>
    public SceneEnvironment Env =>
        environment ?? throw new InvalidOperationException(
            $"Scene.Env is valid only while the scene is loaded; this scene is {state}.");

    /// <summary>
    /// Gets the finite, positive size of this scene's virtual coordinate space. The default is
    /// 1920 by 1080.
    /// </summary>
    /// <remarks>
    /// A <see cref="ScreenLayer"/>'s coordinates are virtual coordinates outright, and a
    /// <see cref="WorldLayer2D"/>'s camera frames its world against this size. Hosts scale virtual
    /// space onto the output preserving aspect, so this one value drives rendering, pointer math,
    /// and embedding identically.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value does not have finite, positive components.
    /// </exception>
    public Vector2 VirtualResolution
    {
        get => virtualResolution;
        init
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || value.X <= 0f || value.Y <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "A virtual resolution must have finite, positive components.");
            }

            virtualResolution = value;
        }
    }

    /// <summary>Advances one valid, strictly increasing simulation frame.</summary>
    public void Tick(in TickContext tick)
    {
        if (!tick.IsValid)
        {
            throw new ArgumentException("A scene tick requires constructed TickContext.", nameof(tick));
        }
        if (tick.Time.IsFixedStep && !tick.Input.IsEmpty)
        {
            // §2.1, §2.5, §9.1 and Appendix A.1 all state the same rule: a deterministic run takes
            // no input. It is enforced here rather than trusted, because the failure it prevents —
            // a fixed-step export that quietly depends on where a pointer happened to be — does not
            // show up as a crash, only as two renders of the same scene that do not match.
            throw new InvalidOperationException(
                "A fixed-step tick must carry InputBlock.Empty: deterministic runs take no input " +
                "(ARCHITECTURE section 2.1). Interactive behaviour belongs to a live variant of " +
                "the scene (section 2.5), and a live run uses ClockPolicy.Live.");
        }
        if (state is not (SceneState.Loaded or SceneState.Started))
        {
            throw InvalidTransition(nameof(Tick), state);
        }

        var frame = tick.Time.Frame;
        if (frame <= lastFrame)
        {
            throw new InvalidOperationException(
                $"Scene frame indices must increase strictly; received {frame} after {lastFrame}.");
        }

        if (state == SceneState.Loaded)
        {
            state = SceneState.Starting;
            runtime.BeginDeferredMutations();
            try
            {
                OnStart();
                startCompleted = true;
                runtime.CommitDeferredMutations();
                state = SceneState.Started;
            }
            catch
            {
                runtime.AbandonDeferredMutations();
                state = SceneState.Faulted;
                throw;
            }
        }

        try
        {
            runtime.Update(tick);
            lastFrame = frame;
        }
        catch
        {
            state = SceneState.Faulted;
            throw;
        }
    }

    /// <summary>Renders the current scene state into a render target.</summary>
    /// <param name="target">The target to paint this frame into.</param>
    /// <remarks>
    /// <para>
    /// This is the composited path, and it is the only thing this method does. The render scale is
    /// derived from the target's size against <see cref="VirtualResolution"/>, and the frame is
    /// handed to the <see cref="ICompositor"/> acquired at load from
    /// <see cref="SceneEnvironment.Surfaces"/>, which stages each layer through its own target and
    /// merges it with the layer's <see cref="Layer.Opacity"/>, <see cref="Layer.Blend"/>, and
    /// <see cref="Layer.Viewport"/>. Every loaded scene has an environment and therefore a
    /// compositor, so there is no second reading of a frame: each participating layer's
    /// <see cref="Layer.ClearColor"/> is content that merges over everything beneath it.
    /// </para>
    /// <para>
    /// Rendering is idempotent: it does not mutate observable scene state, so one ticked frame can
    /// be rendered any number of times and into any number of targets with identical results. It is
    /// legal before the scene's first <see cref="Tick"/>, and therefore before a node's first
    /// update.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The scene is not loaded, a render is already in progress, or the target's size and the
    /// virtual resolution do not yield a finite, positive render scale.
    /// </exception>
    public void Render(IRenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        EnsureRenderable(nameof(Render));

        var renderScale = ResolveRenderScale(target.Size);
        var active = compositor ?? throw new InvalidOperationException(
            "A loaded scene always holds the compositor acquired from its environment.");

        isRendering = true;
        try
        {
            active.Render(Layers, target, virtualResolution, renderScale);
        }
        finally
        {
            isRendering = false;
        }
    }

    /// <summary>Renders the current scene state into one already-begun draw context.</summary>
    /// <param name="context">The borrowed context every layer paints into, in order.</param>
    /// <remarks>
    /// <para>
    /// This is the direct path: no per-layer target is bound and the surface itself is never
    /// cleared, so the caller owns both the pass and whatever the context already holds. The render
    /// scale is the one the context's pass was begun with, because
    /// <see cref="IDrawContext2D.SetEngineTransform(in Matrix3x2)"/> replaces the pass baseline
    /// wholesale and the traverser must therefore fold that scale back into every transform it
    /// installs.
    /// </para>
    /// <para>
    /// Every participating layer's presentation still applies. Each is walked inside an engine layer
    /// bracket carrying its clear, viewport, opacity, and blend, so the frame matches what
    /// <see cref="Render(IRenderTarget)"/> composites: a layer's <see cref="Layer.ClearColor"/> is
    /// content that fills its region, and its opacity and blend apply to the whole layer as a group.
    /// </para>
    /// <para>
    /// <strong>Placement is the caller's.</strong> This path never letterboxes: the context arrived
    /// with a pass already begun, and where on the surface that pass paints is decided by the base
    /// transform the caller installed. That is not a disagreement with
    /// <see cref="Render(IRenderTarget)"/>, which centres the fitted frame per
    /// <see cref="FramePlacement"/>; it is the same rule applied by whoever owns the surface. A
    /// caller that wants the composited path's placement asks
    /// <see cref="FramePlacement.ResolveContentRect(in Vector2, PixelSize, float)"/> for it and
    /// folds the origin into the transform it begins the pass with.
    /// </para>
    /// <para>Rendering is idempotent, exactly as <see cref="Render(IRenderTarget)"/> is.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The scene is not loaded or a render is already in progress.
    /// </exception>
    public void RenderDirect(IDrawContext2D context)
    {
        ArgumentNullException.ThrowIfNull(context);
        EnsureRenderable(nameof(RenderDirect));

        isRendering = true;
        try
        {
            RenderTraverser.RenderLayers(Layers, context, virtualResolution, context.RenderScale);
        }
        finally
        {
            isRendering = false;
        }
    }

    /// <summary>Starts a scene-lifetime coroutine and returns its handle.</summary>
    /// <param name="routine">
    /// The routine body. It is driven by the coroutine pass, which runs once per tick inside Update
    /// after the whole tree has updated, so a resumed routine observes this frame's settled state.
    /// </param>
    /// <remarks>
    /// <para>
    /// Scene lifetime means the routine is cancelled when the scene stops, which disposes its
    /// enumerator and runs any <c>finally</c> the author wrote. For a routine that should die with a
    /// node instead, use <see cref="Node.Start(IEnumerator{Wait})"/>.
    /// </para>
    /// <para>
    /// The routine is queued, not resumed: one started before this frame's pass — in
    /// <see cref="OnLoad"/>, <see cref="OnStart"/>, a tree update, or an input handler — takes its
    /// first resume in that pass; one started during the pass is appended and resumed later in the
    /// same pass. Starting from a render is a contract violation, asserted in debug builds.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="routine"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The scene is not in a schedulable state.</exception>
    public CoroutineHandle Start(IEnumerator<Wait> routine)
    {
        ArgumentNullException.ThrowIfNull(routine);
        return RequireScheduler(nameof(Start)).Start(routine, owner: null);
    }

    /// <summary>Starts a scene-lifetime tween over a float property and returns its handle.</summary>
    /// <param name="setter">Receives the from-value now and every value the ramp produces after.</param>
    /// <param name="from">The value written synchronously, at this call site.</param>
    /// <param name="to">The exact value written when the tween completes.</param>
    /// <param name="duration">Finite, non-negative simulation seconds the ramp takes.</param>
    /// <param name="ease">The easing curve. The default is <see cref="Ease.Linear"/>.</param>
    /// <remarks>
    /// The from-value is applied immediately so the property never shows a frame of its old value;
    /// the first delta is consumed at the next tween pass. Tween time is simulation time — there is
    /// no wall-clock path — and the pass runs immediately before the coroutine pass, so
    /// <c>yield return Wait.For(handle)</c> resumes in the same frame the tween ends.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="setter"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// An endpoint is not finite, or the duration is not finite and non-negative.
    /// </exception>
    /// <exception cref="InvalidOperationException">The scene is not in a schedulable state.</exception>
    public AnimationHandle Animate(
        Action<float> setter,
        float from,
        float to,
        double duration,
        TimingFunction ease = default) =>
        RequireScheduler(nameof(Animate)).Animate(setter, from, to, duration, ease, custom: null, owner: null);

    /// <inheritdoc cref="Animate(Action{float}, float, float, double, TimingFunction)" />
    /// <param name="setter">Receives the from-value now and every value the ramp produces after.</param>
    /// <param name="from">The value written synchronously, at this call site.</param>
    /// <param name="to">The exact value written when the tween completes.</param>
    /// <param name="duration">Finite, non-negative simulation seconds the ramp takes.</param>
    /// <param name="ease">A custom easing curve.</param>
    /// <exception cref="ArgumentNullException"><paramref name="setter"/> or <paramref name="ease"/> is null.</exception>
    public AnimationHandle Animate(
        Action<float> setter,
        float from,
        float to,
        double duration,
        ITimingFunction ease)
    {
        ArgumentNullException.ThrowIfNull(ease);
        return RequireScheduler(nameof(Animate))
            .Animate(setter, from, to, duration, default, ease, owner: null);
    }

    /// <summary>Starts a scene-lifetime tween over a double property and returns its handle.</summary>
    /// <param name="setter">Receives the from-value now and every value the ramp produces after.</param>
    /// <param name="from">The value written synchronously, at this call site.</param>
    /// <param name="to">The exact value written when the tween completes.</param>
    /// <param name="duration">Finite, non-negative simulation seconds the ramp takes.</param>
    /// <param name="ease">The easing curve. The default is <see cref="Ease.Linear"/>.</param>
    /// <remarks>
    /// <para>
    /// The same tween as the <see cref="float"/> overload in every timing respect — same pass, same
    /// from-value applied at the call site, same exact landing on <paramref name="to"/>. It exists
    /// because the quantities a scene actually animates are doubles: degrees, radii, seconds, and
    /// anything read out of a physical model. Driving one of those through the float overload costs
    /// a widening lambda, <c>f</c>-suffixed literals on numbers that are not floats, and endpoints
    /// rounded to float before the tween has run at all.
    /// </para>
    /// <para>
    /// <strong>Which overload a call site gets is decided by the endpoints.</strong> <c>0d</c> and
    /// <c>1.5</c> reach this one; <c>0</c>, <c>1</c> and <c>0f</c> reach the float one, because an
    /// int literal converts to float in preference to double. That is worth knowing rather than
    /// worth fighting, because a float setter widens into a double field silently:
    /// <c>Animate(v =&gt; azimuth = v, 0, 90, 1d)</c> over a <c>double azimuth</c> compiles, runs the
    /// float tween, and differs only in the last digits. Suffix the endpoints — <c>0d</c>,
    /// <c>90d</c> — or pass double variables, and the intended overload is unambiguous.
    /// </para>
    /// <para>
    /// <strong>Precision.</strong> The endpoints and the interpolation are double; the easing curve
    /// is evaluated in single precision, because <see cref="ITimingFunction"/> is a float contract.
    /// The curve therefore resolves about seven digits of the interval, around endpoints that are
    /// exact and a final write that is <paramref name="to"/> itself.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="setter"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// An endpoint is not finite, or the duration is not finite and non-negative.
    /// </exception>
    /// <exception cref="InvalidOperationException">The scene is not in a schedulable state.</exception>
    public AnimationHandle Animate(
        Action<double> setter,
        double from,
        double to,
        double duration,
        TimingFunction ease = default) =>
        RequireScheduler(nameof(Animate)).Animate(setter, from, to, duration, ease, custom: null, owner: null);

    /// <inheritdoc cref="Animate(Action{double}, double, double, double, TimingFunction)" />
    /// <param name="setter">Receives the from-value now and every value the ramp produces after.</param>
    /// <param name="from">The value written synchronously, at this call site.</param>
    /// <param name="to">The exact value written when the tween completes.</param>
    /// <param name="duration">Finite, non-negative simulation seconds the ramp takes.</param>
    /// <param name="ease">A custom easing curve.</param>
    /// <exception cref="ArgumentNullException"><paramref name="setter"/> or <paramref name="ease"/> is null.</exception>
    public AnimationHandle Animate(
        Action<double> setter,
        double from,
        double to,
        double duration,
        ITimingFunction ease)
    {
        ArgumentNullException.ThrowIfNull(ease);
        return RequireScheduler(nameof(Animate))
            .Animate(setter, from, to, duration, default, ease, owner: null);
    }

    /// <summary>
    /// Gets whether this scene still holds a coroutine or a tween that has not reached a terminal
    /// status.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The question "is the choreography finished?", answered by the only object that knows. It is
    /// what an open-ended offline run keys off — see <see cref="OfflineOptions.Duration"/> — and it
    /// is the honest alternative to a scene publishing a hand-summed duration that the waits inside
    /// it can silently exceed.
    /// </para>
    /// <para>
    /// <strong>Finished is not the same as running.</strong> A paused routine, and one under a
    /// disabled node, is still live here: it has stopped running, not stopped existing, and
    /// something may yet resume it. A routine parked on a condition that never comes true is live
    /// forever, which is a real way to make a run that never ends.
    /// </para>
    /// <para>
    /// Read it after a tick. Inside one — from a routine, or from <c>Update</c> — the answer counts
    /// the routine doing the asking.
    /// </para>
    /// </remarks>
    public bool HasScheduledWork => scheduler.HasLiveWork;

    /// <summary>
    /// Hands a native or otherwise disposable resource to the scene, which releases it when the
    /// scene's life ends, and returns it.
    /// </summary>
    /// <typeparam name="T">The resource type.</typeparam>
    /// <param name="resource">The resource whose lifetime is this scene's.</param>
    /// <returns><paramref name="resource"/>, so the call can wrap the construction that produced it.</returns>
    /// <remarks>
    /// <para>
    /// For a node that owns something the garbage collector will not release — a GL texture, a
    /// framebuffer, an external image wrapping one — written as
    /// <c>renderer = Own(new GlBackedNode(...));</c> in <see cref="OnLoad"/>. It replaces the
    /// <see cref="OnUnload"/> override that would otherwise exist only to null a field and call
    /// <c>Dispose</c> on it.
    /// </para>
    /// <para>
    /// <strong>Scene lifetime, not node lifetime, and that is the honest offer.</strong> The engine
    /// has no "this node is gone for good" signal to hang disposal on:
    /// <see cref="Node.OnDetach"/> runs for <em>any</em> detach, re-parenting inside a live scene
    /// included, so releasing a native resource there would destroy the target of a node that is
    /// about to be added back. A scene, by contrast, has exactly one end, the engine controls it,
    /// and it happens on every path. So the scene is where ownership can be promised and this is
    /// where it is offered. A node that genuinely dies mid-scene is disposed at the call site that
    /// removed it, by the code that decided it was finished.
    /// </para>
    /// <para>
    /// <strong>Order.</strong> Resources are disposed last-registered-first, after
    /// <see cref="OnUnload"/> has returned and after every layer has been detached — so the author's
    /// own teardown and every node's <see cref="Node.OnDetach"/> still see live resources — and
    /// before the compositor is released. Reverse order is what makes a wrap registered over a
    /// texture release before the texture it borrowed the name of.
    /// </para>
    /// <para>
    /// <strong>It also covers the path an <see cref="OnUnload"/> override cannot.</strong> A load
    /// that fails part way through leaves the scene faulted without ever running
    /// <see cref="OnUnload"/>, so a resource acquired earlier in <see cref="OnLoad"/> would have no
    /// route to release at all. Anything registered before the failure is released by the rollback.
    /// </para>
    /// <para>
    /// A <c>Dispose</c> that throws does not stop the others: every remaining resource is still
    /// released and the failures are reported together, the way the rest of scene teardown reports
    /// them.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The scene has already begun tearing down, so nothing registered now would ever be released.
    /// </exception>
    protected T Own<T>(T resource)
        where T : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (state is not (
            SceneState.Constructed or
            SceneState.Loading or
            SceneState.Loaded or
            SceneState.Starting or
            SceneState.Started))
        {
            throw InvalidTransition(nameof(Own), state);
        }

        (owned ??= []).Add(resource);
        return resource;
    }

    /// <summary>Runs after the engine has attached the scene's initial layers.</summary>
    protected virtual void OnLoad()
    {
    }

    /// <summary>Runs exactly once immediately before the first successful Update traversal.</summary>
    protected virtual void OnStart()
    {
    }

    /// <summary>Updates this scene once per tick, before any layer updates.</summary>
    /// <param name="tick">This tick's simulation time and input.</param>
    /// <remarks>
    /// <para>
    /// This is the scene-level counterpart of <see cref="Layer.Update"/>, <see cref="Node.Update"/>
    /// and <see cref="Behavior.Update"/>, and it runs before all three: the Update phase is this
    /// hook, then the layer traversal, then the tween pass, then the coroutine pass, then the
    /// deferred flush. Structural edits requested here are deferred to that flush like any other.
    /// </para>
    /// <para>
    /// It is named <c>Update</c> rather than <c>OnUpdate</c> to match the per-tick override on every
    /// other tier; the <c>On</c> prefix disambiguates hooks from same-named commands, and there is no
    /// <c>Scene.Update</c> command for it to collide with. This member is a documented deviation from
    /// the reference, which gives <see cref="Scene"/> no per-tick hook at all.
    /// </para>
    /// </remarks>
    protected virtual void Update(in TickContext tick)
    {
    }

    /// <summary>Runs at most once after a completed start.</summary>
    protected virtual void OnStop()
    {
    }

    /// <summary>Runs at most once after a completed load.</summary>
    protected virtual void OnUnload()
    {
    }

    /// <summary>
    /// Loads the scene, binding the environment and acquiring its compositor before author load
    /// code runs.
    /// </summary>
    /// <param name="env">
    /// The closed capability set this scene runs against. Its
    /// <see cref="SceneEnvironment.Surfaces"/> is the composition authority: the compositor
    /// <see cref="Render(IRenderTarget)"/> delegates to is acquired from it here and disposed at
    /// <see cref="Unload"/>.
    /// </param>
    /// <remarks>
    /// <para>
    /// <strong>This is a driver's call, not an author's.</strong> §4.6 makes a host the composition
    /// root — it "assembles the environment, owns the clock and platform event pump, feeds ticks,
    /// provides render targets, and delivers output" — and §16 puts hosts in their own assemblies,
    /// outside Core. §4.7 then writes the desktop host's loop with <c>scene.Load(env)</c> in it. A
    /// driver outside this assembly therefore has to be able to say this, and the whole sequence is
    /// public for that reason: <see cref="Load"/>, then <see cref="Tick"/> and
    /// <see cref="Render(IRenderTarget)"/>, then <see cref="Stop"/> and <see cref="Unload"/>.
    /// </para>
    /// <para>
    /// <strong>Being callable is not being callable in the wrong order.</strong> §4.1's promise —
    /// "a host, embedder, or test cannot call hooks out of order" — is kept by the state machine
    /// rather than by visibility: this refuses anything but a freshly constructed scene,
    /// <see cref="Tick"/> refuses a scene that is not loaded and a frame index that does not
    /// advance, <see cref="Stop"/> and <see cref="Unload"/> run at most once each and in that
    /// order whichever is called, and the protected hooks stay protected. What is public is the
    /// command; the transition is still the engine's.
    /// </para>
    /// <para>
    /// A failed load leaves the scene faulted and unusable, and releases whatever <c>OnLoad</c> had
    /// already acquired — including anything registered with <c>Own</c> — because that path is the
    /// one where <c>OnUnload</c> never runs.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="env"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The scene has already left its constructed state.</exception>
    public void Load(SceneEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);
        if (state != SceneState.Constructed)
        {
            throw InvalidTransition(nameof(Load), state);
        }

        var snapshot = Layers.Snapshot();
        state = SceneState.Loading;
        try
        {
            environment = env;
            compositor = env.Surfaces.CreateCompositor();
            runtime.AttachExistingLayers();
            OnLoad();
            loadCompleted = true;
            state = SceneState.Loaded;
        }
        catch (Exception original)
        {
            var cleanup = new List<Exception>(runtime.RollbackLoad(snapshot));

            // The one path where the author's OnUnload never runs: whatever OnLoad had already
            // acquired when it failed is released here or not at all.
            DisposeOwned(cleanup);
            var disposal = ReleaseComposition();
            if (disposal is not null)
            {
                cleanup.Add(disposal);
            }

            state = SceneState.Faulted;
            ThrowCombined(original, cleanup);
            throw;
        }
    }

    /// <summary>Ends the scene's run: <c>OnStop</c>, then every scene-owned routine and tween.</summary>
    /// <remarks>
    /// <para>
    /// Idempotent and safe from any state but <c>Constructed</c>, so a driver's teardown path can
    /// call it without first working out whether the run got that far.
    /// <see cref="Unload"/> calls it for you if you have not.
    /// </para>
    /// <para>
    /// Scheduling ends here <em>even when the author's stop hook throws</em>: an enumerator that
    /// never gets disposed is a <c>finally</c> that never runs. A hook failure is reported after
    /// that cleanup, not instead of it.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The scene has not been loaded.</exception>
    public void Stop()
    {
        if (state is SceneState.Stopped or SceneState.Unloaded || stopAttempted)
        {
            return;
        }
        if (state == SceneState.Constructed)
        {
            throw InvalidTransition(nameof(Stop), state);
        }

        stopAttempted = true;
        var failures = new List<Exception>();
        state = SceneState.Stopping;
        try
        {
            if (startCompleted)
            {
                OnStop();
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        finally
        {
            // Scene-lifetime routines and tweens end here, and they end even when the author's stop
            // hook threw: an enumerator that never gets disposed is a `finally` that never runs.
            scheduler.CancelAll(failures);
            runtime.AbandonMutations();
            state = failures.Count == 0 ? SceneState.Stopped : SceneState.Faulted;
        }

        ThrowFailures(failures);
    }

    /// <summary>Releases everything the scene holds: hooks, layers, owned resources, compositor.</summary>
    /// <remarks>
    /// <para>
    /// The last call of a scene's life and the one a driver must make on every path, including a
    /// failed one. It runs <see cref="Stop"/> first if the run was started and not yet stopped,
    /// then <c>OnUnload</c>, then detaches every layer, then disposes what <c>Own</c> registered,
    /// and releases the compositor last — after the author teardown that may still be using it, and
    /// before the backend that supplied it can be torn down.
    /// </para>
    /// <para>
    /// Idempotent, and it completes even when a hook throws: failures from every stage are gathered
    /// and reported together rather than letting the first one abandon the rest. That is what makes
    /// it safe in a <c>finally</c>.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The scene has not been loaded.</exception>
    public void Unload()
    {
        if (state == SceneState.Unloaded || unloadAttempted)
        {
            return;
        }
        if (state == SceneState.Constructed)
        {
            throw InvalidTransition(nameof(Unload), state);
        }

        var failures = new List<Exception>();
        if (!stopAttempted && startCompleted)
        {
            try
            {
                Stop();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        unloadAttempted = true;
        state = SceneState.Unloading;
        try
        {
            if (loadCompleted)
            {
                OnUnload();
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        finally
        {
            // A scene that loaded but never started never ran Stop, so anything OnLoad scheduled is
            // still live and still holding an undisposed enumerator.
            scheduler.CancelAll(failures);
            failures.AddRange(runtime.DetachAllLayers());

            // After OnUnload and after every OnDetach, so author teardown sees live resources, and
            // before the compositor, which the backend those resources came from may outlive.
            DisposeOwned(failures);
            var disposal = ReleaseComposition();
            if (disposal is not null)
            {
                failures.Add(disposal);
            }

            runtime.AbandonMutations();
            state = SceneState.Unloaded;
        }

        ThrowFailures(failures);
    }

    internal SceneState State => state;

    internal bool IsRendering => isRendering;

    internal void InvokeUpdate(in TickContext tick) => Update(tick);

    /// <summary>Returns the scheduler, refusing the call from a state that cannot schedule.</summary>
    internal Scheduler RequireScheduler(string operation)
    {
        if (state is not (
            SceneState.Loading or
            SceneState.Loaded or
            SceneState.Starting or
            SceneState.Started))
        {
            throw InvalidTransition(operation, state);
        }

        return scheduler;
    }

    internal NodeRegistry Registry => runtime.Registry;

    internal ICompositor? Compositor => compositor;

    internal void MarkFaulted() => state = SceneState.Faulted;

    internal void RequestAddLayer(Layer layer)
    {
        switch (state)
        {
            case SceneState.Constructed:
                Layers.AddImmediate(layer);
                return;
            case SceneState.Loading:
            case SceneState.Loaded:
            case SceneState.Starting:
            case SceneState.Started:
                runtime.RequestAddLayer(layer);
                return;
            default:
                throw InvalidTransition("Layers.Add", state);
        }
    }

    internal bool RequestRemoveLayer(Layer layer)
    {
        return state switch
        {
            SceneState.Constructed => Layers.RemoveImmediate(layer),
            SceneState.Loading or SceneState.Loaded or SceneState.Starting or SceneState.Started =>
                runtime.RequestRemoveLayer(layer),
            _ => throw InvalidTransition("Layers.Remove", state),
        };
    }

    /// <summary>
    /// Returns the virtual-to-device pixel scale for one output size: the largest uniform scale
    /// that fits <see cref="VirtualResolution"/> inside it, matching the aspect-preserving letterbox
    /// the compositor centres the content rect within.
    /// </summary>
    /// <remarks>
    /// The rule lives in <see cref="FramePlacement"/>, which the compositor also places the content
    /// rect with, so the scale and the placement cannot disagree about where the frame lands.
    /// </remarks>
    private float ResolveRenderScale(PixelSize size)
    {
        if (size.IsEmpty)
        {
            throw new InvalidOperationException("A render target must report a positive pixel size.");
        }

        return FramePlacement.ResolveRenderScale(virtualResolution, size);
    }

    private void EnsureRenderable(string operation)
    {
        if (state is not (SceneState.Loaded or SceneState.Started))
        {
            throw InvalidTransition(operation, state);
        }
        if (isRendering)
        {
            throw new InvalidOperationException("Scene rendering is not reentrant.");
        }
    }

    /// <summary>
    /// Releases the environment binding and disposes the acquired compositor at most once, freeing
    /// every layer target and the accumulation surface it owns, and returns a disposal failure
    /// instead of throwing so scene teardown can report it beside the other cleanup failures.
    /// </summary>
    /// <remarks>
    /// The environment goes with the compositor because they arrive together: the compositor is the
    /// environment's provider realized for this scene, so a scene that no longer has one must not
    /// keep claiming the other. Note what is <em>not</em> disposed — the provider, the typesetter,
    /// and the audio sink all outlive the scene and belong to whoever injected them.
    /// </remarks>
    /// <summary>Releases everything <see cref="Own{T}"/> registered, newest first.</summary>
    /// <remarks>
    /// The list is dropped as it is emptied, so a second teardown — an <see cref="Unload"/> after a
    /// failed <see cref="Load"/>, say — cannot dispose anything twice.
    /// </remarks>
    private void DisposeOwned(List<Exception> failures)
    {
        var resources = owned;
        owned = null;
        if (resources is null)
        {
            return;
        }

        for (var index = resources.Count - 1; index >= 0; index--)
        {
            try
            {
                resources[index].Dispose();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
    }

    private Exception? ReleaseComposition()
    {
        var owned = compositor;
        compositor = null;
        environment = null;
        if (owned is null)
        {
            return null;
        }

        try
        {
            owned.Dispose();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static InvalidOperationException InvalidTransition(string operation, SceneState current) =>
        new($"Scene operation '{operation}' is invalid while the scene is {current}.");

    private static void ThrowCombined(Exception original, IReadOnlyList<Exception> cleanup)
    {
        if (cleanup.Count == 0)
        {
            ExceptionDispatchInfo.Capture(original).Throw();
        }

        var failures = new Exception[cleanup.Count + 1];
        failures[0] = original;
        for (var index = 0; index < cleanup.Count; index++)
        {
            failures[index + 1] = cleanup[index];
        }

        throw new AggregateException("Scene transition and rollback both failed.", failures);
    }

    private static void ThrowFailures(IReadOnlyList<Exception> failures)
    {
        if (failures.Count == 0)
        {
            return;
        }
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        throw new AggregateException("Multiple scene cleanup callbacks failed.", failures);
    }
}
