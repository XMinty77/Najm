using System.Numerics;
using Najm.Utils;

namespace Najm.Core;

/// <summary>Defines one coordinate-space root and its scene-level lifecycle.</summary>
public abstract class Layer
{
    private LayerStack? ownerStack;
    private LayerStack? reservationStack;
    private Scene? attachedScene;
    private Node? runtimeRoot;
    private float opacity = 1f;
    private BlendMode blend = BlendMode.SrcOver;
    private Rect? viewport;

    /// <summary>Gets or sets whether this layer participates in rendering.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// Gets or sets the group alpha applied when this layer merges into the frame, from zero to one
    /// inclusive. The default is one.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite and within zero to one.</exception>
    public float Opacity
    {
        get => opacity;
        set
        {
            if (!float.IsFinite(value) || value < 0f || value > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "Layer opacity must be finite and between zero and one inclusive.");
            }

            opacity = value;
        }
    }

    /// <summary>
    /// Gets or sets the blend operation used when this layer merges into the frame. The default is
    /// <see cref="BlendMode.SrcOver"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a defined blend mode.</exception>
    public BlendMode Blend
    {
        get => blend;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentException("The blend mode is not defined.", nameof(value));
            }

            blend = value;
        }
    }

    /// <summary>
    /// Gets or sets the color this layer's target is cleared to before it renders. The default is
    /// <see cref="Color.Transparent"/>.
    /// </summary>
    public Color ClearColor { get; set; } = Color.Transparent;

    /// <summary>
    /// Gets or sets the virtual-space region this layer occupies, or null — the default — to occupy
    /// the full frame.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The rectangle has zero width or height.</exception>
    public Rect? Viewport
    {
        get => viewport;
        set
        {
            if (value is { IsEmpty: true })
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "A layer viewport must have positive width and height; use null for the full frame.");
            }

            viewport = value;
        }
    }

    /// <summary>Gets whether increasing Y points visually upward in this layer's coordinate space.</summary>
    public virtual bool YAxisPointsUp => false;

    /// <summary>
    /// Gets or sets whether the input router walks this layer's tree. The default is true.
    /// </summary>
    /// <remarks>
    /// <para>
    /// §5.2 counts "input participation" among the things a layer <em>is</em>, alongside its space,
    /// its camera, its root, its target, and its presentation state. This is that property. Turning
    /// it off makes a whole layer input-transparent in one assignment — a decorative backdrop, or a
    /// caption layer that must never steal a click from the world beneath it — without touching a
    /// single node.
    /// </para>
    /// <para>
    /// <strong><see cref="Visible"/> gates input too, and this does not gate rendering.</strong>
    /// §6.1 says an invisible node "skips the subtree for Render <em>and</em> hit-testing", and the
    /// same reading one level up is the only consistent one: an invisible layer is not there to be
    /// clicked. So the router requires both, while the renderer requires only
    /// <see cref="Visible"/>. A layer at zero <see cref="Opacity"/> is a different case and still
    /// receives input: it is present and merely transparent, exactly as a fully faded node is.
    /// </para>
    /// </remarks>
    public bool ReceivesInput { get; set; } = true;

    /// <summary>
    /// Resolves a node in this layer against this layer's camera and viewport, producing the
    /// local↔virtual mapping and the resolved bounds §9.2 routes against.
    /// </summary>
    /// <param name="node">A node attached to this layer.</param>
    /// <remarks>
    /// <para>
    /// This is §6.3's camera-aware query, and the reason it exists on the layer rather than on the
    /// node: pinning and camera framing are <strong>never</strong> baked into
    /// <see cref="Node2D.WorldMatrix"/>, so the effective local→virtual mapping only exists where a
    /// camera does. The router uses it, culling uses it, and so does any tool that needs to know
    /// where a node actually landed — an arrangement helper measuring a pinned label, say.
    /// </para>
    /// <para>
    /// The result stops at virtual coordinates and carries no render scale, so it is the same
    /// answer whether the frame is being drawn at 360p or 4K.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="node"/> does not belong to this layer.</exception>
    /// <exception cref="InvalidOperationException">
    /// This layer belongs to no scene, so there is no virtual resolution to frame against.
    /// </exception>
    public ResolvedNodeFrame Resolve(Node2D node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!RootsHere(node))
        {
            throw new ArgumentException(
                "A node resolves against the layer that owns it, because the mapping is that " +
                "layer's camera and viewport. This node's tree does not root at this layer.",
                nameof(node));
        }

        return Resolve(node, VirtualBase);
    }

    /// <summary>Returns the effective local→virtual matrix for a node in this layer.</summary>
    /// <param name="node">A node attached to this layer.</param>
    /// <inheritdoc cref="Resolve(Node2D)" path="/remarks" />
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="node"/> does not belong to this layer.</exception>
    /// <exception cref="InvalidOperationException">
    /// This layer belongs to no scene, so there is no virtual resolution to frame against.
    /// </exception>
    public Matrix3x2 ResolveMatrix(Node2D node) => Resolve(node).LocalToVirtualMatrix;

    /// <summary>
    /// Returns the virtual-space hull of a node's <see cref="Node2D.VisualBounds"/> under this
    /// layer's camera.
    /// </summary>
    /// <param name="node">A node attached to this layer.</param>
    /// <remarks>
    /// Visual bounds rather than hit bounds, because §6.3 names culling and measurement as this
    /// query's consumers and §6.6 gives culling the visual value. Input gating wants
    /// <see cref="ResolvedNodeFrame.HitBoundsVirtual"/>, which <see cref="Resolve(Node2D)"/>
    /// returns alongside it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="node"/> does not belong to this layer.</exception>
    /// <exception cref="InvalidOperationException">
    /// This layer belongs to no scene, so there is no virtual resolution to frame against.
    /// </exception>
    public Rect ResolveBounds(Node2D node) => Resolve(node).VisualBoundsVirtual;

    /// <summary>Runs when this layer becomes attached to a loaded scene.</summary>
    protected virtual void OnAttach(Scene scene)
    {
    }

    /// <summary>Runs when this layer leaves its loaded scene.</summary>
    protected virtual void OnDetach()
    {
    }

    /// <summary>Updates this layer before its root subtree.</summary>
    protected virtual void Update(in TickContext tick)
    {
    }

    /// <summary>Draws into this layer's target before its root subtree renders.</summary>
    /// <param name="context">The draw context, already in this layer's coordinate space.</param>
    /// <remarks>The context is valid only for the duration of this call.</remarks>
    protected virtual void OnBeforeRender(IDrawContext2D context)
    {
    }

    /// <summary>Draws into this layer's target after its root subtree renders.</summary>
    /// <param name="context">The draw context, already in this layer's coordinate space.</param>
    /// <remarks>The context is valid only for the duration of this call.</remarks>
    protected virtual void OnAfterRender(IDrawContext2D context)
    {
    }

    /// <summary>Gets the non-null, identity-stable permanent root used by this layer implementation.</summary>
    /// <remarks>The base class establishes root ownership when the layer first enters runtime use.</remarks>
    protected abstract Node RootNode { get; }

    internal Node RuntimeRoot
    {
        get
        {
            var current = RootNode;
            if (current is null)
            {
                throw new InvalidOperationException("A layer must expose a non-null permanent root node.");
            }

            if (runtimeRoot is null)
            {
                current.AssignLayerRoot(this);
                runtimeRoot = current;
            }
            else if (!ReferenceEquals(runtimeRoot, current))
            {
                throw new InvalidOperationException("A layer's permanent root node identity cannot change.");
            }

            return runtimeRoot;
        }
    }

    internal Node EstablishedRuntimeRoot => runtimeRoot ?? RuntimeRoot;

    /// <summary>
    /// Gets the scene this layer answers to — attached, added, or merely reserved by a pending add.
    /// </summary>
    /// <remarks>
    /// The three-step fallback is what makes a camera query answer the same before load as after
    /// it, so an author who builds a scene's layers in a constructor and frames them there gets the
    /// mapping the render will use.
    /// </remarks>
    internal Scene? ResolvedScene => attachedScene ?? ownerStack?.Owner ?? reservationStack?.Owner;

    /// <summary>Gets this layer's local→virtual base transform: the camera and viewport, no render scale.</summary>
    internal Matrix3x2 VirtualBase =>
        RenderTraverser.ComputeLayerBase(
            this,
            ResolvedScene?.VirtualResolution ??
                throw new InvalidOperationException(
                    "Resolving a node against a camera needs the scene's VirtualResolution, and " +
                    "this layer belongs to no scene. Add the layer to a scene first."),
            renderScale: 1f);

    /// <summary>
    /// Returns whether a node's tree roots at this layer, which is what makes this layer's camera
    /// the right one to resolve it against.
    /// </summary>
    /// <remarks>
    /// Structure rather than <see cref="Node.Layer"/>, which is only populated from attach: an
    /// author framing a scene's layers in a constructor gets the same answer the render will use,
    /// which is the promise <see cref="WorldLayer2D.FitRect(in Rect)"/> already makes.
    /// </remarks>
    private bool RootsHere(Node node)
    {
        var current = node;
        while (current.Parent is { } parent)
        {
            current = parent;
        }

        return ReferenceEquals(current, EstablishedRuntimeRoot);
    }

    /// <summary>Resolves a node against a base transform this layer already computed.</summary>
    /// <remarks>
    /// The router walks hundreds of nodes under one camera, so the base is computed once per layer
    /// and threaded through rather than rebuilt per node.
    /// </remarks>
    internal static ResolvedNodeFrame Resolve(Node2D node, in Matrix3x2 virtualBase) =>
        new(node.WorldMatrix * virtualBase, node.HitBounds, node.VisualBounds);

    internal LayerStack? OwnerStack => ownerStack;

    internal LayerStack? ReservationStack => reservationStack;

    internal Scene? AttachedScene => attachedScene;

    internal void SetOwnerStack(LayerStack? value) => ownerStack = value;

    internal void SetReservationStack(LayerStack? value) => reservationStack = value;

    internal void SetAttachedScene(Scene? value) => attachedScene = value;

    internal void InvokeAttach(Scene scene) => OnAttach(scene);

    internal void InvokeDetach() => OnDetach();

    internal void InvokeUpdate(in TickContext tick) => Update(tick);

    internal void InvokeBeforeRender(IDrawContext2D context) => OnBeforeRender(context);

    internal void InvokeAfterRender(IDrawContext2D context) => OnAfterRender(context);
}
