using System.Numerics;
using System.Runtime.ExceptionServices;

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
    private readonly Vector2 virtualResolution = DefaultVirtualResolution;
    private SceneEnvironment? environment;
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
        runtime = new SceneRuntime(this, Layers);
    }

    /// <summary>Gets this scene's controlled, add-ordered layer stack.</summary>
    public LayerStack Layers { get; }

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
    /// This is the direct path: no per-layer target is bound and no surface is cleared, so the
    /// caller owns both the pass and whatever the context already holds. The render scale is the
    /// one the context's pass was begun with, because
    /// <see cref="IDrawContext2D.SetEngineTransform(in Matrix3x2)"/> replaces the pass baseline
    /// wholesale and the traverser must therefore fold that scale back into every transform it
    /// installs.
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

    /// <summary>Runs after the engine has attached the scene's initial layers.</summary>
    protected virtual void OnLoad()
    {
    }

    /// <summary>Runs exactly once immediately before the first successful Update traversal.</summary>
    protected virtual void OnStart()
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

    internal SceneState State => state;

    internal NodeRegistry Registry => runtime.Registry;

    internal ICompositor? Compositor => compositor;

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
    /// <exception cref="ArgumentNullException"><paramref name="env"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The scene has already left its constructed state.</exception>
    internal void Load(SceneEnvironment env)
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
            var cleanup = runtime.RollbackLoad(snapshot);
            var disposal = ReleaseComposition();
            state = SceneState.Faulted;
            ThrowCombined(original, disposal is null ? cleanup : Append(cleanup, disposal));
            throw;
        }
    }

    internal void Stop()
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
        Exception? failure = null;
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
            failure = exception;
        }
        finally
        {
            runtime.AbandonMutations();
            state = failure is null ? SceneState.Stopped : SceneState.Faulted;
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    internal void Unload()
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
            failures.AddRange(runtime.DetachAllLayers());
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
    /// the host applies around the content rect.
    /// </summary>
    private float ResolveRenderScale(PixelSize size)
    {
        if (size.IsEmpty)
        {
            throw new InvalidOperationException("A render target must report a positive pixel size.");
        }

        var scale = MathF.Min(size.Width / virtualResolution.X, size.Height / virtualResolution.Y);
        if (!float.IsFinite(scale) || scale <= 0f)
        {
            throw new InvalidOperationException(
                $"A {size.Width}×{size.Height} target and a {virtualResolution.X}×{virtualResolution.Y} " +
                "virtual resolution do not yield a finite, positive render scale.");
        }

        return scale;
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

    private static IReadOnlyList<Exception> Append(IReadOnlyList<Exception> failures, Exception added)
    {
        var combined = new List<Exception>(failures.Count + 1);
        combined.AddRange(failures);
        combined.Add(added);
        return combined;
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
