using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    internal sealed class SkewTransformAppliedTests
    {
        private static VisualElement Mount(ReconcilerScope scope, string className)
        {
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(),
                new VNode[] { V.Div(className: className, name: "card") });
            return scope.Root.Q<VisualElement>("card");
        }

        private static (bool Transformed, StyleKeyword Rotate) Read(ReconcilerScope scope, VisualElement card)
            => (scope.Reconciler.Context.SkewTransformed.ContainsKey(card), card.style.rotate.keyword);

        [Test]
        public void Given_APlainSkew_When_Mounted_Then_ItIsTheElementsTransform()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var card = Mount(scope, "skew-x-12");

            // Assert
            Assert.That(Read(scope, card), Is.EqualTo((true, StyleKeyword.Undefined)));
        }

        [Test]
        public void Given_ASkewBesideARotate_When_Mounted_Then_ItIsTheElementsTransform()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var card = Mount(scope, "skew-x-12 rotate-45");

            // Assert
            Assert.That(Read(scope, card), Is.EqualTo((true, StyleKeyword.Undefined)));
        }

        [TestCase("skew-x-12 rotate-45", 45f, 1f, 1f, 12f, 0f, TestName = "Given_ASkewBesideARotateClass_When_Mounted_Then_TheSlotsCarryTheComposite")]
        [TestCase("skew-x-12 -rotate-45", -45f, 1f, 1f, 12f, 0f, TestName = "Given_ASkewBesideANegativeRotateClass_When_Mounted_Then_TheSlotsCarryTheComposite")]
        [TestCase("skew-x-12 rotate-[30deg]", 30f, 1f, 1f, 12f, 0f, TestName = "Given_ASkewBesideAnArbitraryRotate_When_Mounted_Then_TheSlotsCarryTheComposite")]
        [TestCase("skew-6 scale-110", 0f, 1.1f, 1.1f, 6f, 6f, TestName = "Given_ASkewBesideAScaleClass_When_Mounted_Then_TheSlotsCarryTheComposite")]
        [TestCase("skew-x-12 scale-110 scale-x-50", 0f, 0.5f, 1.1f, 12f, 0f, TestName = "Given_ASkewBesideAnAxisScaleOverAUniformOne_When_Mounted_Then_TheSlotsCarryTheComposite")]
        [TestCase("-skew-y-6 rotate-45 scale-x-[-1]", 45f, -1f, 1f, 0f, -6f, TestName = "Given_ASkewBesideARotateAndAMirroringScale_When_Mounted_Then_TheSlotsCarryTheComposite")]
        public void Given_ASkewBesideRotateAndScale_When_Mounted_Then_TheSlotsCarryTheComposite(
            string className, float rotateDeg, float scaleX, float scaleY, float xDeg, float yDeg)
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var card = Mount(scope, className);

            // Assert
            Assert.That(SkewCompositeMath.Drawn(card),
                Is.EqualTo(SkewCompositeMath.Composite(rotateDeg, scaleX, scaleY, xDeg, yDeg)).Within(1e-4f));
        }

        [Test]
        public void Given_ASkewedElement_When_ARotateClassIsAddedByAPatch_Then_TheSlotsCarryTheComposite()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var first = new VNode[] { V.Div(className: "skew-x-12", name: "card") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), first);
            var card = scope.Root.Q<VisualElement>("card");

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, new VNode[] { V.Div(className: "skew-x-12 rotate-45", name: "card") });

            // Assert
            Assert.That(SkewCompositeMath.Drawn(card),
                Is.EqualTo(SkewCompositeMath.Composite(45f, 1f, 1f, 12f, 0f)).Within(1e-4f));
        }

        [Test]
        public void Given_ASkewBesideARotate_When_TheRotateClassIsRemovedByAPatch_Then_TheSlotsCarryTheShear()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var first = new VNode[] { V.Div(className: "skew-x-12 rotate-45", name: "card") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), first);
            var card = scope.Root.Q<VisualElement>("card");

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, new VNode[] { V.Div(className: "skew-x-12", name: "card") });

            // Assert
            Assert.That(SkewCompositeMath.Drawn(card),
                Is.EqualTo(SkewCompositeMath.Composite(0f, 1f, 1f, 12f, 0f)).Within(1e-4f));
        }

        [TestCase(true, 45f, TestName = "Given_ASkewedElement_When_AVariantPutsARotateOn_Then_TheSlotsCarryTheComposite")]
        [TestCase(false, 0f, TestName = "Given_ASkewedElementUnderAVariantRotate_When_TheVariantLeaves_Then_TheSlotsCarryTheShear")]
        public void Given_ASkewedElement_When_AVariantTogglesARotate_Then_TheSlotsFollow(bool on, float rotateDeg)
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var card = Mount(scope, "skew-x-12");
            if (!on)
            {
                StyleVariantPayload.Apply(card, new[] { "rotate-45" }, true, StyleLayerPriority.Hover);
            }

            // Act
            StyleVariantPayload.Apply(card, new[] { "rotate-45" }, on, StyleLayerPriority.Hover);

            // Assert
            Assert.That(SkewCompositeMath.Drawn(card),
                Is.EqualTo(SkewCompositeMath.Composite(rotateDeg, 1f, 1f, 12f, 0f)).Within(1e-4f));
        }

        [Test]
        public void Given_ASkewedElement_When_TheSkewIsRemoved_Then_ItsTransformSlotsAreGivenBack()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var first = new VNode[] { V.Div(className: "skew-x-12", name: "card") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), first);
            var card = scope.Root.Q<VisualElement>("card");

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, new VNode[] { V.Div(className: "p-1", name: "card") });

            // Assert
            Assert.That(Read(scope, card), Is.EqualTo((false, StyleKeyword.Null)));
        }

        private const string Gradient = "bg-gradient-to-b from-[#FF0000] to-[#0000FF]";

        private static (float X, float Y) Angles(VisualElement card)
            => AuthoredTransform.TryGetSkew(card, out var x, out var y) ? (x, y) : (float.NaN, float.NaN);

        [TestCase("skew-6", 6f, 6f, TestName = "Given_APlainSkewClass_When_Mounted_Then_TheTransformShearsBothAxes")]
        [TestCase("skew-x-12 skew-6", 12f, 6f, TestName = "Given_APerAxisSkewBeforeAPlainOne_When_Mounted_Then_ThePerAxisClassHoldsX")]
        [TestCase("skew-6 skew-x-12", 12f, 6f, TestName = "Given_APerAxisSkewAfterAPlainOne_When_Mounted_Then_ThePerAxisClassHoldsX")]
        [TestCase("!skew-x-6 skew-x-12", 6f, 0f, TestName = "Given_AnImportantSkewXBeforeALargerPlainOne_When_Mounted_Then_TheImportantOneHoldsTheAxis")]
        [TestCase("skew-y-3 shadow-md", 0f, 3f, TestName = "Given_ASkewYBesideAShadow_When_Mounted_Then_TheTransformShearsY")]
        public void Given_SkewClasses_When_Mounted_Then_TheTransformCarriesTheWinningAngles(string className, float xDeg, float yDeg)
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var card = Mount(scope, className);

            // Assert
            Assert.That(Angles(card), Is.EqualTo((xDeg, yDeg)));
        }

        [Test]
        public void Given_APerAxisSkew_When_APlainSkewIsAddedByPatch_Then_ItTakesTheOtherAxis()
        {
            // Arrange — x keeps its winner across the patch, so only a probe that reads skew-6 for y re-extracts.
            using var scope = new ReconcilerScope();
            var first = new VNode[] { V.Div(className: "skew-x-12", name: "card") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), first);
            var card = scope.Root.Q<VisualElement>("card");

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, new VNode[] { V.Div(className: "skew-x-12 skew-6", name: "card") });

            // Assert
            Assert.That(Angles(card), Is.EqualTo((12f, 6f)));
        }

        [Test]
        public void Given_APlainSkew_When_APerAxisSkewIsAddedByPatch_Then_TheOtherAxisKeepsThePlainAngle()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var first = new VNode[] { V.Div(className: "skew-6", name: "card") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), first);
            var card = scope.Root.Q<VisualElement>("card");

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, new VNode[] { V.Div(className: "skew-6 skew-x-12", name: "card") });

            // Assert
            Assert.That(Angles(card), Is.EqualTo((12f, 6f)));
        }

        [Test]
        public void Given_AShadowedSkewedElement_When_Mounted_Then_TheShadowAndTheTransformAreBothHeld()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var card = Mount(scope, "-skew-x-6 shadow-md");

            // Assert — the shear is the element's transform, which the shadow is drawn under.
            Assert.That((scope.Reconciler.Context.ShadowBindings.ContainsKey(card), scope.Reconciler.Context.SkewTransformed.ContainsKey(card)),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_SkewOnMotion_When_Mounted_Then_TheTransformCarriesIt()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(),
                new VNode[] { V.Motion("-skew-x-6", key: "m") });

            // Assert
            Assert.That(Angles(scope.Root[0]), Is.EqualTo((-6f, 0f)));
        }

        [Test]
        public void Given_AMotion_When_SkewClassAddedByPatch_Then_TheTransformCarriesIt()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Motion("", key: "m") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), before);

            // Act
            scope.Reconciler.Reconcile(scope.Root, before, new VNode[] { V.Motion("-skew-x-6", key: "m") });

            // Assert
            Assert.That(Angles(scope.Root[0]), Is.EqualTo((-6f, 0f)));
        }

        [Test]
        public void Given_AMotionRestingAtASkewedVariant_When_Mounted_Then_TheTransformCarriesIt()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var poses = new System.Collections.Generic.Dictionary<string, MotionVariant> { ["a"] = "-skew-x-6" };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), new VNode[]
            {
                V.Motion(key: "m", variants: poses, animate: "a", enterOnMount: false, transition: StyleTransition.Fade),
            });

            // Assert
            Assert.That(Angles(scope.Root[0]), Is.EqualTo((-6f, 0f)));
        }

        [Test]
        public void Given_SkewAndGradient_When_Mounted_Then_TheGradientIsTheElementsOwnBackground()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var card = Mount(scope, "-skew-x-6 " + Gradient);

            // Assert — the transform shears the background-image with the element.
            Assert.That(scope.Reconciler.Context.GradientBackgrounds.ContainsKey(card), Is.True);
        }

        [Test]
        public void Given_AStraightGradientElement_When_SkewIsAddedByPatch_Then_TheGradientIsKept()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var first = new VNode[] { V.Div(className: Gradient, name: "card") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), first);

            // Act
            scope.Reconciler.Reconcile(scope.Root, first,
                new VNode[] { V.Div(className: "-skew-x-6 " + Gradient, name: "card") });

            // Assert
            Assert.That(scope.Reconciler.Context.GradientBackgrounds.ContainsKey(scope.Root[0]), Is.True);
        }

        [Test]
        public void Given_ASkewedElement_When_TheTreeUnmounts_Then_NoTransformRecordRemains()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var first = new VNode[] { V.Div(className: "-skew-x-6", name: "card") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), first);

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, System.Array.Empty<VNode>());

            // Assert
            Assert.That(scope.Reconciler.Context.SkewTransformed.Count, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base already drops the shape the skew gave the element.
        [Test]
        public void Given_ASkewedElement_When_TheSkewIsRemoved_Then_ItHoldsNoPickShape()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var first = new VNode[] { V.Div(className: "skew-x-12", name: "card") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), first);
            var target = scope.Root.Q<VisualElement>("card") as ISkewPickTarget;
            var shaped = target?.PickRegion != null;

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, new VNode[] { V.Div(className: "p-1", name: "card") });

            // Assert — a fresh object where the skew never gave the element a shape, which is not null
            Assert.That(shaped ? target.PickRegion : new object(), Is.Null);
        }

        // A play's skew goes through an applier of its own over the context, as StyleAnimationScheduler's does.
        private static FiberSkewApplier PlayApplier(ReconcilerScope scope) => new(scope.Reconciler.Context);

        // GREEN_ON_BASE(characterization): the base already lands a cut play on the angle the patched classes name.
        [Test]
        public void Given_ASkewPlayDrivingAnElement_When_APatchChangesItsSkewClassAndThePlayIsCutShort_Then_ItShowsThePatchedClassesAngle()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var first = new VNode[] { V.Div(className: "skew-x-6", name: "card") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), first);
            var card = scope.Root.Q<VisualElement>("card");
            var play = PlayApplier(scope);
            play.Drive(card, 20f, null);
            scope.Reconciler.Reconcile(scope.Root, first, new VNode[] { V.Div(className: "skew-x-12", name: "card") });

            // Act
            play.EndDrive(card, null, null);

            // Assert
            Assert.That(Angles(card).X, Is.EqualTo(12f));
        }

        // GREEN_ON_BASE(characterization): the base already gives back at disposal the angle a play landed on an element its classes never skewed.
        [Test]
        public void Given_AnUnskewedElementAPlayLandedOnASkew_When_TheTreeIsDisposed_Then_ItCarriesNoSkew()
        {
            // Arrange
            var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(),
                new VNode[] { V.Div(className: "p-1", name: "card") });
            var card = scope.Root.Q<VisualElement>("card");
            var play = PlayApplier(scope);
            play.Drive(card, 12f, null);
            play.EndDrive(card, 12f, null);
            var landed = Angles(card).X == 12f;

            // Act
            scope.Dispose();

            // Assert — 0 where the landing never showed, which is not NaN
            Assert.That(landed ? Angles(card).X : 0f, Is.NaN);
        }
    }
}
