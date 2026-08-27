using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies two properties of a same-key/same-index type flip whose replacement element
    /// construction triggers an error-boundary abort (<see cref="ReconcilerContext.IsAborted"/>),
    /// across every <c>ChildReconciler.PatchOrReplaceAtSlot</c> call site (the Common-phase indexed
    /// loop, and both keyed Pass-1 linear-scan implementations — sync and time-sliced):
    /// (1) the replacement element is still inserted at the slot — <see cref="ReconcilerContext.IsAborted"/>
    /// only ever becomes true via a boundary's SUCCESSFUL fallback render (FiberErrorBoundary.TryCatch
    /// calls SetAborted only after TryShowFallback's own Reconcile call returns without throwing), so by
    /// the time the abort is observed the newly built element already holds the boundary's fully
    /// rendered fallback content, not a half-built one — discarding it would strand the slot with
    /// nothing where the fallback should be, which React's render/commit split never does (an error
    /// boundary's fallback only ever replaces committed DOM, never leaves it empty mid-render); and
    /// (2) the same abort stops the scan from processing any LATER sibling — it is left untouched
    /// instead of being patched while the reconcile is aborted.
    /// </summary>
    [TestFixture]
    internal sealed class ChildReconcilerAbortSafetyTests : ReconcilerTestFixture
    {
        private static bool s_fallbackShown;
        private static StateUpdater<bool> s_setFlag;

        public override void SetUp()
        {
            base.SetUp();
            s_fallbackShown = false;
            s_setFlag = default;
        }

        [Component]
        private static VNode IndexedListHost()
        {
            var (flipped, setFlipped) = Hooks.UseState(false);
            s_setFlag = setFlipped;
            return V.Div(children: flipped
                ? new VNode[]
                {
                    V.Div(children: new VNode[] { V.Component(BoundaryWrappingThrowerRender) }),
                    V.Label(text: "b-updated"),
                }
                : new VNode[]
                {
                    V.Label(text: "a"),
                    V.Label(text: "b"),
                });
        }

        [Test]
        public void Given_ErrorBoundaryAbortsDuringIndexedReplace_When_Reconciled_Then_TheFallbackIsInsertedAndLaterSiblingIsUntouched()
        {
            // Arrange — unkeyed siblings select the Common-phase indexed diff. The first sibling flips
            // from a Label to a Div wrapping an error boundary whose child throws; the flip's CanPatch
            // decision is false, so building the replacement recurses into the boundary before the
            // abort is observed. The boundary catches successfully, so the built Div (containing the
            // "caught" fallback Label) is inserted at slot 0 despite the abort; the abort then stops
            // the scan before the second sibling is reached, so its ORIGINAL text survives instead of
            // being patched to "b-updated".
            using var mounted = V.Mount(Root, V.Component(IndexedListHost, key: "host"));
            var container = Root.ElementAt(0);

            // Act
            s_setFlag.Invoke(true);
            mounted.FlushStateForTest();

            // Assert
            var replacement = (VisualElement)container.ElementAt(0);
            Assert.That(
                (s_fallbackShown, container.childCount, ((Label)replacement.ElementAt(0)).text, ((Label)container.ElementAt(1)).text),
                Is.EqualTo((true, 2, "caught", "b")));
        }

        [Component]
        private static VNode KeyedSyncListHost()
        {
            var (flipped, setFlipped) = Hooks.UseState(false);
            s_setFlag = setFlipped;
            return V.Div(children: flipped
                ? new VNode[]
                {
                    V.Div(key: "k0", children: new VNode[] { V.Component(BoundaryWrappingThrowerRender) }),
                    V.Label(text: "b-updated", key: "k1"),
                }
                : new VNode[]
                {
                    V.Label(text: "a", key: "k0"),
                    V.Label(text: "b", key: "k1"),
                });
        }

        [Test]
        public void Given_ErrorBoundaryAbortsDuringKeyedSyncReplace_When_Reconciled_Then_TheFallbackIsInsertedAndLaterSiblingIsUntouched()
        {
            // Arrange — keyed siblings with both keys present on both sides select the fully
            // synchronous keyed Pass-1 linear scan (the default V.Mount re-render path runs
            // frameBudgetMs: 0). Same type-flip-triggers-abort shape as the indexed case above.
            using var mounted = V.Mount(Root, V.Component(KeyedSyncListHost, key: "host"));
            var container = Root.ElementAt(0);

            // Act
            s_setFlag.Invoke(true);
            mounted.FlushStateForTest();

            // Assert
            var replacement = (VisualElement)container.ElementAt(0);
            Assert.That(
                (s_fallbackShown, container.childCount, ((Label)replacement.ElementAt(0)).text, ((Label)container.ElementAt(1)).text),
                Is.EqualTo((true, 2, "caught", "b")));
        }

        // GREEN_ON_BASE(refactor): the base raises the flag from CreateElement too, so this case is green
        // there whichever of the two callbacks raises it. The swap is what keeps it that way here, where
        // one of the two no longer runs from CreateElement at all.
        [Test]
        public void Given_AbortObservedDuringTimeSlicedKeyedReplace_When_Reconciled_Then_TheReplacementIsInsertedAndLaterSiblingIsUntouched()
        {
            // Arrange — an extremely small frame budget forces the time-sliced keyed Pass-1 linear
            // scan (Pass1Linear) instead of ReconcileKeyedSync, exercising the same helper call
            // site's checkAbortAfterCreate: true path under the state-machine (park/resume)
            // implementation. This path can only be driven by calling Reconciler.Reconcile directly
            // (V.Mount's re-render path always uses frameBudgetMs: 0), and a real error-boundary
            // component mounted this way would bootstrap its OWN isolated ReconcilerContext (its
            // fiber has no parent fiber to inherit the shared one from — see SetupMount), so
            // SetAborted() would never reach the context this test observes. An onCreated fired
            // during CreateElement stands in for the abort a real boundary would raise, exercising
            // the same _ctx.IsAborted contract without depending on component-fiber parentage. A
            // refCallback would not: its setup is queued for the pass boundary, which is past the scan
            // this drives.
            var oldTree = new VNode[] { V.Label(text: "a", key: "k0"), V.Label(text: "b", key: "k1") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var ctx = Reconciler.Context;

            var newTree = new VNode[]
            {
                V.ScrollView(key: "k0", onCreated: _ => ctx.IsAborted = true),
                V.Label(text: "b-updated", key: "k1"),
            };

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree, frameBudgetMs: 0.001);
            DrainPendingWork();

            // Assert — the rebuilt k0 element is inserted despite the abort (onCreated fires after
            // the element is fully built), and the abort stops the scan before k1 is reached, so its
            // original text survives instead of being patched to "b-updated".
            Assert.That((Root.childCount, ((Label)Root.ElementAt(1)).text), Is.EqualTo((2, "b")));
        }

        private void DrainPendingWork(int maxIterations = 500, double budget = 0.001)
        {
            var iterations = 0;
            while (Reconciler!.HasPendingWork)
            {
                if (iterations++ >= maxIterations)
                {
                    Assert.Fail($"DrainPendingWork: {maxIterations} iterations exceeded without completion");
                }
                Reconciler.ContinueReconcile(frameBudgetMs: budget);
            }
        }

        #region BoundaryWrappingThrower component (boundary + Hooks.UseFallback wrapping a throwing child)

        [Component(IsErrorBoundary = true)]
        private static VNode BoundaryWrappingThrowerRender()
        {
            Hooks.UseFallback(_ =>
            {
                s_fallbackShown = true;
                return V.Label(text: "caught");
            });
            return V.Component(ThrowingChildRender, key: "throwing-child");
        }

        [Component]
        private static VNode ThrowingChildRender() => throw new Exception("boom-child");

        #endregion
    }

    /// <summary>
    /// Specifies that <see cref="Reconciler.ContinueReconcile"/>'s own top-level completion point consumes
    /// <see cref="ReconcilerContext.IsAborted"/> exactly like <see cref="Reconciler.Reconcile"/>'s does —
    /// reading it into <see cref="Reconciler.LastTopLevelWasAborted"/> and resetting it (twice, around the
    /// Portal / z-layer drain) rather than leaving it untouched. Without this, an Error Boundary catching
    /// inside a Portal that a RESUMED time-sliced slice enqueued leaves the shared flag true after
    /// ContinueReconcile returns; the very next unrelated fiber's own top-level Reconcile — sharing the same
    /// ReconcilerContext — then hits ChildReconciler.Reconcile's entry guard (<c>if (_ctx.IsAborted) return;</c>)
    /// and silently no-ops its entire pass. The same boundary must also apply the REST of a pass's
    /// per-pass resets (scoped-key registrations, declaring-panel resolution misses, deferred old-tree
    /// pool returns) — a pass that happens to complete in a resumed slice is still that pass's genuine
    /// end, and anything skipped there leaks on the shared context until some unrelated fiber's own
    /// fresh top-level pass happens to clean it.
    /// </summary>
    [TestFixture]
    internal sealed class ContinueReconcileAbortLeakTests
    {
        private VisualElement _root;
        private static bool s_fallbackShown;
        private static int s_listCount;
        private static bool s_portalAdded;
        private static bool s_keyedFragmentAdded;
        private static ComponentFiber s_listFiber;
        private static string s_counterText;
        private static StateUpdater<string> s_setCounterText;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            RuntimeStateProbe.ClearPortalRegistry();
            s_fallbackShown = false;
            s_listCount = 3;
            s_portalAdded = false;
            s_keyedFragmentAdded = false;
            s_listFiber = null;
            s_counterText = "initial";
            s_setCounterText = default;
        }

        [TearDown]
        public void TearDown()
        {
            RuntimeStateProbe.ClearPortalRegistry();
        }

        [Test]
        public void Given_ATimeSlicedResumeDrainsAPortalWhoseErrorBoundaryCatches_When_AnUnrelatedFiberLaterReRenders_Then_ItsChangeStillCommits()
        {
            // Arrange — a target the Portal resolves at enqueue time, and a host with two INDEPENDENT
            // inline-mounted sibling fibers sharing one ReconcilerContext (mirrors TimeSlicedFiberTests' own
            // SiblingHostRender shape): "list" (a flat, keyed, time-sliceable array that grows a brand-new
            // trailing Portal — wrapping an error boundary around a throwing child — only below) and
            // "counter" (an ordinary, unrelated fiber whose own later re-render this test observes).
            var portalTarget = new VisualElement();
            FiberPortalRegistry.Register("continue-reconcile-abort-leak-target", portalTarget);
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            Assume.That(s_listFiber.HasPendingReconcileWorkForTest(), Is.False,
                "Precondition: the initial mount is synchronous (zero budget)");

            // Act (1) — add the trailing Portal under a tiny time-sliced budget: the pass parks on the
            // existing (unchanged) keyed prefix one item per tick, then a RESUMED tick creates the Portal's
            // placeholder (enqueuing it) as the pass's very last entry — so that SAME tick's own top-level
            // finally (Reconciler.ContinueReconcile) is what drains it: the boundary catches the Portal
            // child's throw and calls SetAborted() on the shared context.
            s_portalAdded = true;
            s_listFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_listFiber.FlushStateWithTinyBudgetForTest();
            var parkedMidCommit = s_listFiber.HasPendingReconcileWorkForTest();
            s_listFiber.DrainTimeSlicedReconcileForTest();
            Assume.That(s_fallbackShown, Is.True,
                "Precondition: the Portal's error boundary actually caught the throw");

            // Act (2) — an unrelated fiber sharing the same mounted tree (and so the same ReconcilerContext)
            // re-renders normally, synchronously, well after the time-sliced pass above fully completed and
            // returned.
            s_setCounterText.Invoke("updated");
            mounted.FlushStateForTest();

            // Assert — RED without the fix: a stale IsAborted left over from the Portal drain makes
            // ChildReconciler.Reconcile's entry guard silently no-op this fiber's entire reconcile, leaving
            // the old text in place instead of committing the update.
            Assert.That((parkedMidCommit, _root.Q<Label>("counter-label").text), Is.EqualTo((true, "updated")));
        }

        [Test]
        public void Given_AResumedSliceExpandsAKeyedFragmentSubtree_When_ThePassCompletesThroughItsOwnContinuation_Then_TheSharedContextCarriesNoStaleScopedKeyEntries()
        {
            // Arrange — same two-fiber host; the growth below appends an item whose subtree contains a
            // KEYED Fragment, so the scoped-key registration the expansion performs lands during a RESUMED
            // slice (the initial mount's own top-level pass — which does clear the table at its end — is
            // long finished by then).
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            var ctx = mounted.Root.Reconciler.Context;
            Assume.That(ctx.EffectiveKeys.Count, Is.EqualTo(0),
                "Precondition: the synchronous initial mount left no scoped-key entries behind");

            // Act — the pass parks per-item under the tiny budget, so the brand-new trailing item (and the
            // keyed Fragment inside it) is expanded by a continuation tick, and that SAME tick's own
            // top-level completion is the only boundary this pass ever gets.
            s_keyedFragmentAdded = true;
            s_listFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_listFiber.FlushStateWithTinyBudgetForTest();
            var parkedMidCommit = s_listFiber.HasPendingReconcileWorkForTest();
            s_listFiber.DrainTimeSlicedReconcileForTest();
            Assume.That(_root.Q<Label>("keyed-fragment-leaf"), Is.Not.Null,
                "Precondition: the keyed Fragment's subtree really was expanded and committed");

            // Assert — RED without the continuation boundary clearing the table: the entries registered by
            // the resumed slice's expansion outlive the pass on the shared context (nothing else runs), so
            // the count stays nonzero here instead of resetting at the pass's genuine end.
            Assert.That((parkedMidCommit, ctx.EffectiveKeys.Count), Is.EqualTo((true, 0)));
        }

        [Component]
        private static VNode Host() => V.Div(children: new VNode[]
        {
            V.Component(ListRender, key: "list"),
            V.Component(CounterRender, key: "counter"),
        });

        [Component]
        private static VNode ListRender()
        {
            s_listFiber = FiberAmbientStack.Current;
            var total = s_listCount + (s_portalAdded || s_keyedFragmentAdded ? 1 : 0);
            var children = new VNode[total];
            for (var i = 0; i < s_listCount; i++)
            {
                children[i] = V.Label(text: "item-" + i, key: "item" + i);
            }
            if (s_portalAdded)
            {
                children[s_listCount] = V.Portal("continue-reconcile-abort-leak-target", key: "portal",
                    children: new VNode[] { V.Component(BoundaryWrappingThrowerRender, key: "throwing-child") });
            }
            else if (s_keyedFragmentAdded)
            {
                children[s_listCount] = V.Div(key: "kf-wrap", children: new VNode[]
                {
                    V.Fragment(key: "kf", children: new VNode[]
                    {
                        V.Label(name: "keyed-fragment-leaf", text: "kf-leaf"),
                    }),
                });
            }
            return V.Fragment(children: children);
        }

        [Component]
        private static VNode CounterRender()
        {
            var (text, setText) = Hooks.UseState(s_counterText);
            s_setCounterText = setText;
            return V.Label(name: "counter-label", text: text);
        }

        #region BoundaryWrappingThrower component (boundary + Hooks.UseFallback wrapping a throwing child)

        [Component(IsErrorBoundary = true)]
        private static VNode BoundaryWrappingThrowerRender()
        {
            Hooks.UseFallback(_ =>
            {
                s_fallbackShown = true;
                return V.Label(text: "caught");
            });
            return V.Component(ThrowingChildRender, key: "throwing-child-inner");
        }

        [Component]
        private static VNode ThrowingChildRender() => throw new Exception("boom-child");

        #endregion
    }

    /// <summary>
    /// Characterizes the ChildReconciler DOM-desync RECOVERY guards: <c>TryRebuildDesyncedSlotRange</c> for keyed
    /// children, and the <c>slotExists</c> / remove-skip guards for indexed children. They recover when the live
    /// container is SHORTER than the fiber's committed baseline claims — the state a completing AnimatePresence
    /// exit leaves when its ghost element is dropped out of band while the baseline still counts it.
    ///
    /// Production reaches that state through a rare real-time presence-ghost overlap that does not reproduce
    /// deterministically in a headless run (neither EditMode nor PlayMode batchmode hits the timing window). So
    /// rather than chase the emergent crash, this pins the guard's CONTRACT directly: the same desync condition
    /// is created deterministically — by dropping a live child element out of band — and a re-render over the
    /// short container must RECOVER (rebuild the missing slots) instead of over-indexing <c>parent.ElementAt</c>.
    /// RED without the guards (the reconcile throws / leaves the container short), GREEN with them.
    /// </summary>
    [TestFixture]
    internal sealed class ReconcilerDesyncRecoveryTests
    {
        private VisualElement _root;
        private static Action<int> s_setTick;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_setTick = null;
        }

        // Compiler-memo OFF so a state-only re-render always re-runs the child reconcile (auto-memo would bail on
        // unchanged children and skip the very path under test).
        [Component(Compiler = false)]
        private static VNode IndexedHost()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            // Unkeyed children → the indexed reconcile path (slotExists guard).
            return V.Div(name: "box", children: new VNode[]
            {
                V.Label(text: "a"), V.Label(text: "b"), V.Label(text: "c"),
            });
        }

        [Component(Compiler = false)]
        private static VNode KeyedHost()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            // Keyed list → the keyed reconcile path (TryRebuildDesyncedSlotRange).
            return V.Div(name: "box", children: V.List(new[] { "a", "b", "c" }, s => s, s => V.Label(text: s)));
        }

        // Indexed and keyed children reach DIFFERENT recovery guards (slotExists vs TryRebuildDesyncedSlotRange) but
        // run the identical body; the host render selects the path, named per case to keep each Given.
        private static IEnumerable<TestCaseData> DesyncRecoveryCases()
        {
            yield return new TestCaseData("indexed", (Func<VNode>)IndexedHost)
                .SetName("Given_IndexedChildren_When_LiveContainerIsShorterThanBaseline_Then_ReconcileRecoversInsteadOfOverIndexing");
            yield return new TestCaseData("keyed", (Func<VNode>)KeyedHost)
                .SetName("Given_KeyedChildren_When_LiveContainerIsShorterThanBaseline_Then_ReconcileRecoversInsteadOfOverIndexing");
        }

        [TestCaseSource(nameof(DesyncRecoveryCases))]
        public void Given_Children_When_LiveContainerIsShorterThanBaseline_Then_ReconcileRecoversInsteadOfOverIndexing(
            string path, Func<VNode> host)
        {
            // Arrange — three children committed via the indexed or keyed path.
            using var mounted = V.Mount(_root, V.Component(host, key: path));
            var box = _root.Q<VisualElement>("box");
            Assume.That(box.childCount, Is.EqualTo(3), "Precondition: three children committed");
            // Drop the tail element out of band, mirroring a completing exit whose ghost VE was removed while the
            // fiber baseline still counts it: the live container is now SHORTER than the baseline.
            box.RemoveAt(2);
            Assume.That(box.childCount, Is.EqualTo(2), "Precondition: the live container is now short of the baseline");

            // Act — re-render the owner so the reconcile runs against the short container.
            s_setTick.Invoke(1);
            mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            // Assert — recovered the full ordered child set (not just the count) rather than over-indexing
            // parent.ElementAt, so a guard that recovered the count via a wrong slot/order would still fail.
            var texts = box.Children().Select(c => (c as Label)?.text).ToList();
            Assert.That(texts, Is.EqualTo(new[] { "a", "b", "c" }),
                "The " + path + " desync guard recovered the ordered child set in order rather than over-indexing parent.ElementAt");
        }
    }

    /// <summary>
    /// Specifies that keyed child reconciliation skips patching a child whose new VNode is the SAME
    /// instance as the old one. An immutable VNode reused across renders (as auto-memoization hands
    /// back the cached instance) produces identical output, so re-patching it is wasted work — the
    /// indexed path already short-circuits on reference identity, and the keyed Pass 1 (linear prefix)
    /// and Pass 2 (map lookup) paths must do the same.
    /// <list type="bullet">
    /// <item>A keyed prefix re-reconciled with the same node instances patches none of them: the
    /// reference-identity check bypasses PatchNode, so a per-element ref callback does not re-fire.</item>
    /// <item>A keyed reorder that reuses the same node instances likewise skips the per-node patch
    /// while still re-placing the elements into the new order.</item>
    /// </list>
    /// The ref callback is the observable probe: PatchNode re-invokes it on every patch, so an
    /// unchanged invocation count proves the patch was skipped.
    /// </summary>
    [TestFixture]
    internal sealed class ChildReferenceIdentitySkipTests : ReconcilerTestFixture
    {
        [Test]
        public void Given_KeyedPrefix_When_ReReconciledWithSameInstances_Then_PatchNodeIsSkipped()
        {
            // Arrange
            var refInvocations = 0;
            Func<VisualElement, Action> probe = _ => { refInvocations++; return null; };
            var a = V.Div(key: "a", refCallback: probe);
            var b = V.Div(key: "b", refCallback: probe);
            var tree1 = new VNode[] { a, b };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            var afterMount = refInvocations;
            Assume.That(afterMount, Is.EqualTo(2), "Precondition: each keyed child's ref callback fires once on mount");

            // Act — re-reconcile the same keyed prefix with the SAME VNode instances
            var tree2 = new VNode[] { a, b };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(refInvocations, Is.EqualTo(afterMount),
                "Reference-identical keyed children skip PatchNode, so the ref callback does not re-fire");
        }

        [Test]
        public void Given_KeyedReorder_When_ReusingSameInstances_Then_PatchNodeIsSkipped()
        {
            // Arrange
            var refInvocations = 0;
            Func<VisualElement, Action> probe = _ => { refInvocations++; return null; };
            var a = V.Div(key: "a", refCallback: probe);
            var b = V.Div(key: "b", refCallback: probe);
            var c = V.Div(key: "c", refCallback: probe);
            var tree1 = new VNode[] { a, b, c };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            var afterMount = refInvocations;
            Assume.That(afterMount, Is.EqualTo(3), "Precondition: each keyed child's ref callback fires once on mount");

            // Act — reorder so the head mismatch forces the Pass 2 map lookup, reusing the SAME instances
            var tree2 = new VNode[] { c, a, b };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(refInvocations, Is.EqualTo(afterMount),
                "A keyed reorder that reuses the same VNode instances skips PatchNode on each retained child");
        }
    }

    /// <summary>
    /// Specifies which tree a fiber keeps as its diff baseline when the top-level pass it rendered into was
    /// aborted by a boundary below it: the pre-throw one, not the one that pass built.
    /// <c>FiberRenderer.RenderAndReconcile</c> owns why it is that one.
    /// <para>
    /// The boundary catches during the WALK here. One catching during the ref-setup drain that ends the same
    /// pass is the other half, and that fiber commits instead — <c>ElementCallbackFailureTests</c> holds
    /// those cases and <c>ComponentFiber.FallbackReplacedPreviousTree</c> owns what separates the two.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class AbortedPassBaselineTests
    {
        private const string FailureMessage = "arranged failure during the walk";

        private VisualElement _root;
        private static bool s_fallbackShown;
        private static ComponentFiber s_hostFiber;
        private static StateUpdater<bool> s_setFlipped;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_fallbackShown = false;
            s_hostFiber = null;
            s_setFlipped = default;
        }

        // GREEN_ON_BASE(characterization): the discard this reads predates the branch — the base drops an
        // aborted pass's tree on the same flag. What the branch adds beside that flag is the boundary fiber's
        // own reading, and this case is what keeps the older half measured once the newer one is there.
        [Test]
        public void Given_ABoundaryBelowAFiberCaughtDuringTheWalk_When_ThePassEnds_Then_TheFibersBaselineIsThePreThrowTree()
        {
            // Arrange — the host's own element name carries its state, so the committed baseline and the tree
            // the aborted pass built disagree by name.
            using var mounted = V.Mount(_root, V.Component(AbortingWalkHost, key: "host"));

            // Act
            s_setFlipped.Invoke(true);
            mounted.FlushStateForTest();

            // Assert — the fallback flag gates the name, because a pass that never re-rendered the host
            // reads the same pre-throw name with nothing about the abort measured.
            Assert.That((s_fallbackShown, (s_hostFiber?.PreviousTree?[0] as BaseElementNode)?.Name),
                Is.EqualTo((true, "host-initial")));
        }

        // The type flip at slot 0 is what builds the boundary's subtree inside this pass, so the catch lands
        // in the walk rather than in the drain that ends the pass.
        [Component]
        private static VNode AbortingWalkHost()
        {
            s_hostFiber = FiberAmbientStack.Current;
            var (flipped, setFlipped) = Hooks.UseState(false);
            s_setFlipped = setFlipped;
            return V.Div(name: flipped ? "host-flipped" : "host-initial", children: flipped
                ? new VNode[] { V.Div(children: new VNode[] { V.Component(WalkFailingBoundary) }) }
                : new VNode[] { V.Label(text: "a") });
        }

        [Component(IsErrorBoundary = true)]
        private static VNode WalkFailingBoundary()
        {
            Hooks.UseFallback(_ =>
            {
                s_fallbackShown = true;
                return V.Label(name: "fallback", text: "caught");
            });
            return V.Component(WalkThrowingChild, key: "child");
        }

        [Component]
        private static VNode WalkThrowingChild() => throw new Exception(FailureMessage);
    }
}
