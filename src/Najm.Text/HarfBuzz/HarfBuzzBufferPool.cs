using HbBuffer = HarfBuzzSharp.Buffer;

namespace Najm.Text.HarfBuzz;

internal sealed class HarfBuzzBufferPool : IDisposable
{
    private readonly Stack<HbBuffer> available = [];
    private bool disposed;

    internal HbBuffer Rent()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return available.Count == 0 ? new HbBuffer() : available.Pop();
    }

    internal void Return(HbBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.ClearContents();
        if (disposed)
        {
            buffer.Dispose();
            return;
        }

        available.Push(buffer);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        while (available.TryPop(out var buffer))
        {
            buffer.Dispose();
        }
    }
}
