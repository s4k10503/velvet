using System;
using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the per-case bound the test runner applies to a case that declares none of its own. The
    /// <c>[Timeout]</c> on <see cref="RouterTests"/>, <see cref="RouterCancellationUnwindTests"/> and
    /// <see cref="RouterUnfinishedNavigationTests"/> is chosen against it, and
    /// <c>RouteTestStubs.MakeOneShotBlocker</c> states what the choice buys.
    /// </summary>
    // This fixture must keep declaring no [Timeout] of its own: the reading below is the runner's own
    // bound only for as long as nothing overrides it.
    [TestFixture]
    internal sealed class UnityRunnerDefaultTimeoutTests
    {
        // GREEN_ON_BASE(characterization): the runner applies this bound on the base tree as well.
        // Pinning it is what keeps the reason the router fixtures carry from rotting in silence.
        [Test]
        public void Given_ACaseDeclaringNoTimeout_When_TheRunnerBoundsIt_Then_TheBoundIsThreeMinutes()
        {
            // Arrange
            var contextType = Type.GetType(
                "UnityEngine.TestRunner.NUnitExtensions.Runner.UnityTestExecutionContext, "
                + "UnityEngine.TestRunner");
            var currentContext = contextType
                ?.GetProperty("CurrentContext", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);
            var timeoutProperty = contextType
                ?.GetProperty("TestCaseTimeout", BindingFlags.Public | BindingFlags.Instance);

            // Act — -1 for a runner that has renamed either member, which is the same news as a changed
            // bound: the reason written against it needs reading again.
            var runnerBoundMs = currentContext == null || timeoutProperty == null
                ? -1
                : (int)timeoutProperty.GetValue(currentContext);

            // Assert
            Assert.That(runnerBoundMs, Is.EqualTo(180000));
        }
    }
}
