using NUnit.Framework;

namespace Velvet.Tests
{
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
