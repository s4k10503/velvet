using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Regression coverage for losing focus DURING a discrete-event commit. When a focused element is removed by the
    /// handler it is running inside (a self-removing button), Unity dispatches a blur/detach to that element
    /// synchronously as the reconciler removes it from the panel — a real panel event re-entering mid-commit. The
    /// reconciler must remove the element cleanly, release focus, and run the focused element's variant-manipulator
    /// blur path without throwing or stranding focus on a detached element. Mounted in a real <see cref="EditorWindow"/>
    /// panel so a focus controller exists. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class FocusLossDuringCommitTests
    {
        private EditorWindow _window;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");

            _window = ScriptableObject.CreateInstance<TestHostWindow>();
            _window.Show();
            ResetAll();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            if (_window != null)
            {
                _window.Close();
                UnityEngine.Object.DestroyImmediate(_window);
                _window = null;
            }
        }

        private static StateUpdater<bool> s_setShow;
        private static void ResetAll() => s_setShow = default;

        // A focusable button carrying a focus: variant (so removing it while focused runs the manipulator's blur
        // path). Its own click handler removes it, so the blur is delivered while the removal commit is on the stack.
        [Component]
        private static VNode SelfRemovingHost()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_setShow = setShow;
            return V.Div(name: "host", children: new VNode[]
            {
                show
                    ? V.Button(name: "focusable", className: "focus:ring", onClick: () => setShow.Invoke(_ => false))
                    : V.Fragment(Array.Empty<VNode>()),
                V.Label(name: "keep", text: "keep"),
            });
        }

        // Mounts and drives focus onto the button as deterministically as batchmode allows: focus the window,
        // call Focus(), then force the panel update that commits the pending focus change.
        private Button MountAndFocus()
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Component(SelfRemovingHost, key: "host"));
            var btn = _window.rootVisualElement.Q<Button>("focusable");
            _window.Focus();
            btn.Focus();
            EditorPanelTestHelpers.ForcePanelUpdate(btn.panel);
            return btn;
        }

        [Test]
        public void Given_AFocusedSelfRemovingButton_When_ItIsClicked_Then_ItIsRemovedWithoutThrowing()
        {
            // Arrange — a self-removing button on a live panel (focused if the environment grants it; the removal
            // path is exercised regardless, and must not throw whether or not the blur is delivered).
            var btn = MountAndFocus();

            // Act — the button removes itself from inside its own discrete click handler.
            btn.SimulateClick();

            // Assert — the self-removal committed cleanly inside the event; the button is gone.
            Assert.IsNull(_window.rootVisualElement.Q<Button>("focusable"));
        }

        [Test]
        public void Given_AFocusedSelfRemovingButton_When_ItIsClicked_Then_FocusIsNotStrandedOnTheDetachedButton()
        {
            // Arrange — a self-removing button on a live panel (focused when the environment grants it; batchmode does
            // not always hand an off-screen EditorWindow real focus, so this asserts the invariant unconditionally
            // rather than gating on it: the controller must never end up pointing at the detached button).
            var btn = MountAndFocus();
            var controller = btn.panel.focusController;

            // Act — the button removes itself mid-commit (blur delivered as it detaches).
            btn.SimulateClick();

            // Assert — focus is not stranded on the detached button.
            Assert.That(controller.focusedElement, Is.Not.SameAs(btn));
        }

        [Test]
        public void Given_AFocusedSelfRemovingButton_When_ItIsClicked_Then_TheSurvivingSiblingRemains()
        {
            // Arrange — a focused, self-removing button beside a keep-alive sibling.
            var btn = MountAndFocus();

            // Act — the button removes itself mid-commit.
            btn.SimulateClick();

            // Assert — the unrelated sibling is untouched by the focus-loss removal.
            Assert.IsNotNull(_window.rootVisualElement.Q<Label>("keep"));
        }

        /// <summary>Minimal EditorWindow host that supplies a real panel with a focus controller.</summary>
        private sealed class TestHostWindow : EditorWindow { }
    }
}
