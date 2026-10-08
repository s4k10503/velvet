using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how a gradient element follows its box. A gradient whose geometry depends on the box's
    /// proportions (a diagonal angle, a conic) is baked again when a layout gives the
    /// element a new aspect, and stops being watched wherever its binding is dropped: a patch that removes or
    /// replaces it, a skew taking it over, the element's release and the reconciler's disposal. One that does
    /// not depend on the box registers nothing. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class GradientBoxWatchTests
    {
        private const string Diagonal = "bg-linear-45 from-[#000000] to-[#ffffff]";
        private const string AlongAnAxis = "bg-gradient-to-r from-[#000000] to-[#ffffff]";

        private static void Mount(ReconcilerScope scope, VNode[] tree)
            => scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

        private static GradientBinding BindingOf(ReconcilerScope scope)
            => scope.Reconciler.Context.GradientBackgrounds[scope.Root[0]];

        private static Texture2D BackgroundOf(VisualElement element) => element.style.backgroundImage.value.texture;

        private static void LayOut(VisualElement element, float width, float height)
        {
            using var evt = GeometryChangedEvent.GetPooled(Rect.zero, new Rect(0f, 0f, width, height));
            element.SimulateEvent(evt);
        }

        [Test]
        public void Given_ADiagonalGradient_When_ItsBoxBecomesWide_Then_TheBackgroundIsBakedForTheNewAspect()
        {
            // Arrange — mounted before any layout, so baked for a square.
            using var scope = new ReconcilerScope();
            Mount(scope, new VNode[] { V.Div(className: Diagonal) });
            var square = BackgroundOf(scope.Root[0]);

            // Act
            LayOut(scope.Root[0], 200f, 100f);

            // Assert
            Assert.That(BackgroundOf(scope.Root[0]), Is.Not.SameAs(square));
        }

        // GREEN_ON_BASE(characterization): the base never bakes again, so a size change keeps its texture there too.
        [Test]
        public void Given_ADiagonalGradient_When_ItsBoxChangesSizeAtTheSameAspect_Then_TheBackgroundIsKept()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            Mount(scope, new VNode[] { V.Div(className: Diagonal) });
            LayOut(scope.Root[0], 200f, 100f);
            var wide = BackgroundOf(scope.Root[0]);

            // Act
            LayOut(scope.Root[0], 400f, 200f);

            // Assert
            Assert.That(BackgroundOf(scope.Root[0]), Is.SameAs(wide));
        }

        // GREEN_ON_BASE(characterization): the base bakes one texture per spec, so two elements share it there too.
        [Test]
        public void Given_TwoDiagonalGradientsOfOneAspect_When_Laid_Out_Then_TheyShareATexture()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            Mount(scope, new VNode[] { V.Div(className: Diagonal), V.Div(className: Diagonal) });

            // Act
            LayOut(scope.Root[0], 200f, 100f);
            LayOut(scope.Root[1], 300f, 150f);

            // Assert
            Assert.That(BackgroundOf(scope.Root[1]), Is.SameAs(BackgroundOf(scope.Root[0])));
        }

        [Test]
        public void Given_AGradientAlongAnAxis_When_Mounted_Then_NothingWatchesItsBox()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: AlongAnAxis) });

            // Assert
            Assert.That(BindingOf(scope).OnGeometryChanged, Is.Null);
        }

        [Test]
        public void Given_AGradientToACorner_When_Mounted_Then_NothingWatchesItsBox()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "bg-gradient-to-tr from-[#000000] to-[#ffffff]") });

            // Assert
            Assert.That(BindingOf(scope).OnGeometryChanged, Is.Null);
        }

        [Test]
        public void Given_ARadialGradient_When_Mounted_Then_NothingWatchesItsBox()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "bg-radial from-[#000000] to-[#ffffff]") });

            // Assert
            Assert.That(BindingOf(scope).OnGeometryChanged, Is.Null);
        }

        [Test]
        public void Given_AConicGradient_When_Mounted_Then_ItsBoxIsWatched()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "bg-conic from-[#000000] to-[#ffffff]") });

            // Assert
            Assert.That(BindingOf(scope).OnGeometryChanged, Is.Not.Null);
        }

        [Test]
        public void Given_ADiagonalGradient_When_Mounted_Then_ItsBoxIsWatched()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: Diagonal) });

            // Assert
            Assert.That(BindingOf(scope).OnGeometryChanged, Is.Not.Null);
        }

        [Test]
        public void Given_ADiagonalGradient_When_PatchedToOneAlongAnAxis_Then_ItsBoxIsNoLongerWatched()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: Diagonal) };
            Mount(scope, oldTree);
            var watchedBefore = BindingOf(scope).OnGeometryChanged != null;

            // Act
            scope.Reconciler.Reconcile(scope.Root, oldTree, new VNode[] { V.Div(className: AlongAnAxis) });

            // Assert
            Assert.That((watchedBefore, BindingOf(scope).OnGeometryChanged != null), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AGradientAlongAnAxis_When_PatchedToADiagonalOne_Then_ItsBoxIsWatched()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: AlongAnAxis) };
            Mount(scope, oldTree);
            var watchedBefore = BindingOf(scope).OnGeometryChanged != null;

            // Act
            scope.Reconciler.Reconcile(scope.Root, oldTree, new VNode[] { V.Div(className: Diagonal) });

            // Assert
            Assert.That((watchedBefore, BindingOf(scope).OnGeometryChanged != null), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_ADiagonalGradient_When_ItsGradientClassesAreRemoved_Then_ItsBoxIsNoLongerWatched()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: Diagonal) };
            Mount(scope, oldTree);
            var binding = BindingOf(scope);
            var watchedBefore = binding.OnGeometryChanged != null;

            // Act
            scope.Reconciler.Reconcile(scope.Root, oldTree, new VNode[] { V.Div(className: "p-2") });

            // Assert
            Assert.That((watchedBefore, binding.OnGeometryChanged != null), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ADiagonalGradient_When_SkewTakesItOver_Then_ItsBoxIsNoLongerWatched()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: Diagonal) };
            Mount(scope, oldTree);
            var binding = BindingOf(scope);
            var watchedBefore = binding.OnGeometryChanged != null;

            // Act
            scope.Reconciler.Reconcile(scope.Root, oldTree, new VNode[] { V.Div(className: "-skew-x-6 " + Diagonal) });

            // Assert
            Assert.That((watchedBefore, binding.OnGeometryChanged != null), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ADiagonalGradient_When_TheElementIsReleased_Then_ItsBoxIsNoLongerWatched()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: Diagonal) };
            Mount(scope, oldTree);
            var binding = BindingOf(scope);
            var watchedBefore = binding.OnGeometryChanged != null;

            // Act
            scope.Reconciler.Reconcile(scope.Root, oldTree, Array.Empty<VNode>());

            // Assert
            Assert.That((watchedBefore, binding.OnGeometryChanged != null), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ADiagonalGradient_When_TheReconcilerIsDisposed_Then_ItsBoxIsNoLongerWatched()
        {
            // Arrange
            var scope = new ReconcilerScope();
            Mount(scope, new VNode[] { V.Div(className: Diagonal) });
            var binding = BindingOf(scope);
            var watchedBefore = binding.OnGeometryChanged != null;

            // Act
            scope.Dispose();

            // Assert
            Assert.That((watchedBefore, binding.OnGeometryChanged != null), Is.EqualTo((true, false)));
        }
    }
}
