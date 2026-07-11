using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the keyed diff against a NEW-side duplicate key. The old→(index,node) map lookup never
    /// marked an entry consumed, so a second new sibling with the same key re-resolved the entry the
    /// first occurrence had already claimed: two logical rows aliased one physical element and the
    /// reorder pass collapsed them into a single DOM slot — a row silently vanished and childCount no
    /// longer matched the declared child array. The old-side duplicate guard warns loudly; the
    /// new-side case must do the same and mount a fresh element for the repeated key so every
    /// declared row commits. Covered on both the flat keyed fast path and the general expansion path
    /// (forced by a Fragment sibling), which mirrored the same unguarded lookup.
    /// </summary>
    [TestFixture]
    internal sealed class KeyedNewSideDuplicateKeyTests
    {
        private readonly record struct PhaseState(int Phase);

        private sealed class PhaseStore : Store<PhaseState>
        {
            public PhaseStore() : base(new PhaseState(0)) { }
            public void Set(int phase) => SetState(_ => new PhaseState(phase));
            protected override void ResetCore() => SetState(_ => new PhaseState(0));
        }

        private static PhaseStore s_store;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
        }

        [Component]
        private static VNode DupList()
        {
            var phase = Hooks.UseStore(s_store, s => s.Phase);
            return V.Div(name: "list", children: phase == 0
                ? new VNode[] { V.Label(key: "x", text: "X") }
                : new VNode[]
                {
                    V.Label(key: "y", text: "Y1"),
                    V.Label(key: "x", text: "DupA"),
                    V.Label(key: "x", text: "DupB"),
                });
        }

        // The Fragment sibling forces the general expansion path instead of the flat keyed fast path.
        [Component]
        private static VNode DupListBesideFragment()
        {
            var phase = Hooks.UseStore(s_store, s => s.Phase);
            return V.Div(name: "glist", children: phase == 0
                ? new VNode[]
                {
                    V.Fragment(new VNode?[] { V.Label(key: "f", text: "F") }),
                    V.Label(key: "x", text: "X"),
                }
                : new VNode[]
                {
                    V.Fragment(new VNode?[] { V.Label(key: "f", text: "F") }),
                    V.Label(key: "y", text: "Y1"),
                    V.Label(key: "x", text: "DupA"),
                    V.Label(key: "x", text: "DupB"),
                });
        }

        private static string[] LabelTextsOf(VisualElement root, string containerName)
        {
            var container = root.Q<VisualElement>(containerName);
            var texts = new string[container.childCount];
            for (var i = 0; i < container.childCount; i++)
            {
                texts[i] = ((Label)container.ElementAt(i)).text;
            }
            return texts;
        }

        [Test]
        public void Given_ANewSideDuplicateKey_When_Reconciled_Then_EveryDeclaredRowCommits()
        {
            // Arrange — one keyed row, then a new tree whose tail repeats a key.
            using var store = new PhaseStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(DupList, key: "list"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Assume.That(_root.Q<VisualElement>("list").childCount, Is.EqualTo(1),
                "Precondition: the single keyed row is mounted");

            // Act
            store.Set(1);
            scheduler.DrainImmediateForTest();

            // Assert — three declared rows produce three committed children; none is silently dropped.
            Assert.AreEqual(3, _root.Q<VisualElement>("list").childCount);
        }

        [Test]
        public void Given_ANewSideDuplicateKey_When_Reconciled_Then_RowsCommitInDeclaredOrder()
        {
            // Arrange
            using var store = new PhaseStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(DupList, key: "list"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;

            // Act
            store.Set(1);
            scheduler.DrainImmediateForTest();

            // Assert — the first occurrence keeps the matched element, the repeat mounts fresh after it.
            Assert.That(LabelTextsOf(_root, "list"), Is.EqualTo(new[] { "Y1", "DupA", "DupB" }));
        }

        [Test]
        public void Given_ANewSideDuplicateKey_When_Reconciled_Then_ItWarnsLikeTheOldSideGuard()
        {
            // Arrange
            using var store = new PhaseStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(DupList, key: "list"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Duplicate key detected among new siblings"));

            // Act
            store.Set(1);
            scheduler.DrainImmediateForTest();

            // Assert — every row still committed; the new-side warning itself is enforced by
            // LogAssert at test end (an Assert.Pass would bypass that unmatched-expectation check),
            // and its message is distinct from the old-side guard's so a later unmount diff cannot
            // satisfy it.
            Assert.AreEqual(3, _root.Q<VisualElement>("list").childCount);
        }

        [Test]
        public void Given_ANewSideDuplicateKeyOnTheGeneralPath_When_Reconciled_Then_EveryDeclaredRowCommits()
        {
            // Arrange — a Fragment sibling routes the reconcile through the general expansion path.
            using var store = new PhaseStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(DupListBesideFragment, key: "glist"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Assume.That(_root.Q<VisualElement>("glist").childCount, Is.EqualTo(2),
                "Precondition: the fragment row and the keyed row are mounted");

            // Act
            store.Set(1);
            scheduler.DrainImmediateForTest();

            // Assert — the fragment row plus three declared rows; the duplicate is not dropped.
            Assert.AreEqual(4, _root.Q<VisualElement>("glist").childCount);
        }
    }
}
