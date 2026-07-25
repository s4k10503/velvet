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

    /// <summary>
    /// Specifies the contract of the strongly-typed <c>V.Memoized&lt;T1..Tn&gt;</c> overloads.
    /// <list type="bullet">
    /// <item>Each overload returns a <see cref="MemoNode"/>.</item>
    /// <item>The supplied dependency arguments are captured, in order, into <see cref="MemoNode.Dependencies"/>;
    /// the array length equals the number of dependency arguments and each element equals the argument passed
    /// at that position.</item>
    /// <item>The factory delegate is stored verbatim and invoking <see cref="MemoNode.Factory"/> returns the
    /// VNode the factory produces.</item>
    /// <item>Two nodes whose dependency arrays hold equal values are dependency-equal under
    /// <see cref="ObjectIs.AreEqualDeps"/>; differing values are not.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class MemoGenericOverloadTests
    {
        #region Single dependency

        [Test]
        public void Given_SingleIntDependency_When_MemoCreated_Then_CapturesItAsTheOnlyDependency()
        {
            // Act
            var node = V.Memoized(() => V.Label(text: "test"), 42);

            // Assert
            Assert.That(node.Dependencies, Is.EqualTo(new object[] { 42 }));
        }

        [Test]
        public void Given_SingleBoolDependency_When_MemoCreated_Then_CapturesItAsTheOnlyDependency()
        {
            // Act
            var node = V.Memoized(() => V.Label(text: "test"), true);

            // Assert
            Assert.That(node.Dependencies, Is.EqualTo(new object[] { true }));
        }

        [Test]
        public void Given_SingleStringDependency_When_MemoCreated_Then_CapturesItAsTheOnlyDependency()
        {
            // Act
            var node = V.Memoized(() => V.Label(text: "test"), "hello");

            // Assert
            Assert.That(node.Dependencies, Is.EqualTo(new object[] { "hello" }));
        }

        [Test]
        public void Given_SingleFloatDependency_When_MemoCreated_Then_CapturesItAsTheOnlyDependency()
        {
            // Act
            var node = V.Memoized(() => V.Label(text: "test"), 3.14f);

            // Assert
            Assert.That(node.Dependencies, Is.EqualTo(new object[] { 3.14f }));
        }

        #endregion

        #region Two dependencies

        [Test]
        public void Given_IntAndStringDependencies_When_MemoCreated_Then_CapturesBothInOrder()
        {
            // Act
            var node = V.Memoized(() => V.Label(text: "test"), 42, "world");

            // Assert
            Assert.That(node.Dependencies, Is.EqualTo(new object[] { 42, "world" }));
        }

        [Test]
        public void Given_BoolAndFloatDependencies_When_MemoCreated_Then_CapturesBothInOrder()
        {
            // Act
            var node = V.Memoized(() => V.Label(text: "test"), false, 1.5f);

            // Assert
            Assert.That(node.Dependencies, Is.EqualTo(new object[] { false, 1.5f }));
        }

        #endregion

        #region Factory storage

        [Test]
        public void Given_Factory_When_MemoCreated_Then_InvokingItReturnsTheFactoryProducedVNode()
        {
            // Arrange
            var label = V.Label(text: "cached");

            // Act
            var node = V.Memoized(() => label, 1);

            // Assert
            Assert.That(node.Factory(), Is.SameAs(label));
        }

        #endregion

        #region Dependency equality

        [Test]
        public void Given_TwoNodesWithEqualDependencyValues_When_Compared_Then_AreDependencyEqual()
        {
            // Arrange
            var node1 = V.Memoized(() => V.Label(text: "a"), 42);
            var node2 = V.Memoized(() => V.Label(text: "a"), 42);

            // Act + Assert
            Assert.That(ObjectIs.AreEqualDeps(node1.Dependencies, node2.Dependencies), Is.True);
        }

        [Test]
        public void Given_TwoNodesWithDifferentDependencyValues_When_Compared_Then_AreNotDependencyEqual()
        {
            // Arrange
            var node1 = V.Memoized(() => V.Label(text: "a"), 42);
            var node2 = V.Memoized(() => V.Label(text: "a"), 99);

            // Act + Assert
            Assert.That(ObjectIs.AreEqualDeps(node1.Dependencies, node2.Dependencies), Is.False);
        }

        #endregion
    }
}
