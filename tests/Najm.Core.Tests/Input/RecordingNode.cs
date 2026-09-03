using System.Numerics;

namespace Najm.Core.Tests.Input;

/// <summary>A hit-testable node that implements every <see cref="IInteractive"/> member and records it.</summary>
internal sealed class RecordingNode(Rect hit) : HitNode(hit), IInteractive
{
    /// <summary>Gets the shared log this node appends dispatch names to.</summary>
    internal List<string> Log { get; init; } = [];

    /// <summary>Gets or sets whether handlers report the event as consumed.</summary>
    internal bool Handles { get; set; }

    /// <summary>Gets or sets an action run inside <see cref="IInteractive.OnPointerDown"/>.</summary>
    internal Action<RecordingNode, PointerArgs>? PointerDown { get; set; }

    /// <summary>Gets or sets an action run inside <see cref="IInteractive.OnDrag"/>.</summary>
    internal Action<RecordingNode, PointerArgs>? Drag { get; set; }

    /// <summary>Gets or sets an action run inside <see cref="IInteractive.OnPointerUp"/>.</summary>
    internal Action<RecordingNode, PointerArgs>? PointerUp { get; set; }

    /// <summary>Gets the arguments of the most recent pointer dispatch.</summary>
    internal PointerArgs LastPointer { get; private set; }

    /// <summary>Gets the arguments of the most recent key dispatch.</summary>
    internal KeyArgs LastKey { get; private set; }

    /// <summary>Gets the arguments of the most recent text dispatch.</summary>
    internal TextInputArgs LastText { get; private set; }

    void IInteractive.OnPointerEnter(in PointerArgs args)
    {
        LastPointer = args;
        Log.Add($"{Name}:enter");
    }

    void IInteractive.OnPointerExit(in PointerArgs args)
    {
        LastPointer = args;
        Log.Add($"{Name}:exit");
    }

    bool IInteractive.OnPointerDown(in PointerArgs args)
    {
        LastPointer = args;
        Log.Add($"{Name}:down");
        PointerDown?.Invoke(this, args);
        return Handles;
    }

    bool IInteractive.OnPointerUp(in PointerArgs args)
    {
        LastPointer = args;
        Log.Add($"{Name}:up");
        PointerUp?.Invoke(this, args);
        return Handles;
    }

    bool IInteractive.OnPointerMove(in PointerArgs args)
    {
        LastPointer = args;
        Log.Add($"{Name}:move");
        return Handles;
    }

    bool IInteractive.OnDrag(in PointerArgs args)
    {
        LastPointer = args;
        Log.Add($"{Name}:drag");
        Drag?.Invoke(this, args);
        return Handles;
    }

    bool IInteractive.OnScroll(in PointerArgs args)
    {
        LastPointer = args;
        Log.Add($"{Name}:scroll");
        return Handles;
    }

    void IInteractive.OnFocus() => Log.Add($"{Name}:focus");

    void IInteractive.OnBlur() => Log.Add($"{Name}:blur");

    bool IInteractive.OnKey(in KeyArgs args)
    {
        LastKey = args;
        Log.Add($"{Name}:key({args.Key},{(args.IsDown ? "down" : "up")})");
        return Handles;
    }

    bool IInteractive.OnTextInput(in TextInputArgs args)
    {
        LastText = args;
        Log.Add($"{Name}:text({args.Text})");
        return Handles;
    }
}

/// <summary>Drives a loaded scene one tick at a time over a host-shaped <see cref="InputBuffer"/>.</summary>
internal sealed class RouterHarness : IDisposable
{
    private long frame;

    internal RouterHarness(Scene? scene = null)
    {
        Scene = scene ?? new Scene();
        Layer = new ScreenLayer();
        Scene.Layers.Add(Layer);
    }

    /// <summary>Gets the scene under test.</summary>
    internal Scene Scene { get; }

    /// <summary>Gets the screen layer created with the harness.</summary>
    internal ScreenLayer Layer { get; }

    /// <summary>Gets the buffer standing in for a host's event pump.</summary>
    internal InputBuffer Buffer { get; } = new();

    /// <summary>Gets the scene's router.</summary>
    internal InputRouter Router => Scene.Input;

    /// <summary>Loads the scene against the Core test environment.</summary>
    internal RouterHarness Load()
    {
        Scene.Load(TestEnvironment.Stub());
        return this;
    }

    /// <summary>Ticks the scene with whatever is in the buffer, then empties it for the next frame.</summary>
    internal void Tick()
    {
        Scene.Tick(new TickContext(
            new TimeInfo((frame + 1) * 0.016, 0.016, frame, isFixedStep: false),
            Buffer.Block));
        frame++;
        Buffer.BeginFrame();
    }

    /// <summary>Adds a named, solid recording node to a parent, defaulting to the layer root.</summary>
    /// <remarks>
    /// The hit log is separate from the dispatch log and off by default: an ordering test wants only
    /// the walk, and a dispatch test wants only the dispatches.
    /// </remarks>
    internal RecordingNode Add(
        string name,
        List<string> log,
        Rect hit,
        Node2D? parent = null,
        Vector2 position = default,
        List<string>? hitLog = null)
    {
        var node = new RecordingNode(hit)
        {
            Name = name,
            Log = log,
            HitLog = hitLog,
            Position = position,
        };
        (parent ?? Layer.Root).Add(node);
        return node;
    }

    public void Dispose() => Scene.Unload();
}
