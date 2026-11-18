using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the focus a <c>V.FocusScope</c> with <c>autoFocus</c> takes when its <c>V.Portal</c> mounts into a target
    /// already on a panel, for a boundary that is showing its children and for one keeping them hidden.
    /// </summary>
    internal sealed class SuspenseHiddenPortalFocusTests : PanelTestBase
    {
        private static StateUpdater<int> s_setBlocked;
        private static StateUpdater<bool> s_setPortal;
        private static VisualElement s_target;
        private static VelvetTaskCompletionSource<int> s_source;

        public override void SetUp()
        {
            s_setBlocked = default;
            s_setPortal = default;
            s_source = null;
            base.SetUp();
            s_target = new VisualElement { name = "target" };
            _window.rootVisualElement.Add(s_target);
            _mounted = V.Mount(_window.rootVisualElement, V.Component(HostRender, key: "host"));
            _window.Focus();
        }

        private void Update(StateUpdater<int> setter, int value)
        {
            setter.Invoke(value);
            _mounted.FlushStateForTest();
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private void ShowPortal()
        {
            s_setPortal.Invoke(true);
            _mounted.FlushStateForTest();
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        // GREEN_ON_BASE(characterization): the base already leaves the autofocused button focused where nothing hides it.
        // What this pins is that the hidden case below measures a focus the mount did take.
        [Test]
        public void Given_APortalFocusScopeMountedUnderARevealedBoundary_When_ItMounts_Then_ItsButtonHoldsFocus()
        {
            // Arrange
            var controller = _window.rootVisualElement.panel.focusController;

            // Act
            ShowPortal();

            // Assert
            Assert.That(controller.focusedElement, Is.SameAs(s_target.Q<Button>("focusable")));
        }

        [Test]
        public void Given_APortalFocusScopeMountedUnderAHiddenBoundary_When_ItMounts_Then_NothingHoldsFocus()
        {
            // Arrange
            var controller = _window.rootVisualElement.panel.focusController;
            Update(s_setBlocked, 1);

            // Act
            ShowPortal();

            // Assert
            Assert.That(controller.focusedElement, Is.Null);
        }

        [Test]
        public void Given_APortalFocusScopeMountedUnderAHiddenBoundary_When_TheBoundaryReveals_Then_ItsButtonTakesFocusOnlyThen()
        {
            // Arrange
            var controller = _window.rootVisualElement.panel.focusController;
            Update(s_setBlocked, 1);
            ShowPortal();
            var whileHidden = controller.focusedElement == null ? "none" : "held";

            // Act
            s_source.TrySetResult(5);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(whileHidden + "/" + (ReferenceEquals(controller.focusedElement, s_target.Q<Button>("focusable")) ? "button" : "other"),
                Is.EqualTo("none/button"));
        }

        [Component]
        private static VNode ReaderRender(int blocked)
        {
            var value = Hooks.Use<int>(_ => blocked > 0
                ? (s_source = new VelvetTaskCompletionSource<int>()).Task
                : VelvetTask.FromResult(0), blocked);
            return V.Label(text: "reader:" + value);
        }

        [Component]
        private static VNode HostRender()
        {
            var (blocked, setBlocked) = Hooks.UseState(0);
            var (portal, setPortal) = Hooks.UseState(false);
            s_setBlocked = setBlocked;
            s_setPortal = setPortal;
            return V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[]
                    {
                        V.Component(ReaderRender, blocked, key: "reader"),
                        portal
                            ? V.Portal(s_target, children: new VNode[]
                            {
                                V.FocusScope(autoFocus: true, children: new VNode[] { V.Button(name: "focusable") }),
                            })
                            : null,
                    }),
            });
        }
    }
}
