using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a <c>[&amp;&gt;*]:</c> payload does to a UI Toolkit composite's own part on a panel whose
    /// theme already dresses that part — the escape hatch a caller has for a control Velvet hands no VNode
    /// for.
    /// </summary>
    /// <remarks>
    /// <see cref="ChildVariantClassParityTests"/> pins that the token REACHES a TextField's
    /// <c>#unity-text-input</c>: the control redirects nothing, so the walk's children are its own parts.
    /// Reaching a part and painting it are separate questions once a theme has an opinion about the same
    /// property. Each case reads the theme's own answer off a bare field in the same tree rather than standing
    /// a literal in for it, and covers one of the two forms a payload resolves into: a class and an inline
    /// write.
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

        // GREEN_ON_BASE(characterization): the base lets this utility class win the cascade already.
        // No production code changes here; the case pins the panel result the guide relies on.
        [Test]
        public void Given_AChildVariantClassPayload_When_TheThemeDressesTheSamePart_Then_ThePayloadWins()
        {
            // Arrange / Act — bg-red-500 is a plain USS rule, so the payload lands as a class on a part the
            // panel's theme also has a rule for.
            var (payloaded, bare) = MountPair("[&>*]:bg-red-500");

            // Assert — the class is on the part and changes the resolved colour from the same themed part
            // without the payload.
            Assert.That(
                (payloaded.ClassListContains("bg-red-500"),
                    payloaded.resolvedStyle.backgroundColor == bare.resolvedStyle.backgroundColor),
                Is.EqualTo((true, false)));
        }

        // GREEN_ON_BASE(characterization): the base writes a bracket payload as inline style already.
        // Paired with the class-form case above, this pins both projection paths against the themed part.
        [Test]
        public void Given_AChildVariantBracketPayload_When_TheThemeDressesTheSamePart_Then_ThePayloadWins()
        {
            // Arrange / Act — the same colour in bracket form, which Velvet resolves to an inline write
            // instead of a class.
            var (payloaded, bare) = MountPair("[&>*]:bg-[#ff0000]");

            // Assert — the declared colour reaches resolvedStyle, and it is not what the bare field resolves.
            Assert.That(
                (payloaded.resolvedStyle.backgroundColor == Color.red,
                    payloaded.resolvedStyle.backgroundColor == bare.resolvedStyle.backgroundColor),
                Is.EqualTo((true, false)));
        }
    }
}
