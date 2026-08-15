using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using HarfBuzzSharp;
using Najm.Text.HarfBuzz;

namespace Najm.Text.Tests.HarfBuzz;

[TestClass]
[DoNotParallelize]
public sealed class HarfBuzzNativeSmokeTests
{
    private static readonly Language English = new("en");

    [TestMethod]
    public void EveryBundledFace_LoadsFromEmbeddedBytesAtPinnedUnitsPerEm()
    {
        foreach (var asset in BundledFonts.All)
        {
            using var shaper = new HarfBuzzShaper(asset.Bytes);
            Assert.AreEqual(1_000, shaper.UnitsPerEm, asset.FileName);
        }
    }

    [TestMethod]
    public void Shaper_RejectsMalformedNonemptyFontData()
    {
        var exception = Assert.ThrowsExactly<InvalidDataException>(
            () => new HarfBuzzShaper(new byte[] { 0x4E, 0x41, 0x4A, 0x4D }));

        Assert.AreEqual("The supplied font data contains no readable faces.", exception.Message);
    }

    [TestMethod]
    public void Shaper_RejectsUnavailableFaceIndexBeforeCreatingFace()
    {
        var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new HarfBuzzShaper(BundledFonts.RomanRegular.Bytes, faceIndex: 1));

        Assert.AreEqual("faceIndex", exception.ParamName);
        Assert.AreEqual(1, exception.ActualValue);
        StringAssert.Contains(
            exception.Message,
            "Face index 1 is outside the available range [0, 1).");
    }

    [TestMethod]
    public void RomanRegular_OwnsNativeFontDataAfterTransientSourceIsCollected()
    {
        using var shaper = CreateShaperFromTransientSource(out var sourceReference);

        ForceFullCompactingCollection();
        Assert.IsFalse(
            sourceReference.IsAlive,
            "The production shaper must not retain its caller's managed font buffer.");

        var allocationPressure = CreateLargeObjectHeapPressure(BundledFonts.RomanRegular.ExpectedLength);
        var av = ShapeLatin(shaper, "AV");
        var fi = ShapeLatin(shaper, "fi");

        AssertGlyphs(
            av,
            new ShapedGlyph(27, 0, 639, 0, 0, 0),
            new ShapedGlyph(111, 1, 750, 0, 0, 0));
        Assert.AreEqual(1_389, av.TotalXAdvance);
        AssertGlyphs(fi, new ShapedGlyph(125, 0, 556, 0, 0, 0));
        Assert.AreEqual(556, fi.TotalXAdvance);
        GC.KeepAlive(allocationPressure);
    }

    [TestMethod]
    public void RomanRegular_ProducesPinnedKerningAndLigatureResults()
    {
        using var shaper = new HarfBuzzShaper(BundledFonts.RomanRegular.Bytes);

        var av = ShapeLatin(shaper, "AV");
        var a = ShapeLatin(shaper, "A");
        var v = ShapeLatin(shaper, "V");
        var fi = ShapeLatin(shaper, "fi");

        Assert.AreEqual(1_000, shaper.UnitsPerEm);
        AssertGlyphs(
            av,
            new ShapedGlyph(27, 0, 639, 0, 0, 0),
            new ShapedGlyph(111, 1, 750, 0, 0, 0));
        Assert.AreEqual(1_389, av.TotalXAdvance);
        Assert.AreEqual(1_500, checked(a.TotalXAdvance + v.TotalXAdvance));
        Assert.AreEqual(111, checked(a.TotalXAdvance + v.TotalXAdvance - av.TotalXAdvance));

        AssertGlyphs(fi, new ShapedGlyph(125, 0, 556, 0, 0, 0));
        Assert.AreEqual(556, fi.TotalXAdvance);
    }

    [TestMethod]
    public void RomanRegular_RightToLeftDirection_ProducesDescendingUtf16Clusters()
    {
        using var shaper = new HarfBuzzShaper(BundledFonts.RomanRegular.Bytes);

        var run = shaper.Shape("abc", Direction.RightToLeft, Script.Latin, English);

        AssertGlyphs(
            run,
            new ShapedGlyph(43, 2, 444, 0, 0, 0),
            new ShapedGlyph(35, 1, 556, 0, 0, 0),
            new ShapedGlyph(28, 0, 500, 0, 0, 0));
        Assert.AreEqual(1_500, run.TotalXAdvance);
    }

    [TestMethod]
    public void Shape_CheckedAdvanceOverflow_ReturnsClearedBufferForNextShape()
    {
        using var shaper = new HarfBuzzShaper(BundledFonts.RomanRegular.Bytes);
        var face = GetFaceEntry(shaper);

        // A test-only extreme native scale makes the checked sum overflow after HarfBuzz has
        // populated the rented buffer. Restoring the scale leaves the shaper itself usable.
        face.Font.SetScale(int.MaxValue, int.MaxValue);
        try
        {
            Assert.ThrowsExactly<OverflowException>(() => ShapeLatin(shaper, "AA"));
        }
        finally
        {
            face.Font.SetScale(shaper.UnitsPerEm, shaper.UnitsPerEm);
        }

        AssertGlyphs(
            ShapeLatin(shaper, "A"),
            new ShapedGlyph(27, 0, 750, 0, 0, 0));
    }

    [TestMethod]
    public void Shape_FromNonOwnerThread_FailsBeforeStateMutation_ThenOwnerCanShape()
    {
        using var shaper = new HarfBuzzShaper(BundledFonts.RomanRegular.Bytes);

        var exception = CaptureOnDedicatedThread(
            () => shaper.Shape("AV", Direction.LeftToRight, Script.Latin, English));

        Assert.AreEqual(typeof(InvalidOperationException), exception.GetType());
        Assert.AreEqual(
            "A HarfBuzz shaper must be used and disposed on the thread that created it.",
            exception.Message);
        Assert.AreEqual(1_389, ShapeLatin(shaper, "AV").TotalXAdvance);
    }

    [TestMethod]
    public void Dispose_FromNonOwnerThread_FailsBeforeStateMutation_ThenOwnerCanShapeAndDispose()
    {
        var shaper = new HarfBuzzShaper(BundledFonts.RomanRegular.Bytes);
        try
        {
            var exception = CaptureOnDedicatedThread(shaper.Dispose);

            Assert.AreEqual(typeof(InvalidOperationException), exception.GetType());
            Assert.AreEqual(
                "A HarfBuzz shaper must be used and disposed on the thread that created it.",
                exception.Message);
            Assert.AreEqual(556, ShapeLatin(shaper, "fi").TotalXAdvance);
        }
        finally
        {
            shaper.Dispose();
        }
    }

    [TestMethod]
    public void DisposedShaper_RejectsPropertiesAndShape_WhileDoubleDisposeIsIdempotent()
    {
        var shaper = new HarfBuzzShaper(BundledFonts.RomanRegular.Bytes);

        shaper.Dispose();
        shaper.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = shaper.UnitsPerEm);
        Assert.ThrowsExactly<ObjectDisposedException>(() => ShapeLatin(shaper, "A"));
    }

    [TestMethod]
    public void RepeatedShape_CurrentOwnedRunContract_ReturnsIndependentManagedResults()
    {
        using var shaper = new HarfBuzzShaper(BundledFonts.RomanRegular.Bytes);

        var first = ShapeLatin(shaper, "AV");
        var second = ShapeLatin(shaper, "fi");

        Assert.AreNotSame(first, second);
        AssertGlyphs(
            first,
            new ShapedGlyph(27, 0, 639, 0, 0, 0),
            new ShapedGlyph(111, 1, 750, 0, 0, 0));
        AssertGlyphs(second, new ShapedGlyph(125, 0, 556, 0, 0, 0));
    }

    private static ShapedRun ShapeLatin(HarfBuzzShaper shaper, string text) =>
        shaper.Shape(text, Direction.LeftToRight, Script.Latin, English);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static HarfBuzzShaper CreateShaperFromTransientSource(out WeakReference sourceReference)
    {
        var source = BundledFonts.RomanRegular.Bytes.ToArray();
        sourceReference = new WeakReference(source);
        return new HarfBuzzShaper(source);
    }

    private static HarfBuzzFaceEntry GetFaceEntry(HarfBuzzShaper shaper)
    {
        var faceField = typeof(HarfBuzzShaper).GetField(
            "face",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(faceField, "The overflow test must reach the shaper's owned font.");

        var face = faceField.GetValue(shaper) as HarfBuzzFaceEntry;
        Assert.IsNotNull(face);
        return face;
    }

    private static Exception CaptureOnDedicatedThread(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                captured = exception;
            }
        });

        thread.Start();
        thread.Join();

        Assert.IsNotNull(captured, "The non-owner-thread operation was expected to fail.");
        return captured;
    }

    private static void ForceFullCompactingCollection()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static byte[][] CreateLargeObjectHeapPressure(int allocationLength)
    {
        var allocations = new byte[32][];
        for (var index = 0; index < allocations.Length; index++)
        {
            allocations[index] = GC.AllocateUninitializedArray<byte>(allocationLength);
            allocations[index].AsSpan().Fill(unchecked((byte)(0xA5 + index)));
        }

        return allocations;
    }

    private static void AssertGlyphs(ShapedRun run, params ShapedGlyph[] expected)
    {
        var actual = run.Glyphs.ToArray();
        CollectionAssert.AreEqual(expected, actual);
    }
}
