using NUnit.Framework;
using UnityEngine;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies a composed family the element's own classes reject under a stylesheet class of that family that
    /// a class channel adds: the rejected value keeps the family drawing nothing.
    /// </summary>
    internal sealed class RejectedCompositeChannelTests : PanelTestBase
    {
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        [Test]
        public void Given_AFamilyTheOwnClassesReject_When_AChannelAddsAStylesheetClassOfIt_Then_NothingIsScaled()
        {
            // Arrange — the gesture, presence and drag channels all add their classes through AddClasses.
            var leaf = MountAndResolve("scale-x-[abc] scale-y-[2]");

            // Act
            StyleAnimationClassUtils.AddClasses(leaf, new[] { "scale-150" });
            ForcePanelUpdate(leaf.panel);

            // Assert
            Assert.That(leaf.resolvedStyle.scale.value, Is.EqualTo(Vector3.one));
        }
    }
}
