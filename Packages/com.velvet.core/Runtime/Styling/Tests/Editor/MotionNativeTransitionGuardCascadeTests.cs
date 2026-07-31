using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds <see cref="MotionNativeTransitionGuard.DeclaredSlots"/> against UI Toolkit's own answer: each case
    /// puts a class list on a real panel carrying the bundled stylesheet, then compares the guard's slots with
    /// the slots named by the <c>transition-property</c> the cascade actually resolved.
    /// </summary>
    [TestFixture]
    internal sealed class MotionNativeTransitionGuardCascadeTests : PanelTestBase
    {
        protected override void LoadStyleSheets() =>
            VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        /// <summary>
        /// Classes go straight onto a bare element rather than through a VNode, so the class-list order is the
        /// order written at the call site and a guard answering from it would be visible.
        /// </summary>
        private VisualElement Resolve(params string[] classNames)
        {
            var element = new VisualElement();
            foreach (var className in classNames)
            {
                element.AddToClassList(className);
            }
            _window.rootVisualElement.Add(element);
            ForcePanelUpdate(element.panel);
            return element;
        }

        /// <summary>The slots a driver would contend for, read off the resolved <c>transition-property</c>.</summary>
        private static MotionTransitionSlots CascadeSlots(VisualElement element)
        {
            var slots = MotionTransitionSlots.None;
            foreach (var property in element.resolvedStyle.transitionProperty)
            {
                slots |= SlotOf(property.ToString() ?? string.Empty);
            }
            return slots;
        }

        // No per-frame driver writes filter, so a list naming it contends for nothing, exactly as an empty list
        // does. An unmapped name throws rather than defaulting: silently contributing nothing would let a new
        // utility widen the cascade without any case here going red.
        private static MotionTransitionSlots SlotOf(string ussProperty) => ussProperty switch
        {
            "all" => MotionTransitionSlots.All,
            "opacity" => MotionTransitionSlots.Opacity,
            "translate" => MotionTransitionSlots.Translate,
            "scale" => MotionTransitionSlots.Scale,
            "rotate" => MotionTransitionSlots.Rotate,
            "color" => MotionTransitionSlots.Color,
            "background-color" => MotionTransitionSlots.Color,
            "border-color" => MotionTransitionSlots.Color,
            "border-top-color" => MotionTransitionSlots.Color,
            "border-right-color" => MotionTransitionSlots.Color,
            "border-bottom-color" => MotionTransitionSlots.Color,
            "border-left-color" => MotionTransitionSlots.Color,
            // transition-property: none resolves to one entry that names no property, whose ToString is null;
            // there is no empty resolved list to read instead.
            "" => MotionTransitionSlots.None,
            "filter" => MotionTransitionSlots.None,
            _ => throw new NotSupportedException(
                $"The cascade resolved transition-property to '{ussProperty}', which this oracle does not map."),
        };

        [Test]
        public void Given_TransitionAllThenTransitionNone_When_TheGuardIsAsked_Then_ItAgreesWithTheCascade()
        {
            // Arrange
            var element = Resolve("transition-all", "transition-none");
            Assume.That(element.panel, Is.Not.Null, "Precondition: the element is on a real panel");

            // Act
            var declared = MotionNativeTransitionGuard.DeclaredSlots(element);

            // Assert
            Assert.That(declared, Is.EqualTo(CascadeSlots(element)));
        }

        [Test]
        public void Given_TransitionTransformThenTransitionColors_When_TheGuardIsAsked_Then_ItAgreesWithTheCascade()
        {
            // Arrange
            var element = Resolve("transition-transform", "transition-colors");
            Assume.That(element.panel, Is.Not.Null, "Precondition: the element is on a real panel");

            // Act
            var declared = MotionNativeTransitionGuard.DeclaredSlots(element);

            // Assert
            Assert.That(declared, Is.EqualTo(CascadeSlots(element)));
        }

        [Test]
        public void Given_TransitionAllThenTransitionColors_When_TheGuardIsAsked_Then_ItAgreesWithTheCascade()
        {
            // Arrange
            var element = Resolve("transition-all", "transition-colors");
            Assume.That(element.panel, Is.Not.Null, "Precondition: the element is on a real panel");

            // Act
            var declared = MotionNativeTransitionGuard.DeclaredSlots(element);

            // Assert
            Assert.That(declared, Is.EqualTo(CascadeSlots(element)));
        }

        [Test]
        public void Given_TransitionFilter_When_TheGuardIsAsked_Then_ItAgreesWithTheCascade()
        {
            // Arrange
            var element = Resolve("transition-filter");
            Assume.That(element.panel, Is.Not.Null, "Precondition: the element is on a real panel");

            // Act
            var declared = MotionNativeTransitionGuard.DeclaredSlots(element);

            // Assert
            Assert.That(declared, Is.EqualTo(CascadeSlots(element)));
        }

        [Test]
        public void Given_TransitionColorsWrittenBeforeTransitionTransform_When_TheGuardIsAsked_Then_ItAgreesWithTheCascade()
        {
            // Arrange — the same pair as above with the class-list order reversed; the cascade answer is
            // unchanged, because the stylesheet's declaration order decides it and the class list does not.
            var element = Resolve("transition-colors", "transition-transform");
            Assume.That(element.panel, Is.Not.Null, "Precondition: the element is on a real panel");

            // Act
            var declared = MotionNativeTransitionGuard.DeclaredSlots(element);

            // Assert
            Assert.That(declared, Is.EqualTo(CascadeSlots(element)));
        }

        [Test]
        public void Given_ATransitionFilterElement_When_AnOpacityPlayConsultsTheGuard_Then_TheResolvedTransitionStillNamesFilter()
        {
            // Arrange
            var element = Resolve("transition-filter");
            Assume.That(element.panel, Is.Not.Null, "Precondition: the element is on a real panel");

            // Act
            MotionNativeTransitionGuard.SuspendIfIntercepted(element, new object(), MotionTransitionSlots.Opacity);
            ForcePanelUpdate(element.panel);

            // Assert — StyleFilterTransitionDriver reads this list to decide whether it owns a filter change;
            // without filter in it the change becomes an instant write for as long as the play lasts.
            Assert.That(
                element.resolvedStyle.transitionProperty.Select(p => p.ToString()).ToArray(),
                Contains.Item("filter"));
        }
    }
}
