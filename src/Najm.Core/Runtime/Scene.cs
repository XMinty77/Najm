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
    private readonly SceneRuntime runtime;
    private SceneState state;
    private bool loadCompleted;
    private bool startCompleted;
    private bool stopAttempted;
    private bool unloadAttempted;
    private long lastFrame = -1;

    /// <summary>Creates an unloaded scene with an empty layer stack.</summary>
    public Scene()
    {
        Layers = new LayerStack(this);
        runtime = new SceneRuntime(this, Layers);
    }

    /// <summary>Gets this scene's controlled, add-ordered layer stack.</summary>
    public LayerStack Layers { get; }

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

    internal void Load()
    {
        if (state != SceneState.Constructed)
        {
            throw InvalidTransition(nameof(Load), state);
        }

        var snapshot = Layers.Snapshot();
        state = SceneState.Loading;
        try
        {
            runtime.AttachExistingLayers();
            OnLoad();
            loadCompleted = true;
            state = SceneState.Loaded;
        }
        catch (Exception original)
        {
            var cleanup = runtime.RollbackLoad(snapshot);
            state = SceneState.Faulted;
            ThrowCombined(original, cleanup);
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
