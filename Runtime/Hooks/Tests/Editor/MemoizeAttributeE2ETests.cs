using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how a <c>[Memoize]</c> partial method, expanded by the generator into a
    /// <c>V.Memoized(() =&gt; *_Impl(...), args...)</c> call, behaves across reconciliations.
    /// <list type="bullet">
    /// <item>The first reconciliation always misses the dependency cache, so the underlying <c>_Impl</c> body
    /// runs exactly once.</item>
    /// <item>A later reconciliation whose captured arguments are all dependency-equal to the previous ones is a
    /// cache hit: the cached VNode is reused and <c>_Impl</c> is not re-invoked.</item>
    /// <item>A later reconciliation in which any single argument differs is a cache miss: <c>_Impl</c> is
    /// re-invoked to rebuild the VNode.</item>
    /// <item>The dependency array captures every method argument, so a change to any position (first, middle,
    /// or last) triggers the miss.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Each test reconciles a single-element VNode tree twice through a <see cref="Reconciler"/> and observes
    /// the per-arity <c>*ImplCallCount</c> on <see cref="MemoizeAttributeDemoComponent"/>, which the generated
    /// memo wrapper increments only on a build (cache miss).
    /// </remarks>
    [TestFixture]
    internal sealed class MemoizeAttributeE2ETests : ReconcilerTestFixture
    {
        [Test]
        public void Given_Arity1_When_FirstReconcile_Then_ImplRunsOnce()
        {
            // Arrange
            var demo = new MemoizeAttributeDemoComponent();
            var tree = new VNode[] { demo.BuildArity1("title") };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(demo.Arity1ImplCallCount, Is.EqualTo(1));
        }

        [Test]
        public void Given_Arity1_When_ReconciledAgainWithSameArg_Then_ImplIsNotReinvoked()
        {
            // Arrange
            var demo = new MemoizeAttributeDemoComponent();
            var tree1 = new VNode[] { demo.BuildArity1("title") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            Assume.That(demo.Arity1ImplCallCount, Is.EqualTo(1), "Precondition: the first reconcile built once");

            // Act
            var tree2 = new VNode[] { demo.BuildArity1("title") };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(demo.Arity1ImplCallCount, Is.EqualTo(1),
                "Dependency-equal args are a cache hit, so the impl body is not re-invoked");
        }

        [Test]
        public void Given_Arity1_When_ReconciledAgainWithChangedArg_Then_ImplIsReinvoked()
        {
            // Arrange
            var demo = new MemoizeAttributeDemoComponent();
            var tree1 = new VNode[] { demo.BuildArity1("old") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            Assume.That(demo.Arity1ImplCallCount, Is.EqualTo(1), "Precondition: the first reconcile built once");

            // Act
            var tree2 = new VNode[] { demo.BuildArity1("new") };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(demo.Arity1ImplCallCount, Is.EqualTo(2),
                "A changed dependency is a cache miss, so the impl body rebuilds");
        }

        [Test]
        public void Given_Arity3_When_ReconciledAgainWithSameArgs_Then_ImplIsNotReinvoked()
        {
            // Arrange
            var demo = new MemoizeAttributeDemoComponent();
            var tree1 = new VNode[] { demo.BuildArity3("t", 1, true) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            Assume.That(demo.Arity3ImplCallCount, Is.EqualTo(1), "Precondition: the first reconcile built once");

            // Act
            var tree2 = new VNode[] { demo.BuildArity3("t", 1, true) };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(demo.Arity3ImplCallCount, Is.EqualTo(1),
                "All three args dependency-equal is a cache hit");
        }

        [Test]
        public void Given_Arity3_When_ReconciledAgainWithOneArgChanged_Then_ImplIsReinvoked()
        {
            // Arrange — only the middle argument differs between the two renders
            var demo = new MemoizeAttributeDemoComponent();
            var tree1 = new VNode[] { demo.BuildArity3("t", 1, true) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            Assume.That(demo.Arity3ImplCallCount, Is.EqualTo(1), "Precondition: the first reconcile built once");

            // Act
            var tree2 = new VNode[] { demo.BuildArity3("t", 2, true) };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(demo.Arity3ImplCallCount, Is.EqualTo(2),
                "A single changed argument anywhere in the array is a cache miss");
        }

        [Test]
        public void Given_Arity8_When_ReconciledAgainWithSameArgs_Then_ImplIsNotReinvoked()
        {
            // Arrange
            var demo = new MemoizeAttributeDemoComponent();
            var tree1 = new VNode[] { demo.BuildArity8(1, 2, 3, 4, 5, 6, 7, 8) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            Assume.That(demo.Arity8ImplCallCount, Is.EqualTo(1), "Precondition: the first reconcile built once");

            // Act
            var tree2 = new VNode[] { demo.BuildArity8(1, 2, 3, 4, 5, 6, 7, 8) };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(demo.Arity8ImplCallCount, Is.EqualTo(1),
                "All eight args dependency-equal is a cache hit");
        }

        [Test]
        public void Given_Arity8_When_ReconciledAgainWithLastArgChanged_Then_ImplIsReinvoked()
        {
            // Arrange — only the eighth argument differs
            var demo = new MemoizeAttributeDemoComponent();
            var tree1 = new VNode[] { demo.BuildArity8(1, 2, 3, 4, 5, 6, 7, 8) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            Assume.That(demo.Arity8ImplCallCount, Is.EqualTo(1), "Precondition: the first reconcile built once");

            // Act
            var tree2 = new VNode[] { demo.BuildArity8(1, 2, 3, 4, 5, 6, 7, 9) };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(demo.Arity8ImplCallCount, Is.EqualTo(2),
                "A change in the last dependency position is a cache miss");
        }
    }
}
