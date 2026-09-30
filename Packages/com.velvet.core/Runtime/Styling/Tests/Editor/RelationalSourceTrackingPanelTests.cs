using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a relational variant's sources follow later renders the way a CSS selector follows the
    /// document: a preceding sibling that stops carrying <c>peer</c> stops counting, whatever else still holds
    /// the state; a source that leaves the tree or the marker stops driving the consumer; a consumer that
    /// moves or attaches outside a render keeps reading the sources it has. A real panel is required: a
    /// relational binding resolves its sources only once attached. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class RelationalSourceTrackingPanelTests : PanelTestBase
    {
        internal readonly record struct Peers(
            bool FarChecked, bool NearChecked, bool NearMarked = true, bool ShowNear = true, bool Moved = false);

        private sealed class PeersStore : Store<Peers>
        {
            public PeersStore(Peers initial) : base(initial) { }
            public void Update(System.Func<Peers, Peers> change) => SetState(change);
            protected override void ResetCore() => SetState(_ => default);
        }

        private static PeersStore s_store;
        private bool _darkBefore;

        public override void SetUp()
        {
            base.SetUp();
            _darkBefore = VelvetTheme.IsDark;
        }

        public override void TearDown()
        {
            VelvetTheme.IsDark = _darkBefore;
            base.TearDown();
            s_store?.Dispose();
            s_store = null;
        }

        // A far peer, then a near one the store can unmark or remove, then the consumer.
        [Component]
        private static VNode Screen()
        {
            var s = Hooks.UseStore(s_store, x => x);
            return V.Div(
                "container",
                V.Toggle(key: "far", name: "far", className: "peer", value: s.FarChecked),
                s.ShowNear
                    ? V.Toggle(key: "near", name: "near", className: s.NearMarked ? "peer" : "", value: s.NearChecked)
                    : null,
                V.Label(key: "child", name: "child", className: "peer-checked:bg-on"));
        }

        // A checked peer, then two spacers and the consumer, which the store can move ahead of the spacers.
        [Component]
        private static VNode MovingConsumer()
        {
            var s = Hooks.UseStore(s_store, x => x);
            var child = V.Label(key: "child", name: "child", className: "peer-checked:bg-on");
            var a = V.Div(key: "a");
            var b = V.Div(key: "b");
            return V.Div(
                "container",
                V.Toggle(key: "peer", name: "peer", className: "peer", value: s.FarChecked),
                s.Moved ? child : a,
                s.Moved ? a : b,
                s.Moved ? b : child);
        }

        // A checked peer the store renders or not, ahead of a dark:peer-checked: consumer.
        [Component]
        private static VNode StackedConsumer()
        {
            var s = Hooks.UseStore(s_store, x => x);
            return V.Div(
                "container",
                s.ShowNear ? V.Toggle(key: "peer", name: "peer", className: "peer", value: true) : null,
                V.Label(key: "child", name: "child", className: "dark:peer-checked:bg-on"));
        }

        private void Mount(System.Func<VNode> render, Peers initial)
        {
            s_store = new PeersStore(initial);
            _mounted = V.Mount(_window.rootVisualElement, V.Component(render, key: "screen"));
        }

        private void Change(System.Func<Peers, Peers> change)
        {
            s_store.Update(change);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private bool Lit => _window.rootVisualElement.Q<Label>("child").ClassListContains("bg-on");

        [Test]
        public void Given_TheOnlyCheckedPeerIsTheNearestOne_When_ItLosesThePeerClass_Then_ThePayloadClears()
        {
            // Arrange
            Mount(Screen, new Peers(FarChecked: false, NearChecked: true));
            var before = Lit;

            // Act
            Change(s => s with { NearMarked = false });

            // Assert
            Assert.That((before, Lit), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_APeerThatLostThePeerClass_When_ItIsToggledByTheUser_Then_ThePayloadStaysClear()
        {
            // Arrange
            Mount(Screen, new Peers(FarChecked: false, NearChecked: true));
            Change(s => s with { NearMarked = false });
            var near = _window.rootVisualElement.Q<Toggle>("near");

            // Act
            near.SimulateChange(false);
            near.SimulateChange(true);

            // Assert — the element's own value is a term: the toggle must have landed.
            Assert.That((near.value, Lit), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_TwoCheckedPeers_When_TheNearerLosesThePeerClassAndThenTheFartherUnchecks_Then_OnlyTheSecondStepClears()
        {
            // Arrange
            Mount(Screen, new Peers(FarChecked: true, NearChecked: true));

            // Act
            Change(s => s with { NearMarked = false });
            var afterUnmarking = Lit;
            Change(s => s with { FarChecked = false });

            // Assert
            Assert.That((afterUnmarking, Lit), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_APeerThatLostThePeerClass_When_ItIsRemoved_Then_ThePayloadStaysClear()
        {
            // Arrange
            Mount(Screen, new Peers(FarChecked: false, NearChecked: true));
            Change(s => s with { NearMarked = false });

            // Act
            Change(s => s with { ShowNear = false });

            // Assert — the near peer's absence is a term: the removal must have landed.
            Assert.That(
                (_window.rootVisualElement.Q<Toggle>("near") == null, Lit),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_TheOnlyCheckedPeer_When_TheCleanerReleasesItOutsideARender_Then_ThePayloadClears()
        {
            // Arrange — a cleanup with no render pass after it, so only the cleaner's own release can clear the
            // consumer.
            Mount(Screen, new Peers(FarChecked: true, NearChecked: false));
            var before = Lit;
            var far = _window.rootVisualElement.Q<Toggle>("far");

            // Act
            ((IReconcilerBridge)_mounted.Root.Reconciler).CleanupElementForController(far);

            // Assert
            Assert.That((before, Lit), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_TwoCheckedPeers_When_TheNearerIsRemovedAndThenTheFartherUnchecks_Then_OnlyTheSecondStepClears()
        {
            // Arrange
            Mount(Screen, new Peers(FarChecked: true, NearChecked: true));

            // Act
            Change(s => s with { ShowNear = false });
            var afterRemoval = Lit;
            Change(s => s with { FarChecked = false });

            // Assert
            Assert.That((afterRemoval, Lit), Is.EqualTo((true, false)));
        }

        // GREEN_ON_BASE(characterization): the base already clears a moved consumer once its only peer unchecks.
        [Test]
        public void Given_AConsumerLitByACheckedPeer_When_ItMovesAndThenThePeerUnchecks_Then_ThePayloadClears()
        {
            // Arrange
            Mount(MovingConsumer, new Peers(FarChecked: true, NearChecked: false));
            var child = _window.rootVisualElement.Q<Label>("child");
            Change(s => s with { Moved = true });
            var movedAndLit = (child.parent.IndexOf(child), Lit);

            // Act
            Change(s => s with { FarChecked = false });

            // Assert — the consumer's new slot is a term: it must be the element that moved.
            Assert.That((movedAndLit, Lit), Is.EqualTo(((1, true), false)));
        }

        [Test]
        public void Given_ADarkPeerCheckedConsumerWithDarkOn_When_ACheckedPeerIsInsertedBeforeIt_Then_TheLeafApplies()
        {
            // Arrange
            VelvetTheme.IsDark = true;
            Mount(StackedConsumer, new Peers(FarChecked: false, NearChecked: false, ShowNear: false));
            var before = Lit;

            // Act
            Change(s => s with { ShowNear = true });

            // Assert
            Assert.That((before, Lit), Is.EqualTo((false, true)));
        }

        // GREEN_ON_BASE(characterization): the base already hooks a consumer when it attaches, render or not.
        [Test]
        public void Given_ATreeMountedOffPanel_When_ItIsAttachedWithoutARender_Then_ACheckedPeerLightsTheConsumer()
        {
            // Arrange
            var root = new VisualElement();
            _mounted = V.Mount(root, V.Div("container",
                V.Toggle(name: "peer", className: "peer", value: true),
                V.Label(name: "child", className: "peer-checked:bg-on")));
            var before = root.Q<Label>("child").ClassListContains("bg-on");

            // Act
            _window.rootVisualElement.Add(root);

            // Assert
            Assert.That((before, Lit), Is.EqualTo((false, true)));
        }

        [Component]
        private static VNode NamedGroup()
        {
            var s = Hooks.UseStore(s_store, x => x);
            return V.Div(name: "outer", className: s.NearMarked ? "group/card" : "", children: new VNode[]
            {
                V.Label(name: "child", className: "group-hover/card:bg-on"),
            });
        }

        [Test]
        public void Given_AnAncestorThatGainsANamedGroupClass_When_ItIsHovered_Then_TheNamedGroupHoverPayloadApplies()
        {
            // Arrange
            Mount(NamedGroup, new Peers(FarChecked: false, NearChecked: false, NearMarked: false));
            Change(s => s with { NearMarked = true });
            var outer = _window.rootVisualElement.Q<VisualElement>("outer");
            // A registry of its own, so the simulated event below reaches an outer element nothing hooked.
            outer.RegisterCallback<PointerOverEvent>(_ => { });

            // Act
            using (var evt = PointerOverEvent.GetPooled())
            {
                outer.SimulateEvent(evt);
            }

            // Assert — the class is a term: without it the hover must light nothing.
            Assert.That((outer.ClassListContains("group/card"), Lit), Is.EqualTo((true, true)));
        }
    }
}
