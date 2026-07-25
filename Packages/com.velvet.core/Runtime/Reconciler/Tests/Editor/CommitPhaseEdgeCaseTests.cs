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
    /// Pins context-dependency retention across a failing render. The dependency list was cleared
    /// in place at the top of every render attempt and rebuilt by the UseContext calls that attempt
    /// reached — so a render that threw partway left the committed list empty or partial, and the
    /// Provider-change walk (which skips fibers without a recorded dependency) never re-rendered the
    /// consumer again: it was stuck on a stale context value forever, with no error. A memoized
    /// consumer has no other re-render path, so the reads of each attempt must be staged and swapped
    /// in only when the attempt settles, like the hook-slot machinery already does.
    /// </summary>
    [TestFixture]
    internal sealed class RenderExceptionContextDependencyTests
    {
        private static readonly ComponentContext<int> NumberContext = ComponentContext<int>.Create(0);
        private static readonly ComponentContext<string> LetterContext = ComponentContext<string>.Create("-");

        private readonly record struct ProviderState(int Number, string Letter);

        private sealed class ProviderStore : Store<ProviderState>
        {
            public ProviderStore() : base(new ProviderState(1, "a")) { }
            public void SetNumber(int n) => SetState(s => s with { Number = n });
            public void SetLetter(string l) => SetState(s => s with { Letter = l });
            protected override void ResetCore() => SetState(_ => new ProviderState(1, "a"));
        }

        private static ProviderStore s_store;
        private static bool s_throwOnce;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
            s_throwOnce = false;
        }

        // Memoized: with stable (absent) props, a parent-driven render bails, so a later re-render
        // can arrive only through the Provider-change walk — the exact gate under test. The one-shot
        // throw fires after the first UseContext but before the second, leaving the second context's
        // dependency to the staging discipline.
        [Component(Memoize = true)]
        private static VNode TwoContextConsumer()
        {
            var number = Hooks.UseContext(NumberContext);
            if (s_throwOnce)
            {
                s_throwOnce = false;
                throw new InvalidOperationException("boom");
            }
            var letter = Hooks.UseContext(LetterContext);
            return V.Label(name: "ctx-out", text: number + "-" + letter);
        }

        [Component]
        private static VNode ProviderHost()
        {
            var state = Hooks.UseStore(s_store, s => s);
            return V.Provider(NumberContext, state.Number, new VNode[]
            {
                V.Provider(LetterContext, state.Letter, new VNode[]
                {
                    V.Component(TwoContextConsumer, key: "consumer"),
                }),
            });
        }

        [Test]
        public void Given_ARenderThrewBeforeItsSecondContextRead_When_ThatContextChanges_Then_TheConsumerStillRerenders()
        {
            // Arrange — a successful mount records both context reads; a later render throws between
            // the first and the second read (no error boundary above).
            using var store = new ProviderStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(ProviderHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Assume.That(_root.Q<Label>("ctx-out").text, Is.EqualTo("1-a"), "Precondition: both contexts render");
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("boom"));
            s_throwOnce = true;
            store.SetNumber(2);
            scheduler.DrainImmediateForTest();

            // Act — only the SECOND context's value changes afterwards.
            store.SetLetter("b");
            scheduler.DrainImmediateForTest();

            // Assert — the consumer still re-rendered for it (the failed render did not drop the
            // committed dependency), showing both current values.
            Assert.AreEqual("2-b", _root.Q<Label>("ctx-out").text);
        }
    }

    /// <summary>
    /// Pins that a passive effect committed by an earlier, successful render survives a later render
    /// of the same fiber throwing. The render-exception handler blanket-cleared PendingEffects — but
    /// unlike the layout/insertion lists (rebuilt every render), PendingEffects intentionally
    /// persists across renders until the deferred frame-boundary flush runs it. Because the settled
    /// deps were already promoted at commit time, a wiped mount effect (stable deps) was never
    /// re-staged by any later successful render either: it silently never ran, with no error, while
    /// the component kept rendering normally. The handler must truncate back to the committed
    /// baseline instead of clearing, exactly as the render-phase retry path already does.
    /// </summary>
    [TestFixture]
    internal sealed class RenderExceptionPendingEffectTests
    {
        private readonly record struct ThrowState(bool Throw);

        private sealed class ThrowStore : Store<ThrowState>
        {
            public ThrowStore() : base(new ThrowState(false)) { }
            public void Set(bool value) => SetState(_ => new ThrowState(value));
            protected override void ResetCore() => SetState(_ => new ThrowState(false));
        }

        private static ThrowStore s_store;
        private static bool s_mountEffectRan;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
            s_mountEffectRan = false;
        }

        [Component]
        private static VNode EffectThenThrow()
        {
            var shouldThrow = Hooks.UseStore(s_store, s => s.Throw);
            Hooks.UseEffect(() =>
            {
                s_mountEffectRan = true;
                return (Action)null;
            }, Array.Empty<object>());
            if (shouldThrow)
            {
                throw new InvalidOperationException("boom");
            }
            return V.Div(name: "etr");
        }

        [Test]
        public void Given_ACommittedMountEffectNotYetFlushed_When_ALaterRenderThrows_Then_TheEffectStillRuns()
        {
            // Arrange — mount commits the effect into the pending list; the deferred flush has not
            // run yet when a second render of the same fiber throws (no error boundary above).
            using var store = new ThrowStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(EffectThenThrow, key: "etr"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("boom"));
            store.Set(true);
            scheduler.DrainImmediateForTest();

            // Act — the deferred passive-effect flush finally runs.
            mounted.FlushEffectsForTest();

            // Assert — the already-committed mount effect was not discarded by the failing render.
            Assert.That(s_mountEffectRan, Is.True);
        }
    }

    /// <summary>
    /// Specifies that a DELAYED batch drain that does NOT continue an immediate drain (a "solo" delayed drain —
    /// e.g. a Transition-priority re-render with no Urgent/Normal work in the same wave) opens a FRESH UseStore
    /// snapshot wave instead of reusing a stale pin retained from a prior immediate drain. The cross-tier
    /// tearing guard pins a snapshot so an immediate (Urgent/Normal) drain and the delayed (Deferred/Transition)
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
        private static string s_lastRendered;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
            s_lastRendered = null;
        }

        [Component]
        private static VNode Reader()
        {
            var v = Hooks.UseStore(s_store, s => s);
            s_lastRendered = v;
            return V.Label(name: "reader", text: v);
        }

        [Test]
        public void Given_AStalePinFromAPriorImmediateWave_When_ASoloDelayedDrainRuns_Then_TheReaderReadsTheCurrentSnapshot()
        {
            // Arrange
            using var store = new StrStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            var readerFiber = mounted.Root.Child;
            Assume.That(s_lastRendered, Is.EqualTo("v0"), "Precondition: mounted at v0");

            // Establish a pin: an immediate drain renders the reader, which pins the now-current snapshot (v1).
            store.Set("v1");
            scheduler.DrainImmediateForTest();
            Assume.That(s_lastRendered, Is.EqualTo("v1"), "Precondition: the immediate wave pinned and rendered v1");

            // The reader is inside a transition (the async-transition await window): a store mutation now routes
            // its re-render to the delayed (Transition) tier, so the next drain is a SOLO delayed drain with no
            // immediate drain to open a fresh wave.
            readerFiber.IsInTransition = true;
            store.Set("v2");
            scheduler.DrainDelayedForTest();

            // Assert — the solo delayed drain must open a fresh wave: the reader reads the current snapshot (v2),
            // NOT the stale pin (v1) left over from the prior immediate drain's wave.
            Assert.That(s_lastRendered, Is.EqualTo("v2"),
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

        [Test]
        public void Given_APendingPassiveEffect_When_ADiscreteClickReRenders_Then_TheEffectRunsBeforeTheClicksRender()
        {
            // Arrange — mounted (its mount effect already ran), then a dep change re-renders and SCHEDULES a
            // fresh passive effect (effect:d1) that the EditMode scheduler never drains, so it stays pending.
            _mounted = V.Mount(_root, V.Component(Widget, key: "w"));
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
