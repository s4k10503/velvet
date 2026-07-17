using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <c>Hooks.UseFocusRing</c>'s React Aria <c>useFocusRing</c> contract: <c>IsFocused</c> follows
    /// plain focus from any modality, while <c>IsFocusVisible</c> lights only for focus NOT caused by a
    /// pointer press on the element — the same element-local heuristic the <c>focus-visible:</c> styling
    /// variant rides, exercised here through the hook's re-rendering state channel instead of a class.
    /// </summary>
    internal sealed class UseFocusRingTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        private static FocusRing s_ring;
        private static StateUpdater<bool> s_setShowTarget;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            s_ring = default;
            s_setShowTarget = default;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        [Component]
        private static VNode RingHost()
        {
            var ring = Hooks.UseFocusRing();
            s_ring = ring;
            return V.Button(name: "target", refCallback: ring.Ref);
        }

        private VisualElement Mount()
        {
            _mounted = V.Mount(_host.Root, V.Component(RingHost, key: "root"));
            return _host.Root.Q<VisualElement>("target");
        }

        private static StateUpdater<int> s_bumpComposed;

        // The ring's ref is COMPOSED inside a per-render lambda (the only way to combine it with
        // other per-element work), so every patch cycles the ref with a fresh identity — the
        // cleanup fires on the still-focused element and the setup must re-light the ring.
        [Component]
        private static VNode ComposedRingHost()
        {
            var ring = Hooks.UseFocusRing();
            s_ring = ring;
            var (count, setCount) = Hooks.UseState(0);
            s_bumpComposed = setCount;
            return V.Div(children: new VNode[]
            {
                V.Label(text: "count-" + count),
                V.Button(name: "target", refCallback: element => ring.Ref(element)),
            });
        }

        [Test]
        public void Given_AFocusedRingBehindAPerRenderComposedRef_When_AnUnrelatedRerenderCyclesTheRef_Then_TheRingStaysLit()
        {
            // Arrange — mounted with a composed (fresh-identity) ref, then really focused.
            _mounted = V.Mount(_host.Root, V.Component(ComposedRingHost, key: "root"));
            _host.Root.Q<VisualElement>("target").Focus();
            _mounted.FlushStateForTest();
            Assume.That(s_ring.IsFocused, Is.True, "Precondition: the ring lit for the focused element");

            // Act — an unrelated state change patches the host; the fresh-identity ref cycles
            // (cleanup writes the flags false on the still-focused element, setup re-hooks it).
            s_bumpComposed.Invoke(1);
            _mounted.FlushStateForTest();

            // Assert — the setup's focus seed re-lit the ring within the same flush.
            Assert.That(s_ring.IsFocused, Is.True,
                "A ref cycling on a still-focused element must not leave the ring dark");
        }

        [Test]
        public void Given_AComponentUsingUseFocusRing_When_ItsElementGainsFocusWithoutAPointerPress_Then_IsFocusVisibleBecomesTrue()
        {
            // Arrange
            var target = Mount();
            Assume.That(s_ring.IsFocusVisible, Is.False, "Precondition: the ring starts dark");

            // Act — focus arrives with no preceding pointer-down (Tab navigation / programmatic Focus).
            using (var evt = FocusEvent.GetPooled()) target.SimulateEvent(evt);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(s_ring.IsFocusVisible, Is.True);
        }

        [Test]
        public void Given_AComponentUsingUseFocusRing_When_APointerPressOnTheElementCausesTheFocus_Then_IsFocusVisibleStaysFalse()
        {
            // Arrange
            var target = Mount();

            // Act — the click-to-focus path: a pointer-down on the element, then the focus it causes.
            using (var evt = PointerDownEvent.GetPooled()) target.SimulateEvent(evt);
            using (var evt = FocusEvent.GetPooled()) target.SimulateEvent(evt);
            _mounted.FlushStateForTest();

            // Assert — pointer-driven focus is focus, but not VISIBLE focus.
            Assert.That((s_ring.IsFocused, s_ring.IsFocusVisible), Is.EqualTo((true, false)));
        }

        [Component]
        private static VNode CollapsingRingHost()
        {
            var ring = Hooks.UseFocusRing();
            s_ring = ring;
            var (showTarget, setShowTarget) = Hooks.UseState(true);
            s_setShowTarget = setShowTarget;
            return V.Div(children: new VNode[]
            {
                showTarget ? V.Button(name: "target", key: "target", refCallback: ring.Ref) : null,
            });
        }

        [Test]
        public void Given_AFocusedRingElement_When_ItUnmountsWhileTheComponentSurvives_Then_IsFocusedReturnsToFalse()
        {
            // Arrange — REAL panel focus (not a simulated event): the stuck-state correction keys off
            // the focus controller's view of the element at cleanup time.
            _mounted = V.Mount(_host.Root, V.Component(CollapsingRingHost, key: "root"));
            var target = _host.Root.Q<VisualElement>("target");
            target.Focus();
            _mounted.FlushStateForTest();
            Assume.That(s_ring.IsFocused, Is.True, "Precondition: the ring lit for the focused element");

            // Act — the element unmounts while focused; no Blur can reach the already-unhooked
            // signals, so the ref cleanup writes the correction directly (a commit-phase write that
            // the drain's follow-up pass commits). The extra scheduler tick and flush stay: the
            // contract is "settled by the next tick at the latest", not a specific write path.
            s_setShowTarget.Invoke(false);
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(s_ring.IsFocused, Is.False);
        }

        [Component]
        private static VNode SwappingRingHost()
        {
            var ring = Hooks.UseFocusRing();
            s_ring = ring;
            var (swapped, setSwapped) = Hooks.UseState(false);
            s_setShowTarget = setSwapped;
            return V.Div(children: new VNode[]
            {
                swapped
                    ? (VNode)V.Div(name: "target2", key: "t", refCallback: ring.Ref)
                    : V.Button(name: "target", key: "t", refCallback: ring.Ref),
            });
        }

        [Test]
        public void Given_AFocusedRingElement_When_TheRefMovesToAReplacementElement_Then_IsFocusedReturnsToFalse()
        {
            // Arrange
            _mounted = V.Mount(_host.Root, V.Component(SwappingRingHost, key: "root"));
            _host.Root.Q<VisualElement>("target").Focus();
            _mounted.FlushStateForTest();
            Assume.That(s_ring.IsFocused, Is.True, "Precondition: the ring lit for the focused element");

            // Act — a same-key type flip replaces the element under the SAME ref in one flush: the
            // old element's focus dies with it and the replacement was never focused, so the OLD
            // hookup's cleanup must write the correction even though a newer hookup follows it in
            // the same flush. The extra tick and flush stay: the contract is "settled by the next
            // tick at the latest", not a specific write path.
            s_setShowTarget.Invoke(true);
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(s_ring.IsFocused, Is.False);
        }

        [Test]
        public void Given_AFocusVisibleRing_When_TheElementBlurs_Then_IsFocusVisibleReturnsToFalse()
        {
            // Arrange
            var target = Mount();
            using (var evt = FocusEvent.GetPooled()) target.SimulateEvent(evt);
            _mounted.FlushStateForTest();
            Assume.That(s_ring.IsFocusVisible, Is.True, "Precondition: the ring lit for keyboard focus");

            // Act
            using (var evt = BlurEvent.GetPooled()) target.SimulateEvent(evt);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(s_ring.IsFocusVisible, Is.False);
        }
    }
}
