using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>V.Suspense</c> factory and its wrapper-less rendering when no descendant is suspended.
    /// <list type="bullet">
    /// <item>A null fallback is rejected with <see cref="ArgumentNullException"/>.</item>
    /// <item>Null children are normalized to an empty array.</item>
    /// <item>When nothing is suspended the children render directly into the parent's slot range with no
    /// container element emitted: the children appear in order and an empty boundary emits nothing.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class VSuspenseFactoryTests : ReconcilerTestFixture
    {
        [Test]
        public void Given_NullFallback_When_Suspense_Then_ThrowsArgumentNullException()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(() =>
                V.Suspense(fallback: null, children: new VNode[] { V.Label(text: "content") }));
        }

        [Test]
        public void Given_NullChildren_When_Suspense_Then_ChildrenAreEmpty()
        {
            // Act
            var node = V.Suspense(fallback: V.Label(text: "loading"), children: null);

            // Assert
            Assert.That(node.Children, Is.Empty);
        }

        [Test]
        public void Given_ChildrenAndNotSuspended_When_Reconciled_Then_ChildrenRenderDirectlyInOrder()
        {
            // Arrange
            var tree = new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading..."),
                    children: new VNode[]
                    {
                        V.Label(text: "child1"),
                        V.Label(text: "child2"),
                    }),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert — the children sit directly in Root's slot range, no container element wraps them
            Assert.That(
                (Root.childCount, (Root.ElementAt(0) as Label)?.text, (Root.ElementAt(1) as Label)?.text),
                Is.EqualTo((2, "child1", "child2")));
        }

        [Test]
        public void Given_EmptyChildrenAndNotSuspended_When_Reconciled_Then_NothingIsRendered()
        {
            // Arrange
            var tree = new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading..."),
                    children: Array.Empty<VNode>()),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert — with no children and not suspended, the boundary emits no container element
            Assert.That(Root.childCount, Is.EqualTo(0));
        }
    }
}
