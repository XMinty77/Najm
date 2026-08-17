using System.Diagnostics;
using System.Globalization;
using System.Text;
using Najm.Core;
using CorePixelFormat = Najm.Core.PixelFormat;

namespace Najm.Skia;

/// <summary>Streams raw frames into a spawned ffmpeg process and encodes them to a video file.</summary>
/// <remarks>
/// <para>
/// <strong>This is the default delivery path, and it writes no intermediate files.</strong> Each
/// frame's pixels go straight down ffmpeg's standard input and are encoded on the fly, so a
/// twelve-second 4K clip costs the size of the finished MP4 rather than the roughly thirty gigabytes
/// the equivalent PNG sequence would occupy. Exactly one frame is in flight at a time; nothing is
/// buffered in memory either.
/// </para>
/// <para>
/// <strong>The stderr drain is the single deliberate exception to Najm's single-threaded rule.</strong>
/// ffmpeg writes progress and diagnostics to standard error continuously. A redirected pipe whose
/// reader never reads holds roughly 64 KiB before the writer blocks, and a blocked ffmpeg stops
/// consuming its standard input, so the engine's next frame write blocks too and the render
/// deadlocks with neither side at fault. One background thread therefore does nothing but read that
/// pipe to exhaustion. It touches no engine state — only a capped tail buffer of ffmpeg's own text —
/// and is joined in <see cref="End"/>. That is the whole of Najm's concurrency.
/// </para>
/// <para>
/// <strong>Failure is loud.</strong> A missing executable, a broken pipe, a non-zero exit code, a
/// missing or empty output file: every one throws, with the exact command line and the tail of
/// ffmpeg's own diagnostics in the message. The sink never reports success for a truncated or
/// absent video.
/// </para>
/// <para>
/// Create instances through <see cref="FrameSink.FfmpegPipe(string, FfmpegPipeOptions?)"/>. The
/// sink owns a child process, so dispose it if a run is abandoned before <see cref="End"/>;
/// <see cref="OfflineRenderer"/> does that automatically.
/// </para>
/// </remarks>
public sealed class FfmpegFrameSink : IFrameSink, IDisposable
{
    /// <summary>How many trailing characters of ffmpeg's diagnostics are kept for failure messages.</summary>
    private const int StandardErrorTailLimit = 8192;

    private readonly string outputPath;
    private readonly FfmpegPipeOptions options;
    private readonly StringBuilder standardErrorTail = new();
    private readonly Lock standardErrorGate = new();

    private Process? process;
    private Stream? input;
    private Thread? standardErrorPump;
    private string commandLine = string.Empty;
    private int width;
    private int height;
    private long submittedFrames;
    private long lastFrame = -1L;
    private bool begun;
    private bool ended;
    private bool disposed;

    internal FfmpegFrameSink(string outputPath, FfmpegPipeOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(options);

        this.outputPath = Path.GetFullPath(outputPath);
        this.options = options;
    }

    /// <summary>Gets the absolute path of the video file this sink writes.</summary>
    public string OutputPath => outputPath;

    /// <summary>Gets the exact command line the sink spawned, or an empty string before it starts.</summary>
    /// <remarks>Exposed so a failing render can be reproduced by hand.</remarks>
    public string CommandLine => commandLine;

    /// <summary>Gets how many frames have been written to ffmpeg's input so far.</summary>
    public long SubmittedFrames => submittedFrames;

    /// <inheritdoc />
    /// <remarks>
    /// Spawns ffmpeg and starts the stderr drain. Everything checkable is checked first: the pixel
    /// format must be <see cref="CorePixelFormat.Rgba8888"/>, because that is the straight-alpha
    /// layout ffmpeg's <c>rgba</c> raw format expects, and a chroma-subsampled output format
    /// constrains the frame dimensions to even numbers.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The sink has already begun, the stream cannot be encoded as configured, or ffmpeg could not
    /// be started.
    /// </exception>
    public void Begin(in FrameStreamInfo info)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (begun)
        {
            throw new InvalidOperationException("This ffmpeg frame sink has already begun a stream.");
        }
        if (!info.IsValid)
        {
            throw new ArgumentException("A frame stream description is required.", nameof(info));
        }
        if (info.Format != CorePixelFormat.Rgba8888)
        {
            throw new InvalidOperationException(
                $"The ffmpeg pipe accepts {nameof(CorePixelFormat.Rgba8888)} frames only; the stream " +
                $"declared {info.Format}. Premultiplied and byte-swapped layouts would be encoded as " +
                "straight RGBA and silently shift color.");
        }

        var encodedPixelFormat = options.ResolveOutputPixelFormat();
        ValidateDimensions(info, encodedPixelFormat);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        width = info.Width;
        height = info.Height;
        begun = true;

        var arguments = BuildArguments(info, encodedPixelFormat);
        commandLine = FormatCommandLine(options.Executable, arguments);

        var startInfo = new ProcessStartInfo(options.Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            throw StartFailure(exception);
        }

        if (process is null)
        {
            throw StartFailure(inner: null);
        }

