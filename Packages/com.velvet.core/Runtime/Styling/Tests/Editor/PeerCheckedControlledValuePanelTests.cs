using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Regression coverage for <c>peer-checked:</c> tracking a peer whose value arrives through a fully
    /// controlled prop. <c>SetValueWithoutNotify</c> raises no <c>ChangeEvent</c>, which is what the relational
    /// binding listens for, so the ordinary React shape — the owner holding the peer's state — left the
    /// consuming child's payload behind. The element-local <c>checked:</c> half of the same suppression is
    /// covered by <see cref="CheckedVariantBehaviorTests"/>; what the relational family adds is that the
    /// consumer is a different element from the one written, so the edge has to travel from the source to
    /// whoever resolved to it.
    /// <para>
    /// Both the plain binding and the stacked relational inner (<c>dark:peer-checked:</c>) are driven, because
    /// they hold separate subscriptions to the same source. A real panel is required: a relational binding
    /// resolves its source only once attached.
    /// </para>
    /// <para>
    /// Each case asserts the payload beside the peer's own value and its state before the write: a controlled
    /// write that never landed would make an absent payload correct, and a payload already applied would make
    /// the final term true with the write under test doing nothing. GWT, one assert per case.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class PeerCheckedControlledValuePanelTests : PanelTestBase
    {
        private readonly record struct PeerState(bool Checked);

        private sealed class PeerStore : Store<PeerState>
        {
            public PeerStore(bool initial) : base(new PeerState(initial)) { }
            public void Set(bool value) => SetState(_ => new PeerState(value));
            protected override void ResetCore() => SetState(_ => new PeerState(false));
        }

        private static PeerStore s_store;
        private static string s_childClass;
        private bool _darkBefore;

        public override void SetUp()
        {
            base.SetUp();
            _darkBefore = VelvetTheme.IsDark;
            VelvetTheme.IsDark = false;
        }

        public override void TearDown()
        {
            VelvetTheme.IsDark = _darkBefore;
            // After the base teardown: the store outlives the tree that subscribes to it, so it is released
            // once the mount that reads it is gone.
            base.TearDown();
            s_store?.Dispose();
            s_store = null;
        }

        // The peer's checked state is owned by the store, so a re-render writes it into the control through
        // the controlled-value prop rather than through any interaction.
        [Component]
        private static VNode Screen()
        {
            var isChecked = Hooks.UseStore(s_store, s => s.Checked);
            return V.Div(
                "container",
                V.Toggle(name: "peer", className: "peer", value: isChecked),
                V.Label(name: "child", className: s_childClass));
        }

        private (Toggle Peer, Label Child) Mount(bool initial, string childClass)
        {
            s_store = new PeerStore(initial);
            s_childClass = childClass;
            _mounted = V.Mount(_window.rootVisualElement, V.Component(Screen, key: "screen"));
            return (_window.rootVisualElement.Q<Toggle>("peer"), _window.rootVisualElement.Q<Label>("child"));
        }

        private void Rerender(bool value)
        {
            s_store.Set(value);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        [Test]
        public void Given_APeerCheckedChild_When_ThePeersValueArrivesThroughAControlledProp_Then_ThePayloadApplied()
        {
            // Arrange — a peer-checked child preceded by a controlled `peer` Toggle rendered unchecked.
            var (peer, child) = Mount(initial: false, childClass: "peer-checked:bg-on");
            var beforeTheControlledWrite = child.ClassListContains("bg-on");

            // Act — the owner re-renders with the peer's value flipped; no interaction, so the control is
            // written without notification.
            Rerender(true);

            // Assert — the peer took the value and the consuming child's payload followed it.
            Assert.That(
                (beforeTheControlledWrite, peer.value, child.ClassListContains("bg-on")),
                Is.EqualTo((false, true, true)));
        }

        [Test]
        public void Given_APeerCheckedPayloadApplied_When_AControlledPropUnchecksThePeer_Then_ThePayloadRemoved()
        {
            // Arrange — the same pair rendered checked, so the child's payload is seeded on at mount.
            var (peer, child) = Mount(initial: true, childClass: "peer-checked:bg-on");
            var beforeTheControlledWrite = child.ClassListContains("bg-on");

            // Act — the owner re-renders with the peer's value flipped back.
            Rerender(false);

            // Assert — the payload clears with the value it tracks.
            Assert.That(
                (beforeTheControlledWrite, peer.value, child.ClassListContains("bg-on")),
                Is.EqualTo((true, false, false)));
        }

        [Test]
        public void Given_ADarkPeerCheckedChildWithDarkOn_When_ThePeersValueArrivesThroughAControlledProp_Then_TheLeafIsApplied()
        {
            // Arrange — dark:peer-checked:bg-on over a controlled `peer` Toggle rendered unchecked. Opening
            // the dark gate is what builds the inner and resolves it against the peer.
            var (peer, child) = Mount(initial: false, childClass: "dark:peer-checked:bg-on");
            VelvetTheme.IsDark = true;
            var beforeTheControlledWrite = child.ClassListContains("bg-on");

            // Act — the owner re-renders with the peer's value flipped.
            Rerender(true);

            // Assert — the stacked inner holds its own subscription to the same source, and it tracks the
            // controlled write too.
            Assert.That(
                (beforeTheControlledWrite, peer.value, child.ClassListContains("bg-on")),
                Is.EqualTo((false, true, true)));
        }
    }
}
