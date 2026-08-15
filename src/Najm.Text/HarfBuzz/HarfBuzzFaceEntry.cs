using HarfBuzzSharp;
using HbFont = HarfBuzzSharp.Font;

namespace Najm.Text.HarfBuzz;

internal sealed class HarfBuzzFaceEntry : IDisposable
{
    private Blob? blob;
    private Face? face;
    private HbFont? font;

    internal HarfBuzzFaceEntry(ReadOnlyMemory<byte> fontBytes, int faceIndex = 0)
    {
        if (fontBytes.IsEmpty)
        {
            throw new ArgumentException("Font bytes must not be empty.", nameof(fontBytes));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);

        Blob? loadedBlob = null;
        Face? loadedFace = null;
        HbFont? loadedFont = null;
        try
        {
            loadedBlob = CreateOwnedBlob(fontBytes.Span);
            if (loadedBlob.Length == 0)
            {
                throw new InvalidDataException("HarfBuzz could not create a blob from the supplied font bytes.");
            }

            loadedBlob.MakeImmutable();
            var faceCount = loadedBlob.FaceCount;
            if (faceCount <= 0)
            {
                throw new InvalidDataException("The supplied font data contains no readable faces.");
            }
            if (faceIndex >= faceCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(faceIndex),
                    faceIndex,
                    $"Face index {faceIndex} is outside the available range [0, {faceCount}).");
            }

            loadedFace = new Face(loadedBlob, faceIndex);
            var unitsPerEm = checked((int)loadedFace.UnitsPerEm);
            if (unitsPerEm <= 0)
            {
                throw new InvalidDataException("The supplied font reports an invalid units-per-em value.");
            }

            loadedFont = new HbFont(loadedFace);
            loadedFont.SetFunctionsOpenType();
            loadedFont.SetScale(unitsPerEm, unitsPerEm);

            blob = loadedBlob;
            face = loadedFace;
            font = loadedFont;
            UnitsPerEm = unitsPerEm;
        }
        catch
        {
            loadedFont?.Dispose();
            loadedFace?.Dispose();
            loadedBlob?.Dispose();
            throw;
        }
    }

    private static unsafe Blob CreateOwnedBlob(ReadOnlySpan<byte> fontBytes)
    {
        fixed (byte* fontData = fontBytes)
        {
            return new Blob((IntPtr)fontData, fontBytes.Length, MemoryMode.Duplicate);
        }
    }

    internal int UnitsPerEm { get; }

    internal HbFont Font
    {
        get
        {
            ObjectDisposedException.ThrowIf(font is null, this);
            return font;
        }
    }

    public void Dispose()
    {
        font?.Dispose();
        font = null;
        face?.Dispose();
        face = null;
        blob?.Dispose();
        blob = null;
    }
}
