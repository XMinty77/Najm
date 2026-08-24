using System.Numerics;
using Najm.Core;
using Najm.Core.Text;
using Najm.Utils;

namespace Najm.Lib;

/// <summary>A drawable that shows one string in one style.</summary>
/// <remarks>
/// <para>
/// NAJM-TEXT I.10. This node <strong>never shapes anything</strong> — it cannot, because
/// <c>Najm.Lib</c> references Core alone and the only text-shaped things Core exposes are the
/// environment's <see cref="ITypesetter"/> capability and the Tier-1
/// <see cref="IDrawContext2D.DrawText"/> op. Everything below is bookkeeping around those two: when
/// to ask for a layout, where to put the one it gets, and which way is up.
/// </para>
/// <para>
/// <strong>Laziness.</strong> Writing a property sets a dirty flag and nothing else; the layout is
/// built on the first <em>read</em> — bounds, render, or query. A five-property setup therefore
/// costs one typeset rather than five, which is §4.1's benign memoization doing the work an
/// eager setter would have wasted.
/// </para>
/// <para>
/// <strong>Size versus scale.</strong> <see cref="Size"/> is a typesetting input and a cache key;
/// the documented idiom for growing or shrinking text is <see cref="Node2D.Scale"/>. See
/// <see cref="Size"/> for why.
/// </para>
/// <para>
/// <strong>Color is free.</strong> <see cref="Color"/> is a draw-time override, not part of the
/// request, so recoloring — including a color tween — re-typesets nothing and adds no cache entry.
/// </para>
/// </remarks>
public class TextNode : Drawable
{
    /// <summary>
    /// The em size a node that sets none is drawn at, in local units.
    /// </summary>
    /// <remarks>
    /// A base style has to resolve <em>some</em> size or the typesetter fails loudly, and a node is
    /// the layer that supplies one. Thirty-two virtual units is a readable label against the default
    /// 1920×1080 virtual resolution — roughly a slide's body text — which makes a bare
    /// <c>new TextNode { Text = "…" }</c> visible rather than a puzzle.
    /// </remarks>
    public const float DefaultSize = 32f;

    private string text = string.Empty;
    private string? family;
    private float size = DefaultSize;
    private FontWeight weight = FontWeight.Normal;
    private FontSlant slant = FontSlant.Upright;
    private float letterSpacing;
    private FontFeatures? features;
    private string? language;
    private TextAlign align = TextAlign.Left;
    private float lineSpacing = 1f;
    private TextAnchor anchor = TextAnchor.BaselineLeft;

    private ITypesetter? typesetter;
    private ITextLayout? layout;
    private Matrix3x2 readingToLocal = Matrix3x2.Identity;
    private Rect geometryBounds;
    private Rect visualBounds;
    private float readingFlip = 1f;
    private bool dirty = true;

    /// <summary>Creates an empty text node.</summary>
    public TextNode()
    {
    }

    /// <summary>Creates a text node showing one string.</summary>
    /// <param name="text">The content. See <see cref="Text"/>.</param>
    public TextNode(string text) => Text = text;

