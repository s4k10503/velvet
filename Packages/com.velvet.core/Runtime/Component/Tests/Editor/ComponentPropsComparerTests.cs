using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the props-bail predicate <see cref="ComponentPropsComparer.ShallowEquals"/>, the shallow
    /// per-member comparison that decides whether a memoized component can skip a re-render.
    /// <list type="bullet">
    /// <item>The same reference is equal; both-null is equal; null versus non-null is not equal.</item>
    /// <item>Props of differing runtime types are not equal.</item>
    /// <item>Distinct instances are equal when every public member is <c>Object.is</c>-equal, so the
    /// comparison keys on member values, not instance identity.</item>
    /// <item>Any single member that differs makes the props not equal.</item>
    /// <item>String members compare by value, so content-equal strings built at runtime are equal regardless
    /// of instance identity.</item>
    /// <item>Reference-type members other than string compare by identity and the comparison stops there:
    /// distinct instances with equal content are not equal, the same instance is equal.</item>
    /// <item>A value-type member is decided by its own <c>Equals</c> instead, which reads on into what the
    /// member holds — a nested <c>record class</c> of equal content makes the props equal — and the
    /// <c>float</c> and <c>double</c> fields it carries, directly or inside a value type it holds, are
    /// compared by raw bit pattern on top of that.</item>
    /// <item>A props value that is not a props bag — a value type, a string, a collection — is compared as
    /// a whole rather than through a member set.</item>
    /// <item>A bare <c>float</c> or <c>double</c> props value is decided by raw bit pattern rather than by
    /// its own <c>Equals</c>, so <c>+0</c> and <c>-0</c> are a change.</item>
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
        private sealed record Inner(string Value);
        private readonly record struct Wrapper(Inner Held);
        private sealed record NestedRefProps(Wrapper W);
        private readonly record struct FloatBox(float F);
        private sealed record NestedFloatProps(FloatBox B);
        private readonly record struct MixedBox(float F, Inner Held);
        private sealed record MixedProps(MixedBox M);
        private readonly record struct DoubleBox(double D);
        private readonly record struct OuterBox(FloatBox Inner, int N);
        private sealed record NestedTwiceProps(OuterBox O);
        private sealed record NestedDoubleProps(DoubleBox B);
        private sealed record FloatHolder(float F);
        private readonly record struct HolderBox(float F, FloatHolder Held);
        private sealed record HolderProps(HolderBox B);
        private sealed record NullableRefProps(object? Handle);
        private sealed record ClassMemberProps(FloatHolder? Held);

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

        // GREEN_ON_BASE(characterization): the answer a bare string props value already gives.
        // What shows it can fail is IsPropsBag's route past the member walk deleted, so a props value
        // that is not a bag takes that walk too, measured on this branch: eight cases in this fixture
        // redden under it, this one among them.
        [Test]
        public void Given_BareStringProps_When_ContentDiffersAtEqualLength_Then_IsNotEqual()
        {
            // Act + Assert — the lengths match, which is what made this pair compare equal with the guard removed
            Assert.That(ComponentPropsComparer.ShallowEquals("ab", "cd"), Is.False,
                "A bare string props value is compared as a whole rather than through a member set");
        }

        // GREEN_ON_BASE(characterization): the other half of the route above, on a primitive rather than
        // a string; green on both sides for the same reason, and red under the same deletion.
        [Test]
        public void Given_BarePrimitiveProps_When_ValuesDiffer_Then_IsNotEqual()
        {
            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(1, 2), Is.False,
                "A bare primitive props value is compared as a whole rather than through a member set");
        }

        // GREEN_ON_BASE(characterization): the raw-bit answer a bare float props value already gives.
        // The fixture reached that answer through a member and through a value type's leaf, and never
        // with the float as the props value itself. What shows the case can fail is AreEqualObjects'
        // raw-bit float branch deleted so a boxed float falls to its own Equals, measured on this
        // branch: two cases in this fixture redden under it, this one among them.
        [Test]
        public void Given_BareFloatProps_When_OnlyTheZeroSignDiffers_Then_IsNotEqual()
        {
            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(0f, -0f), Is.False,
                "A bare float props value is decided by raw bit pattern rather than by its own Equals");
        }

        // GREEN_ON_BASE(characterization): the raw-bit answer a bare double props value already gives.
        // A double is decided on a branch of its own, so the float case above does not stand in for it:
        // deleting the raw-bit float branch leaves this pair unequal. What shows this case can fail is
        // the raw-bit double branch deleted, measured on this branch: this case is the only one in this
        // fixture that reddens under it.
        [Test]
        public void Given_BareDoubleProps_When_OnlyTheZeroSignDiffers_Then_IsNotEqual()
        {
            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(0d, -0d), Is.False,
                "A bare double props value is decided by raw bit pattern rather than by its own Equals");
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

        // GREEN_ON_BASE(characterization): the identity a reference-type member is decided by.
        // This change routes an object-declared member through ValueEquals rather than straight to
        // ObjectIs.AreEqualObjects, and a reference reaches the same fall-through either way, so the case
        // is green on both sides. What shows it can fail is that fall-through cut to return true,
        // measured on this branch: the two distinct arrays then compare equal, and the bare-list case
        // below with them.
        [Test]
        public void Given_ReferenceTypeMemberWithEqualContent_When_ShallowEquals_Then_IsNotEqual()
        {
            // Arrange — distinct array instances with equal content
            var a = new RefMemberProps(new[] { 1, 2 });
            var b = new RefMemberProps(new[] { 1, 2 });

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.False,
                "A reference-type member is decided by its instance, with no read into its content");
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

        // GREEN_ON_BASE(characterization): how far a value-type member is read, unchanged here.
        // The member holds no floating-point leaf, so the comparison this change adds does not reach it
        // and the member's own Equals still decides it. What shows the case can fail is AreEqual<T>'s
        // value fall-through cut to return false, measured on this branch: seven cases in this fixture
        // redden under it, this one among them.
        [Test]
        public void Given_RecordStructMemberHoldingFreshEqualRecordClass_When_ShallowEquals_Then_IsEqual()
        {
            // Arrange — the nested record class instances are distinct, so a comparison stopping at the
            // struct member cannot call these props equal
            var a = new NestedRefProps(new Wrapper(new Inner("x")));
            var b = new NestedRefProps(new Wrapper(new Inner("x")));

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.True,
                "A value-type member is decided by its own Equals, which reads on into what it holds");
        }

        [Test]
        public void Given_RecordStructMemberDifferingOnlyInZeroSign_When_ShallowEquals_Then_IsNotEqual()
        {
            // Arrange — the same +0/-0 pair the bare float member above distinguishes, one level inside a
            // value-type member
            var a = new NestedFloatProps(new FloatBox(0f));
            var b = new NestedFloatProps(new FloatBox(-0f));

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.False,
                "Object.is raw-bit equality reaches a float inside a value-type member");
        }

        // GREEN_ON_BASE(characterization): the answer a bare record struct props value already gives.
        // The base reached it through the member walk and this change reaches it through the leaf
        // comparison a value type holding a float takes, so the case is green on both sides. What shows
        // it can fail is that leaf comparison deleted, measured on this branch: this case is the only one
        // in the fixture that reddens under it.
        [Test]
        public void Given_BareRecordStructProps_When_AFloatDiffersOnlyInZeroSign_Then_IsNotEqual()
        {
            // Arrange — the pair the wrapped case above uses, passed as the props value itself
            var a = new FloatBox(0f);
            var b = new FloatBox(-0f);

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.False,
                "A bare record struct props value answers a sign flip the way a wrapped one does");
        }

        // GREEN_ON_BASE(characterization): the content a value-type member holding a record class is
        // decided by, which the float leaves this change compares must not displace. The member holds a
        // float as well, so it takes the added comparison and still answers on its own equality first.
        // What shows the case can fail is AreEqual<T>'s value fall-through cut to return false, measured
        // on this branch: seven cases in this fixture redden under it, this one among them.
        [Test]
        public void Given_FloatBearingStructMemberHoldingFreshEqualRecordClass_When_ShallowEquals_Then_IsEqual()
        {
            // Arrange — the nested record class instances are distinct, and the floats agree
            var a = new MixedProps(new MixedBox(1f, new Inner("x")));
            var b = new MixedProps(new MixedBox(1f, new Inner("x")));

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.True,
                "A value-type member holding a float is still decided by its own Equals for what else it holds");
        }

        [Test]
        public void Given_FloatBearingStructMemberDifferingOnlyInZeroSign_When_ShallowEquals_Then_IsNotEqual()
        {
            // Arrange — one shared record class instance, so the sign of the float is the only difference
            var held = new Inner("x");
            var a = new MixedProps(new MixedBox(0f, held));
            var b = new MixedProps(new MixedBox(-0f, held));

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.False,
                "A float leaf beside a reference field still follows Object.is raw-bit equality");
        }

        [Test]
        public void Given_AFloatTwoValueTypesDeepDifferingOnlyInZeroSign_When_ShallowEquals_Then_IsNotEqual()
        {
            // Arrange — the float sits inside a struct inside the struct the member declares, which is
            // what makes the leaf collector descend from one value type into another; where a member's
            // own struct carries the float, the float branch answers it and that descent never runs
            var a = new NestedTwiceProps(new OuterBox(new FloatBox(0f), 1));
            var b = new NestedTwiceProps(new OuterBox(new FloatBox(-0f), 1));

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.False,
                "The raw-bit rule descends through a value type into a value type it holds");
        }

        [Test]
        public void Given_DoubleInsideAValueTypeMemberDifferingOnlyInZeroSign_When_ShallowEquals_Then_IsNotEqual()
        {
            // Arrange — the float case one type wider, so the double half of the leaf test is exercised
            var a = new NestedDoubleProps(new DoubleBox(0d));
            var b = new NestedDoubleProps(new DoubleBox(-0d));

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.False,
                "A double leaf follows Object.is raw-bit equality as a float leaf does");
        }

        // GREEN_ON_BASE(characterization): where the raw-bit rule stops, which this change must not move.
        // A reference held inside a value-type member is decided by its own Equals, and a record class's
        // Equals answers for a float the way IEEE does rather than the way Object.is does, so the pair
        // below is equal through it. What shows the case can fail is the descent's stop at a reference
        // deleted, measured on this branch: this case is the only one in the fixture that reddens.
        [Test]
        public void Given_RecordClassInsideAFloatBearingStructMemberDifferingOnlyInZeroSign_When_ShallowEquals_Then_IsEqual()
        {
            // Arrange — the sign flip sits one reference deeper than the struct's own float
            var a = new HolderProps(new HolderBox(1f, new FloatHolder(0f)));
            var b = new HolderProps(new HolderBox(1f, new FloatHolder(-0f)));

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.True,
                "The raw-bit rule reaches a float inside a value type, and stops at a reference it holds");
        }

        // GREEN_ON_BASE(characterization): the answer two absent object members already give, which the
        // value route this change adds has to keep giving before it reads a runtime type off one. What
        // shows the case can fail is that route's null return cut to false, measured on this branch: this
        // case is the only one in the fixture that reddens.
        [Test]
        public void Given_ObjectMemberAbsentOnBothSides_When_ShallowEquals_Then_IsEqual()
        {
            // Arrange
            var a = new NullableRefProps(null);
            var b = new NullableRefProps(null);

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.True,
                "Two absent object members are equal");
        }

        // GREEN_ON_BASE(characterization): the answer two absent class-typed members already give. The
        // member's type holds a float, so it is the shape a leaf comparison would descend into, and the
        // case is what fails if one ever descends into an absent reference.
        [Test]
        public void Given_ClassMemberHoldingAFloatAbsentOnBothSides_When_ShallowEquals_Then_IsEqual()
        {
            // Arrange
            var a = new ClassMemberProps(null);
            var b = new ClassMemberProps(null);

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.True,
                "Two absent class-typed members are equal");
        }

        // GREEN_ON_BASE(characterization): the reflected shape of a primitive, which the leaf collector's
        // stop at one rests on rather than on a rule of its own. A descent that read this field would
        // arrive back at the same type, and the case is what fails if the runtime stops declaring it.
        [Test]
        public void Given_APrimitiveType_When_ItsInstanceFieldsAreReflected_Then_OneCarriesItsOwnType()
        {
            // Arrange
            const BindingFlags instanceFields =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            // Act
            var carriers = typeof(int).GetFields(instanceFields).Count(f => f.FieldType == typeof(int));

            // Assert
            Assert.That(carriers, Is.GreaterThan(0),
                "A primitive declares a backing field of its own type, so a descent into one would not end");
        }

        // GREEN_ON_BASE(characterization): the editor reset emptying what the comparer caches, which the
        // base already does for the caches it holds. The case reads the set reflectively, so a cache
        // added later comes under it without being named. What shows it can fail is any one Clear
        // dropped, measured on this branch: this case is the only one in the fixture that reddens.
        [Test]
        public void Given_TheTypeKeyedCachesHoldEntries_When_TheEditorResetRuns_Then_NoneStillDoes()
        {
            // Arrange — one comparison of each shape the comparer caches for
            ComponentPropsComparer.ShallowEquals(new SimpleProps("a", 1), new SimpleProps("a", 1));
            ComponentPropsComparer.ShallowEquals(new NestedFloatProps(new FloatBox(1f)), new NestedFloatProps(new FloatBox(1f)));
            var caches = TypeKeyedCaches();
            var filled = caches.Select(c => c.Value.Count > 0).ToArray();

            // Act
            typeof(ComponentPropsComparer)
                .GetMethod("ResetCache", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, null);

            // Assert — the filled state before and the count after, so an empty cache cannot pass for a cleared one
            Assert.That(
                string.Join(",", caches.Select((c, i) => $"{c.Key}:filled={filled[i]},after={c.Value.Count}")),
                Is.EqualTo(string.Join(",", caches.Select(c => $"{c.Key}:filled=True,after=0"))));
        }

        private static KeyValuePair<string, IDictionary>[] TypeKeyedCaches()
            => typeof(ComponentPropsComparer)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
                .Where(f => typeof(IDictionary).IsAssignableFrom(f.FieldType)
                    && f.FieldType.IsGenericType
                    && f.FieldType.GetGenericArguments()[0] == typeof(Type))
                .OrderBy(f => f.Name, StringComparer.Ordinal)
                .Select(f => new KeyValuePair<string, IDictionary>(f.Name, (IDictionary)f.GetValue(null)!))
                .ToArray();

        [Test]
        public void Given_BareStructPropsHoldingFreshEqualRecordClass_When_ShallowEquals_Then_IsEqual()
        {
            // Arrange — the struct a wrapped member of the same type is decided by its own Equals for,
            // passed as the props value itself
            var a = new Wrapper(new Inner("x"));
            var b = new Wrapper(new Inner("x"));

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.True,
                "A bare struct props value is decided by its own Equals, as a struct member is");
        }

        [Test]
        public void Given_BareDecimalProps_When_ValuesDiffer_Then_IsNotEqual()
        {
            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(1.0m, 2.0m), Is.False,
                "A bare decimal props value is decided whole rather than through a member set");
        }

        [Test]
        public void Given_BareGuidProps_When_ValuesDiffer_Then_IsNotEqual()
        {
            // Arrange
            var a = new Guid("00000000-0000-0000-0000-000000000001");
            var b = new Guid("00000000-0000-0000-0000-000000000002");

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.False,
                "A bare Guid props value is decided whole rather than through a member set");
        }

        [Test]
        public void Given_BareListProps_When_ElementsDifferAtEqualLength_Then_IsNotEqual()
        {
            // Arrange — lists of equal length holding different elements
            var a = new List<int> { 1, 2 };
            var b = new List<int> { 3, 4 };

            // Act + Assert
            Assert.That(ComponentPropsComparer.ShallowEquals(a, b), Is.False,
                "A collection props value is decided by its instance rather than through a member set");
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
