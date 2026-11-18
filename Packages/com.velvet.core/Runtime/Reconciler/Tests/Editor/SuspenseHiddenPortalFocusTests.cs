using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the focus a <c>V.FocusScope</c> with <c>autoFocus</c> takes when its <c>V.Portal</c> mounts into a target
    /// already on a panel, for a boundary that is showing its children and for one keeping them hidden.
    /// </summary>
    internal sealed class SuspenseHiddenPortalFocusTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        private static StateUpdater<int> s_setBlocked;
        private static StateUpdater<int> s_setBlockedOther;
        private static StateUpdater<bool> s_setPortal;
        private static StateUpdater<bool> s_setScoped;
        private static VisualElement s_target;
        private static VelvetTaskCompletionSource<int> s_source;
        private static VelvetTaskCompletionSource<int> s_sourceOther;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            s_setBlocked = default;
            s_setBlockedOther = default;
            s_setPortal = default;
            s_setScoped = default;
            s_source = null;
            s_sourceOther = null;
            s_target = new VisualElement { name = "target" };
            _host.Root.Add(s_target);
            _mounted = V.Mount(_host.Root, V.Component(HostRender, key: "host"));
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        private void Update<T>(StateUpdater<T> setter, T value)
        {
            setter.Invoke(value);
            _mounted.FlushStateForTest();
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private void Resolve(VelvetTaskCompletionSource<int> source)
        {
            source.TrySetResult(5);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private Focusable Focused => _host.Panel.focusController.focusedElement;

        // GREEN_ON_BASE(characterization): the base already leaves the autofocused button focused where nothing hides it.
        // What this pins is that the hidden cases below measure a focus the mount did take.
        [Test]
        public void Given_APortalFocusScopeMountedUnderARevealedBoundary_When_ItMounts_Then_ItsButtonHoldsFocus()
        {
            // Arrange / Act
            Update(s_setPortal, true);
            Update(s_setScoped, true);

            // Assert
            Assert.That(Focused, Is.SameAs(s_target.Q<Button>("focusable")));
        }

        [Test]
        public void Given_APortalFocusScopeMountedUnderAHiddenBoundary_When_ItMounts_Then_NothingHoldsFocus()
        {
            // Arrange
            Update(s_setBlocked, 1);
            Update(s_setPortal, true);

            // Act
            Update(s_setScoped, true);

            // Assert
            Assert.That(Focused, Is.Null);
        }

        [Test]
        public void Given_APortalMountedTogetherWithAFocusScopeUnderAHiddenBoundary_When_ItMounts_Then_NothingHoldsFocus()
        {
            // Arrange
            Update(s_setBlocked, 1);
            Update(s_setScoped, true);

            // Act
            Update(s_setPortal, true);

            // Assert
            Assert.That(Focused, Is.Null);
        }

        [Test]
        public void Given_APortalFocusScopeMountedUnderAHiddenBoundary_When_TheBoundaryReveals_Then_ItsButtonHoldsFocus()
        {
            // Arrange
            Update(s_setBlocked, 1);
            Update(s_setPortal, true);
            Update(s_setScoped, true);

            // Act
            Resolve(s_source);

            // Assert
            Assert.That(Focused, Is.SameAs(s_target.Q<Button>("focusable")));
        }

        // GREEN_ON_BASE(characterization): the base focuses the button at the mount and the unmount takes the focus with it.
        // What this pins is that a scope hidden at its mount and gone before the reveal takes no focus then.
        [Test]
        public void Given_AHiddenPortalFocusScopeThatUnmounts_When_TheBoundaryReveals_Then_NothingHoldsFocus()
        {
            // Arrange
            Update(s_setBlocked, 1);
            Update(s_setPortal, true);
            Update(s_setScoped, true);
            Update(s_setScoped, false);

            // Act
            Resolve(s_source);

            // Assert
            Assert.That(Focused, Is.Null);
        }

        [Test]
        public void Given_AFocusScopeAddedToAPortalWhoseBoundaryIsHidden_When_AnotherBoundaryReveals_Then_NothingHoldsFocus()
        {
            // Arrange
            Update(s_setBlocked, 1);
            Update(s_setPortal, true);
            Update(s_setBlockedOther, 1);
            Update(s_setScoped, true);

            // Act
            Resolve(s_sourceOther);

            // Assert
            Assert.That(Focused, Is.Null);
        }

        [Test]
        public void Diagnostic_PortalScopeFocus()
        {
            Update(s_setPortal, true);
            Update(s_setScoped, true);
            var button = s_target.Q<Button>("focusable");
            var ctx = _mounted.Root.Reconciler.Context;
            var fired = new System.Text.StringBuilder();
            foreach (var pair in ctx.FocusScopeBindings)
            {
                fired.Append(pair.Key.GetType().Name).Append(":fired=").Append(pair.Value.AutoFocusFired)
                    .Append(",autoSetting=").Append(pair.Value.Settings.AutoFocus)
                    .Append(",onAttachNull=").Append(pair.Value.OnAttach == null)
                    .Append(",keyPanel=").Append(pair.Key.panel == _host.Panel).Append(';');
            }
            var kids = new System.Text.StringBuilder();
            foreach (var child in s_target.Children()) kids.Append(child.GetType().Name).Append('/').Append(child.name).Append(',');
            Assert.Fail("button=" + (button != null)
                + " buttonPanelIsHost=" + (button?.panel == _host.Panel)
                + " canGrabFocus=" + button?.canGrabFocus
                + " focusable=" + button?.focusable + " tabIndex=" + button?.tabIndex
                + " display=" + button?.resolvedStyle.display + " visible=" + button?.visible
                + " targetPanelIsHost=" + (s_target.panel == _host.Panel)
                + " targetKids=" + kids
                + " bindings=" + ctx.FocusScopeBindings.Count + " [" + fired + "]"
                + " deferred=" + ctx.DeferredAutoFocus.Count
                + " focused=" + (_host.Panel.focusController.focusedElement as VisualElement)?.name
                + " pendingPortalMounts=" + ctx.PendingPortalMounts.Count);
        }

        [Component]
        private static VNode ReaderRender(int blocked)
        {
            var value = Hooks.Use<int>(_ => blocked == 0
                ? VelvetTask.FromResult(0)
                : (s_source = new VelvetTaskCompletionSource<int>()).Task, blocked);
            return V.Label(text: "reader:" + value);
        }

        [Component]
        private static VNode OtherReaderRender(int blocked)
        {
            var value = Hooks.Use<int>(_ => blocked == 0
                ? VelvetTask.FromResult(0)
                : (s_sourceOther = new VelvetTaskCompletionSource<int>()).Task, blocked);
            return V.Label(text: "other:" + value);
        }

        [Component]
        private static VNode HostRender()
        {
            var (blocked, setBlocked) = Hooks.UseState(0);
            var (blockedOther, setBlockedOther) = Hooks.UseState(0);
            var (portal, setPortal) = Hooks.UseState(false);
            var (scoped, setScoped) = Hooks.UseState(false);
            s_setBlocked = setBlocked;
            s_setBlockedOther = setBlockedOther;
            s_setPortal = setPortal;
            s_setScoped = setScoped;
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
                                V.Div(name: "plain"),
                                scoped
                                    ? V.FocusScope(autoFocus: true, children: new VNode[] { V.Button(name: "focusable") })
                                    : null,
                            })
                            : null,
                    }),
                V.Suspense(
                    fallback: V.Label(text: "loading other"),
                    children: new VNode[] { V.Component(OtherReaderRender, blockedOther, key: "other") }),
            });
        }
    }
}
