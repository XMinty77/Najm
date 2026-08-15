namespace Najm.Core;

/// <summary>
/// Internal attachment seam used to redirect edits owned or reserved by a scene
/// through its unified structural-mutation queue.
/// </summary>
internal interface INodeMutationSink
{
    void RequestAdd(Node parent, Node child);

    bool RequestRemove(Node parent, Node child);

    void RequestAddBehavior(Node node, Behavior behavior);

    bool RequestRemoveBehavior(Node node, Behavior behavior);
}
