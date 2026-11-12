using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a variant moving an element's position utility restacks its parent, as a positioned element paints
    /// after its unpositioned siblings.
    /// </summary>
    internal sealed class PositionVariantRestackTests
    {
        // GREEN_ON_BASE(characterization): the base restacks a parent when hover makes a child absolute.
        [Test]
        public void Given_AChildThatHoverMakesAbsolute_When_HoverStarts_Then_ItPaintsOverItsSibling()
        {
            // Arrange — a carries no stacking class of its own, so only its position decides where it paints.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(name: "parent", className: "relative", children: new VNode[]
                {
                    V.Div(name: "a", className: "hover:absolute"),
                    V.Div(name: "b"),
                }),
            });
            var parent = scope.Root.Q<VisualElement>("parent");
            var a = scope.Root.Q<VisualElement>("a");
            var before = ZBandProbe.PaintOrder(parent);

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                a.SimulateEvent(over);
            }

            // Assert — the order before hover is the control.
            Assert.That(before == "a,b" ? ZBandProbe.PaintOrder(parent) : before, Is.EqualTo("b,a"));
        }
    }
}
