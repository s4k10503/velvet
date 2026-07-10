using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="ObjectIs.AreEqual{T}"/> for nullable value types — the comparer behind the
    /// UseState setter bail, the Store SetState no-op check, and the default UseStore / Select comparer.
    /// A <c>Nullable&lt;T&gt;</c> is a value type, but its lifted <c>default(T) == null</c> comparison is
    /// true, so a null-test misrouted it to the reference-identity branch — where boxing each operand
    /// yields a fresh object every call, making two equal values never compare equal. Every store
    /// notification then looked like a change to an <c>int?</c>-selected slice, re-rendering subscribers
    /// on unrelated updates. Nullable values must compare by value, like every other value type.
    /// </summary>
    [TestFixture]
    internal sealed class ObjectIsNullableValueTests
    {
        [Test]
        public void Given_EqualNullableInts_When_AreEqualGeneric_Then_AreEqual()
        {
            // Arrange — two independently boxed but numerically equal nullable ints.
            int? a = 5;
            int? b = 5;

            // Act
            var equal = ObjectIs.AreEqual(a, b);

            // Assert — equal values bail, so an unchanged int?-selected store slice does not re-render.
            Assert.That(equal, Is.True);
        }

        [Test]
        public void Given_BothNullNullableInts_When_AreEqualGeneric_Then_AreEqual()
        {
            // Arrange
            int? a = null;
            int? b = null;

            // Act
            var equal = ObjectIs.AreEqual(a, b);

            // Assert
            Assert.That(equal, Is.True);
        }

        [Test]
        public void Given_NullAndValuedNullableInt_When_AreEqualGeneric_Then_AreNotEqual()
        {
            // Arrange
            int? a = null;
            int? b = 5;

            // Act
            var equal = ObjectIs.AreEqual(a, b);

            // Assert
            Assert.That(equal, Is.False);
        }

        [Test]
        public void Given_DifferentNullableInts_When_AreEqualGeneric_Then_AreNotEqual()
        {
            // Arrange
            int? a = 5;
            int? b = 6;

            // Act
            var equal = ObjectIs.AreEqual(a, b);

            // Assert
            Assert.That(equal, Is.False);
        }

        [Test]
        public void Given_EqualNullableUserStructs_When_AreEqualGeneric_Then_AreEqual()
        {
            // Arrange — a plain struct with no custom Equals, wrapped in Nullable.
            Point? a = new Point(1, 2);
            Point? b = new Point(1, 2);

            // Act
            var equal = ObjectIs.AreEqual(a, b);

            // Assert
            Assert.That(equal, Is.True);
        }

        private readonly struct Point
        {
            public Point(int x, int y)
            {
                X = x;
                Y = y;
            }

            public int X { get; }
            public int Y { get; }
        }
    }
}
