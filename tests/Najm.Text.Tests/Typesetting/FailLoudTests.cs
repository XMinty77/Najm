using Najm.Core.Text;

namespace Najm.Text.Tests.Typesetting;

/// <summary>
/// NAJM-TEXT VI.3: author mistakes fail loud, and a field this slice does not honour is refused
/// rather than ignored.
/// </summary>
/// <remarks>
/// The failure mode being designed against is specific and has bitten this project before: a
/// property that accepts a value and silently does nothing with it. The author sets it, sees the
/// old behaviour, and has no way to tell "my value was wrong" from "the feature does not exist".
/// Every message below therefore names the field, says what the engine does instead, and says what
/// to do about it.
/// </remarks>
[TestClass]
public sealed class FailLoudTests
{
    [TestMethod]
    public void MaxWidthIsRefusedRatherThanIgnored()
    {
        using var typesetter = new Typesetter();
        var request = new TypesetRequest("wrap me", new Style { Size = 32f }) { MaxWidth = 100f };

        var error = Assert.ThrowsExactly<NotSupportedException>(() => typesetter.Typeset(request));

        Assert.Contains("MaxWidth", error.Message);
        Assert.Contains("hard newlines", error.Message);
        Assert.Contains("UAX #14", error.Message);
    }

    [TestMethod]
    public void ParagraphSpacingIsRefusedBecauseItsMeaningWillChange()
    {
        using var typesetter = new Typesetter();
        var request = new TypesetRequest("a\n\nb", new Style { Size = 32f }) { ParagraphSpacing = 12f };

        var error = Assert.ThrowsExactly<NotSupportedException>(() => typesetter.Typeset(request));

        // Not merely unimplemented: honouring it now would bake in a spacing that changes the day
        // paragraph splitting lands, because real splitting consumes the blank line this slice lays
        // out. The message has to say so, or the refusal looks arbitrary.
        Assert.Contains("ParagraphSpacing", error.Message);
        Assert.Contains("blank line", error.Message);
    }

    [TestMethod]
    public void DynamicIsRefusedBecauseASharedLayoutCannotBeMutated()
    {
        using var typesetter = new Typesetter();
        var request = new TypesetRequest("0.000", new Style { Size = 32f }) { Dynamic = true };

        var error = Assert.ThrowsExactly<NotSupportedException>(() => typesetter.Typeset(request));

        Assert.Contains("Dynamic", error.Message);
        Assert.Contains("mutable layout", error.Message);
    }

    [TestMethod]
    public void ARefusedRequestLeavesNoCacheEntryBehind()
    {
        using var typesetter = new Typesetter();

        Assert.ThrowsExactly<NotSupportedException>(
            () => typesetter.Typeset(new TypesetRequest("x", new Style { Size = 32f }) { MaxWidth = 10f }));

        // Validation runs before anything touches a cache, so a refusal cannot leave half an entry
        // for a later, legal request to collide with.
        Assert.AreEqual(0, typesetter.CachedLayoutCount);
        Assert.AreEqual(0, typesetter.CachedShapedRunCount);
    }

    [TestMethod]
    public void AnUnknownFamilyNamesWhatIsActuallyRegistered()
    {
        using var typesetter = new Typesetter();
        var request = new TypesetRequest("x", new Style { Size = 32f, Family = "Helvetica" });

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => typesetter.Typeset(request));

        // A "font not found" that does not say which fonts were found is a message that sends the
        // author to the source. This one answers the next question in the same breath.
        Assert.Contains("Helvetica", error.Message);
        Assert.Contains(Typesetter.LatinModernRoman, error.Message);
        Assert.Contains("RegisterFamily", error.Message);
    }

    [TestMethod]
    public void AMissingFaceIsRefusedRatherThanSubstituted()
    {
        using var typesetter = new Typesetter();
        var request = new TypesetRequest("x", new Style
        {
            Size = 32f,
            Weight = FontWeight.Thin,
            Slant = FontSlant.Oblique,
        });

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => typesetter.Typeset(request));

        // Latin Modern has no thin oblique. Serving the nearest cut instead would change every
        // advance and move every glyph after the first — a layout that is subtly wrong and never
        // announces itself.
        Assert.Contains("Thin/Oblique", error.Message);
        Assert.Contains("Normal/Upright", error.Message);
        Assert.Contains("substituted", error.Message);
    }

    [TestMethod]
    public void ABaseStyleWithNoSizeIsRefused()
    {
        using var typesetter = new Typesetter();

        var error = Assert.ThrowsExactly<InvalidOperationException>(
            () => typesetter.Typeset(new TypesetRequest("x", default)));

        Assert.Contains("must resolve a size", error.Message);
    }

    [TestMethod]
    public void SetDefaultFamiliesRefusesNamesThatAreNotRegistered()
    {
        using var typesetter = new Typesetter();

        Assert.ThrowsExactly<ArgumentException>(
            () => typesetter.SetDefaultFamilies("Nonesuch", Typesetter.LatinModernMath));
        Assert.ThrowsExactly<ArgumentException>(
            () => typesetter.SetDefaultFamilies(Typesetter.LatinModernRoman, "Nonesuch"));

        // The bundled pair is registered by the constructor, so the legal call is the default state.
        typesetter.SetDefaultFamilies(Typesetter.LatinModernRoman, Typesetter.LatinModernMath);
        Assert.AreEqual(Typesetter.LatinModernRoman, typesetter.DefaultTextFamily);
    }

    [TestMethod]
    public void ATypesetterRefusesUseFromASecondThread()
    {
        using var typesetter = new Typesetter();
        Exception? captured = null;

        // II.1: render-thread affine, with no internal locking to pay for. A cross-thread call has
        // to be refused rather than raced, because the failure it would otherwise produce is a
        // corrupted dictionary discovered somewhere else entirely.
        var thread = new Thread(() =>
        {
            try
            {
                typesetter.Typeset(new TypesetRequest("x", new Style { Size = 32f }));
            }
            catch (Exception error)
            {
                captured = error;
            }
        });
        thread.Start();
        thread.Join();

        Assert.IsInstanceOfType<InvalidOperationException>(captured);
        Assert.Contains("render-thread affine", captured!.Message);
    }
}
