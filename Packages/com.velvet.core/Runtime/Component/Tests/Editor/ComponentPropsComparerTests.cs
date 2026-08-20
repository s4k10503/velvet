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
    /// <item>Reference-type members other than string compare by identity and the comparison never recurses:
    /// distinct instances with equal content are not equal, the same instance is equal.</item>
    /// <item>A props value passed with no record wrapper — a bare string, a bare primitive — is compared as a
    /// whole rather than through a member set.</item>
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
        private sealed record NullableFloatProps(float? X);

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

        private sealed record StructMemberProps(Point Location);

        // A record's own EqualityContract getter is synthesized as non-public, so it never reaches
        // the comparer's reflected member set through BindingFlags.Public alone; this type carries a
        // public member of the same name to exercise the comparer's explicit name-based exclusion.
        private sealed class ExplicitEqualityContractMember
        {
            public int Value { get; init; }
            public string EqualityContract { get; init; } = string.Empty;
        }

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

        // GREEN_ON_BASE(characterization): this case adds no production change — it pins the guard that
        // routes an unwrapped props value away from the member-set comparison, which the base already
        // has — so it is green on both sides. What shows it can fail is that guard deleted, measured:
        // this case and the primitive one below are the only two in the fixture that redden.
        [Test]
        public void Given_BareStringProps_When_ContentDiffersAtEqualLength_Then_IsNotEqual()
        {
            // Act + Assert — the lengths match, which is what made this pair compare equal with the guard removed
            Assert.That(ComponentPropsComparer.ShallowEquals("ab", "cd"), Is.False,
                "A bare string props value is compared as a whole rather than through a member set");
        }

        // GREEN_ON_BASE(characterization): the other half of the guard above, on a primitive rather than a
        // string; green on both sides for the same reason, and red under the same deletion.
        [Test]
        public void Given_BarePrimitiveProps_When_ValuesDiffer_Then_IsNotEqual()
        {
            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(1, 2), Is.False,
                "A bare primitive props value is compared as a whole rather than through a member set");
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

        [Test]
        public void Given_StructMemberWithEqualValues_When_ShallowEquals_Then_IsEqual()
        {
            // Arrange — distinct struct instances with equal field values
            var a = new StructMemberProps(new Point(1, 2));
            var b = new StructMemberProps(new Point(1, 2));

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.True);
        }

        [Test]
        public void Given_StructMemberWithDifferentValues_When_ShallowEquals_Then_IsNotEqual()
        {
            // Arrange
            var a = new StructMemberProps(new Point(1, 2));
            var b = new StructMemberProps(new Point(1, 3));

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.False);
        }

        [Test]
        public void Given_MembersDifferOnlyInEqualityContract_When_ShallowEquals_Then_IsEqual()
        {
            // Arrange — the EqualityContract member is excluded by name, so it must not affect the outcome
            var a = new ExplicitEqualityContractMember { Value = 1, EqualityContract = "A" };
            var b = new ExplicitEqualityContractMember { Value = 1, EqualityContract = "B" };

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.True);
        }

        [Test]
        public void Given_ObjectMemberHoldingEqualValueBoxedFloatsInDistinctBoxes_When_ShallowEquals_Then_IsEqual()
        {
            // Arrange — two separate boxing conversions of the same float value
            var a = new RefMemberProps(1.5f);
            var b = new RefMemberProps(1.5f);
            Assume.That(ReferenceEquals(a.Handle, b.Handle), Is.False, "Precondition: the boxed floats are distinct instances");

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.True,
                "An object-declared member holding a boxed float compares by unboxed value, not by box identity");
        }

        [Test]
        public void Given_ObjectMemberHoldingEqualContentDistinctStringInstances_When_ShallowEquals_Then_IsEqual()
        {
            // Arrange — distinct string instances with equal content, held in an object-declared member
            var a = new RefMemberProps(string.Concat("i", "d"));
            var b = new RefMemberProps(string.Concat("i", "d"));
            Assume.That(ReferenceEquals(a.Handle, b.Handle), Is.False, "Precondition: the string instances are distinct");

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.True,
                "An object-declared member holding a string compares by content, matching the reflection path");
        }

        [Test]
        public void Given_ObjectMemberHoldingBoxedFloatNaNBothSides_When_ShallowEquals_Then_IsEqual()
        {
            // Arrange — two separate boxing conversions of NaN
            var a = new RefMemberProps(float.NaN);
            var b = new RefMemberProps(float.NaN);

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.True,
                "An object-declared member holding boxed NaN is Object.is-equal to itself, matching the reflection path");
        }

        [Test]
        public void Given_NullableFloatMemberPositiveZeroVsNegativeZero_When_ShallowEquals_Then_IsNotEqual()
        {
            // Arrange — Object.is distinguishes +0 from -0 through a Nullable<float> member
            var a = new NullableFloatProps(0f);
            var b = new NullableFloatProps(-0f);

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.False);
        }

        [Test]
        public void Given_NullableFloatMemberBothNull_When_ShallowEquals_Then_IsEqual()
        {
            // Arrange
            var a = new NullableFloatProps(null);
            var b = new NullableFloatProps(null);

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.True);
        }

        [Test]
        public void Given_NullableFloatMemberNaNBothSides_When_ShallowEquals_Then_IsEqual()
        {
            // Arrange
            var a = new NullableFloatProps(float.NaN);
            var b = new NullableFloatProps(float.NaN);

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.True);
        }
    }
}
