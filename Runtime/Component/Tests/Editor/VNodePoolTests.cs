using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the recycling contract of <see cref="VNodePool"/> for props bags, event arrays, and node
    /// arrays.
    /// <list type="bullet">
    /// <item>A rented props bag is clean: every mutable field is null.</item>
    /// <item>Returning then renting reuses the same instance with its fields reset, so a pooled bag never
    /// leaks stale state to the next consumer.</item>
    /// <item>The shared empty singleton is never pooled, so renting never hands back the singleton.</item>
    /// <item>A rented single-event array has length one; returning then renting reuses it with its element
    /// cleared, and an empty array is never pooled.</item>
    /// <item>A rented node array has the requested length; returning then renting reuses it with its elements
    /// cleared.</item>
    /// <item>The pool caps at its maximum size, so returning more instances than the cap forces new
    /// allocations on the next rents beyond the cap.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class VNodePoolTests
    {
        [Test]
        public void Given_RentProps_When_Rented_Then_AllFieldsAreClean()
        {
            // Act
            var props = VNodePool.RentProps();

            // Assert — a freshly rented props object has every settable field cleared to null
            Assert.That(props.Text, Is.Null);
            Assert.That(props.Tooltip, Is.Null);
            Assert.That(props.Enabled, Is.Null);
            Assert.That(props.FieldValue, Is.Null);
            Assert.That(props.Slider, Is.Null);
            Assert.That(props.ScrollView, Is.Null);
            Assert.That(props.TextField, Is.Null);
        }

        [Test]
        public void Given_ReturnedProps_When_RentedAgain_Then_ReusesSameInstance()
        {
            // Arrange
            var props = VNodePool.RentProps();
            props.Text = "hello";
            props.Enabled = true;
            VNodePool.ReturnProps(props);

            // Act
            var reused = VNodePool.RentProps();

            // Assert
            Assert.That(reused, Is.SameAs(props));
        }

        [Test]
        public void Given_ReturnedProps_When_RentedAgain_Then_MutatedFieldsAreReset()
        {
            // Arrange
            var props = VNodePool.RentProps();
            props.Text = "hello";
            props.Enabled = true;
            VNodePool.ReturnProps(props);

            // Act
            var reused = VNodePool.RentProps();
            Assume.That(reused, Is.SameAs(props), "Precondition: the same instance was reused");

            // Assert — returning a props object clears its mutated fields back to null on the next rent
            Assert.That(reused.Text, Is.Null);
            Assert.That(reused.Enabled, Is.Null);
        }

        [Test]
        public void Given_ReturnedPropsWithChoices_When_RentedAgain_Then_ChoicesAreReset()
        {
            // Arrange
            var props = VNodePool.RentProps();
            props.Choices = new ChoicesSettings(new List<string> { "A", "B" });
            VNodePool.ReturnProps(props);

            // Act
            var reused = VNodePool.RentProps();
            Assume.That(reused, Is.SameAs(props), "Precondition: the same instance was reused");

            // Assert
            Assert.That(reused.Choices, Is.Null,
                "Choices is reset so a pooled bag does not leak a stale list to the next consumer");
        }

        [Test]
        public void Given_EmptySingleton_When_Returned_Then_NextRentIsNotTheSingleton()
        {
            // Arrange
            VNodePool.ReturnProps(FiberElementProps.Empty);

            // Act
            var props = VNodePool.RentProps();

            // Assert
            Assert.That(props, Is.Not.SameAs(FiberElementProps.Empty),
                "The empty singleton is never pooled");
        }

        [Test]
        public void Given_RentSingleEventArray_When_Rented_Then_HasLengthOne()
        {
            // Act
            var arr = VNodePool.RentSingleEventArray();

            // Assert
            Assert.That(arr.Length, Is.EqualTo(1));
        }

        [Test]
        public void Given_ReturnedEventArray_When_RentedAgain_Then_ReusesSameInstance()
        {
            // Arrange
            var arr = VNodePool.RentSingleEventArray();
            arr[0] = new ClickedBinding { Handler = () => { } };
            VNodePool.ReturnEventArray(arr);

            // Act
            var reused = VNodePool.RentSingleEventArray();

            // Assert
            Assert.That(reused, Is.SameAs(arr));
        }

        [Test]
        public void Given_ReturnedEventArray_When_RentedAgain_Then_ElementIsCleared()
        {
            // Arrange
            var arr = VNodePool.RentSingleEventArray();
            arr[0] = new ClickedBinding { Handler = () => { } };
            VNodePool.ReturnEventArray(arr);

            // Act
            var reused = VNodePool.RentSingleEventArray();
            Assume.That(reused, Is.SameAs(arr), "Precondition: the same array was reused");

            // Assert
            Assert.That(reused[0], Is.Null, "The delegate is cleared on return");
        }

        [Test]
        public void Given_EmptyEventArray_When_Returned_Then_DoesNotThrow()
        {
            // Act + Assert — the empty singleton is not pooled and returning it is harmless
            Assert.DoesNotThrow(() => VNodePool.ReturnEventArray(Array.Empty<FiberEventBinding>()));
        }

        [Test]
        public void Given_RentNodeArray_When_Rented_Then_HasRequestedLength()
        {
            // Act
            var arr = VNodePool.RentNodeArray(5);

            // Assert
            Assert.That(arr.Length, Is.EqualTo(5));
        }

        [Test]
        public void Given_UserArrayNotRentedFromPool_When_ReturnNodeArray_Then_ItIsNotCleared()
        {
            // Arrange — an array the caller owns (e.g. a cached `static readonly VNode[]` passed as children),
            // NOT obtained from RentNodeArray. The recycle path calls ReturnNodeArray on every elem.Children.
            var userArray = new VNode[] { V.Label(text: "a"), V.Label(text: "b") };

            // Act — returning a non-pool-owned array must be a no-op (it is not the pool's to clear/recycle).
            VNodePool.ReturnNodeArray(userArray);

            // Assert — the caller's array is intact (a reused/cached children array would otherwise be wiped).
            Assert.That(userArray[0], Is.Not.Null,
                "A user-owned array (not rented from the pool) must not be cleared by ReturnNodeArray");
        }

        [Test]
        public void Given_ReturnedNodeArray_When_RentedAgain_Then_ReusesSameInstance()
        {
            // Arrange
            var arr = VNodePool.RentNodeArray(3);
            arr[0] = V.Label(text: "a");
            arr[1] = V.Label(text: "b");
            VNodePool.ReturnNodeArray(arr);

            // Act
            var reused = VNodePool.RentNodeArray(3);

            // Assert
            Assert.That(reused, Is.SameAs(arr));
        }

        [Test]
        public void Given_ReturnedNodeArray_When_RentedAgain_Then_ElementsAreCleared()
        {
            // Arrange
            var arr = VNodePool.RentNodeArray(3);
            arr[0] = V.Label(text: "a");
            arr[1] = V.Label(text: "b");
            VNodePool.ReturnNodeArray(arr);

            // Act
            var reused = VNodePool.RentNodeArray(3);
            Assume.That(reused, Is.SameAs(arr), "Precondition: the same array was reused");

            // Assert
            Assert.That(new[] { reused[0], reused[1] }, Is.All.Null);
        }

        [Test]
        public void Given_ReturnsBeyondTheCap_When_RentedBack_Then_SomeAreNewAllocations()
        {
            // Arrange — rent and return more props bags than the pool cap
            var instances = new FiberElementProps[10];
            for (var i = 0; i < instances.Length; i++)
            {
                instances[i] = VNodePool.RentProps();
            }
            foreach (var instance in instances)
            {
                VNodePool.ReturnProps(instance);
            }

            // Act — rent them all back; instances beyond the cap cannot have been pooled
            var rented = new FiberElementProps[10];
            for (var i = 0; i < rented.Length; i++)
            {
                rented[i] = VNodePool.RentProps();
            }

            // Assert — at least the over-cap count are instances that were not in the original set
            var newCount = 0;
            foreach (var r in rented)
            {
                if (Array.IndexOf(instances, r) < 0)
                {
                    newCount++;
                }
            }
            Assert.That(newCount, Is.GreaterThanOrEqualTo(2),
                "The pool caps its size, so rents beyond the cap require new allocations");
        }
    }
}
