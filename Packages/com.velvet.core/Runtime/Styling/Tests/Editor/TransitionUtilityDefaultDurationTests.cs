using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that the transition-* property utilities work standalone, like their Tailwind
    /// counterparts: each bundles a default transition-duration and timing-function alongside its
    /// transition-property. UI Toolkit's initial transition-duration is 0s, so a property-only
    /// utility (e.g. <c>transition-opacity</c> plus a hover-driven value change) never visibly
    /// animated until the developer also added a duration-* class — while the sibling
    /// <c>transition-transform</c> already bundled its own duration, leaving one of six utilities
    /// functional standalone. Explicit duration-*/ease-* classes still override (declared later in
    /// the same sheet).
    /// </summary>
    [TestFixture]
    internal sealed class TransitionUtilityDefaultDurationTests : PanelTestBase
    {
        private const string StyleSheetPath = "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";

        protected override void LoadStyleSheets()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            _window.rootVisualElement.styleSheets.Add(sheet);
        }

        private VisualElement MountLeaf(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "leaf", className: className));
            var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");
            ForcePanelUpdate(leaf.panel);
            return leaf;
        }

        [Test]
        public void Given_TransitionOpacityAlone_When_Resolved_Then_ItCarriesANonZeroDefaultDuration()
        {
            // Arrange / Act — the utility stands alone, with no duration-* class.
            var leaf = MountLeaf("transition-opacity");

            // Assert — the class animates by itself instead of resolving to the 0s initial value.
            Assert.That(leaf.resolvedStyle.transitionDuration.First().value, Is.GreaterThan(0f));
        }

        [Test]
        public void Given_TransitionColorsAlone_When_Resolved_Then_ItCarriesANonZeroDefaultDuration()
        {
            // Arrange / Act
            var leaf = MountLeaf("transition-colors");

            // Assert
            Assert.That(leaf.resolvedStyle.transitionDuration.First().value, Is.GreaterThan(0f));
        }

        [Test]
        public void Given_TransitionAllAlone_When_Resolved_Then_ItCarriesANonZeroDefaultDuration()
        {
            // Arrange / Act
            var leaf = MountLeaf("transition-all");

            // Assert
            Assert.That(leaf.resolvedStyle.transitionDuration.First().value, Is.GreaterThan(0f));
        }

        [Test]
        public void Given_TransitionOpacityWithExplicitDurationZero_When_Resolved_Then_TheExplicitClassWins()
        {
            // Arrange / Act — duration-* is declared after the transition-* utilities, so an
            // explicit opt-out still overrides the bundled default.
            var leaf = MountLeaf("transition-opacity duration-0");

            // Assert
            Assert.That(leaf.resolvedStyle.transitionDuration.First().value, Is.EqualTo(0f));
        }

        [Test]
        public void Given_TransitionColorsAlone_When_Resolved_Then_ItsDefaultCurveEasesInAndOut()
        {
            // Tailwind's default transition timing is cubic-bezier(0.4, 0, 0.2, 1); UI Toolkit has no
            // cubic-bezier, so the bundled default is its closest keyword, ease-in-out (not fast-start ease-out).
            var leaf = MountLeaf("transition-colors");

            Assert.That(leaf.resolvedStyle.transitionTimingFunction.First().mode, Is.EqualTo(EasingMode.EaseInOut));
        }

        [Test]
        public void Given_AnExplicitEaseClass_When_Resolved_Then_ItStillOverridesTheDefaultCurve()
        {
            // The .ease-* utilities are declared after the transition-* defaults, so an explicit curve wins.
            var leaf = MountLeaf("transition-colors ease-linear");

            Assert.That(leaf.resolvedStyle.transitionTimingFunction.First().mode, Is.EqualTo(EasingMode.Linear));
        }
    }
}
