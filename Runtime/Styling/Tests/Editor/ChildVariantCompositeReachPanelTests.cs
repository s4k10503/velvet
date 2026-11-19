using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a <c>[&amp;&gt;*]:</c> payload leaves a UI Toolkit composite's own part alone on a panel whose
    /// theme dresses that part, while the field's own utility classes still paint it.
    /// </summary>
    /// <remarks>
    /// Each case reads the theme's own answer off a bare field in the same tree rather than standing a literal in
    /// for it, and covers one of the two forms a payload resolves into: a class and an inline write.
    /// </remarks>
    internal sealed class ChildVariantCompositeReachPanelTests : PanelTestBase
    {
        // bg-* is a plain USS rule: without the sheet the class-form case would land a class no rule matches
        // and read as the theme winning.
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        // Two fields in one tree — one carrying the payload, one bare. The bare one is the theme's own answer
        // for the same part on the same panel, which is what makes the comparison need no literal colour.
        private (VisualElement Payloaded, VisualElement Bare) MountPair(string payloadClassName)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(children: new VNode?[]
                {
                    V.TextField(name: "payloaded", className: payloadClassName),
                    V.TextField(name: "bare"),
                }));
            var root = _window.rootVisualElement;
            ForcePanelUpdate(root.panel);
            return (root.Q<VisualElement>("payloaded").Q<VisualElement>(TextField.textInputUssName),
                root.Q<VisualElement>("bare").Q<VisualElement>(TextField.textInputUssName));
        }

        [Test]
        public void Given_AChildVariantClassPayload_When_TheThemeDressesTheSamePart_Then_ThePartKeepsTheThemesColour()
        {
            // Arrange / Act — bg-red-500 is a plain USS rule, so a payload that reached the part would land as a
            // class the panel's theme also has a rule for.
            var (payloaded, bare) = MountPair("[&>*]:bg-red-500");

            // Assert
            Assert.That(
                (payloaded.ClassListContains("bg-red-500"),
                    payloaded.resolvedStyle.backgroundColor == bare.resolvedStyle.backgroundColor),
                Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_AChildVariantBracketPayload_When_TheThemeDressesTheSamePart_Then_ThePartKeepsTheThemesColour()
        {
            // Arrange / Act — the same colour in bracket form, which Velvet resolves to an inline write
            // instead of a class.
            var (payloaded, bare) = MountPair("[&>*]:bg-[#ff0000]");

            // Assert
            Assert.That(payloaded.resolvedStyle.backgroundColor == bare.resolvedStyle.backgroundColor, Is.True);
        }

        [Test]
        public void Given_ABackgroundClassOnTheField_When_TheThemeDressesTheInput_Then_TheInputBoxIsPainted()
        {
            // Arrange / Act — the field's own utility, with no child-combinator token, against the bare field's
            // input in the same tree.
            var (painted, bare) = MountPair("bg-red-500");

            // Assert — the box the text is typed into resolves a different background from the themed one.
            Assert.That(painted.resolvedStyle.backgroundColor == bare.resolvedStyle.backgroundColor, Is.False);
        }
    }
}
