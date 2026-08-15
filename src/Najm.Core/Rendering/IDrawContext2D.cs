using System.Numerics;
using Najm.Utils;

namespace Najm.Core;

/// <summary>Defines the backend-neutral Tier-1 drawing surface used by portable drawables.</summary>
/// <remarks>
/// A render target owns and reuses its context. Callers must not dispose or retain it beyond the
/// target's lifetime. Geometry and paint values are consumed synchronously and are not retained.
/// This interface is pre-release; its contract will be completed before external package
/// publication.
/// </remarks>
public interface IDrawContext2D
{
    /// <summary>Gets the normalized specification of the context's current target.</summary>
    SurfaceSpec SurfaceSpec { get; }

    /// <summary>Gets backend capabilities available on the current target.</summary>
    RenderCaps Caps { get; }

    /// <summary>Gets the finite positive physical-pixel scale installed by the render driver.</summary>
    float RenderScale { get; }

    /// <summary>
    /// Gets the current local-to-virtual geometric-mean scale, including pushed transforms.
    /// </summary>
    /// <remarks>
    /// The value is <c>sqrt(abs(det(current linear transform))) / RenderScale</c>. A singular finite
    /// transform has zero scale.
    /// </remarks>
    float Scale { get; }

    /// <summary>
    /// Replaces pixels inside the current clip with a tagged sRGB color, ignoring the transform.
    /// </summary>
    void Clear(Color color);

    /// <summary>Fills or strokes a backend-neutral path.</summary>
    /// <param name="path">The path geometry and fill rule.</param>
    /// <param name="paint">The fill or stroke descriptor.</param>
    void DrawPath(PathBuilder path, in Paint paint);

    /// <summary>Draws an immutable image through an affine mapping.</summary>
    /// <param name="image">The borrowed source image, consumed synchronously.</param>
    /// <param name="imageToLocal">
    /// Maps the image's top-left pixel-edge rectangle <c>[0, Width] × [0, Height]</c> into the
    /// context's current local coordinates.
    /// </param>
    /// <param name="sampling">The portable source sampling mode.</param>
    void DrawImage(
        IImage image,
        in Matrix3x2 imageToLocal,
        ImageSampling sampling = ImageSampling.Linear);

    /// <summary>Saves state and composes a finite local transform below the engine transform.</summary>
    void PushTransform(in Matrix3x2 localTransform);

    /// <summary>Restores the most recently pushed transform.</summary>
    /// <exception cref="InvalidOperationException">
    /// The stack is empty or a different state kind was pushed more recently. The stack is not
    /// changed when this exception is thrown.
    /// </exception>
    void PopTransform();

    /// <summary>Saves state and intersects the current clip with an antialiased rectangle.</summary>
    void PushClip(in Rect bounds);

    /// <summary>
    /// Saves state and intersects the current clip with an antialiased path using its fill rule.
    /// </summary>
    /// <remarks>The mutable path is consumed synchronously and is not retained.</remarks>
    void PushClip(PathBuilder path);

    /// <summary>Restores the most recently pushed rectangle or path clip.</summary>
    /// <exception cref="InvalidOperationException">
    /// The stack is empty or a different state kind was pushed more recently. The stack is not
    /// changed when this exception is thrown.
    /// </exception>
    void PopClip();

    /// <summary>Saves state and begins a true group-opacity layer.</summary>
    /// <param name="opacity">A finite value in the inclusive range [0, 1].</param>
    void PushOpacity(float opacity);

    /// <summary>Restores and composites the most recently pushed opacity layer.</summary>
    /// <exception cref="InvalidOperationException">
    /// The stack is empty or a different state kind was pushed more recently. The stack is not
    /// changed when this exception is thrown.
    /// </exception>
    void PopOpacity();
}
