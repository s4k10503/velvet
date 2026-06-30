using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="FiberTreeTraversal.NotifyContextChanged"/>, which walks a fiber
    /// subtree on a context value change and schedules the consumers that read the changed key.
    /// <list type="bullet">
    /// <item>Notification only SCHEDULES dependent consumers; it distributes no per-fiber value snapshot. Each
    /// scheduled consumer reads the new value live from the context cursor on its own re-render.</item>
    /// <item>A null root is tolerated and notifies nothing; a null key means "no specific context changed" and
    /// schedules nothing even for fibers that registered a dependency.</item>
    /// <item>Only fibers that registered a dependency on the changed key are scheduled; non-dependent fibers —
    /// including ancestors on the path to a deep dependent consumer — are not.</item>
    /// <item>Every dependent consumer in the walked subtree is reached, regardless of depth or sibling position.</item>
    /// <item>Within one propagation generation, a fiber is scheduled at most once even if it depends on several
    /// keys changed in that generation; across distinct generations it is scheduled once per generation.</item>
    /// <item>The default generation sentinel (<see cref="int.MinValue"/>) disables dedup, so repeated
    /// notifications of the same fiber each schedule it.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Re-render scheduling is observed through the <see cref="ComponentFiber.RequestRenderForContextHandler"/>
    /// delegate slot, counted per fiber.
    /// </remarks>
    [TestFixture]
    internal sealed class FiberTreeTraversalTests
    {
        [Test]
        public void Given_NullRoot_When_Notified_Then_DoesNotThrow()
        {
            // Act + Assert
            Assert.DoesNotThrow(() => FiberTreeTraversal.NotifyContextChanged(null, new object()));
        }

        [Test]
        public void Given_DependentFiber_When_NotifiedWithNullKey_Then_NotScheduled()
        {
            // Arrange
            var fiber = new ComponentFiber();
            var scheduledCount = 0;
            fiber.RequestRenderForContextHandler = _ => scheduledCount++;
            fiber.RegisterContextDependency(new object());

            // Act
            FiberTreeTraversal.NotifyContextChanged(fiber, null);

            // Assert
            Assert.That(scheduledCount, Is.EqualTo(0));
        }

        [Test]
        public void Given_DependentFiber_When_NotifiedWithDependedKey_Then_ScheduledOnce()
        {
            // Arrange
            var fiber = new ComponentFiber();
            var scheduledCount = 0;
            fiber.RequestRenderForContextHandler = _ => scheduledCount++;
            var ctx = new object();
            fiber.RegisterContextDependency(ctx);

            // Act
            FiberTreeTraversal.NotifyContextChanged(fiber, ctx);

            // Assert
            Assert.That(scheduledCount, Is.EqualTo(1));
        }

        [Test]
        public void Given_NonDependentFiber_When_Notified_Then_NotScheduled()
        {
            // Arrange
            var fiber = new ComponentFiber();
            var scheduledCount = 0;
            fiber.RequestRenderForContextHandler = _ => scheduledCount++;

            // Act
            FiberTreeTraversal.NotifyContextChanged(fiber, new object());

            // Assert
            Assert.That(scheduledCount, Is.EqualTo(0));
        }

        [Test]
        public void Given_OnlyDescendantDependsOnContext_When_NotifiedFromParent_Then_DescendantIsScheduled()
        {
            // Arrange
            var parent = new ComponentFiber();
            var child = new ComponentFiber();
            var childScheduledCount = 0;
            child.RequestRenderForContextHandler = _ => childScheduledCount++;
            parent.AppendChild(child);
            var ctx = new object();
            child.RegisterContextDependency(ctx);

            // Act
            FiberTreeTraversal.NotifyContextChanged(parent, ctx);

            // Assert
            Assert.That(childScheduledCount, Is.EqualTo(1));
        }

        [Test]
        public void Given_OnlyDescendantDependsOnContext_When_NotifiedFromParent_Then_NonDependentParentIsNotScheduled()
        {
            // Arrange
            var parent = new ComponentFiber();
            var child = new ComponentFiber();
            var parentScheduledCount = 0;
            parent.RequestRenderForContextHandler = _ => parentScheduledCount++;
            parent.AppendChild(child);
            var ctx = new object();
            child.RegisterContextDependency(ctx);

            // Act
            FiberTreeTraversal.NotifyContextChanged(parent, ctx);

            // Assert
            Assert.That(parentScheduledCount, Is.EqualTo(0));
        }

        [Test]
        public void Given_TwoDependentSiblings_When_NotifiedFromRoot_Then_BothAreScheduled()
        {
            // Arrange
            var root = new ComponentFiber();
            var childA = new ComponentFiber();
            var childB = new ComponentFiber();
            var childAScheduledCount = 0;
            var childBScheduledCount = 0;
            childA.RequestRenderForContextHandler = _ => childAScheduledCount++;
            childB.RequestRenderForContextHandler = _ => childBScheduledCount++;
            root.AppendChild(childA);
            root.AppendChild(childB);
            var ctx = new object();
            childA.RegisterContextDependency(ctx);
            childB.RegisterContextDependency(ctx);

            // Act
            FiberTreeTraversal.NotifyContextChanged(root, ctx);

            // Assert
            Assert.That((childAScheduledCount, childBScheduledCount), Is.EqualTo((1, 1)));
        }

        [Test]
        public void Given_DeepDependentConsumer_When_NotifiedFromRoot_Then_DeepConsumerIsScheduled()
        {
            // Arrange — a consumer three levels below the change root depends on the key
            var ctx = new object();
            var root = new ComponentFiber();
            var directChild = new ComponentFiber();
            var grandchild = new ComponentFiber();
            var greatGrandchild = new ComponentFiber();
            root.AppendChild(directChild);
            directChild.AppendChild(grandchild);
            grandchild.AppendChild(greatGrandchild);
            var greatScheduledCount = 0;
            greatGrandchild.RequestRenderForContextHandler = _ => greatScheduledCount++;
            greatGrandchild.RegisterContextDependency(ctx);

            // Act
            FiberTreeTraversal.NotifyContextChanged(root, ctx);

            // Assert
            Assert.That(greatScheduledCount, Is.EqualTo(1), "A deep dependent consumer is reached.");
        }

        [Test]
        public void Given_DeepDependentConsumer_When_NotifiedFromRoot_Then_NonDependentAncestorOnPathIsNotScheduled()
        {
            // Arrange
            var ctx = new object();
            var root = new ComponentFiber();
            var directChild = new ComponentFiber();
            var grandchild = new ComponentFiber();
            var greatGrandchild = new ComponentFiber();
            root.AppendChild(directChild);
            directChild.AppendChild(grandchild);
            grandchild.AppendChild(greatGrandchild);
            var directScheduledCount = 0;
            directChild.RequestRenderForContextHandler = _ => directScheduledCount++;
            greatGrandchild.RegisterContextDependency(ctx);

            // Act
            FiberTreeTraversal.NotifyContextChanged(root, ctx);

            // Assert
            Assert.That(directScheduledCount, Is.EqualTo(0), "A non-dependent ancestor on the path is not scheduled.");
        }

        [Test]
        public void Given_FiberDependingOnTwoKeys_When_BothChangeInSameGeneration_Then_ScheduledOnce()
        {
            // Arrange — one fiber depends on two keys that both change within the same propagation generation
            var ctxA = new object();
            var ctxB = new object();
            var fiber = new ComponentFiber();
            var scheduledCount = 0;
            fiber.RequestRenderForContextHandler = _ => scheduledCount++;
            fiber.RegisterContextDependency(ctxA);
            fiber.RegisterContextDependency(ctxB);
            const int generation = 7;

            // Act
            FiberTreeTraversal.NotifyContextChanged(fiber, ctxA, generation);
            FiberTreeTraversal.NotifyContextChanged(fiber, ctxB, generation);

            // Assert
            Assert.That(scheduledCount, Is.EqualTo(1));
        }

        [Test]
        public void Given_DependentFiber_When_NotifiedTwiceWithDefaultGeneration_Then_ScheduledEachTime()
        {
            // Arrange — the default generation sentinel disables dedup
            var ctx = new object();
            var fiber = new ComponentFiber();
            var scheduledCount = 0;
            fiber.RequestRenderForContextHandler = _ => scheduledCount++;
            fiber.RegisterContextDependency(ctx);

            // Act
            FiberTreeTraversal.NotifyContextChanged(fiber, ctx);
            FiberTreeTraversal.NotifyContextChanged(fiber, ctx);

            // Assert
            Assert.That(scheduledCount, Is.EqualTo(2));
        }

        [Test]
        public void Given_DependentFiber_When_NotifiedInTwoDistinctGenerations_Then_ScheduledOncePerGeneration()
        {
            // Arrange
            var ctx = new object();
            var fiber = new ComponentFiber();
            var scheduledCount = 0;
            fiber.RequestRenderForContextHandler = _ => scheduledCount++;
            fiber.RegisterContextDependency(ctx);

            // Act
            FiberTreeTraversal.NotifyContextChanged(fiber, ctx, 1);
            FiberTreeTraversal.NotifyContextChanged(fiber, ctx, 2);

            // Assert
            Assert.That(scheduledCount, Is.EqualTo(2));
        }
    }
}