    /// <summary>Gets or sets the string to draw. The default is empty.</summary>
    /// <remarks>
    /// <c>\n</c>, <c>\r\n</c>, and <c>\r</c> break the line. Nothing else does: automatic wrapping
    /// is deferred, and asking for it through <c>MaxWidth</c> is refused loudly rather than ignored.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public string Text
    {
        get => text;
        set => Set(ref text, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Gets or sets the registered family name, or null for the typesetter's default.</summary>
    public string? Family
    {
        get => family;
        set => Set(ref family, value);
    }

    /// <summary>Gets or sets the em size in local units. The default is <see cref="DefaultSize"/>.</summary>
    /// <remarks>
    /// <para>
    /// <strong>Size is a typesetting input and a cache-key component</strong> (NAJM-TEXT I.10,
    /// ARCHITECTURE §12.3): changing it re-shapes nothing — shaping is size-independent — but it
    /// does re-lay-out and does add a layout cache entry.
    /// </para>
    /// <para>
    /// <strong>The documented idiom for grow and shrink animation is
    /// <see cref="Node2D.Scale"/>, not this.</strong> Scaling is vector-crisp, visually identical,
    /// and costs no relayout at all; a size tween re-typesets on every frame it runs. A size tween
    /// still works and is sometimes what you want — mixed-size text that must stay on a shared
    /// baseline, for instance — but the transition counter on the debug overlay is where the
    /// difference between the two shows up, and it will show up.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite and positive.</exception>
    public float Size
    {
        get => size;
        set
        {
            if (!float.IsFinite(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A text size must be finite and positive.");
            }

            Set(ref size, value);
        }
    }

    /// <summary>Gets or sets the weight. The default is <see cref="FontWeight.Normal"/>.</summary>
    /// <exception cref="ArgumentException">The value is not a defined weight.</exception>
    public FontWeight Weight
    {
        get => weight;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentException("The font weight is not defined.", nameof(value));
            }

            Set(ref weight, value);
        }
    }

    /// <summary>Gets or sets the slant. The default is <see cref="FontSlant.Upright"/>.</summary>
    /// <exception cref="ArgumentException">The value is not a defined slant.</exception>
    public FontSlant Slant
    {
        get => slant;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentException("The font slant is not defined.", nameof(value));
            }

            Set(ref slant, value);
        }
    }

    /// <summary>Gets or sets whether the text is bold, which is <see cref="FontWeight.Bold"/>.</summary>
    /// <remarks>Sugar over <see cref="Weight"/>; setting it false returns to <see cref="FontWeight.Normal"/>.</remarks>
    public bool Bold
    {
        get => weight == FontWeight.Bold;
        set => Weight = value ? FontWeight.Bold : FontWeight.Normal;
    }

    /// <summary>Gets or sets whether the text is italic.</summary>
    /// <remarks>Sugar over <see cref="Slant"/>; setting it false returns to <see cref="FontSlant.Upright"/>.</remarks>
    public bool Italic
    {
        get => slant == FontSlant.Italic;
        set => Slant = value ? FontSlant.Italic : FontSlant.Upright;
    }

    /// <summary>Gets or sets extra tracking in local units. The default is zero.</summary>
    /// <remarks>Applied post-shape, at cluster boundaries only, so it never opens a ligature.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite.</exception>
    public float LetterSpacing
    {
        get => letterSpacing;
        set
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Letter spacing must be finite.");
            }

            Set(ref letterSpacing, value);
        }
    }

    /// <summary>Gets or sets the OpenType features, or null for the defaults (ligatures on).</summary>
    public FontFeatures? Features
    {
        get => features;
        set => Set(ref features, value);
    }

    /// <summary>Gets or sets the BCP-47 language this text shapes as, or null for the default.</summary>
    public string? Language
    {
        get => language;
        set => Set(ref language, value);
    }

    /// <summary>Gets or sets the wrapping width. Setting anything but null throws.</summary>
    /// <remarks>
    /// <para>
    /// <strong>This property exists in order to refuse.</strong> NAJM-TEXT I.10 lists
    /// <c>MaxWidth</c> among a text node's properties and this slice does not implement it —
    /// automatic line breaking is the UAX #14 stage (II.7). Leaving the property off entirely would
    /// have been quieter, but an author following the design would then hit a compile error naming
    /// nothing, and an author following an example would hit one naming a missing member. Leaving
    /// it on and ignoring it is the one thing that is definitely wrong: the text would simply not
    /// wrap, and there is no way to tell "the value was ignored" from "the value was wrong".
    /// </para>
    /// <para>
    /// So it fails at the property set, with a position in the author's own code and a message that
    /// says what to do instead — VI.3's rule that author mistakes fail loud, applied at the earliest
    /// moment it can be applied. When wrapping lands, this becomes an ordinary property and nothing
    /// that compiles today changes meaning.
    /// </para>
    /// </remarks>
    /// <exception cref="NotSupportedException">The value is not null.</exception>
    public float? MaxWidth
    {
        get => null;
        set
        {
            if (value is null)
            {
                return;
            }

            throw new NotSupportedException(
                $"TextNode.MaxWidth was set to {value.Value}, but this engine breaks lines only at " +
                "hard newlines: automatic line breaking (UAX #14) is not implemented yet. Put '\n' " +
                "where the line should break. The value is refused here rather than ignored, so that " +
                "text which does not wrap can never be mistaken for a wrapping bug.");
        }
    }

    /// <summary>Gets or sets how lines sit in the alignment box. The default is <see cref="TextAlign.Left"/>.</summary>
    /// <exception cref="ArgumentException">The value is not a defined alignment.</exception>
    public TextAlign Align
    {
        get => align;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentException("The text alignment is not defined.", nameof(value));
            }

            Set(ref align, value);
        }
    }

    /// <summary>Gets or sets the leading multiplier. The default is one.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite and positive.</exception>
    public float LineSpacing
    {
        get => lineSpacing;
        set
        {
            if (!float.IsFinite(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Line spacing must be finite and positive.");
            }

            Set(ref lineSpacing, value);
        }
    }

    /// <summary>
    /// Gets or sets which point of the layout sits at this node's origin. The default is
    /// <see cref="TextAnchor.BaselineLeft"/>.
    /// </summary>
    /// <remarks>
    /// The anchor is a node-side offset over the finished layout and takes no part in typesetting
    /// (NAJM-TEXT I.9), so changing it moves the text without re-laying it out and without adding a
    /// cache entry. Twelve nodes at twelve anchors showing the same string share one layout.
    /// </remarks>
    /// <exception cref="ArgumentException">The value is not a defined anchor.</exception>
    public TextAnchor Anchor
    {
        get => anchor;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentException("The text anchor is not defined.", nameof(value));
            }

            if (anchor == value)
            {
                return;
            }

            anchor = value;

            // The layout is unaffected — only where it sits is — so this invalidates the placement
            // without discarding what was typeset.
            if (layout is not null)
            {
                Place(layout);
            }

            InvalidateBounds();
        }
    }

    /// <summary>Gets or sets the uniform fill color. The default is <see cref="Utils.Color.Black"/>.</summary>
    /// <remarks>
    /// This is a <em>draw-time</em> override (NAJM-TEXT I.4), not part of the typeset request, so
    /// recoloring costs nothing: a color tween runs for a hundred frames and leaves the layout cache
    /// exactly as it found it.
    /// </remarks>
    public Color Color { get; set; } = Color.Black;

    /// <summary>Gets the layout this node currently draws, building it if a property changed.</summary>
    /// <remarks>
    /// Reading this is one of the three things that forces the build — the others are reading bounds
    /// and rendering. The instance is shared with every other node asking for the same content in the
    /// same style, and is immutable.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The node is not attached to a loaded scene.</exception>
    public ITextLayout Layout => EnsureLayout();

    /// <summary>
    /// Gets the transform mapping the layout's reading frame into this node's local coordinates.
    /// </summary>
    /// <remarks>
    /// This is the anchor offset composed with the upright rule's flip — see <see cref="Render"/>.
    /// A caller working with <see cref="ITextLayout.Positions"/> directly, or baking the layout's
    /// outlines for a clip, applies this to get into the node's space.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The node is not attached to a loaded scene.</exception>
    public Matrix3x2 ReadingToLocal
    {
        get
        {
            EnsureLayout();
            return readingToLocal;
        }
    }

    /// <summary>Gets whether this node's local space has +y pointing visually upward.</summary>
    /// <remarks>
    /// Read from <see cref="Layer.YAxisPointsUp"/> at attach — a layer fact, not a camera fact, which
    /// is what keeps bounds camera-free by construction (§6.6). A detached node reports false and
    /// lays out in the unflipped reading frame.
    /// </remarks>
    public bool YAxisPointsUp => readingFlip < 0f;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The node is not attached to a loaded scene.</exception>
    public override Rect GeometryBounds
    {
        get
        {
            EnsureLayout();
            return geometryBounds;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Ink rather than metrics: an italic's overhang and a tall accent both paint outside the logical
    /// box, and §6.6 asks a visual bound to be conservative.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The node is not attached to a loaded scene.</exception>
    public override Rect VisualBounds
    {
        get
        {
            EnsureLayout();
            return visualBounds;
        }
    }

    /// <summary>Gets one line's baseline position in this node's local coordinates.</summary>
    /// <param name="line">The zero-based line index.</param>
    /// <returns>The point where that line's baseline meets the alignment box's left edge, in local space.</returns>
    /// <remarks>
    /// The point an alignment helper wants: it already carries the anchor offset and the upright
    /// rule, so placing a rule under line 2 is this value plus a local-space offset, in whichever
    /// direction the layer's visual up happens to be.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="line"/> is outside the layout.</exception>
    /// <exception cref="InvalidOperationException">The node is not attached to a loaded scene.</exception>
    public Vector2 Baseline(int line)
    {
        var current = EnsureLayout();
        var metrics = current.Line(line);
        return Vector2.Transform(new Vector2(current.LogicalBounds.Left, metrics.Baseline), readingToLocal);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <strong>The upright rule, realized (NAJM-TEXT I.9).</strong> Fonts and Skia are Y-down;
    /// <see cref="WorldLayer2D"/> is Y-up with the flip living in the camera. Drawing a layout
    /// straight into a world layer would therefore render every glyph mirrored, because the camera's
    /// flip is still to come. The fix is not to flip the glyphs but to state where the reading frame
    /// sits: this node composes the reading frame into its local space, and in a Y-up layer that
    /// composition is itself a flip, which the camera's flip then cancels.
    /// </para>
    /// <para>
    /// The consequences are the ones the design states plainly, and they are observable through
    /// <see cref="GeometryBounds"/>: in a world layer ascenders extend toward <c>+y</c> and line 2
    /// stacks toward <c>−y</c>; in a screen layer the reverse; hit boxes live in the same corrected
    /// frame. An author's own <c>Scale(1, −1)</c> composes below this one and still mirrors the text,
    /// deliberately — the rule corrects for the layer, not for the author.
    /// </para>
    /// </remarks>
    public override void Render(IDrawContext2D context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var current = EnsureLayout();
        if (current.Runs.Length == 0)
        {
            return;
        }

        context.PushTransform(readingToLocal);
        try
        {
            context.DrawText(current, Color);
        }
        finally
        {
            context.PopTransform();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The layer's y-axis orientation is read here, once. It is a layer fact rather than a camera
    /// fact, so nothing about panning, zooming, or swapping cameras can change it, and bounds stay
    /// camera-free (§6.6).
    /// </remarks>
    protected override void OnAttach()
    {
        typesetter = Scene?.Env.Typesetter;
        readingFlip = Layer?.YAxisPointsUp == true ? -1f : 1f;
        Invalidate();
    }

    /// <inheritdoc />
    protected override void OnDetach()
    {
        typesetter = null;
        layout = null;
        readingFlip = 1f;
        Invalidate();
    }

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        Invalidate();
    }

    /// <summary>Marks the layout stale without building anything.</summary>
    /// <remarks>
    /// This is the whole of laziness: a property write costs a comparison, a field store, and a flag.
    /// The typeset happens at the next read, so setting five properties in a row typesets once.
    /// </remarks>
    private void Invalidate()
    {
        dirty = true;
        layout = null;
        InvalidateBounds();
    }

    private ITextLayout EnsureLayout()
    {
        if (!dirty && layout is { } cached)
        {
            return cached;
        }

        var owner = typesetter ?? throw new InvalidOperationException(
            "A TextNode measures and draws through its scene's ITypesetter, which it resolves when " +
            "it attaches — so its layout, bounds, and baselines are available only once it is part " +
            "of a loaded scene. Add it to a layer first. (If it is attached and you are seeing this, " +
            "the scene's environment carries Core's NullTypesetter: inject Najm.Text.Typesetter.)");

        var request = new TypesetRequest(text, BuildStyle())
        {
            Align = align,
            LineSpacing = lineSpacing,
            Language = language,
        };

        var built = owner.Typeset(request);
        layout = built;
        dirty = false;
        Place(built);
        return built;
    }

    private Style BuildStyle() => new()
    {
        Family = family,
        Weight = weight,
        Slant = slant,
        Size = size,
        LetterSpacing = letterSpacing == 0f ? null : letterSpacing,
        Features = features,
        Lang = language,
    };

    /// <summary>
    /// Recomputes the reading-to-local transform and the bounds it puts the layout's boxes in.
    /// </summary>
    /// <remarks>
    /// Anchoring first, then the flip: the anchor is resolved in the reading frame with its own
    /// Y-down sense, so <see cref="TextAnchor.TopCenter"/> names the top of the text as it reads, and
    /// the flip carries that meaning through unchanged into a Y-up layer.
    /// </remarks>
    private void Place(ITextLayout current)
    {
        var origin = AnchorOrigin(current);
        readingToLocal = Matrix3x2.CreateTranslation(-origin) * Matrix3x2.CreateScale(1f, readingFlip);
        geometryBounds = Project(current.LogicalBounds, readingToLocal);
        visualBounds = current.InkBounds.IsEmpty
            ? geometryBounds
            : Project(current.InkBounds, readingToLocal);
    }

    /// <summary>Finds the reading-frame point the anchor names.</summary>
    private Vector2 AnchorOrigin(ITextLayout current)
    {
        var box = current.LogicalBounds;
        var x = anchor switch
        {
            TextAnchor.BaselineLeft or TextAnchor.TopLeft or TextAnchor.CenterLeft or TextAnchor.BottomLeft =>
                box.Left,
            TextAnchor.BaselineCenter or TextAnchor.TopCenter or TextAnchor.Center or TextAnchor.BottomCenter =>
                box.Left + (box.Width * 0.5f),
            _ => box.Right,
        };
        var y = anchor switch
        {
            // Baseline anchors reference the first line's baseline, which the reading frame puts at
            // y = 0 by construction — that is what makes them baseline-true across mixed sizes.
            TextAnchor.BaselineLeft or TextAnchor.BaselineCenter or TextAnchor.BaselineRight => 0f,
            TextAnchor.TopLeft or TextAnchor.TopCenter or TextAnchor.TopRight => box.Top,
            TextAnchor.CenterLeft or TextAnchor.Center or TextAnchor.CenterRight => box.Top + (box.Height * 0.5f),
            _ => box.Bottom,
        };

        return new Vector2(x, y);
    }

    /// <summary>Maps one reading-frame rectangle into local space.</summary>
    /// <remarks>
    /// <para>
    /// Two corners suffice rather than four: the transform is an axis-preserving translate and
    /// y-flip, never a rotation, so the mapped corners are still the extremes — they have only
    /// possibly swapped which is which, which the min/max below settles.
    /// </para>
    /// <para>
    /// A rectangle covering no area collapses to <c>default</c>, matching what
    /// <see cref="Node2D"/> does to its own subtree aggregates: an empty contribution must not carry
    /// a position an ancestor could mistake for one. Empty text is the case that reaches this — its
    /// logical box keeps the face's height but has zero width — and the height it loses here is
    /// still readable through <see cref="Layout"/> and <see cref="Baseline"/>, which is where a
    /// caret or an underline wants it anyway.
    /// </para>
    /// </remarks>
    private static Rect Project(in Rect source, in Matrix3x2 transform)
    {
        if (source.IsEmpty)
        {
            return default;
        }

        var topLeft = Vector2.Transform(new Vector2(source.Left, source.Top), transform);
        var bottomRight = Vector2.Transform(new Vector2(source.Right, source.Bottom), transform);
        var left = MathF.Min(topLeft.X, bottomRight.X);
        var top = MathF.Min(topLeft.Y, bottomRight.Y);
        return new Rect(left, top, MathF.Abs(bottomRight.X - topLeft.X), MathF.Abs(bottomRight.Y - topLeft.Y));
    }
}
