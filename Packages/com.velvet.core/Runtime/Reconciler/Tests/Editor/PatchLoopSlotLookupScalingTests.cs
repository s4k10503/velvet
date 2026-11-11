using System;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that reconciling a list of labels whose rows are kept and patched makes a number of
    /// contentContainer calls on their container proportional to the list, not to its square: what a diff still asks
    /// by slot of each row it patches is answered from the slot it asked before rather than from the first child.
    /// One case for the indexed diff, which asks whether each slot it patches is occupied, and one each for the
    /// synchronous and the time-sliced keyed Pass 2, which ask it of each old row they take, wherever
    /// UNITY_ASSERTIONS is defined, as it is in the editor.
    /// </summary>
    /// <remarks>
    /// Diverges from <see cref="InsertLoopSlotLookupScalingTests"/>, whose list grows from none: here the list keeps
    /// its rows and patches each, and the rows are reconciled straight into the box, as the reconciler benchmarks
    /// drive them, so a budget can be handed to the diff. The keyed rows are reversed, so Pass 2 takes the old rows
    /// from the last slot down.
    /// </remarks>
    [TestFixture]
    internal sealed class PatchLoopSlotLookupScalingTests
    {
        // The case's NaN gate is what holds that the reconcile's reads of the box reach this override at all.
        private sealed class CountingBox : VisualElement
        {
            public int Reads;

            public override VisualElement contentContainer
            {
                get
                {
                    Reads++;
                    return this;
                }
            }
        }

        // A budget no slice of these lists reaches, so the time-sliced diff runs to its end in one slice.
        private const double OneSliceBudgetMs = 60_000;

        private static VNode[] Labels(int rows, bool keyed, string text)
        {
            var labels = new VNode[rows];
            for (var i = 0; i < rows; i++)
            {
                labels[i] = V.Label(text: text + i, key: keyed ? "k" + i : null);
            }
            return labels;
        }

        // Every row gets a new text, so the diff patches each one; a keyed list is reversed as well.
        private static VNode[] Retexted(int rows, bool keyed)
        {
            var retexted = Labels(rows, keyed, "b");
            if (keyed)
            {
                Array.Reverse(retexted);
            }
            return retexted;
        }

        private static int BoxReadsDuring(int rows, bool keyed, double budgetMs)
        {
            var box = new CountingBox();
            using var reconciler = new Reconciler();
            var before = Labels(rows, keyed, "a");
            var after = Retexted(rows, keyed);
            reconciler.Reconcile(box, Array.Empty<VNode>(), before);
            box.Reads = 0;
            reconciler.Reconcile(box, before, after, budgetMs);
            while (reconciler.HasPendingWork)
            {
                reconciler.ContinueReconcile(budgetMs);
            }
            return box.Reads;
        }

        [TestCase(false, 0d)]
        [TestCase(true, 0d)]
        [TestCase(true, OneSliceBudgetMs)]
        public void Given_ALabelListInOneContainer_When_ItIsReconciledAtTwiceTheLength_Then_TheContainerReadsGrowLessThanTwoAndAHalfTimes(
            bool keyed, double budgetMs)
        {
            // Arrange
            var readsAtN = BoxReadsDuring(100, keyed, budgetMs);

            // Act
            var readsAt2N = BoxReadsDuring(200, keyed, budgetMs);

            // Assert — doubling the list doubles a linear count and quadruples a quadratic one; the bound sits
            // between. A pass that read nothing leaves no ratio, so it fails rather than passing as linear.
            var growth = readsAtN > 0 ? (double)readsAt2N / readsAtN : double.NaN;
            Assert.That(growth, Is.LessThan(2.5));
        }
    }
}
