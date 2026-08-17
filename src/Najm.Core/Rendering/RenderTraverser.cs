using System.Numerics;

namespace Najm.Core;

/// <summary>
/// Walks scene layers and their node trees into a draw context: the single home of the render
/// walk, shared by every render path so the paths cannot drift.
/// </summary>
/// <remarks>
/// <para>
/// This type is backend-facing engine machinery, not an authoring API. It is driven by
/// <see cref="Scene.Render(IRenderTarget)"/>, by <see cref="Scene.RenderDirect(IDrawContext2D)"/>,
/// and — once it exists — by a backend compositor that binds one target per layer and calls
/// <see cref="RenderLayer(Layer, IDrawContext2D, in Vector2, float)"/> for each.
/// </para>
/// <para>
/// The walk is depth-first pre-order in paint order, so a parent paints beneath its children and
/// siblings paint in ascending <see cref="Node2D.ZIndex"/> with insertion order breaking ties. Every
/// node receives its own engine transform through
/// <see cref="IDrawContext2D.SetEngineTransform(in Matrix3x2)"/> before its
/// <see cref="Node.Render(IDrawContext2D)"/> runs, so the traverser never brackets the walk in
/// author state — an outstanding push would make the next engine transform illegal.
/// </para>
/// <para>
/// Traversal reads the tree and writes only to the context: it never mutates observable scene
/// state, which is what makes rendering one ticked frame more than once produce identical output.
/// A warm walk over an unchanged tree allocates nothing.
/// </para>
/// </remarks>
public static class RenderTraverser
{
    /// <summary>
    /// Renders every participating layer of one stack into a single context, in add order.
    /// </summary>
    /// <param name="layers">The layer stack to walk, bottom layer first.</param>
    /// <param name="context">The borrowed draw context every layer paints into.</param>
    /// <param name="virtualResolution">The scene's finite, positive virtual resolution.</param>
    /// <param name="renderScale">The finite, positive virtual-to-device pixel scale.</param>
    /// <remarks>
    /// This is the direct path's walk: no per-layer target is bound and no per-layer isolation
    /// bracket is opened. Layers that do not participate — see
    /// <see cref="ParticipatesInRender(Layer)"/> — are skipped whole.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="layers"/> or <paramref name="context"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="virtualResolution"/> or <paramref name="renderScale"/> is not finite and
    /// positive.
    /// </exception>
    public static void RenderLayers(
        LayerStack layers,
        IDrawContext2D context,
        in Vector2 virtualResolution,
        float renderScale)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(context);
        EnsureVirtualResolution(virtualResolution);
        EnsureRenderScale(renderScale);

