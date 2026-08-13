using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the retirement of an AnimatePresence boundary's per-expansion state. The entry is keyed by
    /// (boundary fiber, parent element, scoped position key) and holds the committed VNodes plus each
    /// key's exit anchor and Motion element, so it outliving its AnimatePresence node pins elements that
    /// have already gone back to the pool — and, where the node is rendered again at the same key, hands
    /// the next expansion a committed set describing leaves the DOM no longer has: the departed children
    /// are spliced back in as exiting ghosts.
    /// <list type="bullet">
    /// <item>The node ceasing to be rendered while its boundary fiber and parent element both live on
    /// retires the entry, so a later render at the same position starts from an empty committed set.</item>
    /// <item>The parent element being torn down retires it too — that route reaches no reconcile walk,
    /// since a real element is an opaque leaf to the enclosing expansion.</item>
    /// <item>A poolable parent (a Button) rented back for a fresh AnimatePresence at the same position
    /// therefore finds no prior state.</item>
    /// <item>A presence rendered again is a first render again, so <c>initial: false</c> suppresses its
    /// child's enter as it did on the first — a surviving entry makes the second mount a later addition
    /// to a presence already on screen instead.</item>
    /// <item>A presence inside a <c>V.Portal</c>'s own children retires with the Portal's range, which is
    /// the only route that reaches it.</item>
    /// <item>An abort stops the removal pass of the container it reaches and holds for the rest of the
    /// pass, so whether an entry may retire is read per container: one whose own removals ran retires
    /// even though a later container's did not. The reading covers the fast path too, where the removals
    /// are the time-sliced diff's rather than the general walk's finalize.</item>
    /// <item>A pass that walks neither side of a presence retires nothing of it — the last case holds
    /// that to what it already did, since retiring a live entry strands its leaf where the next old side
    /// can no longer name it.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class AnimatePresenceStateRetirementTests
    {
        private readonly record struct PresenceState(bool Shown, string ChildKey, int Nonce);

        private sealed class PresenceStore : Store<PresenceState>
        {
            public PresenceStore() : base(new PresenceState(true, "a", 0)) { }

            public void Set(bool shown, string childKey)
                => SetState(previous => new PresenceState(shown, childKey, previous.Nonce));

            // Re-renders the declaring component without changing what it renders, which is what puts the
            // presence through a pass as an old-side reproduction the new side repeats.
            public void Touch() => SetState(previous => previous with { Nonce = previous.Nonce + 1 });

            protected override void ResetCore() => SetState(_ => new PresenceState(true, "a", 0));
        }

        private readonly record struct TickState(int Value);

        private sealed class TickStore : Store<TickState>
        {
            public TickStore() : base(new TickState(0)) { }
            public void Bump() => SetState(previous => new TickState(previous.Value + 1));
            protected override void ResetCore() => SetState(_ => new TickState(0));
        }

        private static PresenceStore s_store;
        private static TickStore s_ticks;
        private static int s_enterCompletions;
        private static VisualElement s_overlay;
        private static VisualElement s_otherOverlay;

        private static readonly Dictionary<string, string> s_fade = new()
        {
            ["visible"] = "opacity-100",
            ["hidden"] = "opacity-0",
        };

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
            s_ticks = null;
            s_enterCompletions = 0;
            s_overlay = new VisualElement { name = "overlay" };
            s_otherOverlay = new VisualElement { name = "other-overlay" };
        }

        [Component]
        private static VNode SiblingHost()
        {
            var state = Hooks.UseStore(s_store, s => s);
            return V.Div(name: "host", children: new VNode[] { state.Shown ? Presence(state.ChildKey) : null });
        }

        [Component]
        private static VNode NestedDivHost()
        {
            var state = Hooks.UseStore(s_store, s => s);
            VNode inner = V.Div(name: "inner", children: new VNode[] { Presence(state.ChildKey) });
            return V.Div(name: "outer", children: new VNode[] { state.Shown ? inner : null });
        }

        // A classic Motion, deliberately not a variant one: a variant Motion with no initial label rests
        // at variants[animate] and fires OnEnterComplete from the dispatch as well, so the reading would
        // not separate a suppressed enter from a played one.
        [Component]
        private static VNode SuppressedEnterHost()
        {
            var state = Hooks.UseStore(s_store, s => s);
            VNode presence = V.AnimatePresence(key: "presence", initial: false, children: new VNode[]
            {
                V.Motion(name: "item-" + state.ChildKey, key: state.ChildKey,
                    transition: StyleTransition.Fade,
                    onEnterComplete: () => s_enterCompletions++),
            });
            return V.Div(name: "host", children: new VNode[] { state.Shown ? presence : null });
        }

        [Component]
        private static VNode UnrelatedSibling()
        {
            var tick = Hooks.UseStore(s_ticks, s => s.Value);
            return V.Label(text: tick.ToString());
        }

        [Component]
        private static VNode SiblingPairHost()
        {
            var state = Hooks.UseStore(s_store, s => s);
            return V.Div(name: "outer", children: new VNode[]
            {
                V.Div(name: "host", children: new VNode[] { state.Shown ? Presence(state.ChildKey) : null }),
                V.Component(UnrelatedSibling, key: "tick"),
            });
        }

        // The presence is replaced by a flat list of host leaves, which needs no inline expansion — so the
        // new side takes the time-sliced diff rather than the general walk, and the removals that empty
        // its slots run there instead.
        [Component]
        private static VNode FastPathHost()
        {
            var state = Hooks.UseStore(s_store, s => s);
            return V.Div(name: "host", children: state.Shown
                ? new VNode[] { Presence(state.ChildKey) }
                : new VNode[] { V.Label(text: "gone") });
        }

        [Component]
        private static VNode PortalHost()
        {
            var state = Hooks.UseStore(s_store, s => s);
            VNode portal = V.Portal(s_overlay, new VNode[] { Presence(state.ChildKey) });
            return V.Div(name: "host", children: new VNode[] { state.Shown ? portal : null });
        }

        [Component]
        private static VNode RetargetingPortalHost()
        {
            var state = Hooks.UseStore(s_store, s => s);
            var target = state.Shown ? s_overlay : s_otherOverlay;
            return V.Div(name: "host", children: new VNode[]
            {
                V.Portal(target, new VNode[] { Presence(state.ChildKey) }),
            });
        }

        // A presence beside a boundary that catches on the same update. Its own container finalizes
        // before the boundary aborts the pass.
        [Component]
        private static VNode PresenceBesideAThrowingBoundary()
        {
            var state = Hooks.UseStore(s_store, s => s);
            return V.Div(name: "outer", children: new VNode[]
            {
                V.Div(name: "host", children: new VNode[] { state.Shown ? Presence(state.ChildKey) : null }),
                state.Shown ? null : V.Component(CatchingBoundary, key: "boundary"),
            });
        }

        [Component(IsErrorBoundary = true)]
        private static VNode CatchingBoundary()
        {
            Hooks.UseFallback(_ => V.Label(text: "caught"));
            return V.Component(Thrower, key: "thrower");
        }

        [Component]
        private static VNode Thrower() => throw new InvalidOperationException("boom");

        [Component]
        private static VNode PooledButtonHost()
        {
            var state = Hooks.UseStore(s_store, s => s);
            VNode inner = V.Button(name: "inner", children: new VNode[] { Presence(state.ChildKey) });
            return V.Div(name: "outer", children: new VNode[] { state.Shown ? inner : null });
        }

        [Test]
        public void Given_AnAnimatePresenceThatStoppedBeingRendered_When_ThePassCompletes_Then_ItsBoundaryStateIsRetired()
        {
            // Arrange — one presence committed under a parent element that stays mounted throughout.
            using var store = new PresenceStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(SiblingHost, key: "host"));
            var ctx = mounted.Root.Reconciler.Context;
            var recorded = ctx.PresenceStates.Count;

            // Act — the AnimatePresence node leaves the tree; the parent element and boundary fiber live on.
            store.Set(false, "a");
            mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            // Assert — that the expansion recorded a state at all is folded in: an expansion that recorded
            // none would satisfy the retirement on its own.
            Assert.That((recorded, ctx.PresenceStates.Count), Is.EqualTo((1, 0)));
        }

        [Test]
        public void Given_AnAnimatePresenceThatStoppedBeingRendered_When_ItIsRenderedAgainUnderANewChildKey_Then_TheDepartedChildIsNotResurrected()
        {
            // Arrange
            using var store = new PresenceStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(SiblingHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            var host = _root.Q<VisualElement>("host");
            store.Set(false, "a");
            scheduler.DrainImmediateForTest();

            // Act — the presence returns carrying a different keyed child.
            store.Set(true, "b");
            scheduler.DrainImmediateForTest();

            // Assert — a surviving committed set would splice "a" back in as an exiting ghost. Names rather
            // than a count, so the one element left standing is identified.
            Assert.That(NamesOf(host), Is.EqualTo("item-b"));
        }

        [Test]
        public void Given_AnAnimatePresenceInsideAnElementBeingTornDown_When_TheElementIsRemoved_Then_ItsBoundaryStateIsRetired()
        {
            // Arrange — the presence expands into a nested element, which is what its state is keyed on.
            using var store = new PresenceStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(NestedDivHost, key: "host"));
            var ctx = mounted.Root.Reconciler.Context;
            var recorded = ctx.PresenceStates.Count;

            // Act — removing the nested element takes the presence with it.
            store.Set(false, "a");
            mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            // Assert — same fold as the sibling case above.
            Assert.That((recorded, ctx.PresenceStates.Count), Is.EqualTo((1, 0)));
        }

        [Test]
        public void Given_APooledPresenceParent_When_ItIsRentedBackForAFreshAnimatePresence_Then_TheDepartedChildIsNotResurrected()
        {
            // Arrange — a Button parent returns to the element pool on removal and is rented back on the
            // next mount, so the reused instance re-forms the very key its prior state was recorded under.
            using var store = new PresenceStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(PooledButtonHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            var pooled = _root.Q<VisualElement>("inner");
            store.Set(false, "a");
            scheduler.DrainImmediateForTest();

            // Act
            store.Set(true, "b");
            scheduler.DrainImmediateForTest();

            // Assert — that the pool did hand the same instance back rides along, since a fresh parent
            // would form a different key and clear the case of the state it exists to catch.
            var reused = _root.Q<VisualElement>("inner");
            Assert.That((ReferenceEquals(pooled, reused), NamesOf(reused)), Is.EqualTo((true, "item-b")));
        }

        [Test]
        public void Given_AnAnimatePresenceWithInitialFalse_When_ItIsRenderedAgainAfterLeavingTheTree_Then_ItsChildMountsWithTheEnterSuppressedAgain()
        {
            // Arrange — a suppressed enter is what fires a classic Motion's OnEnterComplete straight away,
            // where a played one defers it to the animation, so counting those readings reads the
            // suppression. A boundary state outliving the node makes the second mount a later addition to
            // a presence already on screen instead.
            using var store = new PresenceStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(SuppressedEnterHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            var onFirstMount = s_enterCompletions;
            store.Set(false, "a");
            scheduler.DrainImmediateForTest();

            // Act
            store.Set(true, "a");
            scheduler.DrainImmediateForTest();

            // Assert — that the first mount contributed exactly one is folded in, so a second mount
            // reading twice cannot stand in for it.
            Assert.That((onFirstMount, s_enterCompletions), Is.EqualTo((1, 2)));
        }

        [Test]
        public void Given_APresenceReplacedByPlainLeaves_When_TheNewSideTakesTheFastPath_Then_ItsBoundaryStateIsRetired()
        {
            // Arrange — the container reconciles through the indexed/keyed diff rather than the general
            // walk, so what says its slots were emptied is not the general path's own finalize.
            using var store = new PresenceStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(FastPathHost, key: "host"));
            var ctx = mounted.Root.Reconciler.Context;
            var recorded = ctx.PresenceStates.Count;

            // Act
            store.Set(false, "a");
            mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            // Assert — same fold as the sibling case above.
            Assert.That((recorded, ctx.PresenceStates.Count), Is.EqualTo((1, 0)));
        }

        [Test]
        public void Given_AnAnimatePresenceInsideAPortal_When_ThePortalStopsBeingRendered_Then_TheDepartedChildIsNotResurrected()
        {
            // Arrange — the entry is keyed by the Portal's resolved target, a container the caller owns
            // and nothing tears down, and no walk descends into a PortalNode. Neither of the other routes
            // can see this one go.
            using var store = new PresenceStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(PortalHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            store.Set(false, "a");
            scheduler.DrainImmediateForTest();

            // Act
            store.Set(true, "b");
            scheduler.DrainImmediateForTest();

            // Assert
            Assert.That(NamesOf(s_overlay), Is.EqualTo("item-b"));
        }

        [Test]
        public void Given_AnAnimatePresenceInsideAPortal_When_ThePortalIsRetargetedAndBack_Then_TheDepartedChildIsNotResurrected()
        {
            // Arrange — a new target releases the range on the old one, which is the same teardown the
            // unmount above takes.
            using var store = new PresenceStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(RetargetingPortalHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            store.Set(false, "a");
            scheduler.DrainImmediateForTest();

            // Act
            store.Set(true, "b");
            scheduler.DrainImmediateForTest();

            // Assert
            Assert.That(NamesOf(s_overlay), Is.EqualTo("item-b"));
        }

        [Test]
        public void Given_APresenceContainerThatFinalized_When_ALaterBoundaryAbortsTheSamePass_Then_ItsBoundaryStateStillRetires()
        {
            // Arrange — an abort holds for the rest of the pass, so a reading taken per pass would spare
            // this entry; the container it names emptied its own slots before the abort was raised.
            using var store = new PresenceStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(PresenceBesideAThrowingBoundary, key: "host"));
            var ctx = mounted.Root.Reconciler.Context;
            var recorded = ctx.PresenceStates.Count;

            // Act — the presence leaves and the boundary catches, in one update.
            store.Set(false, "a");
            mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            // Assert — same fold as the sibling case above. Nothing reproduces this presence again, so a
            // pass that skipped it is the last one this route can act on.
            Assert.That((recorded, ctx.PresenceStates.Count), Is.EqualTo((1, 0)));
        }

        // GREEN_ON_BASE(characterization): the live state a retirement route must not reach, since a pass
        // that walks neither side of a presence says nothing about it.
        [Test]
        public void Given_APassThatWalksNeitherSideOfAPresence_When_ItCompletes_Then_TheLiveBoundaryStateSurvivesIt()
        {
            // Arrange — the sibling owns its own store, so its update reconciles its own slot alone.
            using var store = new PresenceStore();
            using var ticks = new TickStore();
            s_store = store;
            s_ticks = ticks;
            using var mounted = V.Mount(_root, V.Component(SiblingPairHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            var host = _root.Q<VisualElement>("host");

            // Act — a pass that reproduces the presence, then a top-level pass it takes no part in, then
            // the swap that reads its state. The middle pass is the one under test; the first is what
            // leaves a reading of the presence behind for it to be wrong about.
            store.Touch();
            scheduler.DrainImmediateForTest();
            ticks.Bump();
            scheduler.DrainImmediateForTest();
            store.Set(true, "b");
            scheduler.DrainImmediateForTest();

            // Assert — the departing key holds its slot as an exiting ghost, which it can only do out of
            // the committed set the sibling's pass left alone.
            Assert.That(NamesOf(host), Is.EqualTo("item-a,item-b"));
        }

        #region Helpers

        private static VNode Presence(string childKey) => V.AnimatePresence(key: "presence", children: new VNode[]
        {
            V.Motion(name: "item-" + childKey, key: childKey,
                variants: s_fade, animate: "visible", exit: "hidden",
                transition: new StyleTransitionConfig { DurationSec = 0.3f }),
        });

        private static string NamesOf(VisualElement parent)
        {
            var names = new List<string>();
            for (var i = 0; i < parent.childCount; i++) names.Add(parent.ElementAt(i).name);
            return string.Join(",", names);
        }

        #endregion
    }
}
