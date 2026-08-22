using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds the two implementations of the props member walk against each other: the compiled delegate a
    /// JIT runtime gets, and the reflection walk an <c>ENABLE_IL2CPP</c> player gets. Both are reached
    /// past the shared prologue <see cref="ComponentPropsComparer.ShallowEquals"/> runs, so a rule written
    /// into one and not the other shows up here rather than in an AOT player alone.
    /// </summary>
    [TestFixture]
    internal sealed class ComponentPropsComparerArmParityTests
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;

        private static readonly MethodInfo ReflectionWalk =
            typeof(ComponentPropsComparer).GetMethod("CompareMembersByReflection", Hidden)!;

        private static readonly MethodInfo CompiledWalkFactory =
            typeof(ComponentPropsComparer).GetMethod("GetCompiledComparer", Hidden)!;

        private sealed record SimpleBag(string Id, int Value);
        private sealed record FloatBag(float X);
        private readonly record struct FloatPair(float A, float B);
        private sealed record StructBag(FloatPair P);
        private sealed record NullableStructBag(FloatPair? P);
        private sealed record ObjectBag(object Held);
        private sealed record DecimalBag(decimal D);
        private readonly record struct IntPair(int A, int B);
        private sealed record IntBag(IntPair P);
        private sealed record Leaf(string V);
        private readonly record struct HoldsLeaf(Leaf L);
        private sealed record LeafBag(HoldsLeaf H);
        private readonly record struct MixedFloat(float F, Leaf L);
        private sealed record MixedBag(MixedFloat M);
        private readonly record struct NullableLeaf(float? F, int N);
        private sealed record NullableLeafBag(NullableLeaf L);
        private sealed record LooseBag(LooseNullable L);
        private readonly record struct DoublePair(double A, double B);
        private readonly record struct OuterPair(FloatPair Inner, int N);
        private sealed record OuterBag(OuterPair O);
        private sealed record DoubleBag(DoublePair D);
        private sealed record FloatLeafClass(float F);
        private readonly record struct HolderFloat(float F, FloatLeafClass Held);
        private sealed record HolderBag(HolderFloat H);
        private sealed record ClassMemberBag(FloatLeafClass? L);
        private sealed record RectBag(Rect R);

        private readonly record struct Pair(string Label, object Prev, object Next);

        // An Equals answering for less than the type holds is what leaves a floating-point leaf reachable
        // with the two sides disagreeing over whether they carry one, which the arms have to pass over
        // alike rather than one of them reading Value off an empty Nullable.
        private readonly struct LooseNullable
        {
            public LooseNullable(float? f) => F = f;

            public float? F { get; }

            public override bool Equals(object? obj) => obj is LooseNullable;

            public override int GetHashCode() => 0;
        }

        // Rect carries its floats in private fields declared in another assembly, which is the reach a
        // leaf comparison needs and a compiled expression therefore has to have.
        private static Pair[] PropsPairs()
        {
            var shared = new Leaf("x");
            return new[]
            {
                new Pair("equal-members", new SimpleBag("a", 1), new SimpleBag("a", 1)),
                new Pair("differing-member", new SimpleBag("a", 1), new SimpleBag("a", 2)),
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
                new Pair("distinct-references", new ObjectBag(new object()), new ObjectBag(new object())),
                new Pair("decimal-member", new DecimalBag(1.0m), new DecimalBag(2.0m)),
                new Pair("int-struct-member", new IntBag(new IntPair(1, 2)), new IntBag(new IntPair(1, 2))),
                new Pair("struct-holding-record", new LeafBag(new HoldsLeaf(new Leaf("x"))), new LeafBag(new HoldsLeaf(new Leaf("x")))),
                new Pair("rect-zero-sign", new RectBag(new Rect(0f, 1f, 2f, 3f)), new RectBag(new Rect(-0f, 1f, 2f, 3f))),
                new Pair("rect-equal", new RectBag(new Rect(1f, 2f, 3f, 4f)), new RectBag(new Rect(1f, 2f, 3f, 4f))),
                new Pair("mixed-zero-sign",
                    new MixedBag(new MixedFloat(0f, shared)), new MixedBag(new MixedFloat(-0f, shared))),
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
                new Pair("nested-struct-zero-sign",
                    new OuterBag(new OuterPair(new FloatPair(0f, 1f), 1)),
                    new OuterBag(new OuterPair(new FloatPair(-0f, 1f), 1))),
                new Pair("nested-struct-equal",
                    new OuterBag(new OuterPair(new FloatPair(1f, 2f), 1)),
                    new OuterBag(new OuterPair(new FloatPair(1f, 2f), 1))),
                new Pair("double-equal",
                    new DoubleBag(new DoublePair(1d, 2d)), new DoubleBag(new DoublePair(1d, 2d))),
                new Pair("class-member-absent", new ClassMemberBag(null), new ClassMemberBag(null)),
                new Pair("record-class-inside-struct-zero-sign",
                    new HolderBag(new HolderFloat(1f, new FloatLeafClass(0f))),
                    new HolderBag(new HolderFloat(1f, new FloatLeafClass(-0f)))),
            };
        }

        private static bool ByCompiledDelegate(object prev, object next)
            => ((Func<object, object, bool>)CompiledWalkFactory.Invoke(null, new object[] { prev.GetType() })!)(prev, next);

        private static string Answers(Func<object, object, bool> walk)
        {
            var lines = new List<string>();
            foreach (var pair in PropsPairs())
            {
                lines.Add($"{pair.Label}={walk(pair.Prev, pair.Next)}");
            }

            return string.Join("\n", lines);
        }

        // A missing reflection walk answers with this rather than throwing, so a tree carrying one arm
        // alone fails the comparison below instead of dying before it reaches one.
        private static string ReflectedAnswers()
            => ReflectionWalk is null
                ? "ComponentPropsComparer declares no reflection walk to hold the compiled one against"
                : Answers((prev, next) => (bool)ReflectionWalk.Invoke(null, new[] { prev.GetType(), prev, next })!);

        [Test]
        public void Given_APropsPairTable_When_EachArmWalksIt_Then_TheArmsAnswerAlike()
        {
            // Arrange
            var compiled = Answers(ByCompiledDelegate);

            // Act
            var reflected = ReflectedAnswers();

            // Assert
            Assert.That(reflected, Is.EqualTo(compiled),
                "The reflection walk an IL2CPP player runs must answer what the compiled walk answers");
        }
    }
}
