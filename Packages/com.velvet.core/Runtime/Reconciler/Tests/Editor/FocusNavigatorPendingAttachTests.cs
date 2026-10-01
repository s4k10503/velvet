using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the focus navigator's deferred attach on an element with no panel: one hook however many
    /// times it is asked for, gone from the element once it has fired, asked for afresh after that, and
    /// taken off the element when the element is torn down or the reconciler disposed.
    /// </summary>
    [TestFixture]
    internal sealed class FocusNavigatorPendingAttachTests : PanelTestBase
    {
        private Reconciler _reconciler;
        private VisualElement _element;
        private EditorWindow _secondWindow;
        private static StateUpdater<bool> s_setVictimShown;

        public override void SetUp()
        {
            base.SetUp();
            _reconciler = new Reconciler();
            _element = new VisualElement();
            s_setVictimShown = default;
        }

        public override void TearDown()
        {
            _reconciler?.Dispose();
            _reconciler = null;
            if (_secondWindow != null)
            {
                _secondWindow.Close();
                Object.DestroyImmediate(_secondWindow);
                _secondWindow = null;
            }
            base.TearDown();
        }

        // GREEN_ON_BASE(refactor): the base's tuple list dedups by element too; moving the hook onto
        // NavigatorPendingAttach has to keep one per element.
        [Test]
        public void Given_AnElementWithNoPanel_When_TheNavigatorIsAskedTwice_Then_ItHoldsOneHook()
        {
            // Arrange
            var before = CallbackRegistryProbe.BubbleUpCallbackCount(_element);

            // Act
            FiberFocusNavigator.EnsureAttached(_element, _reconciler.Context);
            FiberFocusNavigator.EnsureAttached(_element, _reconciler.Context);

            // Assert
            Assert.That(CallbackRegistryProbe.BubbleUpCallbackCount(_element) - before, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(refactor): the base's hook unregisters itself when it fires too; moving it
        // onto NavigatorPendingAttach has to keep that.
        [Test]
        public void Given_ADeferredAttach_When_TheElementReachesAPanel_Then_TheHookLeavesTheElement()
        {
            // Arrange
            var before = CallbackRegistryProbe.BubbleUpCallbackCount(_element);
            FiberFocusNavigator.EnsureAttached(_element, _reconciler.Context);

            // Act
            _window.rootVisualElement.Add(_element);

            // Assert — the attachment is read beside the count, because a hook that never fired leaves the
            // count where firing should have.
            Assert.That(
                (_reconciler.Context.NavigatorAttachments.ContainsKey(_element.panel.visualTree),
                    CallbackRegistryProbe.BubbleUpCallbackCount(_element) - before),
                Is.EqualTo((true, 0)));
        }

        // GREEN_ON_BASE(refactor): the base's hook drops its list entry when it fires too, so a later
        // request registers afresh; moving it onto NavigatorPendingAttach has to keep that.
        [Test]
        public void Given_ADeferredAttachThatFired_When_TheDetachedElementIsAskedForAgain_Then_TheNavigatorAttachesToItsNextPanel()
        {
            // Arrange
            FiberFocusNavigator.EnsureAttached(_element, _reconciler.Context);
            _window.rootVisualElement.Add(_element);
            _element.RemoveFromHierarchy();
            FiberFocusNavigator.EnsureAttached(_element, _reconciler.Context);
            _secondWindow = ScriptableObject.CreateInstance<SecondHostWindow>();
            _secondWindow.Show();

            // Act
            _secondWindow.rootVisualElement.Add(_element);

            // Assert
            Assert.That(
                _reconciler.Context.NavigatorAttachments.ContainsKey(_secondWindow.rootVisualElement.panel.visualTree),
                Is.True);
        }

        // GREEN_ON_BASE(refactor): the base's DetachAll unregisters a hook that never fired too; moving
        // it onto NavigatorPendingAttach has to keep that.
        [Test]
        public void Given_ADeferredAttach_When_TheReconcilerIsDisposed_Then_TheHookLeavesTheElement()
        {
            // Arrange
            var before = CallbackRegistryProbe.BubbleUpCallbackCount(_element);
            FiberFocusNavigator.EnsureAttached(_element, _reconciler.Context);
            var held = CallbackRegistryProbe.BubbleUpCallbackCount(_element) - before;

            // Act
            _reconciler.Dispose();
            _reconciler = null;

            // Assert — the held count is folded in, since a hook never registered leaves nothing either.
            Assert.That((held, CallbackRegistryProbe.BubbleUpCallbackCount(_element) - before), Is.EqualTo((1, 0)));
        }

        [Component]
        private static VNode VictimHostRender()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setVictimShown = setShown;
            return V.Div(children: new VNode[] { shown ? V.Div(name: "victim") : null });
        }

        // GREEN_ON_BASE(refactor): the base's element teardown unregisters a pending hook too;
        // moving it onto NavigatorPendingAttach has to keep that.
        [Test]
        public void Given_ADeferredAttachOnARenderedElement_When_TheElementLeavesTheTree_Then_TheHookLeavesTheElement()
        {
            // Arrange — mounted on an element with no panel, so the attach stays deferred.
            var mountRoot = new VisualElement();
            _mounted = V.Mount(mountRoot, V.Component(VictimHostRender, key: "host"));
            var victim = mountRoot.Q<VisualElement>("victim");
            var before = CallbackRegistryProbe.BubbleUpCallbackCount(victim);
            FiberFocusNavigator.EnsureAttached(victim, _mounted.Root.Reconciler.Context);
            var held = CallbackRegistryProbe.BubbleUpCallbackCount(victim) - before;

            // Act
            s_setVictimShown.Invoke(false);
            _mounted.FlushStateForTest();

            // Assert — the held count is folded in, since a hook never registered leaves nothing either.
            Assert.That((held, CallbackRegistryProbe.BubbleUpCallbackCount(victim) - before), Is.EqualTo((1, 0)));
        }

        private sealed class SecondHostWindow : EditorWindow
        {
        }
    }
}
