using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds the comparer the source generator emits for a <c>[Component(Memoize = true)]</c> props type
    /// against the reflection walk, over the shapes <see cref="ComponentPropsComparerArmParityTests"/> holds
    /// the two older arms against. A type declines the generator where it holds a struct from another
    /// assembly, so <c>Rect</c> is not among them.
    /// </summary>
    [TestFixture]
    internal sealed class ComponentPropsComparerGeneratedParityTests
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;

        private static readonly MethodInfo ReflectionWalk =
            typeof(ComponentPropsComparer).GetMethod("CompareMembersByReflection", Hidden)!;

        internal readonly record struct FloatPair(float A, float B);
        internal readonly record struct DoublePair(double A, double B);
        internal readonly record struct IntPair(int A, int B);
        internal readonly record struct OuterPair(FloatPair Inner, int N);
        internal sealed record Leaf(string V);
        internal sealed record FloatLeafClass(float F);
        internal readonly record struct HoldsLeaf(Leaf L);
        internal readonly record struct MixedFloat(float F, Leaf L);
        internal readonly record struct NullableLeaf(float? F, int N);
        internal readonly record struct HolderFloat(float F, FloatLeafClass Held);

        // An Equals answering for less than the type holds, so the two sides can disagree over whether
        // they carry a floating-point leaf while the struct calls them equal.
        internal readonly struct LooseNullable
        {
            public LooseNullable(float? f) => F = f;

            public float? F { get; }

            public override bool Equals(object? obj) => obj is LooseNullable;

            public override int GetHashCode() => 0;
        }

        internal sealed record SimpleBag(string Id, int Value);
        internal sealed record FloatBag(float X);
        internal sealed record StructBag(FloatPair P);
        internal sealed record NullableStructBag(FloatPair? P);
        internal sealed record ObjectBag(object Held);
        internal sealed record DecimalBag(decimal D);
        internal sealed record IntBag(IntPair P);
        internal sealed record LeafBag(HoldsLeaf H);
        internal sealed record MixedBag(MixedFloat M);
        internal sealed record NullableLeafBag(NullableLeaf L);
        internal sealed record LooseBag(LooseNullable L);
        internal sealed record OuterBag(OuterPair O);
        internal sealed record DoubleBag(DoublePair D);
        internal sealed record HolderBag(HolderFloat H);
        internal sealed record ClassMemberBag(FloatLeafClass? L);

        [Component(Memoize = true, Compiler = false)]
        internal static VNode RenderSimpleBag(SimpleBag props) => V.Label(name: "SimpleBag");
        [Component(Memoize = true, Compiler = false)]
        internal static VNode RenderFloatBag(FloatBag props) => V.Label(name: "FloatBag");
        [Component(Memoize = true, Compiler = false)]
        internal static VNode RenderStructBag(StructBag props) => V.Label(name: "StructBag");
        [Component(Memoize = true, Compiler = false)]
        internal static VNode RenderNullableStructBag(NullableStructBag props) => V.Label(name: "NullableStructBag");
        [Component(Memoize = true, Compiler = false)]
        internal static VNode RenderObjectBag(ObjectBag props) => V.Label(name: "ObjectBag");
        [Component(Memoize = true, Compiler = false)]
        internal static VNode RenderDecimalBag(DecimalBag props) => V.Label(name: "DecimalBag");
        [Component(Memoize = true, Compiler = false)]
        internal static VNode RenderIntBag(IntBag props) => V.Label(name: "IntBag");
        [Component(Memoize = true, Compiler = false)]
        internal static VNode RenderLeafBag(LeafBag props) => V.Label(name: "LeafBag");
        [Component(Memoize = true, Compiler = false)]
        internal static VNode RenderMixedBag(MixedBag props) => V.Label(name: "MixedBag");
        [Component(Memoize = true, Compiler = false)]
        internal static VNode RenderNullableLeafBag(NullableLeafBag props) => V.Label(name: "NullableLeafBag");
        [Component(Memoize = true, Compiler = false)]
        internal static VNode RenderLooseBag(LooseBag props) => V.Label(name: "LooseBag");
        [Component(Memoize = true, Compiler = false)]
        internal static VNode RenderOuterBag(OuterBag props) => V.Label(name: "OuterBag");
        [Component(Memoize = true, Compiler = false)]
        internal static VNode RenderDoubleBag(DoubleBag props) => V.Label(name: "DoubleBag");
        [Component(Memoize = true, Compiler = false)]
        internal static VNode RenderHolderBag(HolderBag props) => V.Label(name: "HolderBag");
        [Component(Memoize = true, Compiler = false)]
        internal static VNode RenderClassMemberBag(ClassMemberBag props) => V.Label(name: "ClassMemberBag");

        private readonly record struct Pair(string Label, object Prev, object Next);

        private static Pair[] PropsPairs()
        {
            var shared = new Leaf("x");
            return new[]
            {
                new Pair("equal-members", new SimpleBag("a", 1), new SimpleBag("a", 1)),
                new Pair("differing-member", new SimpleBag("a", 1), new SimpleBag("a", 2)),
                new Pair("rebuilt-string", new SimpleBag(new string('a', 3), 1), new SimpleBag(new string('a', 3), 1)),
                new Pair("float-zero-sign", new FloatBag(0f), new FloatBag(-0f)),
                new Pair("float-nan", new FloatBag(float.NaN), new FloatBag(float.NaN)),
                new Pair("struct-zero-sign", new StructBag(new FloatPair(0f, 1f)), new StructBag(new FloatPair(-0f, 1f))),
                new Pair("struct-equal", new StructBag(new FloatPair(1f, 2f)), new StructBag(new FloatPair(1f, 2f))),
                new Pair("struct-nan", new StructBag(new FloatPair(float.NaN, 1f)), new StructBag(new FloatPair(float.NaN, 1f))),
                new Pair("nullable-struct-zero-sign",
                    new NullableStructBag(new FloatPair(0f, 1f)), new NullableStructBag(new FloatPair(-0f, 1f))),
                new Pair("nullable-struct-both-null", new NullableStructBag(null), new NullableStructBag(null)),
                new Pair("nullable-struct-one-null", new NullableStructBag(new FloatPair(0f, 1f)), new NullableStructBag(null)),
                new Pair("boxed-struct-zero-sign",
                    new ObjectBag(new FloatPair(0f, 1f)), new ObjectBag(new FloatPair(-0f, 1f))),
                new Pair("boxed-float-zero-sign", new ObjectBag(0f), new ObjectBag(-0f)),
                new Pair("boxed-equal-ints", new ObjectBag(1), new ObjectBag(1)),
                new Pair("distinct-references", new ObjectBag(new object()), new ObjectBag(new object())),
                new Pair("decimal-member", new DecimalBag(1.0m), new DecimalBag(2.0m)),
                new Pair("int-struct-member", new IntBag(new IntPair(1, 2)), new IntBag(new IntPair(1, 2))),
                new Pair("struct-holding-record", new LeafBag(new HoldsLeaf(new Leaf("x"))), new LeafBag(new HoldsLeaf(new Leaf("x")))),
                new Pair("mixed-zero-sign", new MixedBag(new MixedFloat(0f, shared)), new MixedBag(new MixedFloat(-0f, shared))),
                new Pair("mixed-fresh-record",
                    new MixedBag(new MixedFloat(1f, new Leaf("x"))), new MixedBag(new MixedFloat(1f, new Leaf("x")))),
                new Pair("nullable-leaf-empty",
                    new NullableLeafBag(new NullableLeaf(null, 1)), new NullableLeafBag(new NullableLeaf(null, 1))),
                new Pair("nullable-leaf-zero-sign",
                    new NullableLeafBag(new NullableLeaf(0f, 1)), new NullableLeafBag(new NullableLeaf(-0f, 1))),
                new Pair("loose-nullable-one-empty",
                    new LooseBag(new LooseNullable(0f)), new LooseBag(new LooseNullable(null))),
                new Pair("loose-nullable-zero-sign",
                    new LooseBag(new LooseNullable(0f)), new LooseBag(new LooseNullable(-0f))),
                new Pair("double-zero-sign",
                    new DoubleBag(new DoublePair(0d, 1d)), new DoubleBag(new DoublePair(-0d, 1d))),
                new Pair("double-equal",
                    new DoubleBag(new DoublePair(1d, 2d)), new DoubleBag(new DoublePair(1d, 2d))),
                new Pair("nested-struct-zero-sign",
                    new OuterBag(new OuterPair(new FloatPair(0f, 1f), 1)),
                    new OuterBag(new OuterPair(new FloatPair(-0f, 1f), 1))),
                new Pair("nested-struct-equal",
                    new OuterBag(new OuterPair(new FloatPair(1f, 2f), 1)),
                    new OuterBag(new OuterPair(new FloatPair(1f, 2f), 1))),
                new Pair("class-member-absent", new ClassMemberBag(null), new ClassMemberBag(null)),
                new Pair("class-member-distinct", new ClassMemberBag(new FloatLeafClass(1f)), new ClassMemberBag(new FloatLeafClass(1f))),
                new Pair("record-class-inside-struct-zero-sign",
                    new HolderBag(new HolderFloat(1f, new FloatLeafClass(0f))),
                    new HolderBag(new HolderFloat(1f, new FloatLeafClass(-0f)))),
            };
        }

        private static string Answers(Func<object, object, bool> walk) =>
            string.Join("\n", PropsPairs().Select(pair => $"{pair.Label}={walk(pair.Prev, pair.Next)}"));

        [Test]
        public void Given_EachPropsTypeOfThePairTable_When_ItsComponentIsMemoized_Then_AComparerIsRegisteredForIt()
        {
            // Arrange
            var types = PropsPairs().Select(pair => pair.Prev.GetType()).Distinct();

            // Act
            var unregistered = types.Where(type => !PropsComparerRegistry.TryGet(type, out _)).Select(type => type.Name);

            // Assert
            Assert.That(string.Join(",", unregistered), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Given_APropsPairTable_When_TheGeneratedComparerAndTheReflectionWalkAnswerIt_Then_TheyAnswerAlike()
        {
            // Arrange
            var reflected = Answers((prev, next) => (bool)ReflectionWalk.Invoke(null, new[] { prev.GetType(), prev, next })!);

            // Act
            var generated = Answers((prev, next) =>
                PropsComparerRegistry.TryGet(prev.GetType(), out var comparer) && comparer(prev, next));

            // Assert
            Assert.That(generated, Is.EqualTo(reflected));
        }

        [Test]
        public void Given_APropsPairTable_When_ShallowEqualsAnswersIt_Then_ItAnswersWhatTheReflectionWalkDoes()
        {
            // Arrange
            var reflected = Answers((prev, next) => (bool)ReflectionWalk.Invoke(null, new[] { prev.GetType(), prev, next })!);

            // Act
            var routed = Answers(ComponentPropsComparer.ShallowEquals);

            // Assert
            Assert.That(routed, Is.EqualTo(reflected));
        }
    }
}
