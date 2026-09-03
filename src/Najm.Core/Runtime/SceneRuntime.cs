using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

namespace Najm.Core;

internal sealed class SceneRuntime : INodeMutationSink
{
    private readonly Scene scene;
    private readonly LayerStack layers;
    private readonly Scheduler scheduler;
    private readonly List<Mutation> mutations = [];
    private readonly Dictionary<Node, Node?> projectedParents = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Behavior, Node?> projectedBehaviorOwners = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Layer, LayerStack?> projectedLayerOwners = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Node> reservedNodes = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Behavior> reservedBehaviors = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Layer> reservedLayers = new(ReferenceEqualityComparer.Instance);
    private bool isCleanup;
    private bool isDeferring;
    private bool isFlushing;
    private bool isUpdating;

    internal SceneRuntime(Scene scene, LayerStack layers, Scheduler scheduler)
    {
        this.scene = scene;
        this.layers = layers;
        this.scheduler = scheduler;
    }

    internal NodeRegistry Registry { get; } = new();

    public void RequestAdd(Node parent, Node child)
    {
        EnsureSceneAcceptsMutations();
        EnsureRequestComesFromThisRuntime(parent);
        ValidateProjectedAdd(parent, child);
        ReserveSubtree(child);

        var attachmentLayer = EffectiveLayer(parent);
        projectedParents[child] = parent;
        mutations.Add(new Mutation(MutationKind.AddChild, parent, child, attachmentLayer));
        FlushIfIdle();
    }

    public bool RequestRemove(Node parent, Node child)
    {
        EnsureSceneAcceptsMutations();
        EnsureRequestComesFromThisRuntime(parent);
        if (!ReferenceEquals(EffectiveParent(child), parent))
        {
            return false;
        }

        ReserveSubtree(child);
        var attachmentLayer = EffectiveLayer(child);
        projectedParents[child] = null;
        mutations.Add(new Mutation(MutationKind.RemoveChild, parent, child, attachmentLayer));
        FlushIfIdle();
        return true;
    }

    public void RequestAddBehavior(Node node, Behavior behavior)
    {
        EnsureSceneAcceptsMutations();
        EnsureRequestComesFromThisRuntime(node);
        var currentOwner = EffectiveBehaviorOwner(behavior);
        if (currentOwner is not null)
        {
            var message = ReferenceEquals(currentOwner, node)
                ? "The behavior is already owned by this node."
                : "The behavior is already owned by a different node.";
            throw new InvalidOperationException(message);
        }
        if (behavior.ReservationSink is not null && !ReferenceEquals(behavior.ReservationSink, this))
        {
            throw new InvalidOperationException("A behavior reserved by another scene cannot be claimed.");
        }

        ReserveBehavior(behavior);
        var attachmentLayer = EffectiveLayer(node);
        projectedBehaviorOwners[behavior] = node;
        mutations.Add(new Mutation(MutationKind.AddBehavior, node, behavior, attachmentLayer));
        FlushIfIdle();
    }

    public bool RequestRemoveBehavior(Node node, Behavior behavior)
    {
        EnsureSceneAcceptsMutations();
        EnsureRequestComesFromThisRuntime(node);
        if (!ReferenceEquals(EffectiveBehaviorOwner(behavior), node))
        {
            return false;
        }

        ReserveBehavior(behavior);
        var attachmentLayer = EffectiveLayer(node);
        projectedBehaviorOwners[behavior] = null;
        mutations.Add(new Mutation(MutationKind.RemoveBehavior, node, behavior, attachmentLayer));
        FlushIfIdle();
        return true;
    }

