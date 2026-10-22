using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// A class a running play shows, against a removal of the same class on the live class list: a payload going
    /// leaves it, as a variant rule turning off leaves the class attribute alone, and the className dropping it
    /// takes it off.
    /// </summary>
    [TestFixture]
    internal sealed class PlayClassProjectionModelTests
    {
        private static VisualElement ShowingAPlayClassBeside(string cls, long priority)
        {
            var element = new VisualElement();
            StyleClassProjection.Add(element, "my-card", StyleLayerPriority.Base);
            StyleClassProjection.Add(element, cls, priority);
            StyleAnimationClassUtils.AddClasses(element, new[] { cls }, StyleLayerPriority.Animation);
            return element;
        }

        [Test]
        public void Given_APlayShowingAClassAPayloadAlsoAdds_When_ThePayloadTurnsOff_Then_TheClassStays()
        {
            // Arrange
            var element = ShowingAPlayClassBeside("bg-blue-500", StyleLayerPriority.Hover);

            // Act
            StyleClassProjection.Remove(element, "bg-blue-500", StyleLayerPriority.Hover);

            // Assert
            Assert.IsTrue(element.ClassListContains("bg-blue-500"));
        }

        [Test]
        public void Given_APlayShowingAClassTheClassNameDeclaresImportant_When_TheClassNameDropsIt_Then_TheClassGoes()
        {
            // Arrange
            var important = StyleLayerPriority.ImportantOf(StyleLayerPriority.Base);
            var element = ShowingAPlayClassBeside("bg-blue-500", important);

            // Act
            StyleClassProjection.Remove(element, "bg-blue-500", important);

            // Assert
            Assert.IsFalse(element.ClassListContains("bg-blue-500"));
        }

        [Test]
        public void Given_AnElementPooledWhileAPlayShowsABracketWidth_When_ItsNextConsumerDropsThatClass_Then_ItsOwnWidthStays()
        {
            // Arrange — the pool drops the element's layers, the play's among them.
            var element = new VisualElement();
            StyleAnimationClassUtils.AddClasses(element, new[] { "w-[40px]" }, StyleLayerPriority.Animation,
                realiseInline: true);
            FiberElementPoolReset.ResetClassListAndCommon(element, string.Empty, string.Empty);
            element.style.width = 10f;

            // Act
            StyleAnimationClassUtils.RemoveClasses(element, new[] { "w-[40px]" });

            // Assert — a record outliving its layer clears a layer that is gone, which nulls the slot.
            Assert.That(element.style.width.value.value, Is.EqualTo(10f));
        }

        [Test]
        public void Given_AClippedElement_When_APlayShowsAnotherClipPathClass_Then_TheMaskFollows()
        {
            // Arrange
            using var scope = new Velvet.TestUtilities.ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(),
                new VNode[] { V.Div(className: "clip-path-[circle(50%)]", name: "card") });
            var inner = scope.Root.Q<VisualElement>("card");

            // Act
            StyleAnimationClassUtils.AddClasses(inner, new[] { "clip-path-[inset(10%)]" }, StyleLayerPriority.Animation,
                scope.Reconciler.Context);

            // Assert
            Assert.That(scope.Reconciler.Context.ClipPathBindings[inner].Spec.Kind, Is.EqualTo(ClipPathKind.Inset));
        }
    }
}
