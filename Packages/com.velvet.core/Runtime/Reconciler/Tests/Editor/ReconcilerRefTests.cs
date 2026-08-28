using System;
using System.Collections.Generic;
using NUnit.Framework;
using Velvet.TestUtilities;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the callback-ref and creation-callback contract of element reconciliation.
    /// <list type="bullet">
    /// <item>A callback ref runs once for the pass that created its element, receiving the live element
    /// (RefAttachOrderingTests states where in the pass). On a patch it is
    /// identity-gated (React's contract): the same callback delegate leaves the installed ref
    /// untouched, while a changed identity cycles it — the old cleanup fires, then the new callback
    /// runs as setup against the same reused instance. (The per-render lambdas these tests pass are
    /// fresh identities, so their patches cycle.)</item>
    /// <item>A callback ref may return a cleanup action; the cleanup fires when the element is removed.
    /// A null cleanup return is a no-op on removal.</item>
    /// <item>A typed <c>Ref&lt;T&gt;</c> exposes the element through <c>Current</c>; its
    /// <c>SetElement</c> delegate is identity-stable for the Ref's lifetime (so patches leave it
    /// installed), and its cleanup resets <c>Current</c> to null on removal.</item>
    /// <item>Keyed reconciliation that reuses an element keeps the same instance bound to its ref
    /// across a reorder.</item>
    /// <item>The creation callback (<c>OnCreated</c>) runs only when the element is created — never on
    /// a patch — and runs again when a type change forces recreation.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ReconcilerRefTests : ReconcilerTestFixture
    {
        [Test]
        public void Given_RefCallback_When_ElementCreated_Then_ReceivesTheCreatedElement()
        {
            // Arrange
            VisualElement captured = null;
            var tree = new VNode[]
            {
                V.Button(text: "click me", refCallback: el => { captured = el; return null; }),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(captured, Is.SameAs(Root.ElementAt(0)),
                "The callback receives the freshly created element");
        }

        [Test]
        public void Given_RefCallback_When_ElementPatched_Then_ReceivesTheSameReusedElement()
        {
            // Arrange
            VisualElement capturedOnCreate = null;
            VisualElement capturedOnPatch = null;
            var tree1 = new VNode[]
            {
                V.Label(text: "old", refCallback: el => { capturedOnCreate = el; return null; }),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);

            // Act
            var tree2 = new VNode[]
            {
                V.Label(text: "new", refCallback: el => { capturedOnPatch = el; return null; }),
            };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(capturedOnPatch, Is.SameAs(capturedOnCreate),
                "A patch reuses the element and re-invokes the callback with that same instance");
        }

        [Test]
        public void Given_NoRefCallback_When_ElementCreated_Then_ReconcileDoesNotThrow()
        {
            // Arrange
            var tree = new VNode[] { V.Label(text: "no ref") };

            // Act + Assert
            Assert.DoesNotThrow(() => Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree));
        }

        [Test]
        public void Given_TypedRef_When_ElementCreated_Then_CurrentHoldsTheTypedElement()
        {
            // Arrange
            var buttonRef = new Ref<Button>();
            var tree = new VNode[]
            {
                V.Button(text: "typed", refCallback: buttonRef.SetElement),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(buttonRef.Current, Is.SameAs(Root.ElementAt(0)),
                "Ref<T>.Current exposes the created element");
        }

        [Test]
        public void Given_KeyedRefs_When_SiblingsReordered_Then_EachRefKeepsItsReusedElement()
        {
            // Arrange
            var refA = new Ref<Label>();
            var refB = new Ref<Label>();
            var tree1 = new VNode[]
            {
                V.Label(text: "A", key: "a", refCallback: refA.SetElement),
                V.Label(text: "B", key: "b", refCallback: refB.SetElement),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            var elementA = refA.Current;
            var elementB = refB.Current;
            Assume.That(elementA, Is.Not.Null, "Precondition: ref A captured an element on mount");
            Assume.That(elementB, Is.Not.Null, "Precondition: ref B captured an element on mount");

            // Act
            var tree2 = new VNode[]
            {
                V.Label(text: "B-updated", key: "b", refCallback: refB.SetElement),
                V.Label(text: "A-updated", key: "a", refCallback: refA.SetElement),
            };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That((refA.Current, refB.Current), Is.EqualTo((elementA, elementB)),
                "Keyed reorder reuses each element, so each ref keeps its original instance");
        }

        [Test]
        public void Given_RefCallbackWithCleanup_When_ElementRemoved_Then_CleanupFires()
        {
            // Arrange
            var setupCount = 0;
            var cleanupCount = 0;
            var tree1 = new VNode[]
            {
                V.Label(text: "with-cleanup", refCallback: _ =>
                {
                    setupCount++;
                    return () => cleanupCount++;
                }),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            Assume.That(setupCount, Is.EqualTo(1), "Precondition: setup ran once on mount");
            Assume.That(cleanupCount, Is.EqualTo(0), "Precondition: cleanup has not fired yet");

            // Act
            Reconciler.Reconcile(Root, tree1, Array.Empty<VNode>());

            // Assert
            Assert.That(cleanupCount, Is.EqualTo(1), "Cleanup fires when the element is removed");
        }

        [Test]
        public void Given_RefCallbackSwappedOnPatch_When_Patched_Then_OldCleanupFires()
        {
            // Arrange
            var oldCleanupCount = 0;
            var newSetupCount = 0;
            var tree1 = new VNode[]
            {
                V.Label(text: "patch-target", refCallback: _ => () => oldCleanupCount++),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);

            // Act
            var tree2 = new VNode[]
            {
                V.Label(text: "patch-target", refCallback: _ => { newSetupCount++; return null; }),
            };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(oldCleanupCount, Is.EqualTo(1),
                "Swapping the callback on the same element fires the old cleanup first");
        }

        [Test]
        public void Given_RefCallbackSwappedOnPatch_When_Patched_Then_NewCallbackRunsAsSetup()
        {
            // Arrange
            var newSetupCount = 0;
            var tree1 = new VNode[]
            {
                V.Label(text: "patch-target", refCallback: _ => () => { }),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);

            // Act
            var tree2 = new VNode[]
            {
                V.Label(text: "patch-target", refCallback: _ => { newSetupCount++; return null; }),
            };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(newSetupCount, Is.EqualTo(1), "After the old cleanup, the new callback runs as setup");
        }

        [Test]
        public void Given_TypedRef_When_PatchedRepeatedly_Then_CurrentStaysTheLiveInstance()
        {
            // Arrange
            var labelRef = new Ref<Label>();
            var tree1 = new VNode[] { V.Label(text: "v1", refCallback: labelRef.SetElement) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            var firstElement = labelRef.Current;
            Assume.That(firstElement, Is.Not.Null, "Precondition: the ref captured an element on mount");

            // Act
            var tree2 = new VNode[] { V.Label(text: "v2", refCallback: labelRef.SetElement) };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(labelRef.Current, Is.SameAs(firstElement),
                "Across a patch the cleanup-then-resetup never leaves Current transiently null");
        }

        [Test]
        public void Given_RefCallbackReturningNullCleanup_When_ElementRemoved_Then_RemovalIsANoOp()
        {
            // Arrange
            var tree = new VNode[] { V.Label(text: "no-cleanup", refCallback: _ => null) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Act + Assert
            Assert.DoesNotThrow(() => Reconciler.Reconcile(Root, tree, Array.Empty<VNode>()),
                "A null cleanup return value is allowed and is a no-op on removal");
        }

        [Test]
        public void Given_TypedRef_When_ElementRemoved_Then_CurrentResetsToNull()
        {
            // Arrange
            var labelRef = new Ref<Label>();
            var tree = new VNode[] { V.Label(text: "auto-clear", refCallback: labelRef.SetElement) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);
            Assume.That(labelRef.Current, Is.Not.Null, "Precondition: Current holds the element after mount");

            // Act
            Reconciler.Reconcile(Root, tree, Array.Empty<VNode>());

            // Assert
            Assert.That(labelRef.Current, Is.Null,
                "Ref<T>.SetElement's cleanup resets Current to null on removal");
        }

        [Test]
        public void Given_OnCreated_When_ElementPatched_Then_NotInvokedAgain()
        {
            // Arrange
            var createCount = 0;
            var tree1 = new VNode[] { MakeNode(typeof(VisualElement), _ => createCount++) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            Assume.That(createCount, Is.EqualTo(1), "Precondition: OnCreated ran once on creation");

            // Act
            var tree2 = new VNode[] { MakeNode(typeof(VisualElement), _ => createCount++) };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(createCount, Is.EqualTo(1), "OnCreated does not fire on a patch");
        }

        [Test]
        public void Given_OnCreated_When_TypeChangeForcesRecreation_Then_InvokedAgain()
        {
            // Arrange
            var createCount = 0;
            var tree1 = new VNode[] { MakeNode(typeof(Button), _ => createCount++) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            Assume.That(createCount, Is.EqualTo(1), "Precondition: OnCreated ran once on creation");

            // Act
            var tree2 = new VNode[] { MakeNode(typeof(Label), _ => createCount++) };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(createCount, Is.EqualTo(2), "A type change recreates the element and re-fires OnCreated");
        }

        private static ElementNode MakeNode(Type elementType, Action<VisualElement> onCreated)
            => new()
            {
                ElementType = elementType,
                ClassNames = Array.Empty<string>(),
                Children = Array.Empty<VNode>(),
                Events = Array.Empty<FiberEventBinding>(),
                OnCreated = onCreated,
            };
    }

    /// <summary>
    /// Specifies what a <c>refCallback</c> cleanup that throws does to the unmount it was invoked from.
    /// The delegate is the user's, and <see cref="Reconciler"/> already contains the same one at
    /// disposal; these read the unmount entrance, where the reach of an escape is wider.
    /// <list type="bullet">
    /// <item>The rest of the departing element's own teardown still runs, read on the ring band — it
    /// lives in the element's PARENT rather than in its subtree, so removing the element does not take
    /// it.</item>
    /// <item>The removal batch continues, so the rows the walk had not reached yet leave too.</item>
    /// <item>A <c>V.Portal</c> closing empties its whole range, the removal loop of its own that no
    /// container's diff walks.</item>
    /// </list>
    /// Each reads whether the failure left the reconcile call beside the state it is named for, because a
    /// tree where it escapes never reaches that state and a bare rethrow says nothing about which of the
    /// two moved.
    /// </summary>
    [TestFixture]
    internal sealed class RefCleanupFailureTests : ReconcilerTestFixture
    {
        private const string CleanupFailureMessage = "arranged failure out of a refCallback cleanup";

        [Test]
        public void Given_ARingedRowWhoseRefCleanupThrows_When_ItIsRemoved_Then_ItsBandLeavesWithIt()
        {
            // Arrange
            var mounted = new VNode[]
            {
                V.Div(name: "ringed", className: "ring-2",
                    refCallback: _ => () => throw new InvalidOperationException(CleanupFailureMessage)),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), mounted);
            ContainedFailureLog.Expect<InvalidOperationException>("FiberElementCleaner", CleanupFailureMessage);

            // Act
            var escaped = EscapesFrom(() => Reconciler.Reconcile(Root, mounted, Array.Empty<VNode>()));

            // Assert
            Assert.That((escaped, NamesOf(Root)), Is.EqualTo((false, "")));
        }

        [Test]
        public void Given_ARefCleanupThrowsOnTheRowRemovedFirst_When_TheBatchContinues_Then_TheRestOfTheRowsGoToo()
        {
            // Arrange — the walk removes from the tail, so the throwing row is the one it reaches first.
            var mounted = new VNode[]
            {
                V.Div(name: "head"),
                V.Div(name: "middle"),
                ThrowingCleanupRow("tail"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), mounted);
            ContainedFailureLog.Expect<InvalidOperationException>("FiberElementCleaner", CleanupFailureMessage);

            // Act
            var escaped = EscapesFrom(() => Reconciler.Reconcile(Root, mounted, Array.Empty<VNode>()));

            // Assert
            Assert.That((escaped, NamesOf(Root)), Is.EqualTo((false, "")));
        }

        [Test]
        public void Given_APortalChildsRefCleanupThrows_When_ThePortalCloses_Then_TheWholeRangeLeavesTheTarget()
        {
            // Arrange — CleanupPortal walks its range in reverse, so the throwing child is at the tail:
            // at the head it would be the last removal and the case could not tell a continued loop from
            // a finished one.
            var target = new VisualElement();
            var mounted = new VNode[]
            {
                V.Portal(target, children: new VNode?[] { V.Div(name: "first"), ThrowingCleanupRow("second") }),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), mounted);
            ContainedFailureLog.Expect<InvalidOperationException>("FiberElementCleaner", CleanupFailureMessage);

            // Act
            var escaped = EscapesFrom(() => Reconciler.Reconcile(Root, mounted, Array.Empty<VNode>()));

            // Assert
            Assert.That((escaped, NamesOf(target)), Is.EqualTo((false, "")));
        }

        private static VNode ThrowingCleanupRow(string name)
            => V.Div(name: name, refCallback: _ => () => throw new InvalidOperationException(CleanupFailureMessage));

        // Filtered on the arranged message so any other InvalidOperationException still leaves the case.
        private static bool EscapesFrom(Action reconcile)
        {
            try
            {
                reconcile();
                return false;
            }
            catch (InvalidOperationException exception) when (exception.Message == CleanupFailureMessage)
            {
                return true;
            }
        }

        private static string NamesOf(VisualElement parent)
        {
            var names = new List<string>();
            for (var i = 0; i < parent.childCount; i++) names.Add(parent.ElementAt(i).name);
            return string.Join(",", names);
        }
    }

    /// <summary>
    /// Pins the callback-ref re-invocation contract to React's: a ref cycles (cleanup, then setup)
    /// only when its identity changes or the host element remounts — a patch that carries the SAME
    /// callback delegate leaves the installed ref untouched. Unconditionally re-invoking on every
    /// patch made any state write inside a ref cleanup a per-patch mid-flush write, which is what
    /// forced consumers (focus-ring style hooks) into deferred-correction workarounds.
    /// </summary>
    [TestFixture]
    internal sealed class RefCallbackIdentityTests
    {
        private sealed class CounterStore : Store<int>
        {
            public CounterStore() : base(0) { }
            public void Increment() => SetState(x => x + 1);
            protected override void ResetCore() => SetState(_ => 0);
        }

        private static CounterStore s_store;
        private static int s_setupCount;
        private static int s_cleanupCount;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
            s_setupCount = 0;
            s_cleanupCount = 0;
        }

        // The ref is reference-stable across renders (UseCallback with stable deps), while the
        // label text changes every render so each store write really patches the host subtree.
        [Component]
        private static VNode StableRefHost()
        {
            var count = Hooks.UseStore(s_store, x => x);
            var refCallback = Hooks.UseCallback<Func<VisualElement, Action>>(element =>
            {
                s_setupCount++;
                return () => s_cleanupCount++;
            }, 1);
            return V.Div(name: "host", children: new VNode[]
            {
                V.Label(text: "count-" + count),
                V.Button(name: "target", text: "target-" + count, refCallback: refCallback),
            });
        }

        [Test]
        public void Given_AStableRefCallback_When_TheHostElementPatches_Then_TheRefIsNotReinvoked()
        {
            // Arrange — mounted once (one setup), then patched twice with the same callback identity.
            using var store = new CounterStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(StableRefHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Assume.That(s_setupCount, Is.EqualTo(1), "Precondition: mount ran the ref setup once");

            // Act
            store.Increment();
            scheduler.DrainImmediateForTest();
            store.Increment();
            scheduler.DrainImmediateForTest();

            // Assert — the installed ref survived both patches untouched (no cleanup+setup cycles).
            Assert.That((s_setupCount, s_cleanupCount), Is.EqualTo((1, 0)),
                "A patch carrying the same callback identity must not re-invoke the ref");
        }

        // Alternates between two distinct callback identities per render parity.
        private static readonly Func<VisualElement, Action> s_refA = _ => { s_setupCount++; return () => s_cleanupCount++; };
        private static readonly Func<VisualElement, Action> s_refB = _ => { s_setupCount++; return () => s_cleanupCount++; };

        [Component]
        private static VNode SwappingRefHost()
        {
            var count = Hooks.UseStore(s_store, x => x);
            return V.Div(name: "host", children: new VNode[]
            {
                V.Button(name: "target", text: "target", refCallback: count % 2 == 0 ? s_refA : s_refB),
            });
        }

        [Test]
        public void Given_ARefCallbackIdentityChange_When_TheHostElementPatches_Then_TheOldRefCleansUpAndTheNewOneRuns()
        {
            // Arrange — mounted with identity A installed.
            using var store = new CounterStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(SwappingRefHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Assume.That(s_setupCount, Is.EqualTo(1), "Precondition: mount ran identity A's setup once");

            // Act — the patch swaps to identity B.
            store.Increment();
            scheduler.DrainImmediateForTest();

            // Assert — A's cleanup fired and B's setup ran (React's identity-change cycle).
            Assert.That((s_setupCount, s_cleanupCount), Is.EqualTo((2, 1)),
                "An identity change must run the old cleanup and the new setup exactly once each");
        }

        [Test]
        public void Given_AStableRefCallback_When_TheHostElementUnmounts_Then_TheCleanupStillFires()
        {
            // Arrange — mounted, patched once (no re-invoke), then the whole tree unmounts.
            using var store = new CounterStore();
            s_store = store;
            var mounted = V.Mount(_root, V.Component(StableRefHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            store.Increment();
            scheduler.DrainImmediateForTest();
            Assume.That(s_cleanupCount, Is.EqualTo(0), "Precondition: no cleanup while mounted");

            // Act
            mounted.Dispose();

            // Assert — the detach path still finds and fires the stored cleanup.
            Assert.That(s_cleanupCount, Is.EqualTo(1),
                "Unmount must fire the installed ref's cleanup exactly once");
        }

        // A capture-less static delegate: the same identity across two tenancies WITHIN one mounted
        // tree (one ReconcilerContext), which is how a pooled element re-rented under the SAME
        // per-context identity table could meet the same callback again.
        private static readonly Func<VisualElement, Action> s_pooledTenantRef = _ =>
        {
            s_setupCount++;
            return () => s_cleanupCount++;
        };

        [Component]
        private static VNode PooledTenantHost()
        {
            var count = Hooks.UseStore(s_store, x => x);
            return V.Div(name: "host", children: new VNode[]
            {
                count % 2 == 0
                    ? V.Button(name: "target", text: "target", key: "tenant", refCallback: s_pooledTenantRef)
                    : null,
            });
        }

        [Test]
        public void Given_ATenantRemovedAndRecreatedWithinOneTree_When_ThePooledElementIsReRented_Then_TheSetupRunsAgain()
        {
            // Arrange — mounted with the tenant visible (setup ran), then hidden: the cleanup fires
            // and the element returns to the pool, which must scrub the per-context identity entry —
            // a stale entry would make the same-identity re-rent under the SAME context silently
            // skip its setup (no signals hooked, refs never set) instead of running it.
            using var store = new CounterStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(PooledTenantHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            store.Increment();
            scheduler.DrainImmediateForTest();
            var setupsAfterFirstTenancy = s_setupCount;
            var cleanupsAfterFirstTenancy = s_cleanupCount;

            // Act — show the tenant again: the recreated button (likely the pooled instance) mounts
            // under the same callback identity and the same context.
            store.Increment();
            scheduler.DrainImmediateForTest();

            // Assert — the new tenancy's setup ran. The first tenancy's counts are asserted with it: a
            // second setup that ran during the first tenancy instead reaches the same total.
            Assert.That((setupsAfterFirstTenancy, cleanupsAfterFirstTenancy, s_setupCount),
                Is.EqualTo((1, 1, 2)),
                "A re-rented element under the same ref identity must run its setup again");
        }
    }
}
