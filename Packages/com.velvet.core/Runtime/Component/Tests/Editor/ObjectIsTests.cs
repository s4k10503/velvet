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
    /// <item>Deps elements are compared pairwise on each element's runtime type. A reference element other
    /// than string is decided by its instance alone, with no recursion into a list's or a <c>record class</c>'s
    /// contents; a value element is decided by its own <c>Equals</c>, which does read what it holds.</item>
    /// <item>Value-type elements (int, enum) and strings compare by value, matching JS <c>Object.is("a","a")</c>;
    /// without the string special case, a dynamically-built but content-equal string would never bail and would
    /// force a re-render every time.</item>
    /// <item>Reference-type elements other than string compare by identity: a fresh-but-content-equal
    /// <c>record class</c> or list counts as changed, while the same instance counts as unchanged. A
    /// <c>record struct</c> element takes the value branch instead, so a fresh one of equal content counts
    /// as unchanged — and that branch reads on into what the element holds, a nested <c>record class</c>
    /// included.</item>
    /// <item>The two overloads read the branch from different places — <see cref="ObjectIs.AreEqual{T}"/>
    /// from the static <c>T</c>, <see cref="ObjectIs.AreEqualObjects"/> from the runtime type — and an
    /// operand whose static type is <c>object</c> is where that separates their answers, for a boxed value
    /// type and for a rebuilt string alike.</item>
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

        private readonly record struct DepRecStruct(int Value);

        private readonly record struct NestingStruct(DepRec Held);

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

        // GREEN_ON_BASE(characterization): this branch changes no production code — it says which kind of
        // record the arrangement builds, where the bare word covered a struct it does not describe — so the
        // case is green on both sides. What shows it can fail is the reference fall-through of
        // AreEqualObjects cut to return true, measured: this case reddens beside the fresh-list case.
        [Test]
        public void Given_FreshRecordInstanceSameContent_When_AreEqualDeps_Then_AreNotEqual()
        {
            // Act + Assert — a record class reconstructed with identical content is a changed dep
            Assert.That(
                ObjectIs.AreEqualDeps(new object[] { new DepRec("x") }, new object[] { new DepRec("x") }),
                Is.False);
        }

        // GREEN_ON_BASE(characterization): this case adds no production change — it pins the value branch a
        // boxed element takes, which the base already has — so it is green on both sides. What shows it can
        // fail is that branch of AreEqualObjects cut to return false, measured: this case reddens beside the
        // int and enum element cases, the nested case below and the boxed overload-split case, which share
        // the branch.
        [Test]
        public void Given_FreshRecordStructElementSameContent_When_AreEqualDeps_Then_AreEqual()
        {
            // Act + Assert — a record struct element boxes into the array, so the value branch decides it
            // through the record's synthesized field-wise equality
            Assert.That(
                ObjectIs.AreEqualDeps(
                    new object[] { new DepRecStruct(1) },
                    new object[] { new DepRecStruct(1) }),
                Is.True);
        }

        // GREEN_ON_BASE(characterization): this case adds no production change — it pins how far the value
        // branch reads, which the base already decides this way — so it is green on both sides. What shows
        // it can fail is that branch of AreEqualObjects cut to return false, measured: this case reddens
        // beside the int, enum and bare record struct element cases and the boxed overload-split case,
        // which share the branch.
        [Test]
        public void Given_RecordStructElementHoldingFreshEqualRecordClass_When_AreEqualDeps_Then_AreEqual()
        {
            // Arrange — the nested record class instances are distinct, so a comparison stopping at the
            // boxed element cannot call these deps equal
            var a = new object[] { new NestingStruct(new DepRec("x")) };
            var b = new object[] { new NestingStruct(new DepRec("x")) };

            // Act + Assert
            Assert.That(ObjectIs.AreEqualDeps(a, b), Is.True,
                "The value branch is the element's own Equals, which reads on into what it holds");
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

        // GREEN_ON_BASE(characterization): the same wording correction as the deps case above, on the
        // generic overload — green on both sides. What shows it can fail is AreEqual<T>'s reference branch
        // cut to EqualityComparer<T>.Default, measured: this case reddens beside the overload-split case.
        [Test]
        public void Given_FreshRecordInstanceSameContent_When_AreEqualGeneric_Then_AreNotEqual()
        {
            // Act + Assert — non-string reference types still compare by reference identity (a fresh-but-equal
            // record class is a change), unchanged by the string special case.
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

        #region AreEqual<T> versus AreEqualObjects

        // GREEN_ON_BASE(characterization): this case adds no production change — it pins the split the base
        // already has between the two overloads — so it is green on both sides. What shows it can fail is
        // AreEqual<T>'s reference guard narrowed to exclude object, measured: this case and the string one
        // below are the only two in the fixture that redden.
        [Test]
        public void Given_ValueTypeErasedToObject_When_ComparedBothWays_Then_TheTwoOverloadsDisagree()
        {
            // Arrange — one value in two boxes, so the static type is object where the runtime type is not
            object a = new DepRecStruct(1);
            object b = new DepRecStruct(1);

            // Act + Assert — the generic overload branches on the static type and the boxed one on the
            // runtime type, which is why rerouting AreEqualObjects through AreEqual<T> would change answers
            Assert.That(
                (ObjectIs.AreEqual(a, b), ObjectIs.AreEqualObjects(a, b)),
                Is.EqualTo((false, true)));
        }

        // GREEN_ON_BASE(characterization): this case adds no production change — it pins the same split on
        // the other operand class the base already splits, a string rather than a boxed value type — so it
        // is green on both sides. What shows it can fail is AreEqualObjects' string branch deleted,
        // measured: this case reddens beside the string element cases, and the boxed-value-type case above
        // stays green under that cut.
        [Test]
        public void Given_StringErasedToObject_When_ComparedBothWays_Then_TheTwoOverloadsDisagree()
        {
            // Arrange — a run-time-built string held in an object, so the static type is object where the
            // runtime type is string
            object a = string.Concat("va", "l1");
            object b = string.Concat("val", "1");

            // Act + Assert — AreEqual<object> never reaches its string branch, because that branch tests T
            Assert.That(
                (ObjectIs.AreEqual(a, b), ObjectIs.AreEqualObjects(a, b)),
                Is.EqualTo((false, true)));
        }

        #endregion
    }
}
