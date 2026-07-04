using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
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
}
