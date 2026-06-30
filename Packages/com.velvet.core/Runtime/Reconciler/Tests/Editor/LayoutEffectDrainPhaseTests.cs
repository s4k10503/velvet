using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the commit-phase ordering across a batched drain: ALL fibers' mutations (renders) complete
    /// before ANY fiber's layout effect runs, so a layout effect observes the already-committed output of the
    /// other fibers flushed in the same batch — not a half-updated tree. Previously each fiber ran its layout
    /// effects immediately after its own render, so an earlier sibling's layout effect fired before a later
    /// sibling had even rendered.
    /// </summary>
    [TestFixture]
    internal sealed class LayoutEffectDrainPhaseTests
    {
        private VisualElement _root;

        private readonly record struct AppState(int Tick);

        private sealed class TickStore : Store<AppState>
        {
            public TickStore() : base(new AppState(0)) { }
            public void Bump() => SetState(s => new AppState(s.Tick + 1));
            protected override void ResetCore() => SetState(_ => new AppState(0));
        }

        private static TickStore s_store;
        private static List<string> s_log;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
            s_log = new List<string>();
        }

        // Sibling A: re-renders on the store tick and has a layout effect (keyed on the tick so it re-runs).
        [Component]
        private static VNode SiblingA()
        {
            var tick = Hooks.UseStore(s_store, s => s.Tick);
            Hooks.UseLayoutEffect(() => { s_log.Add("A-layout"); return (Action)null; }, new object[] { tick });
            return V.Label(name: "a", text: "A" + tick);
        }

        // Sibling B: re-renders on the store tick and logs its render. Enqueued AFTER A.
        [Component]
        private static VNode SiblingB()
        {
            var tick = Hooks.UseStore(s_store, s => s.Tick);
            s_log.Add("B-render");
            return V.Label(name: "b", text: "B" + tick);
        }

        [Component]
        private static VNode Host()
            => V.Div(children: new VNode[]
            {
                V.Component(SiblingA, key: "a"),
                V.Component(SiblingB, key: "b"),
            });

        [Test]
        public void Given_TwoSiblingsDirtiedInOneDrain_When_Drained_Then_AllRendersPrecedeAnyLayoutEffect()
        {
            // Arrange — both siblings subscribe to the store; one bump dirties both into the same drain.
            using var store = new TickStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            s_log.Clear();

            // Act — one store update enqueues both A and B (A first); drain the batch.
            store.Bump();
            scheduler.DrainImmediateForTest();

            // Assert — A's layout effect runs AFTER B has rendered (all mutations precede any layout effect).
            // Per-fiber commit would run A-layout before B-render.
            Assume.That(s_log, Does.Contain("A-layout").And.Contain("B-render"),
                "Precondition: both the layout effect and the sibling render ran in this drain");
            Assert.That(s_log.IndexOf("A-layout"), Is.GreaterThan(s_log.IndexOf("B-render")),
                "All renders in a batch must complete before any layout effect runs (React commit-phase order)");
        }
    }
}
