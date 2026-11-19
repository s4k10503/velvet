using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a null child renders nothing and a fragment's children render in its place, through
    /// the path a mount actually takes. <c>cond ? node : null</c> is this framework's "render nothing", so
    /// the drop is the contract, and until now the only case that would have failed if it stopped happening
    /// was one named for anchor ordering that happened to arrange a null.
    /// </summary>
    [TestFixture]
    internal sealed class NullChildRendersNothingTests
    {
        private static List<string> NamesOf(VisualElement container) =>
            container.Children().Select(child => child.name).ToList();

        private static List<string> Rendered(params VNode?[] children)
        {
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), children);
            return NamesOf(scope.Root);
        }

        [Test]
        public void Given_AChildArrayHoldingANullAndNoFragment_When_Mounted_Then_TheNullRendersNothing()
        {
            // Arrange & Act
            var rendered = Rendered(V.Div(name: "a"), null, V.Div(name: "b"));

            // Assert — the names rather than the count: a count alone passes against an implementation that
            // rendered the null as some element and dropped one of the two that should be there.
            Assert.That(rendered, Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void Given_AChildArrayHoldingAFragmentAndNoNull_When_Mounted_Then_ItsChildrenTakeItsPlace()
        {
            // Arrange & Act — the fragment's children keep their order and sit between its siblings, which is
            // the whole of what inlining means here.
            var rendered = Rendered(
                V.Div(name: "a"),
                V.Fragment(new VNode?[] { V.Div(name: "b"), V.Div(name: "c") }),
                V.Div(name: "d"));

            // Assert
            Assert.That(rendered, Is.EqualTo(new[] { "a", "b", "c", "d" }));
        }

        [Test]
        public void Given_ANullInsideAFragment_When_Mounted_Then_ItRendersNothingEither()
        {
            // Arrange & Act — the recursive descent has its own null arm, and a fixture that only ever put a
            // null at the top level would leave it deletable with nothing going red.
            var rendered = Rendered(
                V.Div(name: "a"),
                null,
                V.Fragment(new VNode?[] { V.Div(name: "b"), null }));

            // Assert
            Assert.That(rendered, Is.EqualTo(new[] { "a", "b" }));
        }
    }
}
