using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins who writes an element's paint layers while a Motion play holds them: the class pass records what it
    /// is given, the play draws from the pose it started on to the one the pass named, and the last release
    /// hands the layers back.
    /// </summary>
    [TestFixture]
    internal sealed class PaintLeaseTests
    {
        private static readonly string[] Small = { "shadow-sm" };
        private static readonly string[] Large = { "shadow-lg" };

        private static float BlurOf(string[] classNames)
        {
            StyleShadowClass.TryExtract(classNames, out var layers);
            return layers[0].Blur;
        }

        private static (ReconcilerScope Scope, VisualElement Card, VNode[] Tree) MountSmall()
        {
            var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Div(className: "shadow-sm", name: "card") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            return (scope, scope.Root[0], tree);
        }

        private static BoxShadowBinding ShadowOf(ReconcilerScope scope, VisualElement card)
            => scope.Reconciler.Context.ShadowBindings[card];

        // The lease of a play that starts, is told the pose it ends on by a pass, and has drawn twice (the first
        // draw is the one that starts it).
        private static IMotionPaintLease Play(ReconcilerScope scope, VisualElement card, float progress)
        {
            var lease = scope.Reconciler.Context.PaintLeases.Open(card, Small, Large, isExit: false);
            scope.Reconciler.Context.PaintLeases.Note(card, Large);
            lease.Apply(0f);
            lease.Apply(progress);
            return lease;
        }

        [Test]
        public void Given_APlayHoldingTheShadow_When_ItDrawsHalfway_Then_TheBlurIsTheMeanOfTheTwoPoses()
        {
            // Arrange
            var (scope, card, _) = MountSmall();
            using var _s = scope;

            // Act
            Play(scope, card, 0.5f);

            // Assert
            Assert.That(ShadowOf(scope, card).Layers[0].Blur, Is.EqualTo((BlurOf(Small) + BlurOf(Large)) / 2f).Within(1e-3f));
        }

        [Test]
        public void Given_APlayHoldingTheShadow_When_ItIsReleased_Then_TheBlurIsTheEndPoses()
        {
            // Arrange
            var (scope, card, _) = MountSmall();
            using var _s = scope;
            var lease = Play(scope, card, 0.5f);

            // Act
            lease.Release();

            // Assert
            Assert.That(ShadowOf(scope, card).Layers[0].Blur, Is.EqualTo(BlurOf(Large)).Within(1e-3f));
        }

        [Test]
        public void Given_APlayHoldingTheShadow_When_APatchNamesTheEndPose_Then_TheLayerKeepsTheStartPoseUntilItDraws()
        {
            // Arrange
            var (scope, card, tree) = MountSmall();
            using var _s = scope;
            scope.Reconciler.Context.PaintLeases.Open(card, Small, Large, isExit: false);

            // Act
            scope.Reconciler.Reconcile(scope.Root, tree, new VNode[] { V.Div(className: "shadow-lg", name: "card") });

            // Assert
            Assert.That(ShadowOf(scope, card).Layers[0].Blur, Is.EqualTo(BlurOf(Small)).Within(1e-3f));
        }

        [Test]
        public void Given_APlayEndingOnAShadowTheElementLacks_When_ItDraws_Then_ALayerIsAttached()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Div(className: "", name: "card") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            var card = scope.Root[0];
            var leases = scope.Reconciler.Context.PaintLeases;
            var lease = leases.Open(card, Array.Empty<string>(), Large, isExit: false);
            leases.Note(card, Large);

            // Act
            lease.Apply(0f);
            lease.Apply(0.5f);

            // Assert
            Assert.That(scope.Reconciler.Context.ShadowBindings.ContainsKey(card), Is.True);
        }

        [Test]
        public void Given_APlayEndingWithoutTheShadow_When_ItIsReleased_Then_TheLayerIsDetached()
        {
            // Arrange
            var (scope, card, _) = MountSmall();
            using var _s = scope;
            var leases = scope.Reconciler.Context.PaintLeases;
            var lease = leases.Open(card, Small, Array.Empty<string>(), isExit: false);
            leases.Note(card, Array.Empty<string>());
            lease.Apply(0f);
            lease.Apply(0.5f);

            // Act
            lease.Release();

            // Assert
            Assert.That(scope.Reconciler.Context.ShadowBindings.ContainsKey(card), Is.False);
        }

        [Test]
        public void Given_TwoPlaysOnOneElement_When_TheFirstIsReleased_Then_TheSecondStillHoldsTheShadow()
        {
            // Arrange
            var (scope, card, _) = MountSmall();
            using var _s = scope;
            var leases = scope.Reconciler.Context.PaintLeases;
            var first = leases.Open(card, Small, Large, isExit: false);
            leases.Open(card, Large, Small, isExit: false);

            // Act
            first.Release();

            // Assert
            Assert.That(leases.Holds(card, FiberPaintLeases.Layer.Shadow), Is.True);
        }
    }
}
