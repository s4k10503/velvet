using System;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <see cref="SpringIntegrator.Solve"/> in each damping regime against a fourth-order Runge-Kutta
    /// integration of the spring's own equation, m·x″ = −k·x − d·x′, which shares no code with the closed form.
    /// </summary>
    [TestFixture]
    internal sealed class SpringIntegratorSolveTests
    {
        private const double StepSec = 1e-4;

        // The integrated displacement and velocity at `t`, released at `x0` with `v0`.
        private static (double X, double V) Integrate(double x0, double v0, double t, double k, double d, double m)
        {
            double Acceleration(double x, double v) => (-(k * x) - (d * v)) / m;
            var (x, v) = (x0, v0);
            var steps = (int)Math.Round(t / StepSec);
            for (var i = 0; i < steps; i++)
            {
                var (k1x, k1v) = (v, Acceleration(x, v));
                var (k2x, k2v) = (v + (StepSec / 2 * k1v), Acceleration(x + (StepSec / 2 * k1x), v + (StepSec / 2 * k1v)));
                var (k3x, k3v) = (v + (StepSec / 2 * k2v), Acceleration(x + (StepSec / 2 * k2x), v + (StepSec / 2 * k2v)));
                var (k4x, k4v) = (v + (StepSec * k3v), Acceleration(x + (StepSec * k3x), v + (StepSec * k3v)));
                x += StepSec / 6 * (k1x + (2 * k2x) + (2 * k3x) + k4x);
                v += StepSec / 6 * (k1v + (2 * k2v) + (2 * k3v) + k4v);
            }
            return (x, v);
        }

        // GREEN_ON_BASE(characterization): the base already solves Framer's closed form; these pin it in each regime.
        [TestCase("underdamped", 100.0, 10.0, 1.0, 0.05)]
        [TestCase("underdamped", 100.0, 10.0, 1.0, 0.3)]
        [TestCase("underdamped", 100.0, 10.0, 1.0, 1.0)]
        [TestCase("underdamped", 200.0, 20.0, 2.0, 0.3)]
        [TestCase("critically damped", 100.0, 20.0, 1.0, 0.3)]
        [TestCase("critically damped", 100.0, 20.0, 1.0, 1.0)]
        [TestCase("critically damped", 50.0, 20.0, 2.0, 0.5)]
        [TestCase("overdamped", 100.0, 50.0, 1.0, 0.3)]
        [TestCase("overdamped", 100.0, 50.0, 1.0, 1.0)]
        [TestCase("overdamped", 50.0, 60.0, 2.0, 0.5)]
        public void Given_ASpringReleasedAwayFromItsTargetWithAVelocity_When_ItIsSolvedAtATime_Then_ItMatchesItsIntegratedEquation(
            string regime, double stiffness, double damping, double mass, double t)
        {
            // Arrange — 100 below the target, moving away from it.
            var (x0, v0) = (-100.0, -30.0);
            var (x, v) = Integrate(x0, v0, t, stiffness, damping, mass);

            // Act
            var (displacement, velocity) = SpringIntegrator.Solve(x0, v0, t, (stiffness, damping, mass));

            // Assert — the larger of the two errors; the integration's own is below 1e-9 at this step.
            Assert.That(Math.Max(Math.Abs(displacement - x), Math.Abs(velocity - v)), Is.LessThan(1e-6), regime);
        }
    }
}
