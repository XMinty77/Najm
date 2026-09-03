using Najm.Core;
using Najm.Core.Text;
using Najm.Utils;

namespace Najm.Host.Desktop;

/// <summary>Everything a <see cref="DesktopHost"/> needs that is a decision rather than a discovery.</summary>
/// <remarks>
/// <para>
/// <strong>Named by the reference before it existed.</strong> ARCHITECTURE §5.1 puts
/// <c>BarColor</c> here; §9.1 puts the host-reserved overlay and restart keys here, "both
/// rebindable"; §4.2 puts <c>Typesetter</c> and <c>Audio</c> here, because that is what keeps §16's
/// dependency rows true — a host that constructed a typesetter would have to reference
/// <c>Najm.Text</c>, and this project references neither it nor <c>Najm.Audio</c>. Everything else
/// here is window configuration, which has nowhere else to live.
/// </para>
/// <para>
/// <strong>What is deliberately absent: the scene, and anything about it.</strong> There is no
/// virtual resolution here and no clear color; both are the scene's (§5.1, §5.2). A host that let
/// you override a scene's virtual resolution would be letting the output size reach back into the
/// scene, which is the one thing §5.1 exists to prevent.
/// </para>
/// <para>
/// An initialized property bag rather than a constructor: every value has a defensible default, and
/// the shape §4.6 writes — <c>new DesktopHost(options).Run(factory)</c> — reads best when the
/// options name only what is being changed.
/// </para>
/// </remarks>
public sealed class HostOptions
{
    private int width = 1280;
    private int height = 720;
    private double maxDt = 0.25d;
    private string title = "Najm";

    /// <summary>Gets or sets the window's initial width in logical pixels. Default 1280.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int Width
    {
        get => width;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            width = value;
        }
    }

    /// <summary>Gets or sets the window's initial height in logical pixels. Default 720.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int Height
    {
        get => height;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            height = value;
        }
    }

    /// <summary>Gets or sets the window title. Default <c>"Najm"</c>.</summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public string Title
    {
        get => title;
        set => title = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets or sets the color the letterbox bars are cleared to. Default opaque black.</summary>
    /// <remarks>
    /// §5.1's <c>HostOptions.BarColor</c>, by that name. The bars are everything outside the fitted
    /// content rectangle, and the host clears them every frame — see <see cref="DesktopHost"/> for
    /// why that happens after the scene renders rather than before it.
    /// </remarks>
    public Color BarColor { get; set; } = Color.Black;

    /// <summary>Gets or sets whether presentation waits for the display's refresh. Default true.</summary>
    /// <remarks>
    /// True is right for a live talk: it is what stops tearing across a projector. Turn it off to
    /// measure how long a frame really takes, which vsync otherwise hides behind the refresh
    /// interval.
    /// </remarks>
    public bool VSync { get; set; } = true;

    /// <summary>Gets or sets the largest simulation delta one live frame may carry, in seconds. Default 0.25.</summary>
    /// <remarks>
    /// <see cref="ClockPolicy.Live(double)"/>'s clamp, and the reason a scene survives being paused
    /// at a breakpoint: without it the frame after a ten-second stall would advance the simulation
    /// ten seconds and fling everything off screen. The cost of the clamp is that simulation time
    /// falls behind wall time whenever a frame overruns it, which is the right trade for a live
    /// visual and the wrong one for anything that has to match a wall clock.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite and positive.</exception>
    public double MaxDt
    {
        get => maxDt;
        set
        {
            if (!double.IsFinite(value) || value <= 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "The maximum live delta must be finite and positive.");
            }

            maxDt = value;
        }
    }

    /// <summary>Gets or sets the key that warm-restarts the scene. Default <see cref="Key.F5"/>.</summary>
    /// <remarks>
    /// §15's manual warm restart: it reconstructs the scene from its factory and keeps the window,
    /// the GL context, the GPU provider, and their caches. §9.1 reserves it to the host, so a scene
    /// never sees this key as an event or as a held-key snapshot. <see cref="Key.Unknown"/> disables
    /// the reservation and gives the key back to the scene.
    /// </remarks>
    public Key RestartKey { get; set; } = Key.F5;

    /// <summary>Gets or sets the key reserved for the debug overlay. Default <see cref="Key.F1"/>.</summary>
    /// <remarks>
    /// §9.1 reserves it to the host alongside <see cref="RestartKey"/>. <strong>There is no overlay
    /// yet</strong> — §15's overlay is not built — and this host reserves the key anyway, so that
    /// building it later does not silently take a key a scene had come to rely on. Until then the
    /// key is swallowed and nothing happens. <see cref="Key.Unknown"/> gives it back to the scene.
    /// </remarks>
    public Key OverlayKey { get; set; } = Key.F1;

    /// <summary>Gets or sets the asset store scenes load through, or null for <see cref="NullAssets"/>.</summary>
    /// <remarks>
    /// §4.2 has the host construct assets natively. Nothing in this repository realizes
    /// <see cref="IAssets"/> yet, so the default is Core's null object and this property is the door
    /// for the implementation that arrives later or for one an application already has.
    /// </remarks>
    public IAssets? Assets { get; set; }

    /// <summary>Gets or sets the typesetter, or null for the fail-loud <see cref="NullTypesetter"/>.</summary>
    /// <remarks>
    /// §4.2: injected rather than constructed, so this project does not reference
    /// <c>Najm.Text</c> and §16's dependency row stays true. The default throws on first use naming
    /// this property, which is the intended failure for a scene that draws text under a host nobody
    /// gave a typesetter to.
    /// </remarks>
    public ITypesetter? Typesetter { get; set; }

    /// <summary>Gets or sets the audio sink, or null for the silent <see cref="NullAudioSink"/>.</summary>
    /// <remarks>§4.2, for the same reason as <see cref="Typesetter"/>: injected, never constructed here.</remarks>
    public IAudioSink? Audio { get; set; }
}
