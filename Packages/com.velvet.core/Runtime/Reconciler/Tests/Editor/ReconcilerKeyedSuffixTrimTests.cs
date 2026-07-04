using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the keyed-diff suffix-trim prepass: the symmetric counterpart to the existing head linear
    /// scan. When the tail of the old and new child lists matches (key-equal and patch-compatible) and the
    /// middle that remains after trimming both ends collapses to a single contiguous insert or remove, the
    /// Pass-2 map build + LIS reorder is skipped entirely — the headline case being a pure prepend.
    ///
    /// Two contracts are pinned: (1) ORACLE — the final child order equals the new key order for every edit
    /// shape (append / prepend / interior insert / interior remove / head remove / swap / rotation / reverse),
    /// including unkeyed and duplicate-key lists, so the prepass can never reorder differently from the
    /// pure-LIS path it short-circuits; and (2) the prepass actually fires — a pure prepend rents NO Pass-2
    /// buffer (observed via reflection on the pooled <c>oldKeyMap</c>), while a rotation still does.
    /// </summary>
    [TestFixture]
    internal sealed class ReconcilerKeyedSuffixTrimTests : ReconcilerTestFixture
    {
        private static VNode K(string key) => V.Label(text: key, key: key);

        private static VNode[] Tree(params string[] keys) => keys.Select(K).ToArray();

        private string[] DomKeys()
        {
            var keys = new string[Root.childCount];
            for (var i = 0; i < Root.childCount; i++)
            {
                keys[i] = ((Label)Root.ElementAt(i)).text;
            }
            return keys;
        }

        // Final-order oracle: across every edit shape, the reconciled DOM order must equal the new key order —
        // the property the suffix-trim prepass must preserve relative to the pure-LIS path.
        private static IEnumerable<TestCaseData> OrderOracleCases()
        {
            TestCaseData C(string name, string[] from, string[] to) =>
                new TestCaseData((object)from, (object)to).SetName(name);

            yield return C("Append", new[] { "a", "b", "c" }, new[] { "a", "b", "c", "d" });
            yield return C("Prepend", new[] { "a", "b", "c" }, new[] { "x", "a", "b", "c" });
            yield return C("PrependMany", new[] { "a", "b", "c" }, new[] { "x", "y", "a", "b", "c" });
            yield return C("InteriorInsert", new[] { "a", "b", "c" }, new[] { "a", "x", "b", "c" });
            yield return C("InteriorRemove", new[] { "a", "b", "c", "d" }, new[] { "a", "c", "d" });
            yield return C("HeadRemove", new[] { "a", "b", "c" }, new[] { "b", "c" });
            yield return C("HeadReplaceTailKept", new[] { "x", "a", "b", "c" }, new[] { "y", "a", "b", "c" });
            yield return C("Swap", new[] { "a", "b", "c" }, new[] { "a", "c", "b" });
            yield return C("Rotation", new[] { "a", "b", "c", "d" }, new[] { "d", "a", "b", "c" });
            yield return C("Reverse", new[] { "a", "b", "c" }, new[] { "c", "b", "a" });
            yield return C("FullReplace", new[] { "a", "b", "c" }, new[] { "x", "y", "z" });
            yield return C("EmptyToList", Array.Empty<string>(), new[] { "a", "b" });
            yield return C("ListToEmpty", new[] { "a", "b" }, Array.Empty<string>());
        }

        [TestCaseSource(nameof(OrderOracleCases))]
        public void Given_AnyEditShape_When_Reconciled_Then_DomOrderEqualsNewKeyOrder(string[] from, string[] to)
        {
            // Arrange
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree(from));
            Assume.That(DomKeys(), Is.EqualTo(from), "Precondition: the initial list mounted in order");

            // Act
            Reconciler.Reconcile(Root, Tree(from), Tree(to));

            // Assert
            Assert.That(DomKeys(), Is.EqualTo(to),
                "The reconciled DOM order must equal the new key order for every edit shape");
        }

        [Test]
        public void Given_Prepend_When_Reconciled_Then_UnchangedTailKeepsItsElementInstances()
        {
            // Arrange — capture a tail element's instance before the prepend.
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("a", "b", "c"));
            var tailB = FindByText("b");
            Assume.That(tailB, Is.Not.Null, "Precondition: the tail element 'b' is mounted");

            // Act — prepend a new head; the suffix-trim path patches the tail in place rather than rebuilding.
            Reconciler.Reconcile(Root, Tree("a", "b", "c"), Tree("x", "a", "b", "c"));

            // Assert — the unchanged tail element 'b' is the same instance (not remounted).
            Assert.That(FindByText("b"), Is.SameAs(tailB),
                "A prepend must reuse the unchanged tail's element instances (patch in place, not rebuild)");
        }

        private VisualElement FindByText(string text)
        {
            for (var i = 0; i < Root.childCount; i++)
            {
                if (Root.ElementAt(i) is Label l && l.text == text) return l;
            }
            return null;
        }

        // Reflection probe: the count of pooled oldKeyMap instances. A fresh Reconciler's pool is empty; a
        // reconcile that enters Pass 2 rents (and returns) one, leaving the count at 1, while the suffix-trim
        // fast path rents nothing, leaving it at 0. Reaching the pool internals via reflection keeps the
        // perf-path contract observable without adding a *ForTest hook to production.
        private static int PooledOldKeyMapCount(Reconciler reconciler)
        {
            var pool = reconciler.Context.BufferPool;
            var clearable = pool.GetType()
                .GetField("_oldKeyMapPool", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(pool);
            var stack = clearable.GetType()
                .GetField("_pool", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(clearable);
            return (int)stack.GetType().GetProperty("Count").GetValue(stack);
        }

        [Test]
        public void Given_PurePrepend_When_Reconciled_Then_NoPass2BufferIsRented()
        {
            // Arrange — initial mount is a tail-add and rents no Pass-2 buffer, so the pool starts empty.
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("a", "b", "c"));
            Assume.That(PooledOldKeyMapCount(Reconciler), Is.EqualTo(0),
                "Precondition: the initial mount took the tail-add path and rented no Pass-2 buffer");

            // Act — a pure prepend.
            Reconciler.Reconcile(Root, Tree("a", "b", "c"), Tree("x", "a", "b", "c"));

            // Assert — the suffix-trim prepass short-circuited Pass 2, so no oldKeyMap was rented.
            Assert.That(PooledOldKeyMapCount(Reconciler), Is.EqualTo(0),
                "A pure prepend must skip the Pass-2 map build + LIS (no oldKeyMap rented)");
        }

        [Test]
        public void Given_DuplicateKeyedList_When_ReconciledIntoAPrependShape_Then_DefersToPass2()
        {
            // Arrange — a prepend whose new list repeats a key. The shape would otherwise collapse to a single
            // insert, but a duplicate key must defer to Pass 2 (which de-duplicates) to preserve its behavior.
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("a", "b", "c"));
            Assume.That(PooledOldKeyMapCount(Reconciler), Is.EqualTo(0),
                "Precondition: the initial mount rented no Pass-2 buffer");

            // Act — new list has 'c' twice.
            Reconciler.Reconcile(Root, Tree("a", "b", "c"), Tree("c", "a", "b", "c"));

            // Assert — the suffix-trim uniqueness guard rejected the duplicate and Pass 2 ran (oldKeyMap rented).
            Assert.That(PooledOldKeyMapCount(Reconciler), Is.EqualTo(1),
                "A duplicate-keyed list must defer to Pass 2 rather than take the suffix-trim fast path");
        }

        [Test]
        public void Given_Rotation_When_Reconciled_Then_Pass2BufferIsRented()
        {
            // Arrange
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("a", "b", "c", "d"));
            Assume.That(PooledOldKeyMapCount(Reconciler), Is.EqualTo(0),
                "Precondition: the initial mount rented no Pass-2 buffer");

            // Act — a rotation genuinely needs the LIS reorder, so it must fall through to Pass 2.
            Reconciler.Reconcile(Root, Tree("a", "b", "c", "d"), Tree("d", "a", "b", "c"));

            // Assert — Pass 2 ran, renting (and returning) exactly one oldKeyMap.
            Assert.That(PooledOldKeyMapCount(Reconciler), Is.EqualTo(1),
                "A rotation must fall through to the Pass-2 map build + LIS (oldKeyMap rented)");
        }
    }
}
