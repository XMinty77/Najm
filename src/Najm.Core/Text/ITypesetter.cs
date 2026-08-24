namespace Najm.Core.Text;

/// <summary>Measures, shapes, and lays out every piece of text the engine draws.</summary>
/// <remarks>
/// <para>
/// <strong>The authority pin (NAJM-TEXT I.1).</strong> All text in the engine measures, shapes, and
/// lays out through this interface, then draws through the Tier-1
/// <see cref="IDrawContext2D.DrawText"/> primitive. <em>Nodes never shape. Contexts never
/// shape.</em> That is forced before it is chosen: text nodes live in <c>Najm.Lib</c>, which
/// references Core only, so the only text-shaped things they can reach are this capability and that
/// draw op. It is then also wanted — one owner for the content-hash caches, one determinism story,
/// one thing a host injects, and one seam a decorator wraps.
/// </para>
/// <para>
/// <strong>Three-way split.</strong> Core owns the model — this interface,
/// <see cref="TypesetRequest"/>, <see cref="ITextLayout"/> and the run vocabulary,
/// <see cref="FontFace"/>, <see cref="FontFamily"/>, <see cref="Style"/>. <c>Najm.Text</c> owns
/// production: style resolution, itemization, shaping, line layout, and every cache.
/// <c>Najm.Skia</c> owns lowering: blobs, glyph paths, paints, and export behaviour.
/// </para>
/// <para>
/// <strong>Affinity.</strong> One typesetter per environment, render-thread affine like every other
/// engine service. Calls arrive from the frame thread or the load phase; there is no internal
/// locking.
/// </para>
/// <para>
/// <see cref="NullTypesetter"/> is what an environment holds until a real one is injected, and it
/// throws from every member naming both the option to set and the assembly that supplies the
/// implementation.
/// </para>
/// </remarks>
public interface ITypesetter
{
    /// <summary>Registers a family, replacing any previously registered under the same name.</summary>
    /// <param name="family">The family and its faces.</param>
    /// <remarks>
    /// Registration is a load-phase act. Replacing a family that existing layouts were built against
    /// does not invalidate them — they hold their faces directly — but every later typeset resolves
    /// through the new one.
    /// </remarks>
    void RegisterFamily(FontFamily family);

    /// <summary>Sets the families a style cascade falls back to for text and for mathematics.</summary>
    /// <param name="textFamily">The registered family name used when a style resolves no text family.</param>
    /// <param name="mathFamily">The registered family name used when a style resolves no math family.</param>
    /// <exception cref="ArgumentException">Either name is not registered.</exception>
    void SetDefaultFamilies(string textFamily, string mathFamily);

    /// <summary>Reads one face's vertical metrics at one size, in local units.</summary>
    /// <param name="face">The face to measure.</param>
    /// <param name="size">The finite positive em size, in local units.</param>
    /// <returns>Ascent, descent, and line gap as positive magnitudes at that size.</returns>
    FontMetrics Metrics(FontFace face, float size);

    /// <summary>Produces the layout for one request.</summary>
    /// <param name="request">The content, style, and constraints.</param>
    /// <returns>An immutable layout, possibly shared with every other caller that asked for the same thing.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Pure compute, and content-hash cached.</strong> Nothing here touches I/O — fonts
    /// arrived through registration — so a typeset is legal in <c>OnLoad</c>, in <c>Update</c>, and
    /// inside a coroutine alike. Two identical requests return the <em>same instance</em>; that is
    /// the dedup the whole cache design exists for, and it is why the request carries no anchor.
    /// </para>
    /// <para>
    /// The first typeset of new content is a permitted content transition and may allocate. Steady
    /// state re-reads a cached handle and allocates nothing.
    /// </para>
    /// </remarks>
    /// <exception cref="NotSupportedException">
    /// The request asks for something this typesetter does not implement; see
    /// <see cref="TypesetRequest.Validate"/>.
    /// </exception>
    ITextLayout Typeset(in TypesetRequest request);
}
