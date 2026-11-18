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
        private static StateUpdater<bool> s_setPlain;

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
            s_setPlain = default;
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

        // A scope that never tried to focus reads differently from one that did and is waiting for its reveal.
        private string FocusedOrDeferred()
            => Focused is VisualElement focused
                ? focused.name
                : $"none, {_mounted.Root.Reconciler.Context.DeferredAutoFocus.Count} deferred";

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
            Assert.That(FocusedOrDeferred(), Is.EqualTo("none, 1 deferred"));
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
            Assert.That(FocusedOrDeferred(), Is.EqualTo("none, 1 deferred"));
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

        // GREEN_ON_BASE(characterization): the base never focuses a scope added to a Portal that already holds content.
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
            Assert.That(FocusedOrDeferred(), Is.EqualTo("none, 1 deferred"));
        }

        [Test]
        public void Given_AHiddenPortalFocusScopeAfterAStylePass_When_TheBoundaryRevealsAndTheNextStyleAndTickRun_Then_ItsButtonHoldsFocus()
        {
            // Arrange
            Update(s_setBlocked, 1);
            Update(s_setPortal, true);
            Update(s_setScoped, true);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Act
            Resolve(s_source);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(Focused, Is.SameAs(s_target.Q<Button>("focusable")));
        }

        [Test]
        public void Given_ARevealedPortalFocusScopeWaitingOnAStylePass_When_FocusMovesElsewhere_Then_TheRetryLeavesItThere()
        {
            // Arrange
            Update(s_setBlocked, 1);
            Update(s_setPortal, true);
            Update(s_setScoped, true);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            Resolve(s_source);
            var beside = _host.Root.Q<Button>("beside");
            beside.Focus();

            // Act
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(Focused, Is.SameAs(beside));
        }

        [Test]
        public void Given_ARevealedPortalFocusScopeWaitingOnAStylePass_When_TheBoundaryHidesItAgain_Then_TheRetryFocusesNothing()
        {
            // Arrange
            Update(s_setBlocked, 1);
            Update(s_setPortal, true);
            Update(s_setScoped, true);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            Resolve(s_source);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            Update(s_setBlocked, 2);

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(Focused, Is.Null);
        }

        [Test]
        public void Given_AFocusScopeAddedToAMountedParent_When_ItMounts_Then_ItsButtonHoldsFocus()
        {
            // Arrange
            var holder = new VisualElement();
            _host.Root.Add(holder);
            using var plain = V.Mount(holder, V.Component(PlainHostRender, key: "plain"));

            // Act
            s_setPlain.Invoke(true);
            plain.FlushStateForTest();
            plain.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(Focused, Is.SameAs(holder.Q<Button>("late")));
        }

        [Component]
        private static VNode PlainHostRender()
        {
            var (shown, setShown) = Hooks.UseState(false);
            s_setPlain = setShown;
            return V.Div(children: new VNode[]
            {
                V.Div(name: "before"),
                shown
                    ? V.FocusScope(autoFocus: true, children: new VNode[] { V.Button(name: "late") })
                    : null,
            });
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
                V.Button(name: "beside"),
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
