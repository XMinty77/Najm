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
/// author state — an outstanding push would make the next engine transform illegal. Where the walk
/// does need a bracket, it uses the engine's own:
/// <see cref="IDrawContext2D.BeginLayerBracket(in LayerBracket)"/> around a layer,
/// <see cref="IDrawContext2D.BeginUnitBracket(in UnitBracket)"/> around an isolating node's
/// subtree, and <see cref="IDrawContext2D.BeginClipBracket(in ClipBracket)"/> around a clipped one,
/// whose depths are tracked apart from author state precisely so a per-node transform can be
/// installed inside them.
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
    /// <para>
    /// This is the direct path's walk: no per-layer target is bound, and each participating layer's
    /// presentation is carried by an engine layer bracket instead — its clear, viewport, opacity,
    /// and blend, which is everything the compositor would have applied by staging the layer and
    /// merging it. The bracket is what keeps the two paths from drifting; without it a layer at half
    /// opacity would render fully opaque here and half-opaque through the compositor.
    /// </para>
    /// <para>
    /// Layers that do not participate — see <see cref="ParticipatesInRender(Layer)"/> — are skipped
    /// whole: no bracket, no clear, no walk, no hooks.
    /// </para>
    /// <para>
    /// Node-tier isolation happens inside the walk, one bracket per isolating node — see
    /// <see cref="Node2D.RequiresIsolation"/> — and nests inside the layer bracket. The M2 terms of
    /// §6.7's predicate, a mask, an effect, or a backdrop read, are not implemented and are not
    /// approximated. The rectangular case of <c>Clip</c> is implemented, on a bracket of its own
    /// that bounds without isolating.
    /// </para>
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
            var layer = layers[index];
            if (!ParticipatesInRender(layer))
            {
                continue;
            }

            context.BeginLayerBracket(
                new LayerBracket(
                    layer.ClearColor,
                    layer.Opacity,
                    layer.Blend,
                    ComputeDeviceViewport(layer, renderScale)));
            RenderLayer(layer, context, virtualResolution, renderScale);

            // Not in a finally, for the reason the compositor does not end its pass in one either:
            // a walk that threw has already lost the frame, and closing the bracket here would
            // replace the author's exception with whatever the half-drawn group produced. The
            // context reports the unbalanced bracket when the pass ends.
            context.EndLayerBracket();
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
    /// <para>
    /// A layer that does not participate in rendering is skipped whole: its hooks do not run and
    /// its tree is not walked. Both hooks see the context in layer space, so the layer base is
    /// reinstalled after the walk before <c>OnAfterRender</c> runs.
    /// </para>
    /// <para>
    /// This walks one layer's contents and applies none of its presentation. A caller that binds a
    /// target per layer applies clear, opacity, blend, and placement at its own merge — see
    /// <c>ICompositor</c> — and a caller sharing one context opens an engine layer bracket around
    /// this call, which is what <see cref="RenderLayers(LayerStack, IDrawContext2D, in Vector2, float)"/>
    /// does. Doing both would apply the layer's presentation twice.
    /// </para>
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
    /// Returns the device-pixel region a layer's bracket clips to and fills, or null when the layer
    /// occupies the whole frame.
    /// </summary>
    /// <remarks>
    /// The origin is rounded to the pixel grid and the extent is rounded outward, which is exactly
    /// how the compositor sizes and places the surface it stages a viewport'd layer through. Using
    /// the same rounding means a fractional viewport covers the same pixels on both paths instead of
    /// producing a clip edge on one and a surface edge on the other.
    /// </remarks>
    private static Rect? ComputeDeviceViewport(Layer layer, float renderScale)
    {
        if (layer.Viewport is not { } viewport)
        {
            return null;
        }

        return new Rect(
            MathF.Round(viewport.X * renderScale),
            MathF.Round(viewport.Y * renderScale),
            MathF.Ceiling(viewport.Width * renderScale),
            MathF.Ceiling(viewport.Height * renderScale));
    }

    /// <summary>
    /// Walks one node and its subtree depth-first in paint order, installing each node's engine
    /// transform before its own paint and before its children's, and bracketing the subtree as one
    /// compositing unit where §6.7's isolation predicate demands it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A bracket opens <em>before</em> the node's own engine transform is installed and closes
    /// after the last descendant, so it spans exactly the node's emitted content plus its subtree —
    /// §6.7's compositing unit, no more and no less. Opening one discards the engine transform the
    /// previous sibling left installed, which is why the transform is set after the bracket rather
    /// than before it, and why a <see cref="Node2D.Clip"/> — stated in the node's own local
    /// coordinates — travels into its bracket alongside the mapping it is read under. That
    /// bracketing is exactly what a leaf-level <see cref="IDrawContext2D.PushClip(in Rect)"/>
    /// cannot do: the clip bounds the subtree, and no descendant can push its way back out of it.
    /// </para>
    /// <para>
    /// A clip and an isolating unit are two brackets, not one, because §6.7's table says clip state
    /// alone does not isolate: a clip-only node opens a <see cref="ClipBracket"/> and stages no
    /// offscreen, so a descendant's non-default <see cref="Node2D.Blend"/> still composites against
    /// what lies beneath the clipping node rather than against a scope the clip invented. A node
    /// that does both opens the clip outside the unit, matching §6.7's semantic order, so the clip
    /// bounds what the unit's group captures.
    /// </para>
    /// <para>
    /// The overwhelmingly common node neither clips nor isolates.
    /// <see cref="Node2D.RequiresIsolation"/> is a field test, both brackets are structs passed by
    /// reference, and a node at default opacity, default blend, and no clip therefore reaches its
    /// paint through exactly the calls it reached them through before this existed.
    /// </para>
    /// </remarks>
    private static void RenderNode(Node node, IDrawContext2D context, in Matrix3x2 layerBase)
    {
        if (!node.Visible)
        {
            return;
        }

        // Row vectors: the node's world matrix must reach the point before the layer base does,
        // so the node's matrix is the left operand.
        var spatial = node as Node2D;
        var engineToDevice = spatial is not null
            ? spatial.WorldMatrix * layerBase
            : layerBase;

        // Clip outside unit, which is §6.7's semantic order — clip, then render node and children,
        // then composite with opacity and blend — so the clip bounds what an isolating node's group
        // captures rather than being applied inside it. A node that only clips opens only the clip
        // bracket and stages no offscreen, because clip state alone does not isolate.
        // Read once, so the bracket that closes is the bracket that opened however the tree is
        // mutated mid-walk.
        var clip = spatial?.Clip;
        if (clip is { } bounds)
        {
            // The clip is stated in the node's local coordinates and the bracket opens before that
            // node's engine transform is installed, so the mapping it is read under travels with it.
            context.BeginClipBracket(new ClipBracket(bounds, engineToDevice));
        }

        var isolates = spatial is not null && spatial.RequiresIsolation;
        if (isolates)
        {
            context.BeginUnitBracket(new UnitBracket(spatial!.Opacity, spatial.Blend));
        }

        context.SetEngineTransform(engineToDevice);
        node.InvokeRender(context);

        for (var childIndex = 0; childIndex < node.ChildCount; childIndex++)
        {
            RenderNode(node.GetChildInPaintOrder(childIndex), context, layerBase);
        }

        // Not in a finally, for the reason RenderLayers does not close its layer bracket in one:
        // a walk that threw has already lost the frame, and closing here would replace the author's
        // exception with whatever the half-drawn unit produced. The context reports the unbalanced
        // bracket when the pass ends.
        if (isolates)
        {
            context.EndUnitBracket();
        }
        if (clip is not null)
        {
            context.EndClipBracket();
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
