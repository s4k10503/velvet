using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    internal sealed class GCAllocationProbeSurfaceTests
    {
        // No single window is public, for the reason MedianBlocksDuring's remarks give.
        [Test]
        public void Given_TheAllocationProbe_When_ItsPublicMethodsAreListed_Then_EachCountsOverThreeWindows()
        {
            // Arrange
            var probe = typeof(GCAllocationProbe);

            // Act
            var surface = string.Join(" ", probe.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Select(method => method.Name + "(" + string.Join(",",
                    method.GetParameters().Select(parameter => parameter.ParameterType.Name)) + ")")
                .OrderBy(signature => signature, System.StringComparer.Ordinal));

            // Assert
            Assert.That(surface, Is.EqualTo("MedianBlocksDuring(Action) MedianBlocksDuring(Action,Action)"));
        }
    }
}
