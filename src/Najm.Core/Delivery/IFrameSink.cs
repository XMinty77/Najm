namespace Najm.Core;

/// <summary>Receives a stream of rendered frames and delivers them somewhere.</summary>
/// <remarks>
/// <para>
/// A sink is the far end of the delivery seam: an encoder pipe, a numbered image sequence, a hash
/// accumulator in a determinism test. It is driven exactly once per stream, in order —
/// <see cref="Begin(in FrameStreamInfo)"/>, then zero or more
/// <see cref="Submit(long, PixelFrameLease)"/> calls with non-decreasing frame indices, then
/// <see cref="End"/>.
/// </para>
/// <para>
/// <strong>Ownership.</strong> A submitted <see cref="PixelFrameLease"/> belongs to the sink from
/// the moment <see cref="Submit(long, PixelFrameLease)"/> is entered, exception or not. The sink
/// disposes it — immediately after a synchronous write, or once an asynchronous encoder has drained
/// it. The producer never touches the lease again, which is precisely what lets it hand over pooled
/// memory instead of a snapshot whose backing surface it is about to overwrite.
/// </para>
/// <para>
/// <strong>Failure is loud.</strong> A sink that cannot deliver throws. It never quietly drops a
/// frame, truncates a file, or reports success for output that does not exist; a broken encoder must
/// stop the render, not produce a shorter clip than was asked for.
/// </para>
/// <para>
/// Sinks run on the engine thread and are not thread-safe. A sink that owns an external resource
/// should also implement <see cref="IDisposable"/> for the abandoned-run path;
/// <see cref="OfflineRenderer"/> disposes such a sink when a render fails before
/// <see cref="End"/> could run.
/// </para>
/// </remarks>
public interface IFrameSink
{
    /// <summary>Opens the stream and fixes its dimensions, rate, and pixel format.</summary>
    /// <param name="info">The constant description of every frame that will follow.</param>
    /// <remarks>
    /// Everything a sink needs to validate — an encoder's dimension constraints, an unsupported
    /// pixel layout, an unwritable path — is knowable here, so this is where a misconfigured sink
    /// fails, before a single frame has been rendered.
    /// </remarks>
    void Begin(in FrameStreamInfo info);

    /// <summary>Delivers one frame and takes ownership of its pixel memory.</summary>
    /// <param name="frame">
    /// The zero-based output frame index. Output frame <c>k</c> is the render performed after tick
    /// <c>k</c>, so index and simulation frame are the same number.
    /// </param>
    /// <param name="pixels">
    /// The frame's pixels, whose ownership transfers to this sink. The sink disposes it.
    /// </param>
    void Submit(long frame, PixelFrameLease pixels);

    /// <summary>Closes the stream and finishes the output.</summary>
    /// <remarks>
    /// This is where a sink flushes, closes its encoder, waits for a child process, and verifies
    /// that the output it promised actually exists and is complete. It throws if it does not.
    /// </remarks>
    void End();
}
