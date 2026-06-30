using System;
using NUnit.Framework;
using Velvet;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the dependency-array memoization contract of <see cref="MemoNode"/> under reconcile.
    /// <list type="bullet">
    /// <item>The factory runs on first mount and produces the rendered output.</item>
    /// <item>A re-render whose dependency array is unchanged is a cache hit: the factory is skipped and
    /// the previously produced output (and its VisualElement instance) is preserved.</item>
    /// <item>A re-render whose dependency array changed is a cache miss: the factory re-runs. When the
    /// new inner is the same element type the existing instance is patched in place; a different type
    /// replaces the element.</item>
    /// <item>Dependency equality follows reference identity, so a fresh-but-content-equal record
    /// dependency is a cache miss and recomputes.</item>
    /// <item>An explicit key supplied via <c>V.MemoizedWithKey</c> becomes the cache key, so a re-render
    /// with the same key and the same dependencies is a cache hit.</item>
    /// <item>Sibling Memos cache independently — changing one's dependencies never re-runs the other.</item>
    /// <item>A nested unkeyed Memo resolves under its enclosing Memo's position scope, so it never
    /// cache-hits on the outer Memo's cached inner.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ReconcilerMemoTests : ReconcilerTestFixture
    {
        [Test]
        public void Given_MemoNode_When_FirstMounted_Then_FactoryRunsOnce()
        {
            // Arrange
            var callCount = 0;
            var tree = new VNode[]
            {
                V.Memoized(() => { callCount++; return V.Label(text: "memoized"); }, "dep1"),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(callCount, Is.EqualTo(1));
        }

        [Test]
        public void Given_MemoNode_When_FirstMounted_Then_ProducesRenderedOutput()
        {
            // Arrange
            var tree = new VNode[]
            {
                V.Memoized(() => V.Label(text: "memoized"), "dep1"),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(((Label)Root.ElementAt(0)).text, Is.EqualTo("memoized"));
        }

        [Test]
        public void Given_UnchangedDeps_When_ReRendered_Then_FactoryIsSkipped()
        {
            // Arrange
            var callCount = 0;
            VNode[] CreateTree() => new VNode[]
            {
                V.Memoized(() => { callCount++; return V.Label(text: "memoized"); }, "dep1"),
            };
            var tree1 = CreateTree();
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            Assume.That(callCount, Is.EqualTo(1), "Precondition: the factory ran once on first mount");

            // Act
            var tree2 = CreateTree();
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(callCount, Is.EqualTo(1), "An unchanged dependency array is a cache hit");
        }

        [Test]
        public void Given_UnchangedDeps_When_ReRendered_Then_ElementInstanceIsPreserved()
        {
            // Arrange
            var tree1 = new VNode[] { V.Memoized(() => V.Label(text: "stable"), "dep1") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            var firstElement = Root.ElementAt(0);

            // Act
            var tree2 = new VNode[] { V.Memoized(() => V.Label(text: "stable"), "dep1") };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(Root.ElementAt(0), Is.SameAs(firstElement),
                "A cache hit reuses the previously produced VisualElement");
        }

        [Test]
        public void Given_DepsChanged_When_ReRendered_Then_FactoryReruns()
        {
            // Arrange
            var callCount = 0;
            var tree1 = new VNode[]
            {
                V.Memoized(() => { callCount++; return V.Label(text: $"render-{callCount}"); }, "dep-v1"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            Assume.That(callCount, Is.EqualTo(1), "Precondition: the factory ran once on first mount");

            // Act
            var tree2 = new VNode[]
            {
                V.Memoized(() => { callCount++; return V.Label(text: $"render-{callCount}"); }, "dep-v2"),
            };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(callCount, Is.EqualTo(2), "A changed dependency array re-runs the factory");
        }

        [Test]
        public void Given_DepsChanged_SameInnerType_When_ReRendered_Then_ElementPatchedInPlace()
        {
            // Arrange
            var tree1 = new VNode[] { V.Memoized(() => V.Label(text: "v1"), "dep-v1") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            var firstElement = Root.ElementAt(0);

            // Act
            var tree2 = new VNode[] { V.Memoized(() => V.Label(text: "v2"), "dep-v2") };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(Root.ElementAt(0), Is.SameAs(firstElement),
                "Same inner element type patches the existing instance rather than recreating it");
        }

        [Test]
        public void Given_DepsChanged_SameInnerType_When_ReRendered_Then_NewValueIsReflected()
        {
            // Arrange
            var tree1 = new VNode[] { V.Memoized(() => V.Label(text: "v1"), "dep-v1") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);

            // Act
            var tree2 = new VNode[] { V.Memoized(() => V.Label(text: "v2"), "dep-v2") };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(((Label)Root.ElementAt(0)).text, Is.EqualTo("v2"));
        }

        [Test]
        public void Given_DepsChanged_DifferentInnerType_When_ReRendered_Then_ElementIsReplaced()
        {
            // Arrange
            var tree1 = new VNode[] { V.Memoized(() => V.Label(text: "label"), "dep-v1") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            var firstElement = Root.ElementAt(0);
            Assume.That(firstElement, Is.InstanceOf<Label>(), "Precondition: the first inner is a Label");

            // Act
            var tree2 = new VNode[] { V.Memoized(() => V.Div(), "dep-v2") };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(Root.ElementAt(0), Is.Not.SameAs(firstElement),
                "A different inner element type recreates the element");
        }

        [Test]
        public void Given_FreshRecordDepWithEqualContent_When_ReRendered_Then_RecomputesByReferenceIdentity()
        {
            // Arrange
            var callCount = 0;
            var tree1 = new VNode[]
            {
                V.Memoized(() => { callCount++; return V.Label(text: $"render-{callCount}"); },
                    new MemoDepRecord("x")),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            Assume.That(callCount, Is.EqualTo(1), "Precondition: the factory ran once on first mount");

            // Act
            var tree2 = new VNode[]
            {
                V.Memoized(() => { callCount++; return V.Label(text: $"render-{callCount}"); },
                    new MemoDepRecord("x")),
            };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(callCount, Is.EqualTo(2),
                "Dependency equality is reference-based, so a fresh-but-content-equal record is a cache miss");
        }

        [Test]
        public void Given_MemoWithKey_When_ReRenderedWithSameKeyAndDeps_Then_FactoryIsSkipped()
        {
            // Arrange
            var callCount = 0;
            var tree1 = new VNode[]
            {
                V.MemoizedWithKey("stable-key", () => { callCount++; return V.Label(text: "keyed"); }, "dep1"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            Assume.That(callCount, Is.EqualTo(1), "Precondition: the factory ran once on first mount");

            // Act
            var tree2 = new VNode[]
            {
                V.MemoizedWithKey("stable-key", () => { callCount++; return V.Label(text: "keyed"); }, "dep1"),
            };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(callCount, Is.EqualTo(1), "An explicit key is the cache key, so same key + same deps hit");
        }

        [Test]
        public void Given_TwoSiblingMemos_When_OneDepChanges_Then_ChangedMemoRecomputes()
        {
            // Arrange
            var callCountA = 0;
            var callCountB = 0;
            var tree1 = new VNode[]
            {
                V.Memoized(() => { callCountA++; return V.Label(text: "A"); }, "depA"),
                V.Memoized(() => { callCountB++; return V.Label(text: "B"); }, "depB"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);

            // Act
            var tree2 = new VNode[]
            {
                V.Memoized(() => { callCountA++; return V.Label(text: "A-new"); }, "depA-changed"),
                V.Memoized(() => { callCountB++; return V.Label(text: "B"); }, "depB"),
            };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(callCountA, Is.EqualTo(2), "The Memo whose deps changed recomputes");
        }

        [Test]
        public void Given_TwoSiblingMemos_When_OneDepChanges_Then_UnchangedMemoStaysCached()
        {
            // Arrange
            var callCountA = 0;
            var callCountB = 0;
            var tree1 = new VNode[]
            {
                V.Memoized(() => { callCountA++; return V.Label(text: "A"); }, "depA"),
                V.Memoized(() => { callCountB++; return V.Label(text: "B"); }, "depB"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);

            // Act
            var tree2 = new VNode[]
            {
                V.Memoized(() => { callCountA++; return V.Label(text: "A-new"); }, "depA-changed"),
                V.Memoized(() => { callCountB++; return V.Label(text: "B"); }, "depB"),
            };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(callCountB, Is.EqualTo(1), "The sibling Memo whose deps were untouched stays cached");
        }

        [Test]
        public void Given_NestedUnkeyedMemos_When_Mounted_Then_InnerResolvesWithoutCacheCollision()
        {
            // Arrange
            VNode[] CreateTree() => new VNode[]
            {
                V.Memoized(() => V.Memoized(() => V.Label(text: "inner"))),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), CreateTree());

            // Assert
            Assert.That((Root.ElementAt(0) as Label)?.text, Is.EqualTo("inner"),
                "An outer and inner Memo at the same node index resolve under nested position scopes");
        }

        private sealed record MemoDepRecord(string Value);
    }
}
