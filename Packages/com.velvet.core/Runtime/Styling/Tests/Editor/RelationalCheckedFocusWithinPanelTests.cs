using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Behavioural coverage for the relational variants added alongside the element-local <c>checked:</c>:
    /// <c>peer-checked:</c> (a preceding <c>peer</c> control's <c>ChangeEvent&lt;bool&gt;</c>) and
    /// <c>group-focus-within:</c> / <c>peer-focus-within:</c> (the source's bubbling focus, i.e. focus
    /// reaching the source or any descendant). Mounted in a real <see cref="UnityEditor.EditorWindow"/> panel so the
    /// relational source resolves, then a real event is fired on the source. GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class RelationalCheckedFocusWithinPanelTests : PanelTestBase
    {
        private static void Fire<TEvent>(VisualElement el) where TEvent : EventBase<TEvent>, new()
        {
            using var evt = EventBase<TEvent>.GetPooled();
            el.SimulateEvent(evt);
        }

        [Test]
        public void Given_PeerCheckedChild_When_ThePrecedingPeerIsChecked_Then_PayloadApplied()
        {
            // Arrange — a child with peer-checked:bg-on preceded by a `peer` Toggle.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                "container",
                V.Toggle(name: "peer", className: "peer"),
                V.Label(name: "child", className: "peer-checked:bg-on")));
            var peer = _window.rootVisualElement.Q<Toggle>("peer");
            var child = _window.rootVisualElement.Q<Label>("child");
            Assume.That(child.ClassListContains("bg-on"), Is.False, "Precondition: payload off before peer checked");

            // Act — the preceding peer toggle is checked.
            peer.SimulateChange(true);

            // Assert — the peer-checked payload is applied to the consuming child.
            Assert.IsTrue(child.ClassListContains("bg-on"));
        }

        [Test]
        public void Given_PeerCheckedApplied_When_ThePeerIsUnchecked_Then_PayloadRemoved()
        {
            // Arrange — a child whose peer-checked payload is applied (peer checked).
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                "container",
                V.Toggle(name: "peer", className: "peer"),
                V.Label(name: "child", className: "peer-checked:bg-on")));
            var peer = _window.rootVisualElement.Q<Toggle>("peer");
            var child = _window.rootVisualElement.Q<Label>("child");
            peer.SimulateChange(true);
            Assume.That(child.ClassListContains("bg-on"), Is.True, "Precondition: payload on while peer checked");

            // Act — the peer is unchecked.
            peer.SimulateChange(false);

            // Assert — the payload clears.
            Assert.IsFalse(child.ClassListContains("bg-on"));
        }

        [Test]
        public void Given_GroupFocusWithinChild_When_TheGroupSourceGainsFocus_Then_PayloadApplied()
        {
            // Arrange — a child with group-focus-within:bg-on under a `group` ancestor.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div("group", V.Label(name: "child", className: "group-focus-within:bg-on")));
            var source = _window.rootVisualElement.Q<VisualElement>(className: "group");
            var child = _window.rootVisualElement.Q<Label>("child");
            Assume.That(child.ClassListContains("bg-on"), Is.False, "Precondition: payload off before focus");

            // Act — focus reaches the group source (FocusIn = focus-within).
            Fire<FocusInEvent>(source);

            // Assert — the group-focus-within payload is applied to the consuming child.
            Assert.IsTrue(child.ClassListContains("bg-on"));
        }

        [Test]
        public void Given_PeerFocusWithinChild_When_ThePrecedingPeerGainsFocus_Then_PayloadApplied()
        {
            // Arrange — a child with peer-focus-within:bg-on preceded by a `peer` sibling.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                "container",
                V.Label(name: "peer", className: "peer"),
                V.Label(name: "child", className: "peer-focus-within:bg-on")));
            var peer = _window.rootVisualElement.Q<Label>("peer");
            var child = _window.rootVisualElement.Q<Label>("child");
            Assume.That(child.ClassListContains("bg-on"), Is.False, "Precondition: payload off before focus");

            // Act — focus reaches the preceding peer (FocusIn = focus-within).
            Fire<FocusInEvent>(peer);

            // Assert — the peer-focus-within payload is applied to the consuming child.
            Assert.IsTrue(child.ClassListContains("bg-on"));
        }

        [Test]
        public void Given_NamedPeerCheckedChild_When_TheNamedPeerIsMountedAlreadyChecked_Then_PayloadSeededAtMount()
        {
            // Arrange/Act — a child with peer-checked/opt:bg-on preceded by a `peer/opt` Toggle mounted ALREADY
            // checked. peer-checked is the one relational state seeded by Resolve (not an event), so the named
            // binding must read the initial Toggle value at mount and apply the payload without any ChangeEvent.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                "container",
                V.Toggle(name: "peer", className: "peer/opt", value: true),
                V.Label(name: "child", className: "peer-checked/opt:bg-on")));
            var child = _window.rootVisualElement.Q<Label>("child");

            // Assert — the named peer-checked payload is seeded from the already-checked source at mount.
            Assert.IsTrue(child.ClassListContains("bg-on"));
        }
    }
}
