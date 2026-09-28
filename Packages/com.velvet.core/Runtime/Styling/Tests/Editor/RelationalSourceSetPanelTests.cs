using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which sources a relational variant reacts to: <c>.peer:checked ~ x</c> matches whichever
    /// preceding peer is checked and <c>.group:hover x</c> whichever ancestor group is hovered, and a source or
    /// consumer carrying <c>clip-path-*</c> is read through the wrapper that holds its slot. A real panel is
    /// required: a relational binding resolves its sources only once attached. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class RelationalSourceSetPanelTests : PanelTestBase
    {
        private const string Clip = "clip-path-[circle(50%)]";

        private readonly record struct PeerState(bool Checked);

        private sealed class PeerStore : Store<PeerState>
        {
            public PeerStore(bool initial) : base(new PeerState(initial)) { }
            public void Set(bool value) => SetState(_ => new PeerState(value));
            protected override void ResetCore() => SetState(_ => new PeerState(false));
        }

        private static PeerStore s_store;

        public override void TearDown()
        {
            base.TearDown();
            s_store?.Dispose();
            s_store = null;
        }

        // Two peers: the far one checked for good, the near one owned by the store.
        [Component]
        private static VNode TwoPeers()
        {
            var nearChecked = Hooks.UseStore(s_store, s => s.Checked);
            return V.Div(
                "container",
                V.Toggle(name: "far", className: "peer", value: true),
                V.Toggle(name: "near", className: "peer", value: nearChecked),
                V.Label(name: "child", className: "peer-checked:bg-on"));
        }

        // A checked far peer rendered or not, ahead of an unchecked near one.
        [Component]
        private static VNode OptionalCheckedPeer()
        {
            var showFar = Hooks.UseStore(s_store, s => s.Checked);
            return V.Div(
                "container",
                showFar ? V.Toggle(key: "far", name: "far", className: "peer", value: true) : null,
                V.Toggle(key: "near", name: "near", className: "peer", value: false),
                V.Label(key: "child", name: "child", className: "peer-checked:bg-on"));
        }

        // An outer container that carries `group` or not, around a group-hover consumer.
        [Component]
        private static VNode OptionalGroup()
        {
            var isGroup = Hooks.UseStore(s_store, s => s.Checked);
            return V.Div(name: "outer", className: isGroup ? "group" : "", children: new VNode[]
            {
                V.Label(name: "child", className: "group-hover:bg-on"),
            });
        }

        private void Rerender(bool value)
        {
            s_store.Set(value);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private T Q<T>(string name) where T : VisualElement => _window.rootVisualElement.Q<T>(name);

        [Test]
        public void Given_ACheckedPeerLightingTheConsumer_When_ThatPeerIsRemoved_Then_ThePayloadClears()
        {
            // Arrange
            s_store = new PeerStore(true);
            _mounted = V.Mount(_window.rootVisualElement, V.Component(OptionalCheckedPeer, key: "screen"));
            var child = Q<Label>("child");
            var before = child.ClassListContains("bg-on");

            // Act
            Rerender(false);

            // Assert — the remaining peer is unchecked, and the consumer is the element that was lit, so it never
            // re-attached.
            Assert.That(
                (before, Q<Toggle>("near").value, ReferenceEquals(Q<Label>("child"), child), child.ClassListContains("bg-on")),
                Is.EqualTo((true, false, true, false)));
        }

        [Test]
        public void Given_AnUncheckedPeer_When_ACheckedPeerIsInsertedBeforeTheConsumer_Then_ThePayloadApplies()
        {
            // Arrange
            s_store = new PeerStore(false);
            _mounted = V.Mount(_window.rootVisualElement, V.Component(OptionalCheckedPeer, key: "screen"));
            var child = Q<Label>("child");
            var before = child.ClassListContains("bg-on");

            // Act
            Rerender(true);

            // Assert — the consumer is the element mounted first, so it never re-attached.
            Assert.That(
                (before, ReferenceEquals(Q<Label>("child"), child), child.ClassListContains("bg-on")),
                Is.EqualTo((false, true, true)));
        }

        [Test]
        public void Given_AnAncestorThatGainsTheGroupClass_When_ItIsHovered_Then_TheGroupHoverPayloadApplies()
        {
            // Arrange
            s_store = new PeerStore(false);
            _mounted = V.Mount(_window.rootVisualElement, V.Component(OptionalGroup, key: "screen"));
            Rerender(true);
            // A registry of its own, so the simulated event below reaches an outer element nothing hooked.
            Q<VisualElement>("outer").RegisterCallback<PointerOverEvent>(_ => { });

            // Act
            using (var evt = PointerOverEvent.GetPooled())
            {
                Q<VisualElement>("outer").SimulateEvent(evt);
            }

            // Assert — the class is a term: without it the hover must light nothing.
            Assert.That(
                (Q<VisualElement>("outer").ClassListContains("group"), Q<Label>("child").ClassListContains("bg-on")),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ACheckedPeerBehindAnUncheckedNearerPeer_When_Mounted_Then_ThePeerCheckedPayloadApplies()
        {
            // Arrange
            s_store = new PeerStore(false);

            // Act
            _mounted = V.Mount(_window.rootVisualElement, V.Component(TwoPeers, key: "screen"));

            // Assert — the nearer peer's own state is a term: were it checked, the nearest-only search would
            // light the payload too.
            Assert.That(
                (Q<Toggle>("far").value, Q<Toggle>("near").value, Q<Label>("child").ClassListContains("bg-on")),
                Is.EqualTo((true, false, true)));
        }

        [Test]
        public void Given_TwoCheckedPeers_When_TheNearerOneIsUnchecked_Then_ThePeerCheckedPayloadStays()
        {
            // Arrange
            s_store = new PeerStore(true);
            _mounted = V.Mount(_window.rootVisualElement, V.Component(TwoPeers, key: "screen"));
            var before = Q<Label>("child").ClassListContains("bg-on");

            // Act — a controlled write, which reaches the binding as a settle raised on the near peer.
            s_store.Set(false);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(
                (before, Q<Toggle>("near").value, Q<Label>("child").ClassListContains("bg-on")),
                Is.EqualTo((true, false, true)));
        }

        [Test]
        public void Given_NestedGroups_When_OnlyTheOuterGroupIsHovered_Then_TheGroupHoverPayloadApplies()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "outer", className: "group", children: new VNode[]
            {
                V.Div(name: "inner", className: "group", children: new VNode[]
                {
                    V.Label(name: "child", className: "group-hover:bg-on"),
                }),
            }));

            // Act
            using (var evt = PointerOverEvent.GetPooled())
            {
                Q<VisualElement>("outer").SimulateEvent(evt);
            }

            // Assert
            Assert.That(Q<Label>("child").ClassListContains("bg-on"), Is.True);
        }

        [Test]
        public void Given_ACheckedPeerCarryingAClipPath_When_Mounted_Then_ThePeerCheckedPayloadApplies()
        {
            // Act
            _mounted = V.Mount(_window.rootVisualElement, V.Div("container",
                V.Toggle(name: "peer", className: "peer " + Clip, value: true),
                V.Label(name: "child", className: "peer-checked:bg-on")));

            // Assert — the wrapper is a term: without it the peer is the consumer's own preceding sibling.
            var peer = Q<Toggle>("peer");
            Assert.That(
                (peer.parent.ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass),
                    Q<Label>("child").ClassListContains("bg-on")),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_APeerCheckedConsumerCarryingAClipPath_When_Mounted_Then_ThePeerCheckedPayloadApplies()
        {
            // Act
            _mounted = V.Mount(_window.rootVisualElement, V.Div("container",
                V.Toggle(name: "peer", className: "peer", value: true),
                V.Label(name: "child", className: "peer-checked:bg-on " + Clip)));

            // Assert — the wrapper is a term: without it the consumer's physical parent is the container.
            var child = Q<Label>("child");
            Assert.That(
                (child.parent.ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass),
                    child.ClassListContains("bg-on")),
                Is.EqualTo((true, true)));
        }
    }
}
