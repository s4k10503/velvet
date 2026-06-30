using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the generic <see cref="ObjectIs.AreEqual{T}"/> (the comparer UseState / UseStore /
    /// Provider use) compares <c>string</c> by VALUE, matching JS <c>Object.is("a","a") === true</c> (strings
    /// are primitives) and the boxed <see cref="ObjectIs.AreEqualObjects"/> props path. Without this, a
    /// dynamically-built but content-equal string (interpolation / concat / Format) would never bail and would
    /// force a re-render every time, and the two equality paths (generic vs boxed) would disagree.
    /// </summary>
    [TestFixture]
    internal sealed class ObjectIsGenericStringTests
    {
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
    }
}
