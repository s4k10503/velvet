// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the deps-keyed memoization of <c>V.Memo</c> / <c>V.MemoizedWithKey</c> across reconcile passes.
    /// <list type="bullet">
    /// <item>The factory runs on the first reconcile, then runs again only when the dependency array is no
    /// longer element-wise equal to the previous one.</item>
    /// <item>While every dependency element is equal, the cache hits and the factory is skipped.</item>
    /// <item>Keyed memo nodes own independent caches: changing one node's deps does not invalidate a sibling
    /// keyed node's cache.</item>
    /// <item>A constructed memo node is a <see cref="MemoNode"/> carrying the supplied key and dependency
    /// array, and its factory produces the inner node.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class VMemoComponentTests : ReconcilerTestFixture
    {
        [Test]
        public void Given_MountedMemo_When_DepsUnchanged_Then_FactoryIsSkipped()
        {
            // Arrange
            var counter = 0;
            var tree1 = new VNode[]
            {
                V.Memoized(() => { counter++; return V.Component(StubRender); }, 1, "a"),
            };
            Reconciler.Reconcile(Root, System.Array.Empty<VNode>(), tree1);
            Assume.That(counter, Is.EqualTo(1), "Precondition: the factory ran once on mount");

            // Act — re-reconcile with deps element-wise equal to the previous pass
            var tree2 = new VNode[]
            {
                V.Memoized(() => { counter++; return V.Component(StubRender); }, 1, "a"),
            };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(counter, Is.EqualTo(1),
                "With every dependency element equal, the cache hits and the factory is not called");
        }

        [Test]
        public void Given_MountedMemo_When_DepsChanged_Then_FactoryRunsAgain()
        {
            // Arrange
            var counter = 0;
            var tree1 = new VNode[]
            {
                V.Memoized(() => { counter++; return V.Component(StubRender); }, 1),
            };
            Reconciler.Reconcile(Root, System.Array.Empty<VNode>(), tree1);
            Assume.That(counter, Is.EqualTo(1), "Precondition: the factory ran once on mount");

            // Act — re-reconcile with a changed dependency
            var tree2 = new VNode[]
            {
                V.Memoized(() => { counter++; return V.Component(StubRender); }, 2),
            };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(counter, Is.EqualTo(2), "A changed dependency misses the cache, so the factory runs again");
        }

        [Test]
        public void Given_KeyedSiblingMemos_When_OneNodeDepsChange_Then_OnlyThatNodeFactoryRuns()
        {
            // Arrange
            var counterA = 0;
            var counterB = 0;
            var tree1 = new VNode[]
            {
                V.MemoizedWithKey("comp-a", () => { counterA++; return V.Component(StubRender); }, 1),
                V.MemoizedWithKey("comp-b", () => { counterB++; return V.Component(StubRender); }, 1),
            };
            Reconciler.Reconcile(Root, System.Array.Empty<VNode>(), tree1);
            Assume.That((counterA, counterB), Is.EqualTo((1, 1)), "Precondition: both factories ran once on mount");

            // Act — change only node A's deps
            var tree2 = new VNode[]
            {
                V.MemoizedWithKey("comp-a", () => { counterA++; return V.Component(StubRender); }, 2),
                V.MemoizedWithKey("comp-b", () => { counterB++; return V.Component(StubRender); }, 1),
            };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert — A re-ran (deps changed), B stayed cached (deps unchanged)
            Assert.That((counterA, counterB), Is.EqualTo((2, 1)),
                "Keyed memo nodes own independent caches");
        }

        [Test]
        public void Given_MemoWithKey_When_Constructed_Then_NodeIsMemoNode()
        {
            // Act
            var node = V.MemoizedWithKey("test-key", () => V.Component(StubRender), 42);

            // Assert
            Assert.That(node, Is.InstanceOf<MemoNode>());
        }

        // The "MemoizedWithKey carries the supplied key onto the node" fact is verified by
        // VMemoGeneratedOverloadTests.Given_Arity1_When_MemoWithKey_Then_NodeCarriesKey.

        [Test]
        public void Given_MemoWithKey_When_Constructed_Then_NodeCarriesDependencies()
        {
            // Act
            var node = V.MemoizedWithKey("test-key", () => V.Component(StubRender), 42);

            // Assert
            Assert.That(node.Dependencies, Is.EqualTo(new object[] { 42 }));
        }

        [Test]
        public void Given_MemoWithKey_When_FactoryInvoked_Then_ProducesInnerComponentNode()
        {
            // Arrange
            var node = V.MemoizedWithKey("test-key", () => V.Component(StubRender), 42);

            // Act
            var inner = node.Factory();

            // Assert
            Assert.That(inner, Is.InstanceOf<ComponentNode>());
        }

        [Component]
        private static VNode StubRender() => V.Label(text: "stub");
    }
}