    internal void RequestAddLayer(Layer layer)
    {
        EnsureSceneAcceptsMutations();
        var currentOwner = EffectiveLayerOwner(layer);
        if (currentOwner is not null)
        {
            var message = ReferenceEquals(currentOwner, layers)
                ? "The layer is already in this scene's layer stack."
                : "The layer is already owned by a different scene.";
            throw new InvalidOperationException(message);
        }
        if (layer.ReservationStack is not null && !ReferenceEquals(layer.ReservationStack, layers))
        {
            throw new InvalidOperationException("A layer reserved by another scene cannot be claimed.");
        }

        ReserveLayer(layer);
        projectedLayerOwners[layer] = layers;
        mutations.Add(new Mutation(MutationKind.AddLayer, layers, layer, null));
        FlushIfIdle();
    }

    internal bool RequestRemoveLayer(Layer layer)
    {
        EnsureSceneAcceptsMutations();
        if (!ReferenceEquals(EffectiveLayerOwner(layer), layers))
        {
            return false;
        }

        ReserveLayer(layer);
        projectedLayerOwners[layer] = null;
        mutations.Add(new Mutation(MutationKind.RemoveLayer, layers, layer, null));
        FlushIfIdle();
        return true;
    }

    internal void AttachExistingLayers()
    {
        if (isFlushing || isUpdating)
        {
            throw new InvalidOperationException("Scene attachment cannot begin inside another runtime phase.");
        }

        isFlushing = true;
        try
        {
            var initialCount = layers.Count;
            for (var index = 0; index < initialCount; index++)
            {
                var layer = layers[index];
                if (layer.AttachedScene is null)
                {
                    AttachLayer(layer);
                }
            }

            DrainMutations();
            ClearMutationState();
        }
        catch
        {
            ClearMutationState();
            throw;
        }
        finally
        {
            isFlushing = false;
        }
    }

    internal void Update(in TickContext tick)
    {
        if (isUpdating || isFlushing)
        {
            throw new InvalidOperationException("Scene Update is not reentrant.");
        }

        // INPUT (§4.7): the router dispatches this tick's events, then the phase ends with its own
        // flush — which is what makes an Input-added node update, lay out, and render this frame
        // (§6.4). A deterministic tick carries the empty block and the router returns immediately.
        isUpdating = true;
        try
        {
            scene.Input.Route(tick);
        }
        catch
        {
            ClearMutationState();
            throw;
        }
        finally
        {
            isUpdating = false;
        }

        FlushMutations();

        isUpdating = true;
        try
        {
            scene.InvokeUpdate(tick);
            for (var layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                var layer = layers[layerIndex];
                if (!ReferenceEquals(layer.AttachedScene, scene))
                {
                    throw new InvalidOperationException("An unattached layer appeared in an active scene traversal.");
                }

                layer.InvokeUpdate(tick);
                UpdateNode(layer.RuntimeRoot, tick);
            }

            // Order is normative. Tweens advance first so an animation that ends this frame is
            // already terminal when the coroutine pass evaluates the waiters joined to it, which is
            // what makes chained animations gap-free. Both passes run after the whole tree has
            // updated, so a resumed routine reads this frame's settled state, and before the flush
            // below, so anything either pass queues structurally lands at the end of Update.
            scheduler.RunTweenPass(tick);
            scheduler.RunCoroutinePass(tick);
        }
        catch
        {
            ClearMutationState();
            throw;
        }
        finally
        {
            isUpdating = false;
        }

        FlushMutations();
    }

    internal void BeginDeferredMutations()
    {
        if (isDeferring || isUpdating || isFlushing)
        {
            throw new InvalidOperationException("A deferred runtime phase is already active.");
        }

        isDeferring = true;
    }

    internal void CommitDeferredMutations()
    {
        if (!isDeferring)
        {
            throw new InvalidOperationException("No deferred runtime phase is active.");
        }

        isDeferring = false;
        FlushMutations();
    }

    internal void AbandonDeferredMutations()
    {
        isDeferring = false;
        ClearMutationState();
    }

