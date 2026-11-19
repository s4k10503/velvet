using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.Editor.DevTools;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the target a <see cref="V.Portal"/> line of the DevTools tree dump names, across all three ways a
    /// portal addresses one. An unnamed target reads as an empty <c>target=</c>, which is the same line the
    /// dump would print for a portal it could not resolve at all.
    /// </summary>
    [TestFixture]
    internal sealed class VNodeTreeDumpPortalTargetTests
    {
        [Test]
        public void Given_APortalIntoAHeldContainer_When_TheTreeIsDumped_Then_TheContainerIsNamed()
        {
            // Arrange
            var container = new VisualElement { name = "held-container" };
            var tree = new VNode[] { V.Portal(container, children: new VNode?[] { V.Div(name: "in-portal") }) };

            // Act
            var dump = VNodeTreeRenderer.Render(tree);

            // Assert
            Assert.That(dump, Does.Contain("held-container"));
        }

        [Test]
        public void Given_APortalIntoARegisteredId_When_TheTreeIsDumped_Then_TheIdIsNamed()
        {
            // Arrange
            var tree = new VNode[] { V.Portal("modal-root", children: new VNode?[] { V.Div(name: "in-portal") }) };

            // Act
            var dump = VNodeTreeRenderer.Render(tree);

            // Assert
            Assert.That(dump, Does.Contain("target=modal-root"));
        }

        [Test]
        public void Given_APortalIntoALayer_When_TheTreeIsDumped_Then_TheLayerIsNamed()
        {
            // Arrange
            var tree = new VNode[] { V.Portal(UILayer.Topmost, children: new VNode?[] { V.Div(name: "in-portal") }) };

            // Act
            var dump = VNodeTreeRenderer.Render(tree);

            // Assert
            Assert.That(dump, Does.Contain("target=Topmost"));
        }
    }
}
