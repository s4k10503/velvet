using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Two bundled utilities that write one property, read off a real panel with the stylesheet attached: the one
    /// Tailwind emits later holds it, whichever is written first.
    /// </summary>
    [TestFixture]
    internal sealed class StyleSheetTailwindOrderPanelTests : PanelTestBase
    {
        private static readonly Color Red500 = new Color32(0xEF, 0x44, 0x44, 0xFF);

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        [TestCase("w-px w-4")]
        [TestCase("w-4 w-px")]
        public void Given_TwoWidthsOfTheScale_When_LaidOut_Then_TheStepTailwindEmitsLaterHoldsTheWidth(string className)
        {
            // Act
            var leaf = MountAndResolve(className);

            // Assert — --space-px is 1px and --space-4 is 16px, and digits sort ahead of letters.
            Assert.That(leaf.resolvedStyle.width, Is.EqualTo(1f));
        }

        [TestCase("w-px w-[10px]")]
        [TestCase("w-[10px] w-px")]
        public void Given_AnInlineWidthAndAStepClassTailwindEmitsLater_When_LaidOut_Then_TheClassHoldsTheWidth(string className)
        {
            // Act
            var leaf = MountAndResolve(className);

            // Assert — --space-px is 1px, and px sorts after the bracket.
            Assert.That(leaf.resolvedStyle.width, Is.EqualTo(1f));
        }

        [TestCase("w-1/2 w-4")]
        [TestCase("w-4 w-1/2")]
        public void Given_AFractionWidthAndAStepClassTailwindEmitsLater_When_LaidOut_Then_TheClassHoldsTheWidth(string className)
        {
            // Act
            var leaf = MountAndResolve(className);

            // Assert — --space-4 is 16px, and the 1 of 1/2 sorts ahead of the 4.
            Assert.That(leaf.resolvedStyle.width, Is.EqualTo(16f));
        }

        [TestCase("rotate-6 rotate-n6")]
        [TestCase("rotate-n6 rotate-6")]
        public void Given_ARotationBesideItsNegatedClass_When_LaidOut_Then_TheOneTailwindEmitsLaterHoldsTheAngle(string className)
        {
            // Act
            var leaf = MountAndResolve(className);

            // Assert — Tailwind emits -rotate-6 ahead of rotate-6.
            Assert.That(leaf.resolvedStyle.rotate.angle.ToDegrees(), Is.EqualTo(6f).Within(1e-3f));
        }

        [TestCase("p-1 p-px")]
        [TestCase("p-px p-1")]
        public void Given_TwoPaddingsOfTheScale_When_LaidOut_Then_TheStepTailwindEmitsLaterHoldsThePadding(string className)
        {
            // Act
            var leaf = MountAndResolve(className);

            // Assert — --space-px is 1px and --space-1 is 4px.
            Assert.That(leaf.resolvedStyle.paddingTop, Is.EqualTo(1f));
        }

        [TestCase("items-center items-start")]
        [TestCase("items-start items-center")]
        public void Given_TwoAlignments_When_LaidOut_Then_TheOneTailwindEmitsLaterHoldsTheAlignment(string className)
        {
            // Act
            var leaf = MountAndResolve(className);

            // Assert
            Assert.That(leaf.resolvedStyle.alignItems, Is.EqualTo(Align.FlexStart));
        }

        [TestCase("flex-wrap flex-nowrap")]
        [TestCase("flex-nowrap flex-wrap")]
        public void Given_TwoWrapModes_When_LaidOut_Then_TheOneTailwindEmitsLaterHoldsTheWrap(string className)
        {
            // Act
            var leaf = MountAndResolve(className);

            // Assert
            Assert.That(leaf.resolvedStyle.flexWrap, Is.EqualTo(Wrap.Wrap));
        }

        [TestCase("bg-blue-500 bg-red-500")]
        [TestCase("bg-red-500 bg-blue-500")]
        public void Given_TwoPaletteBackgrounds_When_LaidOut_Then_TheOneTailwindEmitsLaterHoldsTheColor(string className)
        {
            // Act
            var leaf = MountAndResolve(className);

            // Assert
            Assert.That(leaf.resolvedStyle.backgroundColor, Is.EqualTo(Red500));
        }

        [TestCase("text-sm text-lg")]
        [TestCase("text-lg text-sm")]
        public void Given_TwoNamedTextSizes_When_LaidOut_Then_TheOneTailwindEmitsLaterHoldsTheSize(string className)
        {
            // Act
            var leaf = MountAndResolve(className);

            // Assert — --text-sm is 14px.
            Assert.That(leaf.resolvedStyle.fontSize, Is.EqualTo(14f));
        }

        [TestCase("rounded-lg rounded-sm")]
        [TestCase("rounded-sm rounded-lg")]
        public void Given_TwoRadiusSteps_When_LaidOut_Then_TheOneTailwindEmitsLaterHoldsTheCorners(string className)
        {
            // Act
            var leaf = MountAndResolve(className);

            // Assert — --radius-sm is 4px.
            Assert.That(leaf.resolvedStyle.borderTopLeftRadius, Is.EqualTo(4f));
        }

        [TestCase("truncate whitespace-normal")]
        [TestCase("whitespace-normal truncate")]
        public void Given_TruncateBesideAWhiteSpaceClass_When_LaidOut_Then_TheWhiteSpaceClassHoldsTheProperty(string className)
        {
            // Act
            var leaf = MountAndResolve(className);

            // Assert
            Assert.That(leaf.resolvedStyle.whiteSpace, Is.EqualTo(WhiteSpace.Normal));
        }

        [TestCase("transition-colors transition-opacity")]
        [TestCase("transition-opacity transition-colors")]
        public void Given_TwoTransitionUtilities_When_LaidOut_Then_TheOneTailwindEmitsLaterHoldsTheProperty(string className)
        {
            // Act
            var leaf = MountAndResolve(className);

            // Assert
            Assert.That(leaf.resolvedStyle.transitionProperty.Select(property => property.ToString()),
                Is.EqualTo(new[] { "opacity" }));
        }

        // GREEN_ON_BASE(characterization): the base sheet already declares flex-row after flex-col.
        // Declaring flex-col last instead reddens both cases.
        [TestCase("flex-col flex-row")]
        [TestCase("flex-row flex-col")]
        public void Given_TwoFlexDirections_When_LaidOut_Then_TheOneTailwindEmitsLaterHoldsTheDirection(string className)
        {
            // Act
            var leaf = MountAndResolve(className);

            // Assert
            Assert.That(leaf.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Row));
        }

        // GREEN_ON_BASE(characterization): the base sheet already declares hidden after flex.
        // Declaring flex last instead reddens both cases.
        [TestCase("flex hidden")]
        [TestCase("hidden flex")]
        public void Given_FlexBesideHidden_When_LaidOut_Then_TheOneTailwindEmitsLaterHoldsTheDisplay(string className)
        {
            // Act
            var leaf = MountAndResolve(className);

            // Assert
            Assert.That(leaf.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
        }
    }
}