        for (var index = 0; index < layers.Count; index++)
        {
            RenderLayer(layers[index], context, virtualResolution, renderScale);
        }
    }

    /// <summary>
    /// Renders one layer: base transform, <c>OnBeforeRender</c>, the paint-order tree walk, then
    /// <c>OnAfterRender</c>.
    /// </summary>
    /// <param name="layer">The layer to walk.</param>
    /// <param name="context">The borrowed draw context this layer paints into.</param>
    /// <param name="virtualResolution">The scene's finite, positive virtual resolution.</param>
    /// <param name="renderScale">The finite, positive virtual-to-device pixel scale.</param>
    /// <remarks>
    /// A layer that does not participate in rendering is skipped whole: its hooks do not run and
    /// its tree is not walked. Both hooks see the context in layer space, so the layer base is
    /// reinstalled after the walk before <c>OnAfterRender</c> runs.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="layer"/> or <paramref name="context"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="virtualResolution"/> or <paramref name="renderScale"/> is not finite and
    /// positive.
    /// </exception>
    public static void RenderLayer(
        Layer layer,
        IDrawContext2D context,
        in Vector2 virtualResolution,
        float renderScale)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(context);
        EnsureVirtualResolution(virtualResolution);
        EnsureRenderScale(renderScale);

        if (!ParticipatesInRender(layer))
        {
            return;
        }

        var layerBase = ComputeLayerBase(layer, virtualResolution, renderScale);
        context.SetEngineTransform(layerBase);
        layer.InvokeBeforeRender(context);

        RenderNode(layer.EstablishedRuntimeRoot, context, layerBase);

        context.SetEngineTransform(layerBase);
        layer.InvokeAfterRender(context);
    }

    /// <summary>
    /// Returns whether a layer contributes anything to a frame. A layer that is not
    /// <see cref="Layer.Visible"/>, or whose <see cref="Layer.Opacity"/> is zero, contributes
    /// nothing and is skipped whole.
    /// </summary>
    /// <param name="layer">The layer to test.</param>
    /// <exception cref="ArgumentNullException"><paramref name="layer"/> is null.</exception>
    public static bool ParticipatesInRender(Layer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return layer.Visible && layer.Opacity != 0f;
    }

    /// <summary>
    /// Returns a layer's base transform: the mapping from the layer's own coordinate space to
    /// device pixels, above which every node's world matrix composes.
    /// </summary>
    /// <param name="layer">The layer to map.</param>
    /// <param name="virtualResolution">
    /// The scene's finite, positive virtual resolution — the extent framed by a layer that occupies
    /// the whole frame, which a viewport'd layer replaces with its own.
    /// </param>
    /// <param name="renderScale">The finite, positive virtual-to-device pixel scale.</param>
    /// <remarks>
    /// <para>
    /// The space mapping is <see cref="Camera2D.WorldToVirtual(in Vector2)"/> for a
    /// <see cref="WorldLayer2D"/> — the one place the Y flip lives — and identity for a
    /// <see cref="ScreenLayer"/>, whose coordinates already are virtual coordinates. Najm composes
    /// row vectors, so the returned value is <c>spaceMapping * scale(renderScale)</c>: a point is
    /// mapped into virtual space first and scaled to device pixels second.
    /// </para>
    /// <para>
    /// The extent a camera frames is the layer's own: a <see cref="Layer.Viewport"/>'d world layer
    /// frames its viewport rather than <paramref name="virtualResolution"/>, and the viewport's
    /// origin then carries that viewport-local mapping back into absolute frame coordinates. Framing
    /// against the frame instead would degrade the viewport into a crop of a frame-centered world.
    /// A <see cref="ScreenLayer"/> has no camera and nothing to reframe, so its viewport stays a
    /// crop of the absolute virtual coordinates its nodes are already written in.
    /// </para>
    /// <para>
    /// The result is always an absolute frame-device transform. A backend staging a viewport'd layer
    /// through a viewport-sized surface subtracts that surface's device origin itself, which is what
    /// keeps the placement 1:1.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="layer"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="virtualResolution"/> or <paramref name="renderScale"/> is not finite and
    /// positive.
    /// </exception>
    public static Matrix3x2 ComputeLayerBase(
        Layer layer,
        in Vector2 virtualResolution,
        float renderScale)
    {
        ArgumentNullException.ThrowIfNull(layer);
        EnsureVirtualResolution(virtualResolution);
        EnsureRenderScale(renderScale);

        var spaceMapping = Matrix3x2.Identity;
        if (layer is WorldLayer2D world)
        {
            spaceMapping = layer.Viewport is { } viewport
                ? world.Camera.WorldToVirtual(new Vector2(viewport.Width, viewport.Height)) *
                    Matrix3x2.CreateTranslation(viewport.X, viewport.Y)
                : world.Camera.WorldToVirtual(virtualResolution);
        }

        return spaceMapping * Matrix3x2.CreateScale(renderScale);
    }

    /// <summary>
    /// Walks one node and its subtree depth-first in paint order, installing each node's engine
    /// transform before its own paint and before its children's.
    /// </summary>
    private static void RenderNode(Node node, IDrawContext2D context, in Matrix3x2 layerBase)
    {
        if (!node.Visible)
        {
            return;
        }

        // Row vectors: the node's world matrix must reach the point before the layer base does,
        // so the node's matrix is the left operand.
        var engineToDevice = node is Node2D spatial
            ? spatial.WorldMatrix * layerBase
            : layerBase;
        context.SetEngineTransform(engineToDevice);
        node.InvokeRender(context);

        for (var childIndex = 0; childIndex < node.ChildCount; childIndex++)
        {
            RenderNode(node.GetChildInPaintOrder(childIndex), context, layerBase);
        }
    }

    private static void EnsureVirtualResolution(in Vector2 virtualResolution)
    {
        if (!float.IsFinite(virtualResolution.X) ||
            !float.IsFinite(virtualResolution.Y) ||
            virtualResolution.X <= 0f ||
            virtualResolution.Y <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(virtualResolution),
                virtualResolution,
                "A virtual resolution must have finite, positive components.");
        }
    }

    private static void EnsureRenderScale(float renderScale)
    {
        if (!float.IsFinite(renderScale) || renderScale <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(renderScale),
                renderScale,
                "A render scale must be finite and positive.");
        }
    }
}
