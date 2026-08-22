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
    /// <item>What a parked pass holds back is its own entries. An unrelated pass ending on that same
    /// context attaches the ref it created itself — early enough that the layout effect committed after
    /// it reads that ref — and so does a virtual list rendering a range from a scroll.</item>
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

        private static VisualElement s_seenByLayoutEffect;

        private static int s_layoutEffectRuns;

        // Renders the measured element only after the click, so the layout effect that reads it commits
        // in the same flush the element was created in — where the ref has to be attached already.
        [Component]
        private static VNode MeasuringHost()
        {
            var (measured, setMeasured) = Hooks.UseState(false);
            var target = Hooks.UseRef<VisualElement>();
            Hooks.UseLayoutEffect(() =>
            {
                if (measured)
                {
                    s_layoutEffectRuns++;
                    s_seenByLayoutEffect = target.Current;
                }
                return (Action)null;
            }, new object[] { measured });
            return V.Div(children: new VNode[]
            {
                V.Button(name: "trigger", onClick: () => setMeasured.Invoke(true)),
                measured ? V.Div(name: "measured", refCallback: target.SetElement) : null,
            });
        }

        // Parks a pass on a Reconciler of the caller's choosing and reports whether it parked, for the
        // caller to fold into its own assertion.
        private static bool ParkAPassOn(Velvet.Reconciler reconciler)
        {
            var parkedRoot = new VisualElement();
            var departing = TrackedListOf("departing");
            reconciler.Reconcile(parkedRoot, Array.Empty<VNode>(), departing);
            reconciler.Reconcile(parkedRoot, departing, TrackedListOf("arriving"), frameBudgetMs: 0.001);
            return reconciler.HasPendingWork;
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
            s_seenByLayoutEffect = null;
            s_layoutEffectRuns = 0;
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

        // Resumes a slice at a time up to the boundary where the arrival's setup is queued and its
        // departure is still in the tree, which is the window a shared queue opens. The first budgeted
        // call alone does not reach it. Returns whether the window was reached, for the caller to fold
        // into its own assertion.
        private bool ParkAPassWithASetupQueued()
        {
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
            return Reconciler.HasPendingWork && QueuedRefSetupCount() == 1;
        }

        [Test]
        public void Given_APassParkedWithASetupQueued_When_AnUnrelatedPassEndsOnTheSameContext_Then_TheDepartingCleanupStillRunsFirst()
        {
            // Arrange
            var parkedWithASetupQueued = ParkAPassWithASetupQueued();

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

        [Test]
        public void Given_APassParkedWithASetupQueued_When_AnUnrelatedPassCarriesARefOfItsOwn_Then_ItIsAttachedByTheTimeThatPassEnds()
        {
            // Arrange
            var parkedWithASetupQueued = ParkAPassWithASetupQueued();
            var siblingRoot = new VisualElement();
            VisualElement attached = null;

            // Act
            using (var sibling = new Velvet.Reconciler(Reconciler.Context))
            {
                sibling.Reconcile(siblingRoot, Array.Empty<VNode>(), new VNode[]
                {
                    V.Label(name: "unrelated", refCallback: element => { attached = element; return null; }),
                });
            }

            // Assert — read beside the window, because a context holding no parked pass reaches the same
            // attach with nothing this case is about measured. Taken before the parked pass is drained,
            // which is the point the sibling's own effects would commit against.
            Assert.That((parkedWithASetupQueued, ReferenceEquals(attached, siblingRoot.ElementAt(0))),
                Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(characterization): a layout effect reads the ref its own flush created.
        // The base attaches that ref as the walk reaches the element, so the reading holds there without
        // this branch's queue in the way. Moving the setup to the pass boundary has to keep it, and a
        // pass parked on a Reconciler this flush does not own is what nearly took it away.
        [Test]
        public void Given_APassParkedOnTheSameContext_When_AnUnrelatedFlushCommitsALayoutEffect_Then_ItReadsTheRefThatFlushCreated()
        {
            // Arrange — the mounted tree and the parked pass share one context and hold a Reconciler each,
            // which is what puts one pass's park in front of the other pass's boundary.
            using var mounted = V.Mount(Root, V.Component(MeasuringHost, key: "host"));
            using var parkedPass = new Velvet.Reconciler(mounted.Root.Reconciler.Context);
            var parked = ParkAPassOn(parkedPass);

            // Act
            Root.Q<UnityEngine.UIElements.Button>("trigger").SimulateClick();

            // Assert — the run count is read beside the reading, because an effect that never committed
            // holds null for a reason this case is not about.
            Assert.That((parked, s_layoutEffectRuns, s_seenByLayoutEffect != null),
                Is.EqualTo((true, 1, true)));
        }

        // GREEN_ON_BASE(characterization): an item's ref is attached once the range update returns.
        // The base attaches it as the item is created, with no queue for a parked pass to sit in front
        // of. What this adds to the range-update case in VirtualListTests is the parked pass beside it,
        // which is what the refusal this round replaced was keyed on.
        [Test]
        public void Given_APassParkedOnTheSameContext_When_AVirtualListRendersARangeFromAScroll_Then_TheItemRefIsAttached()
        {
            // Arrange — the park sits on a Reconciler of its own, so the range update below is driven from
            // a scroll with no pass of its own on the stack, which is the controller drain's own entrance.
            using var parkedPass = new Velvet.Reconciler(Reconciler!.Context);
            var parked = ParkAPassOn(parkedPass);
            VisualElement attached = null;
            var node = V.VirtualList(
                items: new[] { "a", "b", "c" },
                keySelector: item => item,
                itemHeight: 50f,
                renderer: item => V.Label(text: item, key: item,
                    refCallback: element => { attached = element; return null; }),
                overscan: 0);
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, Reconciler);
            var visibleContainer = scrollView.contentContainer.ElementAt(1);

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 50f);

            // Assert — read beside the park, because a context holding no parked pass reaches the same
            // attach with nothing this case is about measured.
            Assert.That(
                (parked, ReferenceEquals(attached, visibleContainer.ElementAt(visibleContainer.childCount - 1))),
                Is.EqualTo((true, true)));
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
