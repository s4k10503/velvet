using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what <c>ChildReconciler.FlattenAndFilter</c> returns for the three shapes that reach its
    /// early return: a null with no fragment, a fragment with no null, and both. Dropping <c>null</c> from
    /// the pattern that decides whether the array needs processing takes the first of those down the early
    /// return and hands the null straight back, and the whole suite stayed green through that change.
    /// </summary>
    /// <remarks>
    /// Reads the returned array rather than a mounted tree, deliberately. <c>cond ? node : null</c> is this
    /// framework's "render nothing", so the filtering is the contract — and a mounted tree cannot pin it,
    /// because every consumer of the returned array skips a null entry of its own accord. The mount comes
    /// out identical either way, which is what left the behaviour untested while looking covered.
    /// </remarks>
    [TestFixture]
    internal sealed class ChildFlattenFilterTests
    {
        private static VNode?[] Flatten(params VNode?[] nodes) =>
            ChildReconciler.FlattenAndFilter(nodes, new ReconcilerBufferPool());

        [Test]
        public void Given_AChildArrayHoldingANullAndNoFragment_When_Flattened_Then_TheNullIsGone()
        {
            // Arrange & Act
            var flattened = Flatten(V.Div(name: "a"), null, V.Div(name: "b"));

            // Assert — the count and the absence in one comparison: an array handed back untouched has the
            // wrong count, and an implementation that shortened it without removing the null would satisfy
            // either half alone.
            Assert.That((flattened.Length, flattened.Any(node => node is null)), Is.EqualTo((2, false)));
        }

        [Test]
        public void Given_AChildArrayHoldingAFragmentAndNoNull_When_Flattened_Then_TheFragmentIsInlined()
        {
            // Arrange & Act
            var flattened = Flatten(
                V.Div(name: "a"),
                V.Fragment(new VNode?[] { V.Div(name: "b"), V.Div(name: "c") }),
                V.Div(name: "d"));

            // Assert — four elements and no fragment left standing.
            Assert.That(
                (flattened.Length, flattened.Any(node => node is FragmentNode)),
                Is.EqualTo((4, false)));
        }

        [Test]
        public void Given_AChildArrayHoldingBothANullAndAFragment_When_Flattened_Then_NeitherSurvives()
        {
            // Arrange & Act — the same early return governs all three shapes, and this is the one where the
            // fragment would carry the array into processing even with the null arm gone.
            var flattened = Flatten(
                V.Div(name: "a"),
                null,
                V.Fragment(new VNode?[] { V.Div(name: "b"), null }));

            // Assert
            Assert.That(
                (flattened.Length, flattened.Any(node => node is null or FragmentNode)),
                Is.EqualTo((2, false)));
        }
    }
}
