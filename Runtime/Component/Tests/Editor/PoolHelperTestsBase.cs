using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the pooling contract shared by every <see cref="VNodePool"/> widget pool
    /// (Button / Label / Slider / TextField / Toggle).
    /// <list type="bullet">
    /// <item>Resetting a null element is a no-op and never throws.</item>
    /// <item>A Return/Rent cycle hands back the very same instance (the pool recycles rather than allocates).</item>
    /// <item>A recycled instance carries no ghost from its prior use: <c>userData</c>, custom USS classes, and
    /// the widget-specific value state are all cleared before the next consumer sees it.</item>
    /// <item>The pool is bounded by MaxPoolSize: returning more elements than the cap discards the surplus, and
    /// a subsequent burst of rents hands back exactly MaxPoolSize recycled instances before falling back to
    /// fresh allocations.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Subclasses bind the pool operations through the abstract members and describe the widget-specific value
    /// ghost via <see cref="SetElementSpecificGhost"/> / <see cref="AssertElementSpecificGhostCleared"/>. The
    /// static pools are shared across the test session, so <see cref="BasePoolSetUp"/> drains the pool before
    /// each test to keep boundary assertions deterministic.
    /// </remarks>
    public abstract class PoolHelperTestsBase<TElement> where TElement : VisualElement
    {
        protected abstract void ClearPool();
        protected abstract TElement Rent();
        protected abstract void Return(TElement element);
        protected abstract void Reset(TElement element);
        protected abstract int MaxPoolSize { get; }

        protected virtual void SetElementSpecificGhost(TElement element) { }
        protected virtual void AssertElementSpecificGhostCleared(TElement element) { }

        [SetUp]
        public void BasePoolSetUp()
        {
            ClearPool();
        }

        [Test]
        public void Given_NullElement_When_Reset_Then_DoesNotThrow()
        {
            // Act + Assert
            Assert.DoesNotThrow(() => Reset(null));
        }

        [Test]
        public virtual void Given_ReturnedElement_When_RentedAgain_Then_SameInstanceIsHandedBack()
        {
            // Arrange
            var sentinel = Rent();
            Return(sentinel);

            // Act
            var rented = Rent();

            // Assert
            Assert.AreSame(sentinel, rented, "A Return/Rent cycle recycles the same instance");
        }

        [Test]
        public virtual void Given_ReturnedElementWithUserData_When_RentedAgain_Then_UserDataGhostIsCleared()
        {
            // Arrange
            var sentinel = Rent();
            sentinel.userData = "marker";
            Return(sentinel);

            // Act
            var rented = Rent();
            Assume.That(rented, Is.SameAs(sentinel), "Precondition: the pool recycled the returned instance");

            // Assert
            Assert.IsNull(rented.userData, "userData from the previous use does not survive the pool cycle");
        }

        [Test]
        public virtual void Given_ReturnedElementWithCustomClass_When_RentedAgain_Then_CustomClassGhostIsCleared()
        {
            // Arrange
            var sentinel = Rent();
            sentinel.AddToClassList("custom-pool-test");
            Return(sentinel);

            // Act
            var rented = Rent();
            Assume.That(rented, Is.SameAs(sentinel), "Precondition: the pool recycled the returned instance");

            // Assert
            CollectionAssert.DoesNotContain(rented.GetClasses(), "custom-pool-test",
                "A custom USS class from the previous use does not survive the pool cycle");
        }

        [Test]
        public virtual void Given_ReturnedElementWithWidgetValue_When_RentedAgain_Then_ValueGhostIsCleared()
        {
            // Arrange
            var sentinel = Rent();
            SetElementSpecificGhost(sentinel);
            Return(sentinel);

            // Act
            var rented = Rent();
            Assume.That(rented, Is.SameAs(sentinel), "Precondition: the pool recycled the returned instance");

            // Assert — the widget-specific value (text / value / isPassword / ...) is back at its default
            AssertElementSpecificGhostCleared(rented);
        }

        [Test]
        public void Given_MoreReturnsThanCap_When_RentedBack_Then_ExactlyMaxPoolSizeAreRecycled()
        {
            // Arrange — fill and over-return the pool with twice MaxPoolSize elements
            var rented = new List<TElement>(MaxPoolSize * 2);
            for (var i = 0; i < MaxPoolSize * 2; i++)
            {
                rented.Add(Rent());
            }
            foreach (var element in rented)
            {
                Return(element);
            }

            // Act
            var firstHalfReused = 0;
            for (var i = 0; i < MaxPoolSize * 2; i++)
            {
                if (rented.Contains(Rent()))
                {
                    firstHalfReused++;
                }
            }

            // Assert
            Assert.AreEqual(MaxPoolSize, firstHalfReused,
                $"The pool caps at {MaxPoolSize}: exactly that many returned instances are recycled, the surplus is discarded");
        }
    }
}
