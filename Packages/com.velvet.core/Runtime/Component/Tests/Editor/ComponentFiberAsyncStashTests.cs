using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how a fiber keeps a pending resource a transition's render started while a render of another lane
    /// shows the slot's other key: which resource a later render of the slot gets back, which are disposed, and what
    /// an unmount does with the ones still kept.
    /// </summary>
    [TestFixture]
    internal sealed class ComponentFiberAsyncStashTests
    {
        private sealed class FakeResource : IFiberAsyncResource
        {
            public FiberAsyncResourceStatus Status { get; init; } = FiberAsyncResourceStatus.Pending;
            public object ResourceKey { get; init; } = "fake";
            public bool StartedByTransition { get; init; }
            public int Disposals { get; private set; }

            public void Dispose() => Disposals++;
        }

        private static Dictionary<int, IFiberAsyncResource> Stash(ComponentFiber fiber)
        {
            var field = typeof(ComponentFiber).GetField("_stashedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var stash = (Dictionary<int, IFiberAsyncResource>)field.GetValue(fiber);
            if (stash == null)
            {
                stash = new Dictionary<int, IFiberAsyncResource>();
                field.SetValue(fiber, stash);
            }
            return stash;
        }

        [TearDown]
        public void TearDown()
        {
            FiberWorkLoop.IsRenderingTransitionLane = false;
        }

        // GREEN_ON_BASE(characterization): the base already stashes, restores and disposes as asserted here; no case asserted it before.
        [Test]
        public void Given_AStashedResourceOfAKey_When_ATransitionsRenderAsksForThatKey_Then_ItIsGivenBack()
        {
            // Arrange
            var fiber = new ComponentFiber();
            var stashed = new FiberAsyncResource<int>("a") { StartedByTransition = true };
            Stash(fiber)[0] = stashed;
            FiberWorkLoop.IsRenderingTransitionLane = true;

            // Act
            var restored = fiber.RestoreOrRetireAsync<int>(0, new FakeResource { ResourceKey = "b" }, "a");

            // Assert
            Assert.That(restored, Is.SameAs(stashed));
        }

        // GREEN_ON_BASE(characterization): the base already stashes, restores and disposes as asserted here; no case asserted it before.
        [Test]
        public void Given_AStashedResourceOfAKey_When_ATransitionsRenderAsksForAnotherKey_Then_ItIsDisposed()
        {
            // Arrange
            var fiber = new ComponentFiber();
            var stashed = new FakeResource { ResourceKey = "a", StartedByTransition = true };
            Stash(fiber)[0] = stashed;
            FiberWorkLoop.IsRenderingTransitionLane = true;

            // Act
            fiber.RestoreOrRetireAsync<int>(0, new FakeResource { ResourceKey = "b" }, "c");

            // Assert
            Assert.That(stashed.Disposals, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base already stashes, restores and disposes as asserted here; no case asserted it before.
        [Test]
        public void Given_AStashedResourceOfAKey_When_ATransitionsRenderTakesItBack_Then_TheStashNoLongerHoldsIt()
        {
            // Arrange
            var fiber = new ComponentFiber();
            Stash(fiber)[0] = new FiberAsyncResource<int>("a") { StartedByTransition = true };
            FiberWorkLoop.IsRenderingTransitionLane = true;

            // Act
            fiber.RestoreOrRetireAsync<int>(0, new FakeResource { ResourceKey = "b" }, "a");

            // Assert
            Assert.That(Stash(fiber).ContainsKey(0), Is.False);
        }

        // GREEN_ON_BASE(characterization): the base already stashes, restores and disposes as asserted here; no case asserted it before.
        [Test]
        public void Given_NoResourceInTheSlotAndNoneStashed_When_ARenderOutsideATransitionAsks_Then_NoneIsGivenBack()
        {
            // Arrange
            var fiber = new ComponentFiber();

            // Act
            var restored = fiber.RestoreOrRetireAsync<int>(0, null, "a");

            // Assert
            Assert.That(restored, Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base already stashes, restores and disposes as asserted here; no case asserted it before.
        [Test]
        public void Given_AResourceNoTransitionStarted_When_ARenderAsksForAnotherKey_Then_ItIsDisposed()
        {
            // Arrange
            var fiber = new ComponentFiber();
            var existing = new FakeResource { ResourceKey = "a" };

            // Act
            fiber.RestoreOrRetireAsync<int>(0, existing, "b");

            // Assert
            Assert.That(existing.Disposals, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base already stashes, restores and disposes as asserted here; no case asserted it before.
        [Test]
        public void Given_APendingResourceATransitionStarted_When_ARenderOutsideATransitionAsksForAnotherKey_Then_ItIsNotDisposed()
        {
            // Arrange
            var fiber = new ComponentFiber();
            var existing = new FakeResource { ResourceKey = "a", StartedByTransition = true };

            // Act
            fiber.RestoreOrRetireAsync<int>(0, existing, "b");

            // Assert
            Assert.That(existing.Disposals, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base already stashes, restores and disposes as asserted here; no case asserted it before.
        [Test]
        public void Given_APendingResourceATransitionStarted_When_ARenderOutsideATransitionAsksForAnotherKey_Then_ItIsStashed()
        {
            // Arrange
            var fiber = new ComponentFiber();
            var existing = new FakeResource { ResourceKey = "a", StartedByTransition = true };

            // Act
            fiber.RestoreOrRetireAsync<int>(0, existing, "b");

            // Assert
            Assert.That(Stash(fiber).TryGetValue(0, out var held) ? held : null, Is.SameAs(existing));
        }

        // GREEN_ON_BASE(characterization): the base already stashes, restores and disposes as asserted here; no case asserted it before.
        [Test]
        public void Given_AStashedResource_When_ARenderOutsideATransitionStashesAnotherInItsPlace_Then_TheFirstIsDisposed()
        {
            // Arrange
            var fiber = new ComponentFiber();
            var first = new FakeResource { ResourceKey = "a", StartedByTransition = true };
            Stash(fiber)[0] = first;
            var second = new FakeResource { ResourceKey = "b", StartedByTransition = true };

            // Act
            fiber.RestoreOrRetireAsync<int>(0, second, "c");

            // Assert
            Assert.That(first.Disposals, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base already stashes, restores and disposes as asserted here; no case asserted it before.
        [Test]
        public void Given_AStashedResource_When_TheFibersAsyncSlotsAreDisposed_Then_ItIsDisposed()
        {
            // Arrange
            var fiber = new ComponentFiber();
            var stashed = new FakeResource { StartedByTransition = true };
            Stash(fiber)[0] = stashed;

            // Act
            fiber.DisposeAsyncSlots();

            // Assert
            Assert.That(stashed.Disposals, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base already stashes, restores and disposes as asserted here; no case asserted it before.
        [Test]
        public void Given_AStashedResource_When_TheFibersAsyncSlotsAreDisposed_Then_TheStashIsEmpty()
        {
            // Arrange
            var fiber = new ComponentFiber();
            Stash(fiber)[0] = new FakeResource { StartedByTransition = true };

            // Act
            fiber.DisposeAsyncSlots();

            // Assert
            Assert.That(Stash(fiber).Count, Is.EqualTo(0));
        }
    }
}
