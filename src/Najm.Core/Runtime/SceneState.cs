namespace Najm.Core;

internal enum SceneState : byte
{
    Constructed,
    Loading,
    Loaded,
    Starting,
    Started,
    Stopping,
    Stopped,
    Unloading,
    Unloaded,
    Faulted,
}
