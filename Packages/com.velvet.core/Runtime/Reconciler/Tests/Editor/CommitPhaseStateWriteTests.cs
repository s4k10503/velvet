using System;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins React's commit-phase update semantics: a hook state write that lands while the SAME
    /// fiber's flush is past its render phase (a callback ref invoked during the patch, an event
    /// dispatched from a detach) is not a render-phase update — it schedules an ordinary follow-up
    /// render, and the batch drain keeps draining until the queue is quiet (with the
    /// maximum-update-depth cap) so the follow-up commits before the frame yields. Silently
    /// dropping such a write desynced the slot value from the committed UI and poisoned the
    /// setter's equality bail for the NEXT genuine edge with the same value.
    /// </summary>
    [TestFixture]
    internal sealed class CommitPhaseStateWriteTests
    {
        private sealed class CounterStore : Store<int>
        {
            public CounterStore() : base(0) { }
            public void Increment() => SetState(x => x + 1);
            protected override void ResetCore() => SetState(_ => 0);
        }

        private static CounterStore s_store;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
        }

        // The ref callback is deliberately a fresh delegate every render, so every patch cycles it
        // (identity change) and the setup runs DURING the commit — the mid-flush write under test.
        // The write is edge-guarded so the follow-up render (which re-cycles the ref) converges.
        [Component]
        private static VNode CommitWriteHost()
        {
            var count = Hooks.UseStore(s_store, x => x);
            var (sawCommitWrite, setSawCommitWrite) = Hooks.UseState(false);
            return V.Div(name: "host", children: new VNode[]
            {
                V.Label(name: "flag", text: sawCommitWrite ? "written" : "pending"),
                V.Button(name: "target", text: "target-" + count, refCallback: element =>
                {
                    if (count > 0) setSawCommitWrite.Invoke(true);
                    return null;
                }),
            });
        }

        [Test]
        public void Given_ARefSetupWritingStateDuringTheCommit_When_TheDrainEnds_Then_TheWriteHasCommitted()
        {
            // Arrange — mounted with the flag pending; the first store write patches the host and
            // the ref setup (running mid-commit) writes the fiber's own state.
            using var store = new CounterStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(CommitWriteHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Assume.That(_root.Q<Label>("flag").text, Is.EqualTo("pending"),
                "Precondition: the flag state starts false");

            // Act — one drain: the store-driven render runs, the commit writes state, and the
            // drain's follow-up pass commits that write before returning.
            store.Increment();
            scheduler.DrainImmediateForTest();

            // Assert — the commit-phase write re-rendered within the same drain (not dropped, not
            // deferred past the frame).
            Assert.That(_root.Q<Label>("flag").text, Is.EqualTo("written"),
                "A commit-phase state write must schedule and commit a follow-up render");
        }

        // Writes a NEW value on every ref cycle, so each follow-up render schedules another one:
        // a runaway commit-phase loop the drain must cap rather than spin forever.
        [Component]
        private static VNode RunawayCommitWriteHost()
        {
            var count = Hooks.UseStore(s_store, x => x);
            var (spins, setSpins) = Hooks.UseState(0);
            return V.Div(name: "host", children: new VNode[]
            {
                V.Label(name: "spins", text: "spins-" + spins),
                V.Button(name: "target", text: "target-" + count, refCallback: element =>
                {
                    if (count > 0) setSpins.Invoke(spins + 1);
                    return null;
                }),
            });
        }

        [Test]
        public void Given_ARunawayCommitPhaseWriteLoop_When_TheDrainHitsTheUpdateDepthCap_Then_ItLogsAndLeavesTheRestForTheNextFrame()
        {
            // Arrange — mounted quiet, then the first store write starts the self-sustaining loop.
            using var store = new CounterStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(RunawayCommitWriteHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("Maximum update depth"));

            // Act — a single drain must terminate at the cap instead of spinning forever.
            store.Increment();
            scheduler.DrainImmediateForTest();

            // Assert — the loop was cut off with work still pending for the next frame boundary
            // (the queue is not empty), rather than the drain looping to completion or dropping it.
            Assert.That(scheduler.ImmediatePendingCount, Is.GreaterThan(0),
                "The capped drain must defer the still-pending update instead of dropping it");
        }
    }
}
