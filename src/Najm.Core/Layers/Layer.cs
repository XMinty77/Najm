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
