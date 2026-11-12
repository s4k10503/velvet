using System.Collections.Generic;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that invalidating a fiber's memoization slots leaves nothing a later settle could promote back over
    /// the invalidation: a scope the next render does not reach stages nothing, so the committed values after that
    /// settle are the empty ones.
    /// </summary>
    [TestFixture]
    internal sealed class ComponentFiberMemoInvalidationTests
    {
        private static (ComponentFiber Fiber, HookMemoSlot Slot) FiberWithStagedValueSlot()
        {
            var slot = new HookMemoSlot
            {
                LastDeps = new object[] { 1 },
                NextDeps = new object[] { 2 },
                CachedValue = "committed",
                NextCachedValue = "staged",
            };
            var fiber = new ComponentFiber { MemoSlots = new List<HookMemoSlot> { slot } };
            return (fiber, slot);
        }

        // GREEN_ON_BASE(characterization): the base already clears this slot field on invalidation; no case asserted it before.
        [Test]
        public void Given_ASlotWithStagedDeps_When_InvalidatedAndThenSettledWithNothingStaged_Then_ItHoldsNoCommittedDeps()
        {
            // Arrange
            var (fiber, slot) = FiberWithStagedValueSlot();

            // Act
            fiber.InvalidateMemoCache();
            FiberHookCommit.CommitMemoSlots(fiber.MemoSlots);

            // Assert
            Assert.That(slot.LastDeps, Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base already clears this slot field on invalidation; no case asserted it before.
        [Test]
        public void Given_ASlotWithAStagedValue_When_InvalidatedAndThenSettledWithNothingStaged_Then_ItHoldsNoCommittedValue()
        {
            // Arrange
            var (fiber, slot) = FiberWithStagedValueSlot();

            // Act
            fiber.InvalidateMemoCache();
            FiberHookCommit.CommitMemoSlots(fiber.MemoSlots);

            // Assert
            Assert.That(slot.CachedValue, Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base already clears this slot field on invalidation; no case asserted it before.
        // Before any settle: the recycle sweep marks a node held as the committed value live
        // (FiberTreeReturn.VisitDisposableSlotRoots), so one the invalidation left would stay protected.
        [Test]
        public void Given_ASlotWithACommittedValue_When_Invalidated_Then_ItHoldsNoCommittedValue()
        {
            // Arrange
            var (fiber, slot) = FiberWithStagedValueSlot();

            // Act
            fiber.InvalidateMemoCache();

            // Assert
            Assert.That(slot.CachedValue, Is.Null);
        }
    }
}
