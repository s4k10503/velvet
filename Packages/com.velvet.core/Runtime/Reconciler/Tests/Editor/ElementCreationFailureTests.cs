using System;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a pass that fails or is aborted partway leaves behind: the elements built before the
    /// failure are released rather than left to the ref drain and the deferred mounts, so a ref one of them
    /// carries is never set up on an element no tree holds.
    /// <list type="bullet">
    /// <item>A child an element built before a later child's constructor refused gets no ref.</item>
    /// <item>A leaf the general walk built before a later leaf's constructor refused gets no ref, though the
    /// walk had not placed it anywhere yet.</item>
    /// <item>A walk left by a suspend its own Suspense spans did not catch releases the leaves it built the
    /// same way, and the element whose creation that suspend ended is left to the boundary, which reveals
    /// its children once the resource resolves.</item>
    /// <item>A leaf the keyed diff built before a later sibling's constructor refused gets no ref, in a
    /// synchronous pass and in a time-sliced one, and neither does one a parked pass had built when a new
    /// pass discarded it. A component mounted inside such a leaf by a synchronous pass never runs its layout
    /// effect.</item>
    /// <item>Where a boundary's catch aborts the pass, the row the keyed diff was building and the leaf the
    /// general walk had built both get no ref.</item>
    /// <item>A Portal or a z-layer child inside an element whose creation failed mounts nothing when the
    /// deferred mounts drain, and the pooled element of the z-layer child stays detached.</item>
    /// </list>
    /// The virtualized list reaches the same creation paths for a row; <c>VirtualListTests</c> holds what a
    /// half-built row leaves.
    /// </summary>
    [TestFixture]
    internal sealed class ElementCreationFailureTests : ReconcilerTestFixture
    {
        private sealed class ConstructionRefusingElement : VisualElement
        {
            public ConstructionRefusingElement()
            {
                throw new InvalidOperationException("constructor refused");
            }
        }

        private int _refSetUps;
        private int _refCleanUps;

        public override void SetUp()
        {
            base.SetUp();
            _refSetUps = 0;
            _refCleanUps = 0;
        }

        private Action CountRef(VisualElement _)
        {
            _refSetUps++;
            return () => _refCleanUps++;
        }

        // Reconciles tree onto an empty root, which throws, and returns what reached the caller beside what
        // the ref counted: the pass's own boundary has run its ref drain by the time the call returns.
        private string OutcomeOfFailedMount(VNode[] tree)
        {
            var thrown = "nothing";
            try
            {
                Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);
            }
            catch (Exception exception)
            {
                thrown = exception.GetBaseException().Message;
            }

            return "[" + thrown + "] thrown, ref " + _refSetUps + " set up, " + _refCleanUps + " cleaned up";
        }

        [Test]
        public void Given_AnElementWhoseSecondChildRefusesConstruction_When_ItIsCreated_Then_ItsFirstChildGetsNoRef()
        {
            // Arrange — the children take the indexed diff, which places the first child inside the element
            // before the second is built.
            var tree = new VNode[]
            {
                V.Div(children: new VNode[]
                {
                    V.Label(refCallback: CountRef),
                    V.Custom<ConstructionRefusingElement>(),
                }),
            };

            // Act
            var outcome = OutcomeOfFailedMount(tree);

            // Assert — what reached the caller rides along: an element that builds completely is placed, and
            // the ref of a child it holds is set up rightly.
            Assert.That(outcome, Is.EqualTo("[constructor refused] thrown, ref 0 set up, 0 cleaned up"));
        }

        [Test]
        public void Given_ALeafTheGeneralWalkBuiltBeforeASiblingRefusedConstruction_When_TheWalkFails_Then_ThatLeafGetsNoRef()
        {
            // Arrange — a null among the leaves sends them through the general walk
            // (GeneralPathReconciler.NeedsExpansion), which places none of them until every one is built.
            var tree = new VNode[]
            {
                V.Label(refCallback: CountRef),
                null,
                V.Custom<ConstructionRefusingElement>(),
            };

            // Act
            var outcome = OutcomeOfFailedMount(tree);

            // Assert — both terms for the reason the case above gives.
            Assert.That(outcome, Is.EqualTo("[constructor refused] thrown, ref 0 set up, 0 cleaned up"));
        }

        [TestCase(0d, "not parked")]
        [TestCase(double.Epsilon, "parked")]
        public void Given_AKeyedDiffThatBuiltALeafBeforeASiblingRefusedConstruction_When_ItFails_Then_ThatLeafGetsNoRef(
            double frameBudgetMs, string expectedPark)
        {
            // Arrange — neither end of the new side keeps a key from the old side, so both leaves are built by
            // the keyed Pass 2, which places none of them until every one is built.
            var oldTree = new VNode[] { V.Label(key: "old", text: "old"), V.Label(key: "tail", text: "tail") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var nextTree = new VNode[]
            {
                V.Label(key: "new", text: "new", refCallback: CountRef),
                V.Custom<ConstructionRefusingElement>(key: "refuse"),
            };
            var thrown = "nothing";
            var parked = false;

            // Act
            try
            {
                Reconciler.Reconcile(Root, oldTree, nextTree, frameBudgetMs: frameBudgetMs);
                parked = Reconciler.HasPendingWork;
                while (Reconciler.HasPendingWork) Reconciler.ContinueReconcile(frameBudgetMs);
            }
            catch (Exception exception)
            {
                thrown = exception.GetBaseException().Message;
            }

            // Assert — the park term says the budgeted run took the time-sliced machine, whose release is not
            // the synchronous pass's.
            Assert.That(
                "[" + thrown + "] thrown, " + (parked ? "parked" : "not parked") + ", ref " + _refSetUps + " set up",
                Is.EqualTo("[constructor refused] thrown, " + expectedPark + ", ref 0 set up"));
        }

        [Test]
        public void Given_AParkedKeyedPassThatBuiltALeaf_When_ANewPassDiscardsIt_Then_ThatLeafGetsNoRef()
        {
            // Arrange — under the smallest budget the continuation parks again, with the first new leaf built
            // and no row placed yet.
            var oldTree = new VNode[] { V.Label(key: "old", text: "old"), V.Label(key: "tail", text: "tail") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var nextTree = new VNode[]
            {
                V.Label(key: "new", text: "new", refCallback: CountRef),
                V.Label(key: "other", text: "other"),
            };
            Reconciler.Reconcile(Root, oldTree, nextTree, frameBudgetMs: double.Epsilon);
            var parkedFirst = Reconciler.HasPendingWork;
            Reconciler.ContinueReconcile(double.Epsilon);
            var parkedSecond = Reconciler.HasPendingWork;

            // Act
            Reconciler.Reconcile(Root, oldTree, oldTree);

            // Assert — both park terms, since a pass that ran to its end places the leaf and sets its ref up
            // rightly.
            Assert.That(
                (parkedFirst && parkedSecond ? "parked twice" : "not parked twice") + ", ref " + _refSetUps + " set up",
                Is.EqualTo("parked twice, ref 0 set up"));
        }

        private static int s_probeSetUps;
        private static int s_probeCleanUps;

        [Component(Compiler = false)]
        private static VNode LayoutEffectProbeRender()
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_probeSetUps++;
                return () => s_probeCleanUps++;
            }, Array.Empty<object>());
            return V.Label(text: "probe");
        }

        private static Action<int> s_rowHostSetTick;

        [Component(Compiler = false)]
        private static VNode KeyedRowHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_rowHostSetTick = setTick;
            return V.Div(children: tick == 1
                ? new VNode[]
                {
                    V.Div(key: "row", children: new VNode[] { V.Component(LayoutEffectProbeRender, key: "probe") }),
                    V.Custom<ConstructionRefusingElement>(key: "refuse"),
                }
                : new VNode[] { V.Label(key: "old", text: "old"), V.Label(key: "tail", text: "tail") });
        }

        [Test]
        public void Given_AKeyedRowHoldingAComponentBuiltBeforeASiblingRefusedConstruction_When_TheNextRenderCommits_Then_TheComponentsEffectNeverRuns()
        {
            // Arrange — the keyed Pass 2 of the first case, with a component mounted inside the leaf it builds,
            // and no boundary: the host that owns the failed render stays mounted.
            s_probeSetUps = 0;
            s_probeCleanUps = 0;
            var root = new VisualElement();
            var priorIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                using var mounted = V.Mount(root, V.Component(KeyedRowHostRender, key: "host"));
                s_rowHostSetTick.Invoke(1);
                mounted.FlushStateForTest();

                // Act
                s_rowHostSetTick.Invoke(2);
                mounted.FlushStateForTest();
                mounted.FlushEffectsForTest();

                // Assert
                Assert.That("effect " + s_probeSetUps + " set up, " + s_probeCleanUps + " cleaned up",
                    Is.EqualTo("effect 0 set up, 0 cleaned up"));
            }
            finally
            {
                LogAssert.ignoreFailingMessages = priorIgnore;
            }
        }

        private static Func<VisualElement, Action> s_rowRef;
        private static bool s_rowRenderRefused;
        private static int s_keyedAbortShape;
        private static Action<int> s_keyedAbortSetTick;

        [Component(Compiler = false)]
        private static VNode RefusingRowRender()
        {
            if (s_rowRenderRefused) throw new InvalidOperationException("render refused");
            return V.Label(text: "row");
        }

        // The row that raises the abort carries the ref, and reaches the keyed Pass 2 by each of the three
        // branches that build an element there: a key the old side never held, a key whose old element is of
        // another type, and a key a new sibling ahead of it already claimed.
        private static VNode[] KeyedAbortRows(bool next)
        {
            var aborting = V.Div(key: "d", refCallback: s_rowRef,
                children: new VNode[] { V.Component(RefusingRowRender, key: "refusing") });
            return s_keyedAbortShape switch
            {
                0 => next
                    ? new VNode[] { aborting, V.Label(key: "z", text: "z") }
                    : new VNode[] { V.Label(key: "x", text: "x"), V.Label(key: "y", text: "y") },
                1 => next
                    ? new VNode[] { V.Label(key: "w", text: "w"), aborting }
                    : new VNode[] { V.Label(key: "x", text: "x"), V.Label(key: "d", text: "d") },
                _ => next
                    ? new VNode[] { V.Div(key: "d"), aborting }
                    : new VNode[] { V.Label(key: "x", text: "x"), V.Div(key: "d") },
            };
        }

        [Component(Compiler = false)]
        private static VNode KeyedAbortHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_keyedAbortSetTick = setTick;
            return V.Div(children: KeyedAbortRows(next: tick > 0));
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode KeyedAbortBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Component(KeyedAbortHostRender, key: "host");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void Given_AKeyedRowWhoseChildRenderFails_When_ABoundaryAbortsThePass_Then_TheRowGetsNoRef(int shape)
        {
            // Arrange
            s_keyedAbortShape = shape;
            s_rowRenderRefused = true;
            s_rowRef = CountRef;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(KeyedAbortBoundaryRender, key: "boundary"));

            // Act
            s_keyedAbortSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the fallback term says a boundary caught the failure, which is what raises the abort.
            Assert.That(
                (root.FindLabelByText("fallback") != null ? "fell back" : "never fell back")
                    + ", ref " + _refSetUps + " set up",
                Is.EqualTo("fell back, ref 0 set up"));
        }

        private static bool s_wrapAbortedLeaf;

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode GeneralAbortBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            var children = new VNode[]
            {
                V.Label(text: "uncommitted", refCallback: s_rowRef),
                V.Component(RefusingRowRender, key: "refusing"),
            };
            return s_wrapAbortedLeaf ? V.Div(children: children) : V.Fragment(children);
        }

        // Unwrapped, the leaf is built by the walk the boundary itself expands into; wrapped, by the walk of
        // the element it sits in, which that outer walk then builds in turn.
        [TestCase(false)]
        [TestCase(true)]
        public void Given_ALeafTheGeneralWalkBuiltBeforeASiblingRenderFailed_When_ABoundaryAbortsThePass_Then_ThatLeafGetsNoRef(
            bool wrapped)
        {
            // Arrange
            s_wrapAbortedLeaf = wrapped;
            s_rowRenderRefused = true;
            s_rowRef = CountRef;
            var root = new VisualElement();

            // Act
            using var mounted = V.Mount(root, V.Component(GeneralAbortBoundaryRender, key: "boundary"));

            // Assert — the fallback term for the reason the keyed case above gives.
            Assert.That(
                (root.FindLabelByText("fallback") != null ? "fell back" : "never fell back")
                    + ", ref " + _refSetUps + " set up",
                Is.EqualTo("fell back, ref 0 set up"));
        }

        [Test]
        public void Given_APortalWhoseSiblingRefusesConstruction_When_TheFailedCreationDrains_Then_ThePortalMountsNothing()
        {
            // Arrange — the element's children take the indexed diff, which places the portal inside the
            // element before its sibling is built, so the portal is still parented when the drain reaches it.
            var target = new VisualElement();
            var tree = new VNode[]
            {
                V.Div(children: new VNode[]
                {
                    V.Portal(target, new VNode[] { V.Label(text: "orphan") }),
                    V.Custom<ConstructionRefusingElement>(),
                }),
            };

            // Act
            var outcome = OutcomeOfFailedMount(tree);

            // Assert
            Assert.That(outcome + ", target holds " + target.childCount,
                Is.EqualTo("[constructor refused] thrown, ref 0 set up, 0 cleaned up, target holds 0"));
        }

        [Test]
        public void Given_AZLayerChildWhoseSiblingRefusesConstruction_When_TheFailedCreationDrains_Then_ThePooledButtonStaysDetached()
        {
            // Arrange — the same indexed placement as the portal case above, for a child the z-layer mount queues.
            VNodePoolTestAccess.ClearButtonPoolForTest();
            var tree = new VNode[]
            {
                V.Div(children: new VNode[]
                {
                    V.Button(className: "absolute z-10", text: "layered"),
                    V.Custom<ConstructionRefusingElement>(),
                }),
            };

            // Act
            var outcome = OutcomeOfFailedMount(tree);
            var pooled = VNodePoolTestAccess.ButtonPoolCountForTest;
            var rented = VNodePool.RentButton();
            var attached = rented.parent != null;
            rented.RemoveFromHierarchy();
            VNodePool.ReturnButton(rented);

            // Assert — the pool depth says the button that was queued is the one rented back.
            Assert.That(outcome + ", pooled " + pooled + ", " + (attached ? "attached" : "detached"),
                Is.EqualTo("[constructor refused] thrown, ref 0 set up, 0 cleaned up, pooled 1, detached"));
        }

        private static VelvetTaskCompletionSource<string> s_resource;
        private static Func<VisualElement, Action> s_leafRef;

        [Component]
        private static VNode ResourceReaderRender()
        {
            var text = Hooks.Use(_ => s_resource.Task);
            return V.Label(text: text);
        }

        // The component among the element's children is what sends them through the general walk, and its
        // suspend is what ends the element's creation.
        [Component]
        private static VNode SuspendingCreationHostRender()
            => V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[]
                {
                    V.Div(children: new VNode[]
                    {
                        V.Label(text: "leaf", refCallback: s_leafRef),
                        V.Component(ResourceReaderRender, key: "reader"),
                    }),
                });

        [Test]
        public void Given_AnElementWhoseChildSuspendsDuringItsCreation_When_TheBoundaryFallsBack_Then_AChildBuiltBeforeItGetsNoRef()
        {
            // Arrange
            s_resource = new VelvetTaskCompletionSource<string>();
            s_leafRef = CountRef;
            var root = new VisualElement();

            // Act
            using var mounted = V.Mount(root, V.Component(SuspendingCreationHostRender, key: "host"));

            // Assert — the fallback rides along: a creation that never suspended sets the leaf's ref up rightly.
            Assert.That(
                (root.FindLabelByText("loading") != null ? "fell back" : "never fell back")
                    + ", ref " + _refSetUps + " set up",
                Is.EqualTo("fell back, ref 0 set up"));
        }

        // GREEN_ON_BASE(characterization): the base leaves what a suspended creation built alone.
        // Released the way a failed creation's is, it left the Suspense boundary on its fallback after the
        // resource resolved, and this is what fails when that release takes suspends too.
        [Test]
        public void Given_AnElementWhoseChildSuspendsDuringItsCreation_When_TheResourceResolves_Then_TheBoundaryRevealsItsChildren()
        {
            // Arrange
            s_resource = new VelvetTaskCompletionSource<string>();
            s_leafRef = CountRef;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(SuspendingCreationHostRender, key: "host"));
            var fellBack = root.FindLabelByText("loading") != null;

            // Act
            s_resource.TrySetResult("ready");
            mounted.FlushStateForTest();

            // Assert — the fallback term separates a creation that suspended from one that never did.
            Assert.That(
                (fellBack ? "fell back" : "never fell back") + ", then "
                    + (root.FindLabelByText("ready") != null ? "revealed" : "not revealed"),
                Is.EqualTo("fell back, then revealed"));
        }
    }
}
