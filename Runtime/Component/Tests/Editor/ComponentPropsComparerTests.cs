using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the props-bail predicate <see cref="ComponentPropsComparer.ShallowEquals"/>, the shallow
    /// per-property comparison that decides whether a memoized component can skip a re-render.
    /// <list type="bullet">
    /// <item>The same reference is equal; both-null is equal; null versus non-null is not equal.</item>
    /// <item>Props of differing runtime types are not equal.</item>
    /// <item>Distinct instances are equal when every public member is <c>Object.is</c>-equal, so the
    /// comparison keys on member values, not instance identity.</item>
    /// <item>Any single member that differs makes the props not equal.</item>
    /// <item>String members compare by value, so content-equal strings built at runtime are equal regardless
    /// of instance identity.</item>
    /// <item>Reference-type members compare by identity and the comparison never recurses: distinct
    /// instances with equal content are not equal, the same instance is equal.</item>
    /// <item>Float members follow <c>Object.is</c> raw-bit equality: <c>NaN</c> equals itself and <c>+0</c>
    /// does not equal <c>-0</c>.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ComponentPropsComparerTests
    {
        private sealed record SimpleProps(string Id, int Value);
        private sealed record RefMemberProps(object Handle);
        private sealed record FloatProps(float X);

        [Test]
        public void Given_SameReference_When_ShallowEquals_Then_IsEqual()
        {
            // Arrange
            var p = new SimpleProps("a", 1);

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(p, p), Is.True);
        }

        [Test]
        public void Given_BothNull_When_ShallowEquals_Then_IsEqual()
        {
            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(null, null), Is.True);
        }

        [Test]
        public void Given_NullVsNonNull_When_ShallowEquals_Then_IsNotEqual()
        {
            // Act + Assert — not equal in either argument order
            Assert.That(
                new[]
                {
                    ComponentPropsComparer.ShallowEquals(null, new SimpleProps("a", 1)),
                    ComponentPropsComparer.ShallowEquals(new SimpleProps("a", 1), null),
                },
                Is.All.False);
        }

        [Test]
        public void Given_DifferentRuntimeTypes_When_ShallowEquals_Then_IsNotEqual()
        {
            // Act + Assert
            Assert.That(
                ComponentPropsComparer.ShallowEquals(new SimpleProps("a", 1), new FloatProps(1f)),
                Is.False);
        }

        [Test]
        public void Given_DistinctInstancesWithEqualMembers_When_ShallowEquals_Then_IsEqual()
        {
            // Arrange
            var a = new SimpleProps("a", 1);
            var b = new SimpleProps("a", 1);
            Assume.That(ReferenceEquals(a, b), Is.False, "Precondition: the instances are distinct");

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.True,
                "Equal members compare shallow-equal under Object.is");
        }

        [Test]
        public void Given_OneMemberDiffers_When_ShallowEquals_Then_IsNotEqual()
        {
            // Arrange
            var a = new SimpleProps("a", 1);
            var b = new SimpleProps("a", 2);

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.False);
        }

        [Test]
        public void Given_StringMemberWithEqualContent_When_ShallowEquals_Then_IsEqual()
        {
            // Arrange — a dynamically built string instance with content equal to a literal member
            var dynamicId = string.Concat("i", "d");
            var a = new SimpleProps("id", 1);
            var b = new SimpleProps(dynamicId, 1);
            Assume.That(ReferenceEquals(a.Id, b.Id), Is.False, "Precondition: the string members are distinct instances");

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.True,
                "String members compare by value, not by instance identity");
        }

        [Test]
        public void Given_ReferenceTypeMemberWithEqualContent_When_ShallowEquals_Then_IsNotEqual()
        {
            // Arrange — distinct array instances with equal content
            var a = new RefMemberProps(new[] { 1, 2 });
            var b = new RefMemberProps(new[] { 1, 2 });

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.False,
                "Reference-type members compare by identity and the comparison never recurses into content");
        }

        [Test]
        public void Given_ReferenceTypeMemberSameInstance_When_ShallowEquals_Then_IsEqual()
        {
            // Arrange
            var handle = new object();
            var a = new RefMemberProps(handle);
            var b = new RefMemberProps(handle);

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.True,
                "The same reference member is Object.is-equal");
        }

        [Test]
        public void Given_FloatMemberNaN_When_ShallowEquals_Then_IsEqualToItself()
        {
            // Arrange — Object.is treats NaN as equal to NaN, unlike IEEE ==
            var a = new FloatProps(float.NaN);
            var b = new FloatProps(float.NaN);

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.True);
        }

        [Test]
        public void Given_FloatMemberPositiveZeroVsNegativeZero_When_ShallowEquals_Then_IsNotEqual()
        {
            // Arrange — Object.is distinguishes +0 from -0, unlike IEEE ==
            var a = new FloatProps(0f);
            var b = new FloatProps(-0f);

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.False);
        }
    }
}
