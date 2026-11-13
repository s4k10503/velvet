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
        private static readonly string[] Skewed = { "skew-x-6" };
        private static readonly string[] SkewedFurther = { "skew-x-12" };
        private static readonly string[] RedToBlue = { "bg-gradient-to-r", "from-red-500", "to-blue-500" };
        private static readonly string[] GreenToBlue = { "bg-gradient-to-r", "from-green-500", "to-blue-500" };

        private static float BlurOf(string[] classNames)
        {
            StyleShadowClass.TryExtract(classNames, out var layers);
            return layers[0].Blur;
        }

        private static float SkewXOf(string[] classNames)
        {
            StyleSkewClass.TryExtract(classNames, out var spec);
            return spec.XDeg;
        }

        // NaN where the element draws no shadow layer, which no blur a class names can equal.
        private static float ShownBlur(ReconcilerScope scope, VisualElement card)
            => scope.Reconciler.Context.ShadowBindings.TryGetValue(card, out var binding) && binding.Layers.Length > 0
                ? binding.Layers[0].Blur
                : float.NaN;

        private static float ShownSkewX(VisualElement card)
            => AuthoredTransform.TryGetSkew(card, out var x, out _) ? x : float.NaN;

        private static (ReconcilerScope Scope, VisualElement Card) MountWithClasses(string[] classNames)
        {
            var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(),
                new VNode[] { V.Div(className: string.Join(" ", classNames), name: "card") });
            return (scope, scope.Root[0]);
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

        // GREEN_ON_BASE(characterization): the base already attaches it; writing `== 0` for the skew layer's `!= 0` in the lease's Attach reddens it.
        [Test]
        public void Given_APlayEndingOnASkewTheElementLacks_When_ItDraws_Then_TheSkewIsAttached()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[] { V.Div(className: "", name: "card") });
            var card = scope.Root[0];
            var leases = scope.Reconciler.Context.PaintLeases;
            var lease = leases.Open(card, Array.Empty<string>(), Skewed, isExit: false);
            leases.Note(card, Skewed);

            // Act
            lease.Apply(0f);
            lease.Apply(0.5f);

            // Assert
            Assert.That(scope.Reconciler.Context.SkewTransformed.ContainsKey(card), Is.True);
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

        // GREEN_ON_BASE(characterization): the base already keeps the hold; writing `_handles != 0` in Hold.Open reddens it.
        [Test]
        public void Given_APlayHoldingTheShadowThatHasDrawn_When_APlayOfAnotherLayerOpensOverIt_Then_TheShadowIsStillHeld()
        {
            // Arrange
            var (scope, card, _) = MountSmall();
            using var _s = scope;
            var leases = scope.Reconciler.Context.PaintLeases;
            leases.Open(card, Small, Large, isExit: false);

            // Act
            leases.Open(card, Skewed, SkewedFurther, isExit: false);

            // Assert
            Assert.That(leases.Holds(card, FiberPaintLeases.Layer.Shadow), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base already keeps the z; writing `_hasZ = true;` in TakeLive reddens it.
        [Test]
        public void Given_APlayOfTheShadowOnAStackedElement_When_ItIsReleasedHavingBeenToldNoZ_Then_TheElementKeepsItsZ()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(),
                new VNode[] { V.Div(className: "relative z-10 shadow-sm", name: "card") });
            var card = scope.Root.Q<VisualElement>("card");
            var context = scope.Reconciler.Context;
            var lease = context.PaintLeases.Open(card, Small, Large, isExit: false);

            // Act
            lease.Release();

            // Assert — an element with no recorded facts reads -1, which no z the class names can equal
            int? z = context.StackingFacts.TryGetValue(card, out var facts) ? facts.Z : -1;
            Assert.That(z, Is.EqualTo(10));
        }

        // GREEN_ON_BASE(characterization): the base already binds the blend; deleting `GradientBackground.RebindOwned(_element, current, value);` reddens it.
        [Test]
        public void Given_APlayBetweenTwoGradients_When_ItDrawsHalfway_Then_TheBoundGradientIsTheBlend()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(),
                new VNode[] { V.Div(className: string.Join(" ", RedToBlue), name: "card") });
            var card = scope.Root[0];
            var context = scope.Reconciler.Context;
            StyleGradientClass.TryExtract(RedToBlue, card, out var from, out _);
            StyleGradientClass.TryExtract(GreenToBlue, card, out var to, out _);
            var lease = context.PaintLeases.Open(card, RedToBlue, GreenToBlue, isExit: false);
            context.PaintLeases.Note(card, GreenToBlue);
            lease.Apply(0f);

            // Act
            lease.Apply(0.5f);

            // Assert
            Assert.That(context.GradientBackgrounds[card].Spec, Is.EqualTo(MotionPaintInterpolation.Gradient(from, to, 0.5f)));
        }

        [Test]
        public void Given_AnElementMountedOnTheEndPose_When_AnEnterOpensItsLease_Then_TheStartPoseShowsAtOnce()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[] { V.Div(className: "shadow-lg", name: "card") });
            var card = scope.Root[0];

            // Act
            scope.Reconciler.Context.PaintLeases.Open(card, Small, Large, isExit: false);

            // Assert
            Assert.That(ShadowOf(scope, card).Layers[0].Blur, Is.EqualTo(BlurOf(Small)).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): the base already draws the enter's start gradient at once.
        [Test]
        public void Given_AnElementMountedOnTheEndGradient_When_AnEnterOpensItsLease_Then_TheStartGradientShowsAtOnce()
        {
            // Arrange
            var (scope, card) = MountWithClasses(RedToBlue);
            using var _s = scope;
            var context = scope.Reconciler.Context;
            StyleGradientClass.TryExtract(GreenToBlue, card, out var start, out _);
            StyleGradientClass.TryExtract(RedToBlue, card, out var end, out _);

            // Act
            context.PaintLeases.Open(card, GreenToBlue, RedToBlue, isExit: false);

            // Assert
            Assert.That(context.GradientBackgrounds[card].Spec, Is.EqualTo(MotionPaintInterpolation.Gradient(start, end, 0f)));
        }

        // GREEN_ON_BASE(characterization): the base already starts from the shadow the element shows.
        [Test]
        public void Given_AnElementShowingAShadow_When_AnEnterWhoseStartPoseNamesNoShadowOpensItsLease_Then_TheShadowItShowsStays()
        {
            // Arrange
            var (scope, card, _) = MountSmall();
            using var _s = scope;

            // Act
            scope.Reconciler.Context.PaintLeases.Open(card, Array.Empty<string>(), Large, isExit: false);

            // Assert
            Assert.That(ShownBlur(scope, card), Is.EqualTo(BlurOf(Small)).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): the base already starts from the gradient the element shows.
        [Test]
        public void Given_AnElementShowingAGradient_When_AnExitWhoseStartPoseNamesNoGradientOpensItsLease_Then_TheGradientStillShows()
        {
            // Arrange
            var (scope, card) = MountWithClasses(RedToBlue);
            using var _s = scope;

            // Act
            scope.Reconciler.Context.PaintLeases.Open(card, Array.Empty<string>(), GreenToBlue, isExit: true);

            // Assert
            Assert.That(card.style.backgroundImage.value.texture, Is.Not.Null);
        }

        // GREEN_ON_BASE(characterization): the base already opens the enter on its own start pose.
        [Test]
        public void Given_APlayHoldingTheShadowThatAPassToldItsEnd_When_AnEnterWithNoPassOpensOverIt_Then_TheShadowShowsTheEntersStartPose()
        {
            // Arrange
            var (scope, card, _) = MountSmall();
            using var _s = scope;
            Play(scope, card, 0.5f);

            // Act
            scope.Reconciler.Context.PaintLeases.Open(card, Large, Small, isExit: false);

            // Assert
            Assert.That(ShownBlur(scope, card), Is.EqualTo(BlurOf(Large)).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): the base already opens the exit on the drawn pose.
        [Test]
        public void Given_APlayHoldingTheShadowThatHasDrawnHalfway_When_AnExitOpensOverIt_Then_TheShadowHoldsTheHalfwayPose()
        {
            // Arrange
            var (scope, card, _) = MountSmall();
            using var _s = scope;
            Play(scope, card, 0.5f);

            // Act
            scope.Reconciler.Context.PaintLeases.Open(card, Large, Small, isExit: true);

            // Assert
            Assert.That(ShownBlur(scope, card), Is.EqualTo((BlurOf(Small) + BlurOf(Large)) / 2f).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): the base already opens the exit on the drawn angle.
        [Test]
        public void Given_APlayHoldingTheSkewThatHasDrawnHalfway_When_AnExitOpensOverIt_Then_TheSkewHoldsTheHalfwayAngle()
        {
            // Arrange
            var (scope, card) = MountWithClasses(Skewed);
            using var _s = scope;
            var leases = scope.Reconciler.Context.PaintLeases;
            var first = leases.Open(card, Skewed, SkewedFurther, isExit: false);
            leases.Note(card, SkewedFurther);
            first.Apply(0f);
            first.Apply(0.5f);

            // Act
            leases.Open(card, SkewedFurther, Skewed, isExit: true);

            // Assert
            Assert.That(ShownSkewX(card), Is.EqualTo((SkewXOf(Skewed) + SkewXOf(SkewedFurther)) / 2f).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): the base already gives the classes' angle back.
        [Test]
        public void Given_APlayHoldingTheSkewThatEndsOnTheElementsOwnClass_When_ItIsReleasedHalfway_Then_TheSkewIsTheClasses()
        {
            // Arrange
            var (scope, card) = MountWithClasses(Skewed);
            using var _s = scope;
            var leases = scope.Reconciler.Context.PaintLeases;
            var lease = leases.Open(card, SkewedFurther, Skewed, isExit: false);
            leases.Note(card, Skewed);
            lease.Apply(0f);
            lease.Apply(0.5f);

            // Act
            lease.Release();

            // Assert
            Assert.That(ShownSkewX(card), Is.EqualTo(SkewXOf(Skewed)).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): the base already lets go of the cached texture.
        [Test]
        public void Given_APlayFromAGradientToNone_When_ItDrawsPastHalfway_Then_TheGradientHoldsNoCachedTexture()
        {
            // Arrange
            var (scope, card) = MountWithClasses(RedToBlue);
            using var _s = scope;
            var context = scope.Reconciler.Context;
            var binding = context.GradientBackgrounds[card];
            var lease = context.PaintLeases.Open(card, RedToBlue, Array.Empty<string>(), isExit: false);
            context.PaintLeases.Note(card, Array.Empty<string>());
            lease.Apply(0f);

            // Act
            lease.Apply(0.75f);

            // Assert
            Assert.That(binding.Held.HasValue, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base already binds the gradient again.
        [Test]
        public void Given_APlayFromAGradientToNoneThatDrewPastHalfway_When_ItDrawsBeforeHalfwayAgain_Then_TheGradientShowsAgain()
        {
            // Arrange
            var (scope, card) = MountWithClasses(RedToBlue);
            using var _s = scope;
            var leases = scope.Reconciler.Context.PaintLeases;
            var lease = leases.Open(card, RedToBlue, Array.Empty<string>(), isExit: false);
            leases.Note(card, Array.Empty<string>());
            lease.Apply(0f);
            lease.Apply(0.75f);

            // Act
            lease.Apply(0.25f);

            // Assert
            Assert.That(card.style.backgroundImage.value.texture, Is.Not.Null);
        }

        // GREEN_ON_BASE(characterization): the base already restacks at the patched z.
        [Test]
        public void Given_APlayHoldingAStackedElementsZ_When_APatchRestacksItAndThePlayIsReleased_Then_TheElementTakesThePatchedZ()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Div(className: "relative z-10", name: "card") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            var card = scope.Root.Q<VisualElement>("card");
            var context = scope.Reconciler.Context;
            var lease = context.PaintLeases.Open(card, new[] { "relative", "z-10" }, new[] { "relative", "z-20" }, isExit: false);
            scope.Reconciler.Reconcile(scope.Root, tree, new VNode[] { V.Div(className: "relative z-20", name: "card") });

            // Act
            lease.Release();

            // Assert
            int? z = context.StackingFacts.TryGetValue(card, out var facts) ? facts.Z : -1;
            Assert.That(z, Is.EqualTo(20));
        }
    }
}
