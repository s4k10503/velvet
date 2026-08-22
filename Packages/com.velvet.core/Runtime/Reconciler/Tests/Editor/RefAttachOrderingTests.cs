using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies when a callback ref attaches relative to the detach of the ref a replaced element
    /// carried. The general reconcile path creates the arriving element before it removes the
    /// departing one, so a ref that attaches at creation time would run its setup ahead of the
    /// departing element's cleanup — and the register-in-setup / unregister-in-cleanup idiom would
    /// then have the departing cleanup drop the arriving registration.
    /// <list type="bullet">
    /// <item>The departing element's cleanup runs before the arriving element's setup.</item>
    /// <item>A portal id registered by that idiom resolves to the arriving element afterwards.</item>
    /// <item>That order survives a pass split across slices, where the arrival is processed in one and
    /// the departure removed in a later one — including when an unrelated pass on the same context runs
    /// to completion in between, since the queue belongs to the context rather than to one pass.</item>
    /// <item>What a setup reads of an earlier setup's writes is this pass's, not the previous pass's:
    /// the queued setups run as one sequence, so nothing separates a pair of them.</item>
    /// <item>A setup whose own element leaves while it runs leaves no ref entry behind for it, and the
    /// cleanup it returned still runs. Running at the pass boundary is what makes that reachable: a
    /// discrete event a setup dispatches from there commits synchronously, where one dispatched from
    /// inside the walk was held back.</item>
    /// </list>
    /// The first three are driven by a leaf whose explicit key changes, which is what sends the pair down
    /// the create-then-remove branch of the general path rather than the in-place patch.
    /// </summary>
    [TestFixture]
    internal sealed class RefAttachOrderingTests : ReconcilerTestFixture
    {
        private const string ModalRootId = "ref-attach-ordering-modal-root";

        private static readonly List<string> s_log = new();

        [Component]
        private static VNode Screen(string which)
            => V.Div(key: which, name: which, refCallback: Tracked(which));

        [Component]
        private static VNode PortalHostScreen(string which)
            => V.Div(key: which, name: which, refCallback: RegisterModalRoot);

        private static Func<VisualElement, Action> Tracked(string which)
            => _ =>
            {
                s_log.Add("attach:" + which);
                return () => s_log.Add("detach:" + which);
            };

        private static Action RegisterModalRoot(VisualElement element)
        {
            FiberPortalRegistry.Register(ModalRootId, element);
            return () => FiberPortalRegistry.Unregister(ModalRootId);
        }

        // The tracked leaf first, then enough unchanged trailing keys that the keyed machine has somewhere
        // left to yield once it has processed the arrival.
        private static VNode[] TrackedListOf(string which)
        {
            var nodes = new VNode[6];
            nodes[0] = V.Div(key: which, name: which, refCallback: Tracked(which));
            for (var i = 1; i < nodes.Length; i++)
            {
                nodes[i] = V.Label(key: "filler" + i, text: "f" + i);
            }
            return nodes;
        }

        private static VisualElement s_selfRemoved;

        private static int s_selfRemovedCleanups;

        // The setup dispatches a discrete event whose handler stops rendering the very element the setup
        // was handed, so FiberElementCleaner runs for that element while its setup is still on the stack.
        private static Action SelfRemovingSetup(VisualElement element)
        {
            s_selfRemoved = element;
            element.parent?.Q<UnityEngine.UIElements.Button>("trigger")?.SimulateClick();
            return null;
        }

        // The same dispatch, returning the cleanup a setup that acquired a resource would return.
        private static Action SelfRemovingSetupWithCleanup(VisualElement element)
        {
            SelfRemovingSetup(element);
            return () => s_selfRemovedCleanups++;
        }

        [Component]
        private static VNode SelfRemovingHost(Func<VisualElement, Action> setup)
        {
            var (show, setShow) = Hooks.UseState(true);
            return V.Div(children: show
                ? new VNode[]
                {
                    V.Button(name: "trigger", onClick: () => setShow.Invoke(false)),
                    V.Div(name: "victim", refCallback: setup),
                }
                : new VNode[]
                {
                    V.Button(name: "trigger", onClick: () => setShow.Invoke(false)),
                });
        }

        private void DrainPendingWork(int maxIterations = 500)
        {
            var iterations = 0;
            while (Reconciler!.HasPendingWork)
            {
                if (iterations++ >= maxIterations)
                {
                    Assert.Fail($"DrainPendingWork: {maxIterations} iterations exceeded without completion");
                }
                Reconciler.ContinueReconcile(frameBudgetMs: 0.001);
            }
        }

        public override void SetUp()
        {
            base.SetUp();
            s_log.Clear();
            s_selfRemoved = null;
            s_selfRemovedCleanups = 0;
        }

        public override void TearDown()
        {
            base.TearDown();
            FiberPortalRegistry.Unregister(ModalRootId);
        }

        [Test]
        public void Given_AKeyedLeafReplacedInOnePass_When_TheReplacementCarriesARef_Then_TheDepartingCleanupRunsFirst()
        {
            // Arrange
            var departing = new VNode[] { V.Component(Screen, "departing", key: "screen") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), departing);
            s_log.Clear();

            // Act
            Reconciler.Reconcile(Root, departing,
                new VNode[] { V.Component(Screen, "arriving", key: "screen") });

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("detach:departing,attach:arriving"));
        }

        [Test]
        public void Given_AKeyedLeafReplacedUnderAFrameBudget_When_TheSliceYieldsBeforeTheRemovals_Then_TheDepartingCleanupStillRunsFirst()
        {
            // Arrange — trailing keys past the replaced one, so the pass crosses several slice boundaries.
            // Measured, the first budgeted call expires while the keyed machine is still building its old-key
            // map; what splits the pair is a later slice, the one ending with the arrival created and the
            // removal pass not yet reached.
            var departing = TrackedListOf("departing");
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), departing);
            s_log.Clear();

            // Act
            Reconciler.Reconcile(Root, departing, TrackedListOf("arriving"), frameBudgetMs: 0.001);
            DrainPendingWork();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("detach:departing,attach:arriving"));
        }

        [Test]
        public void Given_ASetupThatRemovesTheElementItWasHanded_When_TheDrainRecordsIt_Then_NoRefEntryIsLeftForIt()
        {
            // Arrange / Act
            using var mounted = V.Mount(Root,
                V.Component(SelfRemovingHost, (Func<VisualElement, Action>)SelfRemovingSetup, key: "host"));
            var refCallbacks = mounted.Root.Reconciler.Context.RefCallbacks;

            // Assert — the removal is read beside the entry it should have taken, because the same
            // reading over a tree that still holds the element says nothing about the removal path.
            Assert.That(
                (s_selfRemoved != null && s_selfRemoved.parent == null,
                    s_selfRemoved != null && refCallbacks.ContainsKey(s_selfRemoved)),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_APassParkedWithASetupQueued_When_AnUnrelatedPassEndsOnTheSameContext_Then_TheDepartingCleanupStillRunsFirst()
        {
            // Arrange — resumed a slice at a time up to the boundary where the arrival's setup is queued
            // and its departure is still in the tree, which is the window the queue's context-wide
            // ownership opens. The first budgeted call alone does not reach it.
            var departing = TrackedListOf("departing");
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), departing);
            s_log.Clear();
            Reconciler.Reconcile(Root, departing, TrackedListOf("arriving"), frameBudgetMs: 0.001);
            var slices = 0;
            while (Reconciler.HasPendingWork && QueuedRefSetupCount() == 0)
            {
                if (slices++ >= 500) Assert.Fail("no slice boundary left the arrival's setup queued");
                Reconciler.ContinueReconcile(frameBudgetMs: 0.001);
            }
            var parkedWithASetupQueued = Reconciler.HasPendingWork && QueuedRefSetupCount() == 1;

            // Act — a sibling fiber's Reconciler runs a pass of its own to completion on the shared context.
            using (var sibling = new Velvet.Reconciler(Reconciler.Context))
            {
                sibling.Reconcile(new VisualElement(), Array.Empty<VNode>(),
                    new VNode[] { V.Label(name: "unrelated") });
            }
            DrainPendingWork();

            // Assert — the window is read beside the order, because a pass holding no queued setup by then
            // reaches the same order with nothing about the sibling measured.
            Assert.That((parkedWithASetupQueued, string.Join(",", s_log)),
                Is.EqualTo((true, "detach:departing,attach:arriving")));
        }

        // -1 where the context holds no such queue, so a tree that never had one disagrees with the
        // assertion instead of raising out of this helper and carrying no reading at all.
        private int QueuedRefSetupCount()
        {
            var field = typeof(ReconcilerContext)
                .GetField("_pendingRefAttaches", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) return -1;
            return ((ICollection)field.GetValue(Reconciler!.Context)).Count;
        }

        [Test]
        public void Given_ASetupWhoseElementLeavesWhileItRuns_When_ItReturnedACleanup_Then_ThatCleanupStillRuns()
        {
            // Arrange / Act
            using var mounted = V.Mount(Root,
                V.Component(SelfRemovingHost, (Func<VisualElement, Action>)SelfRemovingSetupWithCleanup, key: "host"));

            // Assert — the removal is read beside the count, because a tree that still holds the element
            // owes the cleanup to a later removal and would read zero for a reason this case is not about.
            Assert.That(
                (s_selfRemoved != null && s_selfRemoved.parent == null, s_selfRemovedCleanups),
                Is.EqualTo((true, 1)));
        }

        // GREEN_ON_BASE(characterization): the base runs a setup as the walk reaches its element, so the
        // second already read the first's write there. The move has to keep that, and it is what the guide
        // now states in place of a claim that this read moved to the previous pass.
        // Measured, walking the drain's queue from its tail reddens this case, beside the one virtual-list
        // case that reads an item's own ref.
        [Test]
        public void Given_TwoElementsCarryingRefsInOnePass_When_TheSecondSetupReadsTheFirstsRef_Then_ItSeesThisPassesElement()
        {
            // Arrange
            VisualElement seen = null;
            var first = new Ref<VisualElement>();
            var tree = new VNode[]
            {
                V.Div(name: "first", refCallback: first.SetElement),
                V.Div(name: "second", refCallback: _ => { seen = first.Current; return null; }),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(seen, Is.SameAs(Root!.ElementAt(0)));
        }

        [Test]
        public void Given_TheRegisterInRefIdiom_When_AKeyedLeafIsReplaced_Then_TheIdResolvesToTheArrivingElement()
        {
            // Arrange
            var departing = new VNode[] { V.Component(PortalHostScreen, "departing", key: "screen") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), departing);

            // Act
            Reconciler.Reconcile(Root, departing,
                new VNode[] { V.Component(PortalHostScreen, "arriving", key: "screen") });

            // Assert
            Assert.That(FiberPortalRegistry.Get(ModalRootId), Is.SameAs(Root!.ElementAt(0)));
        }
    }
}
