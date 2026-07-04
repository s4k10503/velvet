using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the resolution of the generated fixed-arity <c>V.Memo</c> / <c>V.MemoizedWithKey</c> overloads.
    /// <list type="bullet">
    /// <item>Calls with 1 through 8 dependencies resolve to a generated generic overload that packs the
    /// arguments into the dependency array in order, preserving each runtime type.</item>
    /// <item><c>V.MemoizedWithKey</c> additionally carries the supplied key onto the node.</item>
    /// <item>No fixed-arity overload exists for 9 dependencies; such calls fall back to the
    /// <c>params object[]</c> overload, which still captures every dependency.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class VMemoGeneratedOverloadTests
    {
        [Test]
        public void Given_Arity1_When_Memo_Then_DependenciesMatchInOrder()
        {
            // Act
            var node = V.Memoized(() => V.Label(text: "a"), 1);

            // Assert
            AssertDependencies(node, expected: new object[] { 1 });
        }

        [Test]
        public void Given_Arity3_When_Memo_Then_DependenciesMatchInOrder()
        {
            // Act
            var node = V.Memoized(() => V.Label(text: "a"), 1, "x", 2.5f);

            // Assert
            AssertDependencies(node, expected: new object[] { 1, "x", 2.5f });
        }

        [Test]
        public void Given_Arity8_When_Memo_Then_DependenciesMatchInOrder()
        {
            // Act
            var node = V.Memoized(() => V.Label(text: "a"), 1, "x", 2.5f, true, 4L, 5.5d, (byte)6, (short)7);

            // Assert
            AssertDependencies(node, expected: new object[] { 1, "x", 2.5f, true, 4L, 5.5d, (byte)6, (short)7 });
        }

        [Test]
        public void Given_Arity9_When_Memo_Then_CapturesAllNineDependencies()
        {
            // Act
            var node = V.Memoized(() => V.Label(text: "a"), 1, "x", 2.5f, true, 4L, 5.5d, (byte)6, (short)7, 'z');

            // Assert
            AssertDependencies(
                node, expected: new object[] { 1, "x", 2.5f, true, 4L, 5.5d, (byte)6, (short)7, 'z' });
        }

        [Test]
        public void Given_Arity9_When_SelectingFixedArityMemo_Then_NoneExists()
        {
            // Act + Assert — there is no generated 9-arity overload, so a 9-dep call uses the params fallback
            Assert.That(SelectMemoMethod(arity: 9), Is.Null);
        }

        [Test]
        public void Given_Arity1_When_MemoWithKey_Then_DependenciesMatchInOrder()
        {
            // Act
            var node = V.MemoizedWithKey("k1", () => V.Label(text: "a"), 1);

            // Assert
            AssertDependencies(node, expected: new object[] { 1 });
        }

        [Test]
        public void Given_Arity1_When_MemoWithKey_Then_NodeCarriesKey()
        {
            // Act
            var node = V.MemoizedWithKey("k1", () => V.Label(text: "a"), 1);

            // Assert
            Assert.That(node.Key, Is.EqualTo("k1"));
        }

        [Test]
        public void Given_Arity3_When_MemoWithKey_Then_DependenciesMatchInOrder()
        {
            // Act
            var node = V.MemoizedWithKey("k3", () => V.Label(text: "a"), 1, "x", true);

            // Assert
            AssertDependencies(node, expected: new object[] { 1, "x", true });
        }

        [Test]
        public void Given_Arity8_When_MemoWithKey_Then_DependenciesMatchInOrder()
        {
            // Act
            var node = V.MemoizedWithKey("k8", () => V.Label(text: "a"), 1, "x", 2.5f, true, 4L, 5.5d, (byte)6, (short)7);

            // Assert
            AssertDependencies(node, expected: new object[] { 1, "x", 2.5f, true, 4L, 5.5d, (byte)6, (short)7 });
        }

        private static void AssertDependencies(MemoNode node, object[] expected)
        {
            Assert.That(node.Dependencies, Is.EqualTo(expected));
        }

        private static MethodInfo SelectMemoMethod(int arity)
        {
            return typeof(V).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "Memo" && m.IsGenericMethodDefinition)
                .FirstOrDefault(m => m.GetGenericArguments().Length == arity);
        }
    }
}
