using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what becomes of the focus inside children a Suspense hides again: UI Toolkit's own handling of a
    /// focused element whose inline display becomes none, and the blur the hide itself performs. Mounted in a real
    /// <see cref="UnityEditor.EditorWindow"/> panel so a focus controller exists, focus driven as
    /// <c>HasFocusMultiLevelBubblePanelTests</c> drives it.
    /// </summary>
    [TestFixture]
    internal sealed class SuspenseHiddenFocusPanelTests : PanelTestBase
    {
        private static StateUpdater<int> s_setOwn;

        public override void SetUp()
        {
            base.SetUp();
            s_setOwn = default;
        }

        // GREEN_ON_BASE(characterization): the merge base has no Suspense hide involved in this case at all.
        // What it pins is UI Toolkit's own handling, which the hide's explicit blur is written against.
        [Test]
        public void Given_AFocusedButtonOnAPanel_When_ItsInlineDisplayBecomesNone_Then_UIToolkitLeavesItFocused()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Button(name: "focusable"));
            var button = _window.rootVisualElement.Q<Button>("focusable");
            var granted = DriveFocus(button);

            // Act
            button.style.display = DisplayStyle.None;
            ForcePanelUpdate(button.panel);

            // Assert — whether focus was granted is read with it, since a button never focused is not focused after
            Assert.That((granted, ReferenceEquals(button.focusController.focusedElement, button)), Is.EqualTo((true, true)),
                "UI Toolkit leaves the focus on an element an inline display: none hides");
        }

        // GREEN_ON_BASE(characterization): the base removes the focused button, which takes the focus with it.
        // What this pins is that a button the boundary keeps hidden gives the focus up as well.
        [Test]
        public void Given_AFocusedButtonInARevealedPrimary_When_TheBoundarySuspendsAgain_Then_ItIsNoLongerFocused()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Component(FocusHostRender, key: "focus-host"));
            var button = _window.rootVisualElement.Q<Button>("focusable");
            var controller = button.focusController;
            var granted = DriveFocus(button);

            // Act
            s_setOwn.Invoke(1);
            _mounted.FlushStateForTest();
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert — whether focus was granted is read with it, since a button never focused is not focused after
            Assert.That((granted, ReferenceEquals(controller.focusedElement, button)), Is.EqualTo((true, false)),
                "A hidden element is no focus target, so the boundary that hides it takes its focus away");
        }

        // GREEN_ON_BASE(characterization): the base blurs nothing when a boundary suspends.
        // What this pins is that the hide takes away only a focus inside the children it hides.
        [Test]
        public void Given_AFocusedButtonBesideARevealedBoundary_When_TheBoundarySuspendsAgain_Then_ItStaysFocused()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Component(FocusHostRender, key: "focus-host"));
            var button = _window.rootVisualElement.Q<Button>("beside");
            var granted = DriveFocus(button);

            // Act
            s_setOwn.Invoke(1);
            _mounted.FlushStateForTest();
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert — whether focus was granted is read with it, since a button never focused is not focused after
            Assert.That((granted, ReferenceEquals(button.focusController.focusedElement, button)), Is.EqualTo((true, true)),
                "Hiding a boundary's children leaves the focus of an element outside them where it is");
        }

        private bool DriveFocus(Button target)
        {
            _window.Focus();
            target.Focus();
            ForcePanelUpdate(target.panel);
            return ReferenceEquals(target.focusController?.focusedElement, target);
        }

        [Component]
        private static VNode FocusReaderRender()
        {
            var (own, setOwn) = Hooks.UseState(0);
            s_setOwn = setOwn;
            var value = Hooks.Use<int>(_ => own == 0
                ? VelvetTask.FromResult(0)
                : new VelvetTaskCompletionSource<int>().Task, own);
            return V.Label(text: "reader:" + value);
        }

        [Component]
        private static VNode FocusHostRender()
            => V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[]
                    {
                        V.Div(name: "primary", children: new VNode[] { V.Button(name: "focusable") }),
                        V.Component(FocusReaderRender, key: "reader"),
                    }),
                V.Button(name: "beside"),
            });
    }
}
