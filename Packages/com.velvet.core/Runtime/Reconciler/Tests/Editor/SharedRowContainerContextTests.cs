using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which roots a root mounted on an element of the application's is told share a row container with it:
    /// not a root mounted on an element above that one, which places no row into it.
    /// </summary>
    [TestFixture]
    internal sealed class SharedRowContainerContextTests
    {
        [Test]
        public void Given_ARootMountedOnAnApplicationsElement_When_AnotherRootIsMountedOnAnApplicationsElementInsideIt_Then_TheFirstRootSharesNoRowContainer()
        {
            // Arrange — the application puts its own element inside the one the first root is mounted on.
            var outerElement = new VisualElement();
            using var outer = V.Mount(outerElement, V.Div(name: "outer-row"));
            var innerElement = new VisualElement();
            outerElement.Add(innerElement);

            // Act
            using var inner = V.Mount(innerElement, V.Div(name: "inner-row"));

            // Assert
            Assert.That(outer.Root.Reconciler.Context.SharesRowContainers, Is.False);
        }
    }
}
