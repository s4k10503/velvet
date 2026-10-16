using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies a composed family's rejection across the class lists layered on one element: a <c>V.Motion</c>
    /// pose is read with the element's own list as one list, and a family the own list discards holds at its
    /// identity under the stylesheet classes the gesture, presence and drag channels add.
    /// </summary>
    internal sealed class RejectedCompositeLayerTests : PanelTestBase
    {
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        [Test]
        public void Given_AFamilyTheOwnListRejects_When_APoseNamesIt_Then_ThePoseLeavesItAtItsIdentity()
        {
            // Arrange
            var node = V.Motion(className: "translate-y-[10]",
                variants: new Dictionary<string, MotionVariant> { ["open"] = "translate-x-4 opacity-50" });

            // Act
            var applied = MotionVariantResolver.ResolveAppliedAt(node, "open", out _, out _);

            // Assert — opacity-50 is of no rejected family, and stays.
            Assert.That(string.Join(" ", applied), Is.EqualTo("opacity-50 translate-x-[0] translate-y-[0]"));
        }

        // A pose's token outranks an own one it ties with on importance, as an animate pose overrides the element's
        // style, and an important token outranks a plain one whichever list holds it.
        [TestCase("translate-y-[10]", "translate-y-4", "translate-y-4")]
        [TestCase("translate-x-4 translate-y-[10]", "translate-y-4", "translate-x-4 translate-y-4")]
        [TestCase("translate-x-[10] !translate-x-4", "translate-y-4", "!translate-x-4 translate-y-4")]
        [TestCase("!translate-y-[10]", "translate-y-4", "!translate-x-[0] !translate-y-[0]")]
        [TestCase("!translate-x-4", "translate-y-[10]", "!translate-x-[0] !translate-y-[0]")]
        public void Given_AnOwnListAndAPoseOfOneComposedFamily_When_ThePoseApplies_Then_TheyAreReadAsOneList(
            string own, string pose, string expected)
        {
            // Arrange
            var node = V.Motion(className: own, variants: new Dictionary<string, MotionVariant> { ["open"] = pose });

            // Act
            var applied = MotionVariantResolver.ResolveAppliedAt(node, "open", out _, out _);

            // Assert
            Assert.That(string.Join(" ", applied), Is.EqualTo(expected));
        }

        [Test]
        public void Given_APoseRejectingAValue_When_Mounted_Then_TheOwnTranslationIsHeldAtZero()
        {
            // Arrange
            var poses = new Dictionary<string, MotionVariant> { ["open"] = "translate-y-[10]" };
            var root = new VisualElement();
            using var reconciler = new Reconciler();

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), new VNode[]
            {
                V.Motion(className: "translate-x-4"),
                V.Motion(className: "translate-x-4", variants: poses, animate: "open"),
            });

            // Assert — the pose-less Motion is the own class taking effect.
            Assert.That((root.ElementAt(0).style.translate.value.x.value, root.ElementAt(1).style.translate.value.x.value),
                Is.EqualTo((16f, 0f)));
        }

        [Test]
        public void Given_AFamilyTheOwnListRejects_When_AChannelAddsAStylesheetClassOfIt_Then_TheIdentityHolds()
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
