using System.Collections.Generic;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>Object.is</c>-style comparers in <see cref="ObjectIs"/>: <see cref="ObjectIs.AreEqualDeps"/>,
    /// the single dependency-array comparer shared by every deps-taking hook (UseEffect / UseLayoutEffect /
    /// UseInsertionEffect / UseCallback / UseImperativeHandle / UseBlocker / V.Memoized) and the compiler-emitted
    /// component memo; and the generic <see cref="ObjectIs.AreEqual{T}"/> used by the UseState setter bail, the
    /// Store SetState no-op check, and the default UseStore / Select comparer.
    /// <list type="bullet">
    /// <item>Deps arrays: the same array reference is equal; both-null is equal; one-null is not equal; a length
    /// mismatch is not equal.</item>
    /// <item>Deps elements are compared pairwise with <c>Object.is</c> semantics, with no recursion into list or
    /// record contents.</item>
    /// <item>Value-type elements (int, enum) and strings compare by value, matching JS <c>Object.is("a","a")</c>;
    /// without the string special case, a dynamically-built but content-equal string would never bail and would
    /// force a re-render every time.</item>
    /// <item>Reference-type elements compare by identity: a fresh-but-content-equal record or list counts as
    /// changed, while the same instance counts as unchanged.</item>
    /// <item>Float elements follow raw-bit equality: <c>NaN</c> equals itself and <c>+0</c> does not equal
    /// <c>-0</c>.</item>
    /// <item>Nullable value types compare by value: a lifted <c>default(T) == null</c> check would otherwise
    /// misroute them to the reference-identity branch, where boxing each operand yields a fresh object every
    /// call and makes two equal values never compare equal — turning every store notification into an apparent
    /// change for an <c>int?</c>-selected slice and re-rendering subscribers on unrelated updates.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ObjectIsTests
    {
        #region AreEqualDeps

        private sealed record DepRec(string Value);

        private enum Color { Red, Green }

        [Test]
        public void Given_SameArrayReference_When_AreEqualDeps_Then_AreEqual()
        {
            // Arrange
            var deps = new object[] { 1, "a" };

            // Act + Assert
            Assert.That(ObjectIs.AreEqualDeps(deps, deps), Is.True);
        }

        [Test]
        public void Given_BothNull_When_AreEqualDeps_Then_AreEqual()
        {
            // Act + Assert
            Assert.That(ObjectIs.AreEqualDeps(null, null), Is.True);
        }

        [Test]
        public void Given_OneNull_When_AreEqualDeps_Then_AreNotEqual()
        {
            // Act + Assert — not equal in either argument order
            Assert.That(
                new[]
                {
                    ObjectIs.AreEqualDeps(null, new object[] { 1 }),
                    ObjectIs.AreEqualDeps(new object[] { 1 }, null),
                },
                Is.All.False);
        }

        [Test]
        public void Given_LengthMismatch_When_AreEqualDeps_Then_AreNotEqual()
        {
            // Act + Assert
            Assert.That(ObjectIs.AreEqualDeps(new object[] { 1 }, new object[] { 1, 2 }), Is.False);
        }

        [Test]
        public void Given_EqualIntElements_When_AreEqualDeps_Then_AreEqual()
        {
            // Act + Assert
            Assert.That(ObjectIs.AreEqualDeps(new object[] { 42 }, new object[] { 42 }), Is.True);
        }

        [Test]
        public void Given_DifferentIntElements_When_AreEqualDeps_Then_AreNotEqual()
        {
            // Act + Assert
            Assert.That(ObjectIs.AreEqualDeps(new object[] { 42 }, new object[] { 99 }), Is.False);
        }

        [Test]
        public void Given_EqualEnumElements_When_AreEqualDeps_Then_AreEqual()
        {
            // Act + Assert
            Assert.That(
                ObjectIs.AreEqualDeps(new object[] { Color.Red }, new object[] { Color.Red }), Is.True);
        }

        [Test]
        public void Given_DifferentEnumElements_When_AreEqualDeps_Then_AreNotEqual()
        {
            // Act + Assert
            Assert.That(
                ObjectIs.AreEqualDeps(new object[] { Color.Red }, new object[] { Color.Green }), Is.False);
        }

        [Test]
        public void Given_DistinctStringInstancesEqualContent_When_AreEqualDeps_Then_AreEqual()
        {
            // Arrange — two runtime-built instances with identical content
            var a = "val" + 1.ToString();
            var b = "val" + 1.ToString();
            Assume.That(ReferenceEquals(a, b), Is.False, "Precondition: the string instances are distinct");

            // Act + Assert
            Assert.That(ObjectIs.AreEqualDeps(new object[] { a }, new object[] { b }), Is.True,
                "Strings compare by value");
        }

        [Test]
        public void Given_FreshRecordInstanceSameContent_When_AreEqualDeps_Then_AreNotEqual()
        {
            // Act + Assert — a record reconstructed with identical content is a changed dep (by-reference)
            Assert.That(
                ObjectIs.AreEqualDeps(new object[] { new DepRec("x") }, new object[] { new DepRec("x") }),
                Is.False);
        }

        [Test]
        public void Given_SameRecordReference_When_AreEqualDeps_Then_AreEqual()
        {
            // Arrange
            var rec = new DepRec("x");

            // Act + Assert
            Assert.That(ObjectIs.AreEqualDeps(new object[] { rec }, new object[] { rec }), Is.True);
        }

        [Test]
        public void Given_FreshListInstanceSameContent_When_AreEqualDeps_Then_AreNotEqual()
        {
            // Act + Assert — no structural recursion: a fresh list with identical elements is a changed dep
            Assert.That(
                ObjectIs.AreEqualDeps(
                    new object[] { new List<int> { 1, 2 } }, new object[] { new List<int> { 1, 2 } }),
                Is.False);
        }

        [Test]
        public void Given_FloatNaN_When_AreEqualDeps_Then_AreEqual()
        {
            // Act + Assert — raw-bit equality treats NaN as equal to NaN, unlike IEEE ==
            Assert.That(
                ObjectIs.AreEqualDeps(new object[] { float.NaN }, new object[] { float.NaN }), Is.True);
        }

        [Test]
        public void Given_FloatSignedZero_When_AreEqualDeps_Then_AreNotEqual()
        {
            // Act + Assert — raw-bit equality distinguishes +0 from -0, unlike IEEE ==
            Assert.That(ObjectIs.AreEqualDeps(new object[] { 0f }, new object[] { -0f }), Is.False);
        }

        #endregion

        #region AreEqual<T> — generic string value semantics

        private sealed record Rec(string Value);

        [Test]
        public void Given_DistinctStringInstancesEqualContent_When_AreEqualGeneric_Then_AreEqual()
        {
            // Arrange — two runtime-built instances with identical content (not interned to the same reference).
            var a = "val" + 1.ToString();
            var b = "val" + 1.ToString();
            Assume.That(ReferenceEquals(a, b), Is.False, "Precondition: the string instances are distinct");

            // Act + Assert
            Assert.That(ObjectIs.AreEqual(a, b), Is.True,
                "AreEqual<string> compares strings by value (Object.is parity), like the boxed props path");
        }

        [Test]
        public void Given_DifferentStrings_When_AreEqualGeneric_Then_AreNotEqual()
        {
            // Act + Assert
            Assert.That(ObjectIs.AreEqual("a", "b"), Is.False);
        }

        [Test]
        public void Given_FreshRecordInstanceSameContent_When_AreEqualGeneric_Then_AreNotEqual()
        {
            // Act + Assert — non-string reference types still compare by reference identity (a fresh-but-equal
            // record is a change), unchanged by the string special case.
            Assert.That(ObjectIs.AreEqual(new Rec("x"), new Rec("x")), Is.False);
        }

        #endregion

        #region AreEqual<T> — nullable value types

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

        #endregion
    }
}
