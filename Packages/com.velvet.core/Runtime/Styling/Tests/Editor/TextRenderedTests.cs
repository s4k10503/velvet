using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A text leaf keeps the text the app gave and shows the string the text effects make from it, through the
    /// engine's rendered-text hook. GWT, one assert.
    /// </summary>
    [TestFixture]
    internal sealed class TextRenderedTests : PanelTestBase
    {
        private static StateUpdater<string> s_setText;

        protected override Rect WindowSize => new Rect(0, 0, 400, 400);

        public override void SetUp()
        {
            s_setText = default;
            base.SetUp();
        }

        [Test]
        public void Given_UppercaseOnALabel_When_Mounted_Then_TheTextIsWhatTheAppGaveAndTheShownStringIsUppercased()
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Label(className: "uppercase", text: "hello"));
            var label = _window.rootVisualElement.Q<Label>();

            Assert.That((label.text, RenderedTextProbe.Of(label)), Is.EqualTo(("hello", "HELLO")));
        }

        [Test]
        public void Given_AShownStringSetOnALabel_When_ItsTextChanges_Then_TheEngineShowsTheNewText()
        {
            // Pins the engine behaviour ShowText relies on: it hands the engine its string again after every
            // change of text, because the engine drops the one it was given.
            var label = new Label("a");
            label.experimental.SetRenderedText("A");

            label.text = "b";

            Assert.That(RenderedTextProbe.Of(label), Is.EqualTo("b"));
        }

        [Test]
        public void Given_AnUppercaseLabelProp_When_ItsTextChangesToAnotherCasing_Then_TheShownStringIsStillUppercased()
        {
            // The two texts shown the same, so only the prop write that resets the engine's string tells the
            // resolver the string it handed over is gone.
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderCasedLabel));
            s_setText.Invoke("One");

            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            Assert.That(RenderedTextProbe.Of(_window.rootVisualElement.Q<Label>()), Is.EqualTo("ONE"));
        }

        [Test]
        public void Given_AnUnderlinedLabelWithRichTextOff_When_ItsTextChanges_Then_NoMarkupIsShown()
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderUnderlinedLabel));
            var label = _window.rootVisualElement.Q<Label>();
            label.enableRichText = false;

            s_setText.Invoke("two");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            Assert.That(RenderedTextProbe.Of(label), Is.EqualTo("two"));
        }

        [Component]
        private static VNode RenderCasedLabel()
        {
            var (text, setText) = Hooks.UseState("one");
            s_setText = setText;
            return V.Label(className: "uppercase", text: text);
        }

        [Component]
        private static VNode RenderUnderlinedLabel()
        {
            var (text, setText) = Hooks.UseState("one");
            s_setText = setText;
            return V.Label(className: "underline", text: text);
        }
    }
}
