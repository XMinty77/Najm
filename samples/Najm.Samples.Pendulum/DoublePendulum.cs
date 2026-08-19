namespace Najm.Samples.Pendulum;

/// <summary>Fixed physical parameters shared by every pendulum instance in the scene.</summary>
internal readonly record struct DoublePendulumParams(double M1, double M2, double L1, double L2, double G);

/// <summary>
/// A single point-mass double pendulum, integrated with fixed-step RK4. Angles are measured from
/// the downward vertical, which is also the direction gravity points in virtual (Y-down) space, so
/// no coordinate flip is needed between physics and pixels.
/// </summary>
internal sealed class DoublePendulum
{
    private readonly DoublePendulumParams p;

    public DoublePendulum(DoublePendulumParams p, double theta1, double theta2)
    {
        this.p = p;
        Theta1 = theta1;
        Theta2 = theta2;
        Omega1 = 0d;
        Omega2 = 0d;
    }

    public double Theta1 { get; private set; }
    public double Theta2 { get; private set; }
    public double Omega1 { get; private set; }
    public double Omega2 { get; private set; }

    /// <summary>Advances the state by <paramref name="dt"/> seconds using <paramref name="substeps"/> RK4 steps.</summary>
    public void Step(double dt, int substeps)
    {
        var h = dt / substeps;
        for (var i = 0; i < substeps; i++)
        {
            RungeKutta4(h);
        }
    }

    /// <summary>Total mechanical energy (kinetic + potential), for drift diagnostics only.</summary>
    public double Energy()
    {
        var (m1, m2, l1, l2, g) = (p.M1, p.M2, p.L1, p.L2, p.G);
        var v1Sq = l1 * l1 * Omega1 * Omega1;
        var v2Sq = (l1 * l1 * Omega1 * Omega1) + (l2 * l2 * Omega2 * Omega2) +
            (2d * l1 * l2 * Omega1 * Omega2 * Math.Cos(Theta1 - Theta2));

        var kinetic = (0.5d * m1 * v1Sq) + (0.5d * m2 * v2Sq);
        var potential = (-m1 * g * l1 * Math.Cos(Theta1)) - (m2 * g * ((l1 * Math.Cos(Theta1)) + (l2 * Math.Cos(Theta2))));
        return kinetic + potential;
    }

    /// <summary>Bob positions in physics units, pivot at the origin, Y positive downward.</summary>
    public (double X1, double Y1, double X2, double Y2) BobPositions()
    {
        var x1 = p.L1 * Math.Sin(Theta1);
        var y1 = p.L1 * Math.Cos(Theta1);
        var x2 = x1 + (p.L2 * Math.Sin(Theta2));
        var y2 = y1 + (p.L2 * Math.Cos(Theta2));
        return (x1, y1, x2, y2);
    }

    private void RungeKutta4(double h)
    {
        var s0 = new State(Theta1, Theta2, Omega1, Omega2);

        var k1 = Derivative(s0);
        var k2 = Derivative(s0.Add(k1, h * 0.5d));
        var k3 = Derivative(s0.Add(k2, h * 0.5d));
        var k4 = Derivative(s0.Add(k3, h));

        var next = new State(
            s0.Theta1 + (h / 6d * (k1.Theta1 + (2d * k2.Theta1) + (2d * k3.Theta1) + k4.Theta1)),
            s0.Theta2 + (h / 6d * (k1.Theta2 + (2d * k2.Theta2) + (2d * k3.Theta2) + k4.Theta2)),
            s0.Omega1 + (h / 6d * (k1.Omega1 + (2d * k2.Omega1) + (2d * k3.Omega1) + k4.Omega1)),
            s0.Omega2 + (h / 6d * (k1.Omega2 + (2d * k2.Omega2) + (2d * k3.Omega2) + k4.Omega2)));

        Theta1 = next.Theta1;
        Theta2 = next.Theta2;
        Omega1 = next.Omega1;
        Omega2 = next.Omega2;
    }

    /// <summary>The standard planar double-pendulum equations of motion (Lagrangian, point masses).</summary>
    private State Derivative(State s)
    {
        var (m1, m2, l1, l2, g) = (p.M1, p.M2, p.L1, p.L2, p.G);
        var (theta1, theta2, omega1, omega2) = (s.Theta1, s.Theta2, s.Omega1, s.Omega2);

        var delta = theta1 - theta2;
        var sinDelta = Math.Sin(delta);
        var cosDelta = Math.Cos(delta);
        var denom = (2d * m1) + m2 - (m2 * Math.Cos(2d * delta));

        var alpha1 =
            ((-g * ((2d * m1) + m2) * Math.Sin(theta1)) -
             (m2 * g * Math.Sin(theta1 - (2d * theta2))) -
             (2d * sinDelta * m2 * ((omega2 * omega2 * l2) + (omega1 * omega1 * l1 * cosDelta))))
            / (l1 * denom);

        var alpha2 =
            (2d * sinDelta *
                ((omega1 * omega1 * l1 * (m1 + m2)) +
                 (g * (m1 + m2) * Math.Cos(theta1)) +
                 (omega2 * omega2 * l2 * m2 * cosDelta)))
            / (l2 * denom);

        return new State(omega1, omega2, alpha1, alpha2);
    }

    private readonly record struct State(double Theta1, double Theta2, double Omega1, double Omega2)
    {
        public State Add(State derivative, double scale) => new(
            Theta1 + (derivative.Theta1 * scale),
            Theta2 + (derivative.Theta2 * scale),
            Omega1 + (derivative.Omega1 * scale),
            Omega2 + (derivative.Omega2 * scale));
    }
}
