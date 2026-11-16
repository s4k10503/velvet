using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a patch's second half reads of the first within one pass: the stacking classes a variant lights
    /// while the node's own classes are written.
    /// </summary>
    [TestFixture]
    internal sealed class PatchReadBackTests
    {
        private Reconciler _reconciler = null!;
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _reconciler = new Reconciler();
            _root = new VisualElement();
        }

        [TearDown]
        public void TearDown() => _reconciler.Dispose();

        private static VNode[] Tree(string state)
            => new VNode[]
            {
                V.Div(name: "parent", className: "relative", children: new VNode[]
                {
                    V.Div(name: "lit", className: "data-[state=open]:absolute data-[state=open]:z-10",
                        data: new Dictionary<string, string> { ["state"] = state }),
                    V.Div(name: "ordinary"),
                }),
            };

        // GREEN_ON_BASE(characterization): the base writes the class and the variant's payload at once, so the stacking
        // classes the relocation reads are the ones the patch just lit; the case pins that read.
        [Test]
        public void Given_AVariantThatLightsAStackingClassInThePatch_When_ThePatchRuns_Then_TheElementIsRelocatedInThePass()
        {
            // Arrange
            var before = Tree("closed");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), before);
            var lit = _root.Q<VisualElement>("lit");

            // Act
            _reconciler.Reconcile(_root, before, Tree("open"));

            // Assert
            Assert.That(FiberZLayerCoordinator.PlaceholderOf(_reconciler.Context, lit), Is.Not.Null);
        }

        private static VNode[] KeyedRows(bool lifted, params string[] keys)
        {
            var rows = new List<VNode>();
            foreach (var key in keys)
            {
                rows.Add(V.Div(key: key, name: key, className: lifted && key == "a" ? "absolute z-10" : null));
            }
            return new VNode[] { V.Div(name: "parent", className: "relative", children: rows.ToArray()) };
        }

        // GREEN_ON_BASE(refactor): the base relocates the row inside its patch and the reorder then moves the placeholder
        // with the declared slot; what the case pins is that the same order results where the relocation waits for the
        // end of the pass and the reorder moves the row itself.
        [Test]
        public void Given_ARowPatchedToAbsoluteZ10_When_ThePassAlsoReordersItAmongKeyedSiblings_Then_TheSlotsAndTheFrontBandHoldTheFinalOrder()
        {
            // Arrange
            var before = KeyedRows(false, "a", "b", "c");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), before);
            var parent = _root.Q<VisualElement>("parent");

            // Act
            _reconciler.Reconcile(_root, before, KeyedRows(true, "c", "a", "b"));

            // Assert
            var order = new List<string>();
            foreach (var child in parent.Children())
            {
                order.Add(_reconciler.Context.ZLayerPlaceholders.ContainsKey(child) ? "placeholder" : child.name);
            }
            Assert.That(string.Join(",", order), Is.EqualTo("c,placeholder,b,a"));
        }
    }
}
