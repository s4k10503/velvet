using System;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
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
    }
}
