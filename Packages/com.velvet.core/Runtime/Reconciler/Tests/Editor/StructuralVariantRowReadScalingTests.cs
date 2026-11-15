using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that re-deriving a container's structural variants (<c>first:</c>, <c>last:</c>) reads the container
    /// a number of times that does not grow with its rows: the rendered children come from the row model in one call,
    /// where a walk over the physical children read the container for each.
    /// </summary>
    /// <remarks>
    /// Diverges from <see cref="GeneralPathSlotLookupScalingTests"/>, which counts a whole parent re-render: that pass
    /// reads the container once per row for reasons of its own, so the count here is taken over the one
    /// <c>ApplyStructuralVariants</c> call.
    /// </remarks>
    [TestFixture]
    internal sealed class StructuralVariantRowReadScalingTests
    {
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

        private static int BoxReadsDuringOneApplication(int rows)
        {
            using var reconciler = new Reconciler();
            var host = new VisualElement();
            var children = Enumerable.Range(0, rows)
                .Select(i => (VNode?)V.Div(name: "row" + i, className: "first:w-[10px] last:w-[30px]")).ToArray();
            reconciler.Reconcile(host, Array.Empty<VNode>(),
                new VNode?[] { V.Custom<CountingBox>(name: "box", children: children) });
            var box = host.Q<CountingBox>("box");
            var patcher = (FiberNodePatcher)typeof(Reconciler)
                .GetProperty("NodePatcher", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reconciler)!;
            box.Reads = 0;
            patcher.ApplyStructuralVariants(box);
            return box.Reads;
        }

        [Test]
        public void Given_AContainerWithStructuralVariantRows_When_TheyAreRederivedAtTwiceTheLength_Then_TheContainerReadsDoNotGrow()
        {
            // Arrange
            var readsAtN = BoxReadsDuringOneApplication(500);

            // Act
            var readsAt2N = BoxReadsDuringOneApplication(1000);

            // Assert — a linear count doubles, so the bound sits below that. A pass that read nothing leaves no
            // ratio, so it fails rather than passing as constant.
            var growth = readsAtN > 0 ? (double)readsAt2N / readsAtN : double.NaN;
            Assert.That(growth, Is.LessThan(1.5));
        }
    }
}
