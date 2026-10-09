using System;
using System.Globalization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A percentage slice inset against an image Velvet does not write — here one written straight into the
    /// element's inline style, which reaches it the way a stylesheet's does, without passing through Velvet — and
    /// against the image that shows once an override goes away. Every event is the panel's own.
    /// </summary>
    [TestFixture]
    internal sealed class NineSlicePercentPanelTests : PanelTestBase
    {
        private static StateUpdater<int> s_setStep;
        private static Func<int, StyleOverrides> s_stylesFor;
        private static Func<VisualElement, Action> s_ref;
        private Texture2D _tall;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            _tall = new Texture2D(40, 80);
            s_setStep = default;
            s_stylesFor = _ => null;
            s_ref = null;
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            UnityEngine.Object.DestroyImmediate(_tall);
        }

        [Component]
        private static VNode RenderCard()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(className: "w-[100px] h-[40px] slice-[25%]", name: "card", styles: s_stylesFor(step),
                refCallback: s_ref);
        }

        private VisualElement Card => _window.rootVisualElement.Q<VisualElement>("card");

        [Test]
        public void Given_AnImageTheRefCallbackSets_When_TheElementIsFirstLaidOut_Then_ThePercentFollowsIt()
        {
            // Arrange — no stylesheet, so the first layout's geometry change is the only event that arrives;
            // read before it, so a slice resolved against the image at once cannot pass.
            var tall = _tall;
            s_ref = element =>
            {
                element.style.backgroundImage = new StyleBackground(tall);
                return null;
            };
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderCard));
            var before = Insets(Card);

            // Act
            ForcePanelUpdate(Card.panel);

            // Assert
            Assert.That((before, Insets(Card)), Is.EqualTo(("0,0,0,0", "20,10,20,10")));
        }

        [Test]
        public void Given_AnImageWrittenAfterLayout_When_TheElementIsRestyled_Then_ThePercentFollowsIt()
        {
            // Arrange — the bundled stylesheet is attached and the box is laid out before the image is written,
            // so the restyle a class change causes is the event, with no geometry change beside it.
            VelvetStyleUtilities.AttachTo(_window.rootVisualElement);
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderCard));
            ForcePanelUpdate(Card.panel);
            Card.style.backgroundImage = new StyleBackground(_tall);
            var before = Insets(Card);

            // Act
            Card.AddToClassList("restyled");
            ForcePanelUpdate(Card.panel);

            // Assert
            Assert.That((before, Insets(Card)), Is.EqualTo(("0,0,0,0", "20,10,20,10")));
        }

        [Test]
        public void Given_APercentSliceOverAnImageOverride_When_TheOverrideIsRemoved_Then_ItResolvesAgainstWhatShowsNow()
        {
            // Arrange — a style pass while the override shows leaves the computed style holding its image.
            var tall = _tall;
            s_stylesFor = step => step == 0 ? new StyleOverrides { BackgroundImage = new StyleBackground(tall) } : null;
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderCard));
            ForcePanelUpdate(Card.panel);
            var before = Insets(Card);

            // Act — no style pass and no scheduler tick follow the patch.
            s_setStep.Invoke(1);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — no image shows now, so every percentage is of nothing.
            Assert.That((before, Insets(Card)), Is.EqualTo(("20,10,20,10", "0,0,0,0")));
        }

        private static string Insets(VisualElement element)
        {
            var style = element.style;
            return string.Join(",", Read(style.unitySliceTop), Read(style.unitySliceRight),
                Read(style.unitySliceBottom), Read(style.unitySliceLeft));
        }

        private static string Read(StyleInt inset)
            => inset.keyword == StyleKeyword.Null ? "null" : inset.value.ToString(CultureInfo.InvariantCulture);
    }
}
