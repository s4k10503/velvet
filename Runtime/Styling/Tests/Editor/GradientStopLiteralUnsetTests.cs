using System;
using NUnit.Framework;
using UnityEngine;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A gradient stop's literal bracket of another type holding no colour, with an opacity modifier, leaves the stop at
    /// its initial transparent colour, as the reader of a variable holding no colour does.
    /// </summary>
    [TestFixture]
    internal sealed class GradientStopLiteralUnsetTests
    {
        // The class source the passes read for an element with classes, once its readers resolve.
        private static string[] ComposedFor(string classes)
        {
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[] { V.Div(className: classes) });
            return scope.Reconciler.Context.VariantGateClasses.TryGetValue(scope.Root[0], out var state)
                ? state.Resolved ?? Array.Empty<string>()
                : Array.Empty<string>();
        }

        // GREEN_ON_BASE(characterization): the base already writes the transparent stop for such a literal.
        [Test]
        public void Given_AStopLiteralOfAnotherTypeHoldingNoColourWithAModifier_When_Extracted_Then_TheStopIsTransparent()
        {
            // Act
            StyleGradientClass.TryExtract(
                ComposedFor("bg-linear-to-r from-[#0000ff] from-[number:5]/50 to-[#00ff00]"), out var spec);

            // Assert
            Assert.That(spec.Stops != null && spec.Stops.Length > 0 ? ColorUtility.ToHtmlStringRGBA(spec.Stops[0].Color) : "no stop",
                Is.EqualTo("00000000"));
        }
    }
}
