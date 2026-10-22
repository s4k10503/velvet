using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

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
        // (identity change) and its setup runs at that pass's own boundary, still inside the drain that
        // started the render — the mid-flush write under test.
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
        public void Given_ARunawayCommitPhaseWriteLoop_When_TheDrainHitsTheUpdateDepthCap_Then_ItLogsAndDropsTheRunawayUpdate()
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

            // Assert — the loop genuinely ran (the spin counter committed past its initial value)
            // and the runaway update was then DROPPED, not deferred: a deferred runaway would re-arm
            // and burn the full cap again every frame forever, so the queue must end empty.
            Assert.That(
                (_root.Q<Label>("spins").text != "spins-0", scheduler.ImmediatePendingCount),
                Is.EqualTo((true, 0)),
                "The capped drain must drop the runaway update after real passes ran");
        }
    }

    /// <summary>
    /// Specifies that a DELAYED batch drain that does NOT continue an immediate drain (a "solo" delayed drain —
    /// e.g. a Transition-priority re-render with no Urgent/Normal work in the same wave) opens a FRESH UseStore
    /// snapshot wave instead of reusing a stale pin retained from a prior immediate drain. The cross-tier
    /// tearing guard pins a snapshot so an immediate (Urgent/Normal) drain and the delayed (Transition)
    /// drain that follows it in the SAME wave agree; but a solo delayed drain belongs to a new wave and must read
    /// the current store value (an external-store read takes the latest snapshot on each commit).
    /// </summary>
    [TestFixture]
    internal sealed class StoreSnapshotSoloDelayedDrainTests
    {
        private VisualElement _root;

        private sealed class StrStore : Store<string>
        {
            public StrStore() : base("v0") { }
            public void Set(string s) => SetState(_ => s);
            protected override void ResetCore() => SetState(_ => "v0");
        }

        private static StrStore s_store;
        private static string s_selectorRead;
        private static string s_lastRendered;
        private static ComponentFiber s_readerFiber;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
            s_selectorRead = null;
            s_lastRendered = null;
            s_readerFiber = null;
        }

        // Selects the length, which every "vN" snapshot shares, so a store change asks this reader for no render and
        // only the renders a case schedules read the store; the selector records the snapshot each of them read.
        [Component(Compiler = false)]
        private static VNode Reader()
        {
            s_readerFiber = FiberAmbientStack.Current;
            var length = Hooks.UseStore(s_store, s =>
            {
                s_selectorRead = s;
                return s.Length;
            });
            s_lastRendered = s_selectorRead;
            return V.Label(name: "reader", text: length.ToString());
        }

        // GREEN_ON_BASE(characterization): the base's solo delayed drain already opened a fresh wave.
        // A store change now forces a sync-lane render wherever it is made, so the reader selects a length the change
        // leaves equal and the delayed render is scheduled directly.
        [Test]
        public void Given_AStalePinFromAPriorImmediateWave_When_ASoloDelayedDrainRuns_Then_TheReaderReadsTheCurrentSnapshot()
        {
            // Arrange
            using var store = new StrStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));
            mounted.FlushEffectsForTest();
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            var readerFiber = s_readerFiber;
            Assume.That(s_lastRendered, Is.EqualTo("v0"), "Precondition: mounted at v0");

            // Establish a pin: an immediate drain renders the reader, which pins the now-current snapshot (v1).
            store.Set("v1");
            readerFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            scheduler.DrainImmediateForTest();
            var renderedByImmediateWave = s_lastRendered;

            // The store moves on with no render asked for, and the reader's next render is a Transition one, so the
            // next drain is a SOLO delayed drain with no immediate drain to open a fresh wave.
            store.Set("v2");
            readerFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            scheduler.DrainDelayedForTest();

            // Assert — the solo delayed drain must open a fresh wave: the reader reads the current snapshot (v2),
            // NOT the stale pin (v1) left over from the prior immediate drain's wave. What the immediate wave
            // rendered is asserted with it: without a v1 pin in place there is no stale pin to reuse, and the
            // delayed drain reads v2 for reasons this test does not pin.
            Assert.That((renderedByImmediateWave, s_lastRendered), Is.EqualTo(("v1", "v2")),
                "A solo delayed drain must not reuse a stale store snapshot pin from a previous wave");
        }
    }

    /// <summary>
    /// Pins React's flushPassiveEffects-before-update: a prior render's pending passive effect runs before a
    /// new discrete-event update's render, not after. A dep-change re-render schedules the effect (the EditMode
    /// scheduler never ticks, so it stays pending); a discrete click's synchronous flush must drain it first.
    /// GWT, one assert.
    /// </summary>
    [TestFixture]
    internal sealed class PassiveEffectFlushBeforeUpdateTests
    {
        private VisualElement _root;
        private MountedTree _mounted;
        private static readonly List<string> s_log = new();
        private static StateUpdater<int> s_setDep;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_log.Clear();
            s_setDep = default;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
        }

        [Component]
        private static VNode Widget()
        {
            var (dep, setDep) = Hooks.UseState(0);
            var (tick, setTick) = Hooks.UseState(0);
            s_setDep = setDep;
            s_log.Add($"render:t{tick}:d{dep}");
            Hooks.UseEffect(() =>
            {
                s_log.Add($"effect:d{dep}");
                return () => { };
            }, new object[] { dep });
            return V.Button(name: "btn", onClick: () => setTick.Invoke(t => t + 1));
        }

        // GREEN_ON_BASE(characterization): the base's state flush left the mount effect pending and the log clean.
        // FlushStateForTest now runs it first, so the arrangement runs the mount's effects before clearing the log.
        [Test]
        public void Given_APendingPassiveEffect_When_ADiscreteClickReRenders_Then_TheEffectRunsBeforeTheClicksRender()
        {
            // Arrange — mounted (its mount effect already ran), then a dep change re-renders and SCHEDULES a
            // fresh passive effect (effect:d1) that the EditMode scheduler never drains, so it stays pending.
            _mounted = V.Mount(_root, V.Component(Widget, key: "w"));
            _mounted.FlushEffectsForTest();
            s_log.Clear();
            s_setDep.Invoke(1);
            _mounted.FlushStateForTest();
            Assume.That(s_log, Is.EqualTo(new[] { "render:t0:d1" }), "Precondition: re-rendered, effect:d1 pending");
            s_log.Clear();

            // Act — a real discrete click setStates; its FlushImmediate must flush the pending effect first.
            _root.Q<Button>("btn").SimulateClick();

            // Assert — the pending effect commits before the click's re-render (render:t1:d1), as React orders it.
            Assert.That(s_log, Is.EqualTo(new[] { "effect:d1", "render:t1:d1" }));
        }
    }
}
