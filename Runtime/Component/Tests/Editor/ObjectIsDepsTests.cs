using System.Collections.Generic;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="ObjectIs.AreEqualDeps"/>, the single dependency-array comparer shared by every
    /// deps-taking hook (UseEffect / UseLayoutEffect / UseInsertionEffect / UseCallback /
    /// UseImperativeHandle / UseBlocker / V.Memoized) and the compiler-emitted component memo.
    /// <list type="bullet">
    /// <item>The same array reference is equal; both-null is equal; one-null is not equal; a length mismatch
    /// is not equal.</item>
    /// <item>Each element pair is compared with <c>Object.is</c> semantics, with no recursion into list or
    /// record contents.</item>
    /// <item>Value-type elements (int, enum) and strings compare by value.</item>
    /// <item>Reference-type elements compare by identity: a fresh-but-content-equal record or list counts as
    /// changed, while the same instance counts as unchanged.</item>
    /// <item>Float elements follow raw-bit equality: <c>NaN</c> equals itself and <c>+0</c> does not equal
    /// <c>-0</c>.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ObjectIsDepsTests
    {
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
    }
}
