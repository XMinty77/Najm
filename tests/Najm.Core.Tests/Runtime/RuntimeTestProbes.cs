namespace Najm.Core.Tests.Runtime;

internal sealed class ProbeScene : Scene
{
    internal ProbeScene(List<string>? events = null) => Events = events ?? [];

    internal List<string> Events { get; }

    internal Action? LoadAction { get; set; }

    internal Action? StartAction { get; set; }

    internal Action? StopAction { get; set; }

    internal Action? UnloadAction { get; set; }

    protected override void OnLoad()
    {
        Events.Add("scene.load");
        LoadAction?.Invoke();
    }

    protected override void OnStart()
    {
        Events.Add("scene.start");
        StartAction?.Invoke();
    }

    protected override void OnStop()
    {
        Events.Add("scene.stop");
        StopAction?.Invoke();
    }

    protected override void OnUnload()
    {
        Events.Add("scene.unload");
        UnloadAction?.Invoke();
    }
}

internal class ProbeLayer : ScreenLayer
{
    internal ProbeLayer(string name, List<string> events)
    {
        Name = name;
        Events = events;
    }

    internal string Name { get; }

    internal List<string> Events { get; }

    internal Action<Scene>? AttachAction { get; set; }

    internal Action? DetachAction { get; set; }

    internal Action? UpdateAction { get; set; }

    protected override void OnAttach(Scene scene)
    {
        Events.Add($"{Name}.attach");
        AttachAction?.Invoke(scene);
    }

    protected override void OnDetach()
    {
        Events.Add($"{Name}.detach");
        DetachAction?.Invoke();
    }

    protected override void Update(in TickContext tick)
    {
        Events.Add($"{Name}.update");
        UpdateAction?.Invoke();
    }
}

internal class ProbeNode : Node2D
{
    internal ProbeNode(string name, List<string> events)
    {
        Name = name;
        Events = events;
    }

    internal string Name { get; }

    internal List<string> Events { get; }

    internal Action? AttachAction { get; set; }

    internal Action? DetachAction { get; set; }

    internal Action? UpdateAction { get; set; }

    protected override void OnAttach()
    {
        Events.Add($"{Name}.attach");
        AttachAction?.Invoke();
    }

    protected override void OnDetach()
    {
        Events.Add($"{Name}.detach");
        DetachAction?.Invoke();
    }

    protected override void Update(in TickContext tick)
    {
        Events.Add($"{Name}.update");
        UpdateAction?.Invoke();
    }
}

internal class ProbeBehavior : Behavior
{
    internal ProbeBehavior(string name, List<string> events)
    {
        Name = name;
        Events = events;
    }

    internal string Name { get; }

    internal List<string> Events { get; }

    internal Action? AttachAction { get; set; }

    internal Action? DetachAction { get; set; }

    internal Action? UpdateAction { get; set; }

    protected override void OnAttach()
    {
        Events.Add($"{Name}.attach");
        AttachAction?.Invoke();
    }

    protected override void OnDetach()
    {
        Events.Add($"{Name}.detach");
        DetachAction?.Invoke();
    }

    protected override void Update(in TickContext tick)
    {
        Events.Add($"{Name}.update");
        UpdateAction?.Invoke();
    }
}

internal static class RuntimeTicks
{
    internal static TickContext At(long frame)
    {
        var time = new TimeInfo(frame + 1d, 1d, frame, isFixedStep: true);
        return new TickContext(time);
    }
}
