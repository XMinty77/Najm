using System.Numerics;
using Najm.Core;

namespace Najm.Samples.Orrery;

/// <summary>
/// A seamless <see cref="Design.LoopSeconds"/>-second orrery, built for use as a background plate.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the motion is time-parametric rather than scheduled.</b> Everything visible is a
/// function of one number, <c>frac(t / LoopSeconds)</c>: body angles, the sun's swell, the
/// camera's breath, the twinkle of the stars. The loop is seamless because every rate is a whole
/// number of cycles per loop, which is a property of the closed form and cannot drift. Driving
/// the same motion from coroutines would mean re-arming a tween per body per revolution and
/// trusting that 105 restarts across the clip land on exactly the same phase — a promise the
/// closed form makes for free. It also means <c>SkiaExport.Png(at: t)</c> is honest at any
/// <c>t</c>, since there is no accumulated state for a seek to get wrong.
/// </para>
/// <para>
/// <b>Layers, bottom to top.</b> A haze plate in virtual space; the stars, also in virtual space
/// so the camera's breath parallaxes against them; the system itself in a world layer; and a
/// vignette over the top.
/// </para>
/// </remarks>
internal sealed class OrreryScene : Scene
{
    private readonly Body[] bodies;
    private readonly SunNode sun;
    private readonly BeltNode belt;
    private readonly StarfieldNode stars;
    private readonly WorldLayer2D world;
    private readonly PlanetNode[] planets;
    private readonly MoonNode[] moons;

    public OrreryScene()
    {
        bodies = new Body[Design.Bodies.Length];
        for (var i = 0; i < bodies.Length; i++)
        {
            bodies[i] = new Body(Design.Bodies[i]);
        }

        var haze = Layers.Add(new ScreenLayer { ClearColor = Design.Space });
        haze.Root.Add(new HazeNode(VirtualResolution));

        var sky = Layers.Add(new ScreenLayer());
        stars = sky.Root.Add(new StarfieldNode(VirtualResolution));

        world = Layers.Add(new WorldLayer2D());

        // The camera frames world origin at the middle of the frame, so the system is offset
        // instead of the camera: that way the breathing zoom pushes in on the whole composition
        // rather than sliding the sun around.
        var center = VirtualResolution * 0.5f;
        var system = world.Root.Add(new Node2D
        {
            Position = new Vector2(
                Design.SunInFrame.X - center.X,
                center.Y - Design.SunInFrame.Y),
            Rotation = Design.Roll,
        });

        foreach (var body in bodies)
        {
            system.Add(new OrbitRingNode(body));
        }

        belt = system.Add(new BeltNode());
        sun = system.Add(new SunNode());

        planets = new PlanetNode[bodies.Length];
        var moonList = new List<MoonNode>();
        for (var i = 0; i < bodies.Length; i++)
        {
            planets[i] = system.Add(new PlanetNode(bodies[i]));
            if (bodies[i].Spec.HasMoon)
            {
                moonList.Add(planets[i].Add(new MoonNode(bodies[i])));
            }
        }

        moons = [.. moonList];

        var top = Layers.Add(new ScreenLayer());
        top.Root.Add(new VignetteNode(VirtualResolution));

        // A still exported at t = 0 runs zero ticks, so the loaded state has to be a real frame
        // rather than every body stacked on the origin.
        ApplyPhase(0d);
    }

    protected override void Update(in TickContext tick)
    {
        // frac() rather than a wrapping accumulator: at t = LoopSeconds the phase is exactly 0
        // again, so frame 900 of a 900-frame clip is frame 0 and is never emitted.
        var phase = tick.Time.Elapsed / Design.LoopSeconds;
        ApplyPhase(phase - Math.Floor(phase));
    }

    private void ApplyPhase(double phase)
    {
        foreach (var body in bodies)
        {
            body.Advance(phase);
        }

        foreach (var planet in planets)
        {
            planet.Sync();
        }

        foreach (var moon in moons)
        {
            moon.Sync();
        }

        belt.Advance(phase);
        stars.Advance(phase);
        sun.SetBreath(phase);
        world.Camera.Zoom = 1f + (0.016f * (float)(1d - Math.Cos(Math.Tau * phase)) * 0.5f);
    }
}
