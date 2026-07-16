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

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            s_ring = default;
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