        input = process.StandardInput.BaseStream;

        // The single deliberate exception to the single-threaded rule; see the type remarks.
        standardErrorPump = new Thread(PumpStandardError)
        {
            IsBackground = true,
            Name = "najm-ffmpeg-stderr",
        };
        standardErrorPump.Start();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Writes the frame's rows to ffmpeg's standard input and disposes the lease. A pipe that ffmpeg
    /// has closed — because it died — surfaces here as a loud failure carrying its stderr tail,
    /// rather than as a silently short video.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The stream has not begun or has ended, the frame index does not advance, the frame's size
    /// disagrees with the stream, or ffmpeg stopped reading.
    /// </exception>
    public void Submit(long frame, PixelFrameLease pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        // Ownership transferred on entry: this sink disposes the lease on every path.
        using (pixels)
        {
            EnsureStreaming();
            if (frame <= lastFrame)
            {
                throw new InvalidOperationException(
                    $"Frame indices must increase; received {frame} after {lastFrame}.");
            }
            if (pixels.Width != width || pixels.Height != height)
            {
                throw new InvalidOperationException(
                    $"Frame {frame} is {pixels.Width}×{pixels.Height} but the stream declared " +
                    $"{width}×{height}.");
            }
            if (pixels.Format != CorePixelFormat.Rgba8888)
            {
                throw new InvalidOperationException(
                    $"Frame {frame} carries {pixels.Format} pixels; the ffmpeg pipe expects " +
                    $"{nameof(CorePixelFormat.Rgba8888)}.");
            }

            var stream = input!;
            try
            {
                if (pixels.Stride == pixels.RowBytes)
                {
                    stream.Write(pixels.Pixels);
                }
                else
                {
                    for (var row = 0; row < height; row++)
                    {
                        stream.Write(pixels.Row(row));
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                throw Failure(
                    $"ffmpeg stopped reading its input after {submittedFrames} frames, while frame " +
                    $"{frame} was being written",
                    exception);
            }

            lastFrame = frame;
            submittedFrames++;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Closes ffmpeg's input, waits for it to drain and exit, joins the stderr drain, and verifies
    /// both the exit code and that a non-empty file actually landed on disk.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The stream never began, has already ended, or ffmpeg failed to produce a complete file.
    /// </exception>
    public void End()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!begun)
        {
            throw new InvalidOperationException("This ffmpeg frame sink has not begun a stream.");
        }
        if (ended)
        {
            throw new InvalidOperationException("This ffmpeg frame sink has already ended its stream.");
        }

        ended = true;
        var running = process!;

        try
        {
            input!.Flush();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            CloseInput();
            JoinStandardErrorPump();
            throw Failure("ffmpeg closed its input pipe before the stream was finished", exception);
        }

        CloseInput();

        if (!running.WaitForExit((int)options.ShutdownTimeout.TotalMilliseconds))
        {
            KillQuietly();
            JoinStandardErrorPump();
            throw Failure(
                $"ffmpeg did not exit within {options.ShutdownTimeout} of its input closing and was killed",
                inner: null);
        }

        JoinStandardErrorPump();

        if (running.ExitCode != 0)
        {
            throw Failure($"ffmpeg exited with code {running.ExitCode}", inner: null);
        }

        var written = new FileInfo(outputPath);
        if (!written.Exists || written.Length == 0L)
        {
            throw Failure(
                $"ffmpeg reported success but '{outputPath}' is missing or empty after " +
                $"{submittedFrames} frames",
                inner: null);
        }
    }

    /// <summary>Releases the child process, killing it when the stream was abandoned.</summary>
    /// <remarks>
    /// Disposal after a completed <see cref="End"/> only releases handles. Disposal of a stream that
    /// never ended kills ffmpeg rather than leaving it waiting on a pipe forever, and does not throw
    /// — the failure that abandoned the run is the one worth reporting.
    /// </remarks>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (!ended)
        {
            CloseInput();
            KillQuietly();
        }

        try
        {
            JoinStandardErrorPump();
        }
        catch (ThreadStateException)
        {
            // The pump never started; nothing to join.
        }

        process?.Dispose();
        process = null;
        input = null;
    }

    private void ValidateDimensions(in FrameStreamInfo info, string encodedPixelFormat)
    {
        var needsEvenWidth = encodedPixelFormat.StartsWith("yuv42", StringComparison.Ordinal);
        var needsEvenHeight = encodedPixelFormat.StartsWith("yuv420", StringComparison.Ordinal);

        if ((needsEvenWidth && (info.Width & 1) != 0) || (needsEvenHeight && (info.Height & 1) != 0))
        {
            throw new InvalidOperationException(
                $"A {info.Width}×{info.Height} frame cannot be encoded as '{encodedPixelFormat}': " +
                "chroma-subsampled formats require even dimensions. Render an even output size, or " +
                $"set {nameof(FfmpegPipeOptions)}.{nameof(FfmpegPipeOptions.OutputPixelFormat)} to a " +
                "4:4:4 format.");
        }
    }

    private List<string> BuildArguments(in FrameStreamInfo info, string encodedPixelFormat)
    {
        var arguments = new List<string>(28)
        {
            "-hide_banner",
            "-loglevel",
            options.LogLevel,
            options.Overwrite ? "-y" : "-n",
            "-f",
            "rawvideo",
            "-pixel_format",
            "rgba",
            "-video_size",
            string.Create(CultureInfo.InvariantCulture, $"{info.Width}x{info.Height}"),
            "-framerate",
            info.FramesPerSecond.ToString("R", CultureInfo.InvariantCulture),
            "-i",
            "-",
            "-an",
            "-c:v",
        };

        switch (options.Codec)
        {
            case FfmpegVideoCodec.ProRes:
                arguments.Add("prores_ks");
                arguments.Add("-profile:v");
                arguments.Add(options.ProResProfile.ToString(CultureInfo.InvariantCulture));
                break;
            default:
                arguments.Add("libx264");
                arguments.Add("-preset");
                arguments.Add(options.Preset);
                arguments.Add("-crf");
                arguments.Add(options.ConstantRateFactor.ToString(CultureInfo.InvariantCulture));
                break;
        }

        arguments.Add("-pix_fmt");
        arguments.Add(encodedPixelFormat);
        arguments.AddRange(options.ExtraArguments);
        arguments.Add(outputPath);
        return arguments;
    }

    /// <summary>Reads ffmpeg's standard error to exhaustion so the encoder can never block on it.</summary>
    private void PumpStandardError()
    {
        var reader = process?.StandardError;
        if (reader is null)
        {
            return;
        }

        var chunk = new char[1024];
        try
        {
            int read;
            while ((read = reader.Read(chunk, 0, chunk.Length)) > 0)
            {
                AppendStandardError(chunk.AsSpan(0, read));
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The pipe went away with the process; whatever was already captured is the tail.
        }
    }

    private void AppendStandardError(ReadOnlySpan<char> text)
    {
        lock (standardErrorGate)
        {
            standardErrorTail.Append(text);
            if (standardErrorTail.Length > StandardErrorTailLimit)
            {
                standardErrorTail.Remove(0, standardErrorTail.Length - StandardErrorTailLimit);
            }
        }
    }

    private string ReadStandardErrorTail()
    {
        lock (standardErrorGate)
        {
            return standardErrorTail.ToString().TrimEnd();
        }
    }

    private void JoinStandardErrorPump()
    {
        var pump = standardErrorPump;
        standardErrorPump = null;
        pump?.Join(TimeSpan.FromSeconds(10));
    }

    private void CloseInput()
    {
        var stream = input;
        input = null;
        try
        {
            stream?.Dispose();
        }
        catch (IOException)
        {
            // ffmpeg already closed the far end; the exit code is the authority on what happened.
        }
    }

    private void KillQuietly()
    {
        try
        {
            var running = process;
            if (running is not null && !running.HasExited)
            {
                running.Kill(entireProcessTree: true);
                running.WaitForExit(5_000);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // The process is already gone, which is the outcome this method wanted.
        }
    }

    private void EnsureStreaming()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!begun)
        {
            throw new InvalidOperationException("This ffmpeg frame sink has not begun a stream.");
        }
        if (ended)
        {
            throw new InvalidOperationException("This ffmpeg frame sink has already ended its stream.");
        }
    }

    private InvalidOperationException StartFailure(Exception? inner) =>
        new(
            $"Najm could not start ffmpeg. Install ffmpeg and put it on PATH, or set " +
            $"{nameof(FfmpegPipeOptions)}.{nameof(FfmpegPipeOptions.Executable)} to its full path." +
            Environment.NewLine +
            "Command: " + commandLine,
            inner);

    private InvalidOperationException Failure(string what, Exception? inner)
    {
        var tail = ReadStandardErrorTail();
        var message = new StringBuilder(what.Length + commandLine.Length + tail.Length + 96)
            .Append("Offline delivery failed: ")
            .Append(what)
            .Append('.')
            .AppendLine()
            .Append("Command: ")
            .Append(commandLine);

        if (tail.Length > 0)
        {
            message.AppendLine().Append("ffmpeg stderr (tail):").AppendLine().Append(tail);
        }
        else
        {
            message.AppendLine().Append("ffmpeg wrote nothing to standard error.");
        }

        return new InvalidOperationException(message.ToString(), inner);
    }

    private static string FormatCommandLine(string executable, List<string> arguments)
    {
        var builder = new StringBuilder(executable.Length + (arguments.Count * 12));
        builder.Append(Quote(executable));
        foreach (var argument in arguments)
        {
            builder.Append(' ').Append(Quote(argument));
        }

        return builder.ToString();
    }

    private static string Quote(string argument) =>
        argument.Length > 0 && !argument.AsSpan().ContainsAny(" \t\"'")
            ? argument
            : "'" + argument.Replace("'", @"'\''", StringComparison.Ordinal) + "'";
}
