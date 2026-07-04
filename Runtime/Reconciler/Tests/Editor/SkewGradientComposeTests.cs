using System;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the skew-* × bg-gradient-* composition contract. A skewed element cannot show a gradient
    /// through the rectangular background-image path (it cannot follow the shear), so a skewed element OWNS
    /// its gradient: the spec is fed into the <see cref="SkewBinding"/> and painted on the sheared mesh
    /// (<c>SkewSilhouette</c>), while the non-skew gradient path (<c>GradientBackground</c>, tracked in
    /// <c>GradientBackgrounds</c>) stands down — so the gradient renders exactly once, never as a straight
    /// rectangle behind the slant. These cases drive the reconciler handoff across mount and every
    /// add/remove transition; they assert binding / tracking STATE only (no panel paint). GWT, one assert.
    /// </summary>
    [TestFixture]
    internal sealed class SkewGradientComposeTests
    {
        private const string Skew = "-skew-x-6";
        private const string Gradient = "bg-gradient-to-b from-[#FF0000] to-[#0000FF]";

        private static void Mount(ReconcilerScope scope, VNode[] tree)
            => scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

        [Test]
        public void Given_SkewAndGradient_When_Mounted_Then_TheSkewBindingOwnsTheGradient()
        {
            // Arrange / Act — a single element carrying both skew and a gradient.
            using var scope = new ReconcilerScope();
            Mount(scope, new VNode[] { V.Div(className: Skew + " " + Gradient, name: "card") });

            // Assert — the skew binding carries the gradient (it paints the sheared mesh fill).
            Assert.That(scope.Reconciler.Context.SkewBindings[scope.Root[0]].HasGradient, Is.True);
        }

        [Test]
        public void Given_SkewAndGradient_When_Mounted_Then_NoStraightGradientIsTracked()
        {
            // Arrange / Act
            using var scope = new ReconcilerScope();
            Mount(scope, new VNode[] { V.Div(className: Skew + " " + Gradient, name: "card") });

            // Assert — the straight background-image path stood down (deferred to the skew binding), so a
            // second, un-sheared gradient rectangle is never tracked behind the slant.
            Assert.That(scope.Reconciler.Context.GradientBackgrounds.ContainsKey(scope.Root[0]), Is.False);
        }

        [Test]
        public void Given_SkewAndGradient_When_Mounted_Then_TheParsedDirectionReachesTheBinding()
        {
            // Arrange / Act — bg-gradient-to-b resolves to CSS 180°; verify the whole spec threads through,
            // not just the has-gradient flag.
            using var scope = new ReconcilerScope();
            Mount(scope, new VNode[] { V.Div(className: Skew + " " + Gradient, name: "card") });

            // Assert
            Assert.That(scope.Reconciler.Context.SkewBindings[scope.Root[0]].Gradient.AngleDeg,
                Is.EqualTo(180f));
        }

        [Test]
        public void Given_AGradientedSkewedElement_When_TheGradientClassesAreRemovedByPatch_Then_TheBindingRevertsToSolid()
        {
            // Arrange — skew + gradient (the binding owns the gradient).
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: Skew + " " + Gradient, name: "card") };
            Mount(scope, oldTree);
            Assume.That(scope.Reconciler.Context.SkewBindings[scope.Root[0]].HasGradient, Is.True,
                "Precondition: mount fed the gradient into the skew binding");

            // Act — patch keeps skew but drops the gradient classes.
            scope.Reconciler.Reconcile(scope.Root, oldTree,
                new VNode[] { V.Div(className: Skew, name: "card") });

            // Assert — the binding no longer carries a gradient, so Draw paints the solid fill again.
            Assert.That(scope.Reconciler.Context.SkewBindings[scope.Root[0]].HasGradient, Is.False);
        }

        [Test]
        public void Given_AStraightGradientElement_When_SkewIsAddedByPatch_Then_TheStraightGradientIsDropped()
        {
            // Arrange — a gradient WITHOUT skew: it takes the straight background-image path (tracked).
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: Gradient, name: "card") };
            Mount(scope, oldTree);
            Assume.That(scope.Reconciler.Context.GradientBackgrounds.ContainsKey(scope.Root[0]), Is.True,
                "Precondition: a non-skewed gradient is tracked on the straight path");

            // Act — skew is added; the skew binding takes ownership of the gradient.
            scope.Reconciler.Reconcile(scope.Root, oldTree,
                new VNode[] { V.Div(className: Skew + " " + Gradient, name: "card") });

            // Assert — the straight gradient is dropped (else an un-sheared rectangle lingers behind the slant).
            Assert.That(scope.Reconciler.Context.GradientBackgrounds.ContainsKey(scope.Root[0]), Is.False);
        }
    }
}