    internal IReadOnlyList<Exception> RollbackLoad(Layer[] snapshot)
    {
        ClearMutationState();
        var failures = RunGlobalDetachCleanup();
        try
        {
            layers.Restore(snapshot);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        return failures;
    }

    internal IReadOnlyList<Exception> DetachAllLayers()
    {
        ClearMutationState();
        return RunGlobalDetachCleanup();
    }

    internal void AbandonMutations()
    {
        isDeferring = false;
        ClearMutationState();
    }

    private void FlushIfIdle()
    {
        if (!isDeferring && !isUpdating && !isFlushing)
        {
            FlushMutations();
        }
    }

    private void FlushMutations()
    {
        if (mutations.Count == 0)
        {
            ClearMutationState();
            return;
        }
        if (isFlushing)
        {
            return;
        }

        isFlushing = true;
        try
        {
            DrainMutations();
            ClearMutationState();
        }
        catch
        {
            ClearMutationState();
            scene.MarkFaulted();
            throw;
        }
        finally
        {
            isFlushing = false;
        }
    }

    private void DrainMutations()
    {
        for (var index = 0; index < mutations.Count; index++)
        {
            Execute(mutations[index]);
        }
    }

    private void Execute(in Mutation mutation)
    {
        switch (mutation.Kind)
        {
            case MutationKind.AddLayer:
                ExecuteAddLayer((Layer)mutation.Item);
                break;
            case MutationKind.RemoveLayer:
                ExecuteRemoveLayer((Layer)mutation.Item);
                break;
            case MutationKind.AddChild:
                ExecuteAddChild((Node)mutation.Owner, (Node)mutation.Item, mutation.AttachmentLayer);
                break;
            case MutationKind.RemoveChild:
                ExecuteRemoveChild((Node)mutation.Owner, (Node)mutation.Item, mutation.AttachmentLayer);
                break;
            case MutationKind.AddBehavior:
                ExecuteAddBehavior((Node)mutation.Owner, (Behavior)mutation.Item, mutation.AttachmentLayer);
                break;
            case MutationKind.RemoveBehavior:
                ExecuteRemoveBehavior((Node)mutation.Owner, (Behavior)mutation.Item, mutation.AttachmentLayer);
                break;
            default:
                throw new InvalidOperationException($"Unknown scene mutation kind '{mutation.Kind}'.");
        }
    }

    private void ExecuteAddLayer(Layer layer)
    {
        layers.AddImmediate(layer);
        try
        {
            AttachLayer(layer);
        }
        catch
        {
            layers.RemoveImmediate(layer);
            throw;
        }
    }

    private void ExecuteRemoveLayer(Layer layer)
    {
        var failures = ReferenceEquals(layer.AttachedScene, scene)
            ? DetachLayer(layer)
            : [];
        try
        {
            if (!layers.RemoveImmediate(layer))
            {
                failures.Add(new InvalidOperationException("A queued layer removal lost its owner before flush."));
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        ThrowFailures(failures, "Layer detach failed.");
    }

    private void ExecuteAddChild(Node parent, Node child, Layer? attachmentLayer)
    {
        parent.AddImmediate(child);
        if (attachmentLayer is null)
        {
            return;
        }

        try
        {
            AttachSubtree(child, attachmentLayer);
        }
        catch
        {
            parent.RemoveImmediate(child);
            throw;
        }
    }

    private void ExecuteRemoveChild(Node parent, Node child, Layer? attachmentLayer)
    {
        var failures = attachmentLayer is null ? [] : DetachSubtree(child);
        try
        {
            if (!parent.RemoveImmediate(child))
            {
                failures.Add(new InvalidOperationException("A queued child removal lost its parent before flush."));
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        ThrowFailures(failures, "Node detach failed.");
    }

    private void ExecuteAddBehavior(Node node, Behavior behavior, Layer? attachmentLayer)
    {
        node.Behaviors.AddImmediate(behavior);
        if (attachmentLayer is null)
        {
            return;
        }

        try
        {
            behavior.InvokeAttach();
        }
        catch
        {
            node.Behaviors.RemoveImmediate(behavior);
            throw;
        }
    }

    private static void ExecuteRemoveBehavior(Node node, Behavior behavior, Layer? attachmentLayer)
    {
        var failures = new List<Exception>();
        if (attachmentLayer is not null)
        {
            try
            {
                behavior.InvokeDetach();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        try
        {
            if (!node.Behaviors.RemoveImmediate(behavior))
            {
                failures.Add(new InvalidOperationException("A queued behavior removal lost its owner before flush."));
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        ThrowFailures(failures, "Behavior detach failed.");
    }

    private void AttachLayer(Layer layer)
    {
        if (layer.AttachedScene is not null)
        {
            throw new InvalidOperationException("A layer cannot attach to more than one scene.");
        }

        var root = layer.RuntimeRoot;
        ValidateAttachSubtree(root, layer);
        var completed = new List<AttachmentRecord>();
        layer.SetAttachedScene(scene);
        CommitSubtreeAttachment(root, layer);
        try
        {
            layer.InvokeAttach(scene);
            completed.Add(new AttachmentRecord(AttachmentKind.Layer, layer));
            InvokeAttachSubtree(root, completed);
        }
        catch (Exception original)
        {
            var cleanup = RollbackCompletedAttachments(completed);
            CleanupSubtreeAttachment(root, cleanup);
            layer.SetAttachedScene(null);
            ThrowCombined(original, cleanup, "Layer attach and rollback both failed.");
        }
    }

    private void AttachSubtree(Node root, Layer layer)
    {
        ValidateAttachSubtree(root, layer);
        var completed = new List<AttachmentRecord>();
        CommitSubtreeAttachment(root, layer);
        try
        {
            InvokeAttachSubtree(root, completed);
        }
        catch (Exception original)
        {
            var cleanup = RollbackCompletedAttachments(completed);
            CleanupSubtreeAttachment(root, cleanup);
            ThrowCombined(original, cleanup, "Node attach and rollback both failed.");
        }
    }

    private void ValidateAttachSubtree(Node root, Layer layer)
    {
        if (root.Layer is not null)
        {
            throw new InvalidOperationException("An attached node cannot be attached again.");
        }
        if (root.SpaceKind != layer.RuntimeRoot.SpaceKind)
        {
            throw new InvalidOperationException("A node subtree cannot attach to a layer with a different coordinate space.");
        }

        Registry.ValidateAbsentSubtree(root);
        ValidateAttachOwnership(root);
    }

    private void ValidateAttachOwnership(Node node)
    {
        if (node.MutationSink is not null && !ReferenceEquals(node.MutationSink, this))
        {
            throw new InvalidOperationException("A node attached to another scene cannot enter this scene.");
        }
        if (node.ReservationSink is not null && !ReferenceEquals(node.ReservationSink, this))
        {
            throw new InvalidOperationException("A node reserved by another scene cannot enter this scene.");
        }

        for (var behaviorIndex = 0; behaviorIndex < node.BehaviorCount; behaviorIndex++)
        {
            var behavior = node.GetBehavior(behaviorIndex);
            if (!ReferenceEquals(behavior.Node, node))
            {
                throw new InvalidOperationException("A behavior collection contains an inconsistently owned behavior.");
            }
            if (behavior.ReservationSink is not null && !ReferenceEquals(behavior.ReservationSink, this))
            {
                throw new InvalidOperationException("A behavior reserved by another scene cannot enter this scene.");
            }
        }
        for (var childIndex = 0; childIndex < node.ChildCount; childIndex++)
        {
            ValidateAttachOwnership(node.GetChild(childIndex));
        }
    }

    private void CommitSubtreeAttachment(Node root, Layer layer)
    {
        root.SetMutationSinkRecursively(this);
        root.SetLayerRecursively(layer);
        Registry.RegisterSubtree(root);
    }

    private static void InvokeAttachSubtree(Node node, List<AttachmentRecord> completed)
    {
        node.InvokeAttach();
        completed.Add(new AttachmentRecord(AttachmentKind.Node, node));

        for (var behaviorIndex = 0; behaviorIndex < node.BehaviorCount; behaviorIndex++)
        {
            var behavior = node.GetBehavior(behaviorIndex);
            behavior.InvokeAttach();
            completed.Add(new AttachmentRecord(AttachmentKind.Behavior, behavior));
        }
        for (var childIndex = 0; childIndex < node.ChildCount; childIndex++)
        {
            InvokeAttachSubtree(node.GetChild(childIndex), completed);
        }
    }

    private static List<Exception> RollbackCompletedAttachments(List<AttachmentRecord> completed)
    {
        var failures = new List<Exception>();
        for (var index = completed.Count - 1; index >= 0; index--)
        {
            try
            {
                var record = completed[index];
                switch (record.Kind)
                {
                    case AttachmentKind.Layer:
                        ((Layer)record.Target).InvokeDetach();
                        break;
                    case AttachmentKind.Node:
                        ((Node)record.Target).InvokeDetach();
                        break;
                    case AttachmentKind.Behavior:
                        ((Behavior)record.Target).InvokeDetach();
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown attachment kind '{record.Kind}'.");
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        return failures;
    }

    private List<Exception> DetachLayer(Layer layer)
    {
        var root = layer.EstablishedRuntimeRoot;
        var failures = new List<Exception>();
        InvokeDetachSubtree(root, failures);
        try
        {
            layer.InvokeDetach();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        CleanupSubtreeAttachment(root, failures);
        layer.SetAttachedScene(null);
        return failures;
    }

    private List<Exception> DetachSubtree(Node root)
    {
        var failures = new List<Exception>();
        InvokeDetachSubtree(root, failures);
        CleanupSubtreeAttachment(root, failures);
        return failures;
    }

    private static void InvokeDetachSubtree(Node node, List<Exception> failures)
    {
        for (var childIndex = node.ChildCount - 1; childIndex >= 0; childIndex--)
        {
            InvokeDetachSubtree(node.GetChild(childIndex), failures);
        }
        for (var behaviorIndex = node.BehaviorCount - 1; behaviorIndex >= 0; behaviorIndex--)
        {
            try
            {
                node.GetBehavior(behaviorIndex).InvokeDetach();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        try
        {
            node.InvokeDetach();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private void CleanupSubtreeAttachment(Node root, List<Exception> failures)
    {
        // Detach cancels what the subtree owns: routines are cancelled synchronously here, so their
        // enumerators are disposed and author `finally` cleanup runs inside this flush, and tweens
        // stop at their current value. Parent links are still intact at this point, which is what
        // lets ownership be decided by walking ancestors.
        scheduler.CancelOwnedBySubtree(root, failures);

        // §6.4 and §6.6: detach releases input capture, and §9.2 adds keyboard focus. Both are
        // dropped silently — the subtree's OnDetach has already run above.
        scene.Input.ReleaseSubtree(root);

        try
        {
            Registry.UnregisterSubtree(root);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        finally
        {
            root.SetLayerRecursively(null);
            root.SetMutationSinkRecursively(null);
        }
    }

    private List<Exception> DetachAllLayersCore()
    {
        var failures = new List<Exception>();
        for (var index = layers.Count - 1; index >= 0; index--)
        {
            var layer = layers[index];
            if (ReferenceEquals(layer.AttachedScene, scene))
            {
                failures.AddRange(DetachLayer(layer));
            }
        }

        if (Registry.Count != 0)
        {
            failures.Add(new InvalidOperationException("The node registry was not empty after scene detach."));
            Registry.Clear();
        }

        return failures;
    }

    private List<Exception> RunGlobalDetachCleanup()
    {
        if (isFlushing || isUpdating)
        {
            throw new InvalidOperationException("Global scene detach cannot nest inside another runtime phase.");
        }

        isCleanup = true;
        isFlushing = true;
        try
        {
            return DetachAllLayersCore();
        }
        finally
        {
            ClearMutationState();
            isFlushing = false;
            isCleanup = false;
        }
    }

    private static void UpdateNode(Node node, in TickContext tick)
    {
        if (!node.Enabled)
        {
            return;
        }

        node.InvokeUpdate(tick);
        for (var behaviorIndex = 0; behaviorIndex < node.BehaviorCount; behaviorIndex++)
        {
            node.GetBehavior(behaviorIndex).InvokeUpdate(tick);
        }
        for (var childIndex = 0; childIndex < node.ChildCount; childIndex++)
        {
            UpdateNode(node.GetChild(childIndex), tick);
        }
    }

    private void ValidateProjectedAdd(Node parent, Node child)
    {
        if (parent.SpaceKind != child.SpaceKind)
        {
            throw new InvalidOperationException(
                $"A {child.SpaceKind} node cannot be parented beneath a {parent.SpaceKind} node.");
        }
        if (child.LayerRootOwner is not null)
        {
            throw new InvalidOperationException("A layer root cannot be parented beneath another node.");
        }

        var currentParent = EffectiveParent(child);
        if (currentParent is not null)
        {
            var message = ReferenceEquals(currentParent, parent)
                ? "The node is already a child of this parent."
                : "The node already has a different parent.";
            throw new InvalidOperationException(message);
        }
        if (child.MutationSink is not null && !ReferenceEquals(child.MutationSink, this))
        {
            throw new InvalidOperationException("An attached node cannot move between scenes.");
        }
        if (child.ReservationSink is not null && !ReferenceEquals(child.ReservationSink, this))
        {
            throw new InvalidOperationException("A node reserved by another scene cannot be claimed.");
        }

        for (Node? ancestor = parent; ancestor is not null; ancestor = EffectiveParent(ancestor))
        {
            if (ReferenceEquals(ancestor, child))
            {
                throw new InvalidOperationException("A node cannot be added to itself or one of its descendants.");
            }
        }
    }

    private void EnsureRequestComesFromThisRuntime(Node node)
    {
        if (!ReferenceEquals(node.MutationSink, this) && !ReferenceEquals(node.ReservationSink, this))
        {
            throw new InvalidOperationException("A node mutation was routed to a scene that does not own it.");
        }
    }

    private void EnsureSceneAcceptsMutations()
    {
        if (!isCleanup && scene.State is not (
            SceneState.Loading or
            SceneState.Loaded or
            SceneState.Starting or
            SceneState.Started))
        {
            throw new InvalidOperationException(
                $"Structural mutation is invalid while the scene is {scene.State}.");
        }
    }

    private Node? EffectiveParent(Node node) =>
        projectedParents.TryGetValue(node, out var parent) ? parent : node.Parent;

    private Node? EffectiveBehaviorOwner(Behavior behavior) =>
        projectedBehaviorOwners.TryGetValue(behavior, out var owner) ? owner : behavior.Node;

    private LayerStack? EffectiveLayerOwner(Layer layer) =>
        projectedLayerOwners.TryGetValue(layer, out var owner) ? owner : layer.OwnerStack;

    private Layer? EffectiveLayer(Node node)
    {
        for (Node? current = node; current is not null; current = EffectiveParent(current))
        {
            var rootOwner = current.LayerRootOwner;
            if (rootOwner is not null)
            {
                return ReferenceEquals(EffectiveLayerOwner(rootOwner), layers) ? rootOwner : null;
            }
        }

        return null;
    }

    private void ReserveLayer(Layer layer)
    {
        ValidateSubtreeReservation(layer.RuntimeRoot);
        ApplySubtreeReservation(layer.RuntimeRoot);
        layer.SetReservationStack(layers);
        reservedLayers.Add(layer);
    }

    private void ReserveSubtree(Node root)
    {
        ValidateSubtreeReservation(root);
        ApplySubtreeReservation(root);
    }

    private void ValidateSubtreeReservation(Node node)
    {
        if (node.MutationSink is not null && !ReferenceEquals(node.MutationSink, this))
        {
            throw new InvalidOperationException("A subtree attached to another scene cannot be reserved.");
        }
        if (node.ReservationSink is not null && !ReferenceEquals(node.ReservationSink, this))
        {
            throw new InvalidOperationException("A subtree reserved by another scene cannot be claimed.");
        }

        for (var behaviorIndex = 0; behaviorIndex < node.BehaviorCount; behaviorIndex++)
        {
            var behavior = node.GetBehavior(behaviorIndex);
            if (behavior.ReservationSink is not null && !ReferenceEquals(behavior.ReservationSink, this))
            {
                throw new InvalidOperationException("A behavior reserved by another scene cannot be claimed.");
            }
        }
        for (var childIndex = 0; childIndex < node.ChildCount; childIndex++)
        {
            ValidateSubtreeReservation(node.GetChild(childIndex));
        }
    }

    private void ApplySubtreeReservation(Node node)
    {
        node.SetReservationSink(this);
        reservedNodes.Add(node);
        for (var behaviorIndex = 0; behaviorIndex < node.BehaviorCount; behaviorIndex++)
        {
            ReserveBehavior(node.GetBehavior(behaviorIndex));
        }
        for (var childIndex = 0; childIndex < node.ChildCount; childIndex++)
        {
            ApplySubtreeReservation(node.GetChild(childIndex));
        }
    }

    private void ReserveBehavior(Behavior behavior)
    {
        if (behavior.ReservationSink is not null && !ReferenceEquals(behavior.ReservationSink, this))
        {
            throw new InvalidOperationException("A behavior reserved by another scene cannot be claimed.");
        }

        behavior.SetReservationSink(this);
        reservedBehaviors.Add(behavior);
    }

    private void ClearMutationState()
    {
        mutations.Clear();
        projectedParents.Clear();
        projectedBehaviorOwners.Clear();
        projectedLayerOwners.Clear();

        foreach (var node in reservedNodes)
        {
            if (ReferenceEquals(node.ReservationSink, this))
            {
                node.SetReservationSink(null);
            }
        }
        foreach (var behavior in reservedBehaviors)
        {
            if (ReferenceEquals(behavior.ReservationSink, this))
            {
                behavior.SetReservationSink(null);
            }
        }
        foreach (var layer in reservedLayers)
        {
            if (ReferenceEquals(layer.ReservationStack, layers))
            {
                layer.SetReservationStack(null);
            }
        }

        reservedNodes.Clear();
        reservedBehaviors.Clear();
        reservedLayers.Clear();
    }

    [DoesNotReturn]
    private static void ThrowCombined(
        Exception original,
        IReadOnlyList<Exception> cleanup,
        string aggregateMessage)
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

        throw new AggregateException(aggregateMessage, failures);
    }

    private static void ThrowFailures(List<Exception> failures, string aggregateMessage)
    {
        if (failures.Count == 0)
        {
            return;
        }
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        throw new AggregateException(aggregateMessage, failures);
    }

    private enum MutationKind : byte
    {
        AddLayer,
        RemoveLayer,
        AddChild,
        RemoveChild,
        AddBehavior,
        RemoveBehavior,
    }

    private enum AttachmentKind : byte
    {
        Layer,
        Node,
        Behavior,
    }

    private readonly record struct Mutation(
        MutationKind Kind,
        object Owner,
        object Item,
        Layer? AttachmentLayer);

    private readonly record struct AttachmentRecord(AttachmentKind Kind, object Target);
}
