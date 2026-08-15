using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
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
    /// the only route that reaches it — both when the Portal stops being rendered and when its id is
    /// registered to a different element, the one entrance that releases the range without tearing the
    /// placeholder out.</item>
    /// <item>Two Portals rendering into one container reach one entry, and the one that closes first
    /// leaves the other's committed child alone.</item>
    /// <item>A presence beside a container holding another retires only its own entry — the one the same
    /// pass rendered again survives it.</item>
    /// <item>A presence replaced by a flat list of host leaves retires through the time-sliced diff's own
    /// removals, the reading a container that never reaches the general walk's finalize takes.</item>
    /// <item>Both suspended states are read, since a presence with committed children puts their keys on
    /// the container's old side and one with none does not — the two park in different strategies, and
    /// each strategy's resume settles what its own park left owed.</item>
    /// <item>An abort stops the removal pass of the container it reaches and holds for the rest of the
    /// pass, so whether an entry may retire is read per container: one whose own removals ran retires
    /// even though a later container's did not. The reading covers the fast path too, where the removals
    /// are the time-sliced diff's rather than the general walk's finalize.</item>
    /// <item>An abort and an exhausted frame budget both leave those removals unrun, and part there. The
    /// next pass expands both sides again, so an abort's reading is retaken; a park is resumed from the
    /// old side this pass already expanded, so nothing retakes it and the reading is carried to the slice
    /// that finishes the removals. The entry retires then, rather than outliving the leaf it named.</item>
    /// <item>A park a fresh pass discards carries nothing into a later park's resume, since the diff that
    /// owed those removals is the one nobody finishes. A resume that unwinds leaves the same diff behind,
    /// so what it owed must not reach the next park's resume and retire an entry still naming its leaf.</item>
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

        private const string RetargetId = "presence-retarget-target";

        // Distinguishes the arranged throw from any other InvalidOperationException the pass could raise,
        // so the case cannot record an unwind it did not cause.
        private const string ResumeUnwindMessage = "arranged unwind out of a resumed diff";

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
            RuntimeStateProbe.ClearPortalRegistry();
        }

        [TearDown]
        public void TearDown() => RuntimeStateProbe.ClearPortalRegistry();

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
        private static VNode IdPortalHost()
        {
            var state = Hooks.UseStore(s_store, s => s);
            return V.Div(name: "host", children: new VNode[]
            {
                V.Portal(RetargetId, new VNode[] { Presence(state.ChildKey) }),
            });
        }

        // Both Portals resolve to the same container, and each one's children walk starts at the keying
        // root, so the two presences agree on boundary fiber, parent element and position and reach ONE
        // entry.
        [Component]
        private static VNode SharedTargetPortalPairHost()
        {
            var state = Hooks.UseStore(s_store, s => s);
            return V.Div(name: "host", children: new VNode[]
            {
                V.Portal(s_overlay, new VNode[] { Presence("a") }),
                state.Shown ? V.Portal(s_overlay, new VNode[] { Presence("a") }) : null,
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

        // The presence's own container takes the fast path on the new side. The leaf replacing the
        // presence's FIRST sibling raises the abort while being created, which stops the diff between
        // phases — before the one that would take the presence's own leaf out of the tail. So the
        // container's removals do not run, whatever returning from the strategy suggests.
        [Component]
        private static VNode FastPathHostAbortingBeforeItsRemovalPhase()
        {
            var state = Hooks.UseStore(s_store, s => s);
            return V.Div(name: "host", children: state.Shown
                ? new VNode[] { V.Label(text: "first"), Presence(state.ChildKey) }
                : new VNode[]
                {
                    V.Div(name: "replacement", children: new VNode[] { V.Component(CatchingBoundary, key: "boundary") }),
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


        // A presence and a container holding another one, side by side: the outer container's own old-side
        // reproduction is already recorded when the inner container opens its scope, which is the only
        // arrangement where an inner scope begins anywhere but at the start of the list.
        [Component]
        private static VNode PresenceBesideAContainerHoldingAnother()
        {
            var state = Hooks.UseStore(s_store, s => s);
            return V.Div(name: "host", children: new VNode[]
            {
                state.Shown ? Presence(state.ChildKey) : null,
                V.Div(name: "inner", children: new VNode[] { Presence("kept") }),
            });
        }

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
            // Arrange — s_overlay is this Portal's target, and the fixture neither mounts it into the tree
            // nor tears it down, so no element-teardown route reaches the entry keyed on it. The walk of
            // the tree HOLDING the PortalNode never names what is inside it either.
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
        public void Given_AnAnimatePresenceInsideARegistryPortal_When_TheIdIsRegisteredElsewhereAndBack_Then_TheDepartedChildIsNotResurrected()
        {
            // Arrange — an id re-registered to a different element is the one entrance that releases the
            // range rather than tearing the placeholder out; a Portal handed a different container
            // outright cannot patch (ReconcileKeying.CanPatch compares the held element) and takes the
            // unmount route the case above already measures.
            using var store = new PresenceStore();
            s_store = store;
            FiberPortalRegistry.Register(RetargetId, s_overlay);
            using var mounted = V.Mount(_root, V.Component(IdPortalHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            ExpectOverwriteWarning();
            FiberPortalRegistry.Register(RetargetId, s_otherOverlay);
            scheduler.DrainImmediateForTest();

            // Act — the id names the first container again, and the presence returns carrying a
            // different keyed child.
            ExpectOverwriteWarning();
            FiberPortalRegistry.Register(RetargetId, s_overlay);
            store.Set(true, "b");
            scheduler.DrainImmediateForTest();

            // Assert
            Assert.That(NamesOf(s_overlay), Is.EqualTo("item-b"));
        }

        [Test]
        public void Given_APresenceBesideAContainerHoldingAnother_When_TheOuterOneStopsBeingRendered_Then_OnlyItsOwnEntryRetires()
        {
            // Arrange — one entry per parent element, and the inner container's scope opens over a list the
            // outer container's own reproduction is already in.
            using var store = new PresenceStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(PresenceBesideAContainerHoldingAnother, key: "host"));
            var ctx = mounted.Root.Reconciler.Context;
            var recorded = ctx.PresenceStates.Count;

            // Act
            store.Set(false, "a");
            ctx.BatchScheduler.DrainImmediateForTest();

            // Assert — that the two were recorded separately is folded in: one shared entry would make the
            // survivor indistinguishable from the departure.
            Assert.That((recorded, ctx.PresenceStates.Count), Is.EqualTo((2, 1)));
        }

        // GREEN_ON_BASE(characterization): neither Portal closing touched the entry the two of them share.
        // The sharing predates the retirement routes; what is pinned is that adding those left it alone.
        [Test]
        public void Given_TwoPortalsSharingOneContainer_When_TheSecondCloses_Then_TheFirstKeepsItsCommittedChild()
        {
            // Arrange — both Portals render into s_overlay, so their presences reach one entry.
            using var store = new PresenceStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(SharedTargetPortalPairHost, key: "host"));
            var ctx = mounted.Root.Reconciler.Context;
            var shared = ctx.PresenceStates.Count;

            // Act — the second Portal closes, then the first is patched again on the pass after, which is
            // the pass that reads whatever the close left of the entry.
            store.Set(false, "a");
            ctx.BatchScheduler.DrainImmediateForTest();
            store.Touch();
            ctx.BatchScheduler.DrainImmediateForTest();

            // Assert — that the two Portals really did reach ONE entry is folded in: two entries would
            // leave the second's teardown nothing of the first's to take, and nothing would be measured.
            Assert.That((shared, NamesOf(s_overlay)), Is.EqualTo((1, "item-a")));
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

        // GREEN_ON_BASE(characterization): on the base an entry ended with its boundary fiber and nothing else.
        // A container whose removals did not run could strand none of them, and that has to stay true.
        [Test]
        public void Given_APresenceOnAFastPathContainer_When_ItsDiffAbortsBeforeTheRemovalPhase_Then_ItsBoundaryStateSurvives()
        {
            // Arrange — the fast path interleaves its removals with the diff, so whether they ran has to
            // be asked of the strategy afterwards rather than taken from the call returning.
            using var store = new PresenceStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(FastPathHostAbortingBeforeItsRemovalPhase, key: "host"));
            var ctx = mounted.Root.Reconciler.Context;
            var host = _root.Q<VisualElement>("host");

            // Act — the presence leaves and the leaf replacing its first sibling catches, in one update.
            store.Set(false, "a");
            ctx.BatchScheduler.DrainImmediateForTest();

            // Assert — that the abort really did leave the presence's leaf standing is folded in: had the
            // removals emptied the slot, retiring the entry would be right and nothing would be measured.
            Assert.That((host.Q<VisualElement>("item-a") != null, ctx.PresenceStates.Count),
                Is.EqualTo((true, 1)));
        }

        // GREEN_ON_BASE(characterization): the base had no retirement route a parked container could reach.
        // Same reading as the abort case above, for the other way the fast path leaves its removals owed.
        [Test]
        public void Given_APresenceOnAFastPathContainer_When_ItsDiffParksBeforeItsRemovals_Then_ItsBoundaryStateSurvives()
        {
            // Arrange — a hundred rows against a budget that yields after one node, so the park lands in
            // the diff's first phase with the presence's leaf still in the tail a later phase would take.
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var committed = RowsWith(Presence("a"));
            reconciler.Reconcile(root, Array.Empty<VNode>(), committed, frameBudgetMs: 0);
            var ctx = reconciler.Context;

            // Act — the presence is gone from the new side, and the budget parks the diff.
            reconciler.Reconcile(root, committed, Rows("moved-"), frameBudgetMs: 0.001);

            // Assert — that the park really did leave the presence's leaf standing is folded in: a diff
            // that ran to completion would have emptied the slot, and retiring the entry would be right.
            // The strategy is pinned for the reason ParkedInTheKeyedState carries.
            Assert.That(
                (ParkedInTheKeyedState(reconciler), root.Q<VisualElement>("item-a") != null, ctx.PresenceStates.Count),
                Is.EqualTo((true, true, 1)));
        }

        // GREEN_ON_BASE(characterization): the base had no retirement route a parked container could reach.
        // The case above parks in the KEYED state, because a presence reproducing children puts their keys
        // on the container's old side; this is the same reading for the other strategy.
        [Test]
        public void Given_AnEmptyPresenceOnAFastPathContainer_When_ItsIndexedDiffParks_Then_ItsBoundaryStateSurvives()
        {
            // Arrange — a presence with nothing committed reproduces no leaf at all, so the container's
            // old side carries no key and the diff takes the indexed strategy rather than the keyed one.
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var committed = RowsWith(V.AnimatePresence(key: "presence", children: Array.Empty<VNode>()));
            reconciler.Reconcile(root, Array.Empty<VNode>(), committed, frameBudgetMs: 0);
            var ctx = reconciler.Context;

            // Act
            reconciler.Reconcile(root, committed, Rows("moved-"), frameBudgetMs: 0.001);

            // Assert — that the park landed in the indexed state is folded in: parking in the keyed one
            // would make this a second reading of the case above rather than a reading of the other term.
            Assert.That((ParkedInTheIndexedState(reconciler), ctx.PresenceStates.Count),
                Is.EqualTo((true, 1)));
        }

        [Test]
        public void Given_APresenceOnAParkedFastPathContainer_When_TheResumeFinishesTheDiff_Then_ItsBoundaryStateIsRetired()
        {
            // Arrange — the same park as the case above, left where that case stops reading.
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var committed = RowsWith(Presence("a"));
            reconciler.Reconcile(root, Array.Empty<VNode>(), committed, frameBudgetMs: 0);
            var ctx = reconciler.Context;
            reconciler.Reconcile(root, committed, Rows("moved-"), frameBudgetMs: 0.001);
            var parkedKeyed = ParkedInTheKeyedState(reconciler);

            // Act — the continuation runs the removals the park left owed.
            reconciler.ContinueReconcile();

            // Assert — that the diff really did park is folded in: a first slice that ran to completion
            // would retire the entry through the container's own reading and measure nothing of the resume.
            // The strategy is pinned for the reason ParkedInTheKeyedState carries.
            Assert.That((parkedKeyed, root.Q<VisualElement>("item-a") != null, ctx.PresenceStates.Count),
                Is.EqualTo((true, false, 0)));
        }

        [Test]
        public void Given_APresenceOnAParkedFastPathContainer_When_ItIsRenderedAgainAfterTheResume_Then_TheDepartedChildIsNotResurrected()
        {
            // Arrange — the park, then the resume that empties the presence's slot for real.
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var committed = RowsWith(Presence("a"));
            reconciler.Reconcile(root, Array.Empty<VNode>(), committed, frameBudgetMs: 0);
            var moved = Rows("moved-");
            reconciler.Reconcile(root, committed, moved, frameBudgetMs: 0.001);
            var parked = reconciler.HasPendingWork;
            reconciler.ContinueReconcile();

            // Act — the presence returns at the same position carrying a different keyed child.
            reconciler.Reconcile(root, moved, RowsWith(Presence("b")), frameBudgetMs: 0);

            // Assert — a surviving committed set would splice "a" back in as an exiting ghost ahead of it.
            // That the diff parked is folded in for the same reason as the case above.
            Assert.That((parked, TailNamesOf(root, 2)), Is.EqualTo((true, "row-99,item-b")));
        }

        [Test]
        public void Given_APresenceOnAParkedFastPathContainer_When_TheResumeParksAgain_Then_ItsBoundaryStateRetiresAtTheSliceThatFinishes()
        {
            // Arrange — the park, then a resume slice on a budget small enough to park a second time.
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var committed = RowsWith(Presence("a"));
            reconciler.Reconcile(root, Array.Empty<VNode>(), committed, frameBudgetMs: 0);
            var ctx = reconciler.Context;
            reconciler.Reconcile(root, committed, Rows("moved-"), frameBudgetMs: 0.001);
            reconciler.ContinueReconcile(frameBudgetMs: 0.001);
            var parkedAgain = reconciler.HasPendingWork;

            // Act — the slice that finishes the diff.
            reconciler.ContinueReconcile();

            // Assert — that the middle slice parked rather than finishing is folded in: without it this
            // is a second reading of the single-slice resume beside it.
            Assert.That((parkedAgain, ctx.PresenceStates.Count), Is.EqualTo((true, 0)));
        }

        [Test]
        public void Given_AnEmptyPresenceOnAParkedFastPathContainer_When_TheIndexedResumeFinishesTheDiff_Then_ItsBoundaryStateIsRetired()
        {
            // Arrange — the park Given_AnEmptyPresenceOnAFastPathContainer_When_ItsIndexedDiffParks_… stops
            // at, carried to the slice that resumes it. The keyed resume above reaches the same settle
            // through the other strategy's call, and only through that one.
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var committed = RowsWith(V.AnimatePresence(key: "presence", children: Array.Empty<VNode>()));
            reconciler.Reconcile(root, Array.Empty<VNode>(), committed, frameBudgetMs: 0);
            var ctx = reconciler.Context;
            reconciler.Reconcile(root, committed, Rows("moved-"), frameBudgetMs: 0.001);
            var parkedIndexed = ParkedInTheIndexedState(reconciler);

            // Act — the continuation runs the removals the park left owed.
            reconciler.ContinueReconcile();

            // Assert — that the park landed in the indexed state is folded in: parking in the keyed one
            // would make this a second reading of the keyed resume rather than a reading of this one.
            Assert.That((parkedIndexed, ctx.PresenceStates.Count), Is.EqualTo((true, 0)));
        }

        [Test]
        public void Given_AParkedFastPathContainer_When_ItsResumeUnwinds_Then_ALaterParksResumeLeavesTheStrandedEntryAlone()
        {
            // Arrange — one reconciler, two roots. The first root's resume throws partway through the
            // diff, leaving its removals unrun and its leaf in the tree; the second root then parks and
            // resumes cleanly, which is the slice that settles whatever the first left owed.
            using var reconciler = new Reconciler();
            var ctx = reconciler.Context;
            var first = new VisualElement();
            var firstCommitted = RowsWith(Presence("a"));
            reconciler.Reconcile(first, Array.Empty<VNode>(), firstCommitted, frameBudgetMs: 0);
            reconciler.Reconcile(first, firstCommitted, RowsThrowingAt("moved-", 99), frameBudgetMs: 0.001);
            var firstParked = reconciler.HasPendingWork;
            var unwound = false;
            try
            {
                reconciler.ContinueReconcile();
            }
            catch (InvalidOperationException e) when (e.Message == ResumeUnwindMessage)
            {
                unwound = true;
            }
            var second = new VisualElement();
            var secondCommitted = RowsWith(Presence("b"));
            reconciler.Reconcile(second, Array.Empty<VNode>(), secondCommitted, frameBudgetMs: 0);
            reconciler.Reconcile(second, secondCommitted, Rows("gone-"), frameBudgetMs: 0.001);
            var secondParked = reconciler.HasPendingWork;

            // Act — the continuation finishes the second root's diff, and only that one.
            reconciler.ContinueReconcile();

            // Assert — the first root's entry is the one left, since the unwound diff never emptied its
            // slots. Both parks and the unwind are folded in: each is what puts the span in play, and
            // without it the count would be right for the wrong reason.
            Assert.That((firstParked, unwound, secondParked, ctx.PresenceStates.Count),
                Is.EqualTo((true, true, true, 1)));
        }

        [Test]
        public void Given_AParkDiscardedByAFreshPass_When_ALaterParkResumes_Then_OnlyTheLaterParksEntryRetires()
        {
            // Arrange — one reconciler, two roots. The first root's diff parks and is then discarded by a
            // fresh pass on the second, whose own diff parks in turn.
            using var reconciler = new Reconciler();
            var ctx = reconciler.Context;
            var first = new VisualElement();
            var firstCommitted = RowsWith(Presence("a"));
            reconciler.Reconcile(first, Array.Empty<VNode>(), firstCommitted, frameBudgetMs: 0);
            reconciler.Reconcile(first, firstCommitted, Rows("moved-"), frameBudgetMs: 0.001);
            var firstParked = reconciler.HasPendingWork;
            var second = new VisualElement();
            var secondCommitted = RowsWith(Presence("b"));
            reconciler.Reconcile(second, Array.Empty<VNode>(), secondCommitted, frameBudgetMs: 0);
            reconciler.Reconcile(second, secondCommitted, Rows("gone-"), frameBudgetMs: 0.001);
            var secondParked = reconciler.HasPendingWork;

            // Act — the continuation finishes the second root's diff, and only that one.
            reconciler.ContinueReconcile();

            // Assert — the first root's entry has to be the one left, since the discarded diff never
            // emptied its slots. Both parks are folded in: without either, nothing is owed across a discard.
            Assert.That(
                (firstParked, secondParked, ctx.PresenceStates.Count),
                Is.EqualTo((true, true, 1)));
        }

        // GREEN_ON_BASE(characterization): a pass that walks neither side of a presence says nothing of it.
        // This is the live state a retirement route must not reach, held to what the base already did.
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
            // the swap that reads its state. The middle pass is the one under test; the first is what puts
            // the presence through a reproduction, so a route keyed on one has something to act on.
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

        // The last few names, for the arrangements whose hundred leading rows are scaffolding rather than
        // what is read.
        private static string TailNamesOf(VisualElement parent, int count)
        {
            var names = new List<string>();
            for (var i = Math.Max(0, parent.childCount - count); i < parent.childCount; i++)
            {
                names.Add(parent.ElementAt(i).name);
            }
            return string.Join(",", names);
        }

        private static void ExpectOverwriteWarning() => LogAssert.Expect(LogType.Warning,
            $"[FiberPortalRegistry] Id \"{RetargetId}\" is already registered. Overwriting.");

        private static VNode[] Rows(string prefix)
        {
            var rows = new VNode[100];
            for (var i = 0; i < rows.Length; i++) rows[i] = V.Div(name: prefix + i);
            return rows;
        }

        // One row whose child expansion throws, so a resume that reaches it unwinds out of the strategy
        // rather than parking or finishing. The memo sits under the row rather than beside it so the
        // container keeps the fast path: only the nested walk of that row's own children resolves it, and
        // the container's own children stay a flat list of host leaves.
        private static VNode[] RowsThrowingAt(string prefix, int index)
        {
            var rows = Rows(prefix);
            rows[index] = V.Div(name: prefix + index, children: new VNode[]
            {
                V.Memoized(() => throw new InvalidOperationException(ResumeUnwindMessage)),
            });
            return rows;
        }

        private static VNode[] RowsWith(VNode tail)
        {
            var rows = Rows("row-");
            var all = new VNode[rows.Length + 1];
            Array.Copy(rows, all, rows.Length);
            all[rows.Length] = tail;
            return all;
        }

        // Which of the two suspended states the container parked in. Read by reflection rather than from a
        // member on the reconciler, since production types here carry nothing test-only. A case that stands
        // opposite the other strategy's reading pins which state it parked in, so a change moving it onto
        // the other's fails rather than leaving the pair measuring one term twice.
        private static bool ParkedInTheIndexedState(Reconciler reconciler)
            => PendingState(reconciler, "PendingIndexedState") != null;

        private static bool ParkedInTheKeyedState(Reconciler reconciler)
            => PendingState(reconciler, "PendingKeyedState") != null;

        private static object PendingState(Reconciler reconciler, string property)
        {
            var child = typeof(Reconciler)
                .GetField("_childReconciler", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(reconciler)!;
            return child.GetType()
                .GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
                .GetValue(child);
        }

        #endregion
    }
}
