using System;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The repaint watch a text leaf gets while its shown string differs from its text: one per leaf for as long
    /// as the effect is there, gone with the effect, the leaf (a removed leaf goes back to the element pool) or
    /// the reconciler. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class TextShownWatchLifecycleTests
    {
        private static VNode Leaf(string cls) => V.Label(className: cls, text: "hi");

        [Test]
        public void Given_AnUppercaseLabel_When_Mounted_Then_ItIsWatched()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new[] { Leaf("uppercase") });

            // Assert
            Assert.That(scope.Reconciler.Context.TextShownWatches.ContainsKey(scope.Root[0]), Is.True);
        }

        [Test]
        public void Given_AWatchedLabel_When_TheEffectIsPatchedAway_Then_ItIsNoLongerWatched()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var first = new[] { Leaf("uppercase") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), first);
            var label = scope.Root[0];

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, new[] { Leaf("p-2") });

            // Assert
            Assert.That(scope.Reconciler.Context.TextShownWatches.ContainsKey(label), Is.False);
        }

        [Test]
        public void Given_AWatchedLabel_When_ItIsRemovedAndReturnedToThePool_Then_NoWatchRemains()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var first = new[] { Leaf("uppercase") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), first);

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, Array.Empty<VNode>());

            // Assert
            Assert.That(scope.Reconciler.Context.TextShownWatches.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_AReconcilerHoldingAWatch_When_Disposed_Then_NoWatchRemains()
        {
            // Arrange
            var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new[] { Leaf("uppercase") });
            var context = scope.Reconciler.Context;

            // Act
            scope.Dispose();

            // Assert
            Assert.That(context.TextShownWatches.Count, Is.EqualTo(0));
        }
    }
}
