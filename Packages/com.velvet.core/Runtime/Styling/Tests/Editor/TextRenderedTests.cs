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
            // Act
            _mounted = V.Mount(_window.rootVisualElement, V.Label(className: "uppercase", text: "hello"));
            var label = _window.rootVisualElement.Q<Label>();

            // Assert
            Assert.That((label.text, RenderedTextProbe.Of(label)), Is.EqualTo(("hello", "HELLO")));
        }

        // GREEN_ON_BASE(characterization): the engine behaviour this pins involves no Velvet code, so the base shows it too.
        [Test]
        public void Given_AShownStringSetOnALabel_When_ItsTextChanges_Then_TheEngineShowsTheNewText()
        {
            // Arrange — pins the engine behaviour ShowText relies on: it hands the engine its string again after
            // every change of text, because the engine drops the one it was given.
            var label = new Label("a");
            label.experimental.SetRenderedText("A");

            // Act
            label.text = "b";

            // Assert
            Assert.That(RenderedTextProbe.Of(label), Is.EqualTo("b"));
        }

        // GREEN_ON_BASE(characterization): an uppercase label shows upper case after a text change, as the base does.
        [Test]
        public void Given_AnUppercaseLabelProp_When_ItsTextChangesToAnotherCasing_Then_TheShownStringIsStillUppercased()
        {
            // Arrange — both texts show as ONE, so only the text the shown string was made from tells the
            // resolver that the engine dropped it.
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderCasedLabel));

            // Act
            s_setText.Invoke("One");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            // Assert
            Assert.That(RenderedTextProbe.Of(_window.rootVisualElement.Q<Label>()), Is.EqualTo("ONE"));
        }

        [Test]
        public void Given_AnUnderlinedLabelWithRichTextOff_When_ItsTextChanges_Then_NoMarkupIsShown()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderUnderlinedLabel));
            var label = _window.rootVisualElement.Q<Label>();
            label.enableRichText = false;

            // Act
            s_setText.Invoke("two");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            // Assert
            Assert.That(RenderedTextProbe.Of(label), Is.EqualTo("two"));
        }

        [Test]
        public void Given_AnUnderlinedLabel_When_RichTextIsTurnedOff_Then_TheMarkupIsDroppedOnceItRepaints()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Label(className: "underline", text: "one"));
            var label = _window.rootVisualElement.Q<Label>();
            var panel = _window.rootVisualElement.panel;
            ForcePanelUpdate(panel);

            // Act
            label.enableRichText = false;
            ForcePanelUpdate(panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(panel);

            // Assert
            Assert.That(RenderedTextProbe.Of(label), Is.EqualTo("one"));
        }

        [Test]
        public void Given_AnUppercaseLabelElidedFromTheStart_When_ItHasRepainted_Then_TheEngineElidesTheUppercasedString()
        {
            // Arrange — the engine elides a leaf's text, not the string it was handed, from the start or the
            // middle, so the leaf holds the string it shows as its text.
            _mounted = V.Mount(_window.rootVisualElement, V.Label(
                className: "uppercase", text: "hello",
                refCallback: element =>
                {
                    // Inline, because the panel of this fixture carries no bundled sheet to resolve text-ellipsis.
                    element.style.textOverflow = TextOverflow.Ellipsis;
                    element.style.unityTextOverflowPosition = TextOverflowPosition.Start;
                    return null;
                }));
            var label = _window.rootVisualElement.Q<Label>();
            var panel = _window.rootVisualElement.panel;

            // Act — the first repaint finds the ellipsis resolved, the tick after it builds the string again.
            ForcePanelUpdate(panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(panel);
            ForcePanelUpdate(panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(panel);

            // Assert
            Assert.That(label.text, Is.EqualTo("HELLO"));
        }

        [Test]
        public void Given_TabularNumsOnALabel_When_Mounted_Then_ItsDigitsAreOneMonospacedRun()
        {
            // Act
            _mounted = V.Mount(_window.rootVisualElement, V.Label(className: "tabular-nums", text: "11"));
            var panel = _window.rootVisualElement.panel;
            var label = _window.rootVisualElement.Q<Label>();
            ForcePanelUpdate(panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(panel);

            // Assert
            Assert.That(RenderedTextProbe.Of(label), Does.StartWith("<mspace=").And.EndWith(">11</mspace>"));
        }

        [Test]
        public void Given_NormalNumsUnderTabularNums_When_Mounted_Then_TheDigitsAreLeftAlone()
        {
            // Act
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(className: "tabular-nums", V.Label(className: "normal-nums", text: "11")));
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(RenderedTextProbe.Of(_window.rootVisualElement.Q<Label>()), Is.EqualTo("11"));
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
