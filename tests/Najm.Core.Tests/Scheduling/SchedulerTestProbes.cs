namespace Najm.Core.Tests.Scheduling;

/// <summary>The fixed 60 fps tick grid the scheduler's exactness rules are stated against.</summary>
/// <remarks>
/// These go through <see cref="FixedStepTiming"/> rather than building a <see cref="TimeInfo"/> by
/// hand, because the whole point of the <c>Seconds</c> and tween-duration tests is that they hold
/// for the delta the production clock actually produces — the double nearest 1/60, which is slightly
/// below the exact value.
/// </remarks>
internal static class SchedulerTicks
{
    /// <summary>The frame rate every exactness assertion in this folder is stated at.</summary>
    internal const double FramesPerSecond = 60d;

    /// <summary>Builds the tick for one frame of the 60 fps grid.</summary>
    internal static TickContext At(long frame) => new(FixedStepTiming.Tick(frame, FramesPerSecond));
}

/// <summary>A scene that records its lifecycle and exposes hooks the tests fill in.</summary>
internal sealed class SchedulerScene : Scene
{
    /// <summary>Gets the ordered log of scene, layer, and node callbacks.</summary>
    internal List<string> Events { get; } = [];

    /// <summary>Gets how many times the per-tick scene hook ran.</summary>
    internal int UpdateCount { get; private set; }

    /// <summary>Gets or sets what runs inside <see cref="OnStart"/>.</summary>
    internal Action<SchedulerScene>? StartAction { get; set; }

    /// <summary>Gets or sets what runs inside <see cref="OnStop"/>.</summary>
    internal Action<SchedulerScene>? StopAction { get; set; }

    /// <summary>Gets or sets what runs inside the per-tick scene hook.</summary>
    internal Action<SchedulerScene>? UpdateAction { get; set; }

    protected override void OnStart()
    {
        Events.Add("scene.start");
        StartAction?.Invoke(this);
    }

    protected override void OnStop()
    {
        Events.Add("scene.stop");
        StopAction?.Invoke(this);
    }

    protected override void Update(in TickContext tick)
    {
        UpdateCount++;
        Events.Add("scene.update");
        UpdateAction?.Invoke(this);
    }
}

/// <summary>A layer that logs its per-tick update into a shared event list.</summary>
internal sealed class LoggingLayer : ScreenLayer
{
    private readonly List<string> events;
    private readonly string name;

    internal LoggingLayer(string name, List<string> events)
    {
        this.name = name;
        this.events = events;
    }

    protected override void Update(in TickContext tick) => events.Add($"{name}.update");
}

/// <summary>A node that logs its per-tick update into a shared event list.</summary>
internal sealed class LoggingNode : Node2D
{
    private readonly List<string> events;
    private readonly string name;

    internal LoggingNode(string name, List<string> events)
    {
        this.name = name;
        this.events = events;
    }

    protected override void Update(in TickContext tick) => events.Add($"{name}.update");
}

/// <summary>A scene loaded with one screen layer, which is all most of these tests need.</summary>
internal sealed class SchedulerHarness
{
    internal SchedulerHarness()
    {
        Scene = new SchedulerScene();
        Layer = Scene.Layers.Add(new ScreenLayer());
    }

    /// <summary>Gets the scene under test.</summary>
    internal SchedulerScene Scene { get; }

    /// <summary>Gets the scene's one layer.</summary>
    internal ScreenLayer Layer { get; }

    /// <summary>Gets the layer's root node.</summary>
    internal Node2D Root => Layer.Root;

    /// <summary>Gets the index of the frame most recently ticked, or -1 before the first tick.</summary>
    internal long Frame { get; private set; } = -1;

    /// <summary>Loads the scene against the standard stub environment.</summary>
    internal SchedulerHarness Load()
    {
        Scene.Load(TestEnvironment.Stub());
        return this;
    }

    /// <summary>Ticks the next frame of the 60 fps grid.</summary>
    internal void Tick()
    {
        Frame++;
        Scene.Tick(SchedulerTicks.At(Frame));
    }

    /// <summary>Ticks <paramref name="count"/> further frames.</summary>
    internal void Tick(int count)
    {
        for (var index = 0; index < count; index++)
        {
            Tick();
        }
    }

    /// <summary>
    /// Ticks until <paramref name="predicate"/> holds after a tick, and returns the frame it held on.
    /// </summary>
    /// <param name="predicate">Checked once after each tick.</param>
    /// <param name="limit">The most frames to tick before failing.</param>
    internal long TickUntil(Func<bool> predicate, int limit = 600)
    {
        for (var index = 0; index < limit; index++)
        {
            Tick();
            if (predicate())
            {
                return Frame;
            }
        }

        Assert.Fail($"The condition never held within {limit} frames.");
        return -1;
    }
}
