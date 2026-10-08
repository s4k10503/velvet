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

        // After a drop, a layout at another aspect must leave the background as the drop left it.

        private static Texture2D LaidOutAfter(VisualElement element)
        {
            LayOut(element, 200f, 100f);
            return BackgroundOf(element);
        }

        [Test]
        public void Given_ADiagonalGradient_When_PatchedToOneAlongAnAxis_Then_ALaterLayoutLeavesItsBackground()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: Diagonal) };
            Mount(scope, oldTree);
            scope.Reconciler.Reconcile(scope.Root, oldTree, new VNode[] { V.Div(className: AlongAnAxis) });
            var element = scope.Root[0];
            var before = BackgroundOf(element);

            // Act
            var after = LaidOutAfter(element);

            // Assert
            Assert.That(after, Is.SameAs(before));
        }

        [Test]
        public void Given_ADiagonalGradient_When_ItsGradientClassesAreRemoved_Then_ALaterLayoutLeavesItsBackground()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: Diagonal) };
            Mount(scope, oldTree);
            scope.Reconciler.Reconcile(scope.Root, oldTree, new VNode[] { V.Div(className: "p-2") });
            var element = scope.Root[0];
            var before = BackgroundOf(element);

            // Act
            var after = LaidOutAfter(element);

            // Assert
            Assert.That(after, Is.SameAs(before));
        }

        [Test]
        public void Given_ADiagonalGradient_When_SkewTakesItOver_Then_ALaterLayoutLeavesItsBackground()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: Diagonal) };
            Mount(scope, oldTree);
            scope.Reconciler.Reconcile(scope.Root, oldTree, new VNode[] { V.Div(className: "-skew-x-6 " + Diagonal) });
            var element = scope.Root[0];
            var before = BackgroundOf(element);

            // Act
            var after = LaidOutAfter(element);

            // Assert
            Assert.That(after, Is.SameAs(before));
        }

        [Test]
        public void Given_ADiagonalGradient_When_TheElementIsReleased_Then_ALaterLayoutLeavesItsBackground()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: Diagonal) };
            Mount(scope, oldTree);
            var element = scope.Root[0];
            scope.Reconciler.Reconcile(scope.Root, oldTree, Array.Empty<VNode>());
            var before = BackgroundOf(element);

            // Act
            var after = LaidOutAfter(element);

            // Assert
            Assert.That(after, Is.SameAs(before));
        }

        [Test]
        public void Given_ADiagonalGradient_When_TheReconcilerIsDisposed_Then_ALaterLayoutLeavesItsBackground()
        {
            // Arrange
            var scope = new ReconcilerScope();
            Mount(scope, new VNode[] { V.Div(className: Diagonal) });
            var element = scope.Root[0];
            scope.Dispose();
            var before = BackgroundOf(element);

            // Act
            var after = LaidOutAfter(element);

            // Assert
            Assert.That(after, Is.SameAs(before));
        }

        [Test]
        public void Given_ABackgroundWrittenAfterTheGradient_When_TheBoxChangesAspect_Then_ItIsKept()
        {
            // Arrange — another writer (a className-driven image) took the slot after the gradient bound.
            using var scope = new ReconcilerScope();
            Mount(scope, new VNode[] { V.Div(className: Diagonal) });
            var element = scope.Root[0];
            var other = new Texture2D(2, 2);
            element.style.backgroundImage = new StyleBackground(other);

            // Act
            LayOut(element, 200f, 100f);
            var kept = BackgroundOf(element);
            UnityEngine.Object.DestroyImmediate(other);

            // Assert
            Assert.That(kept, Is.SameAs(other));
        }

        // A pan mode paints the background twice as large along its axis.

        private const string Stops = " from-[#000000] to-[#ffffff]";

        private static (float, float) ScaleOf(ReconcilerScope scope, int index)
        {
            var scale = scope.Reconciler.Context.GradientBackgrounds[scope.Root[index]].BoxScale;
            return (scale.x, scale.y);
        }

        [Test]
        public void Given_AGradientPannedAlongX_When_Mounted_Then_ItIsLaidOutOverTwiceTheWidth()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "bg-linear-60" + Stops + " animate-gradient") });

            // Assert
            Assert.That(ScaleOf(scope, 0), Is.EqualTo((2f, 1f)));
        }

        [Test]
        public void Given_AGradientPannedAlongY_When_Mounted_Then_ItIsLaidOutOverTwiceTheHeight()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "bg-linear-30" + Stops + " animate-gradient") });

            // Assert
            Assert.That(ScaleOf(scope, 0), Is.EqualTo((1f, 2f)));
        }

        [Test]
        public void Given_AShimmerOverAGradient_When_Mounted_Then_ItIsLaidOutOverTheElementsBox()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "bg-linear-60" + Stops + " animate-shimmer") });

            // Assert
            Assert.That(ScaleOf(scope, 0), Is.EqualTo((1f, 1f)));
        }

        [Test]
        public void Given_AGradientPannedAlongX_When_ThePanIsRemoved_Then_ItIsLaidOutOverTheElementsBoxAgain()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: "bg-linear-60" + Stops + " animate-gradient") };
            Mount(scope, oldTree);

            // Act
            scope.Reconciler.Reconcile(scope.Root, oldTree, new VNode[] { V.Div(className: "bg-linear-60" + Stops) });

            // Assert
            Assert.That(ScaleOf(scope, 0), Is.EqualTo((1f, 1f)));
        }

        [Test]
        public void Given_APannedDiagonalGradient_When_Laid_Out_Then_ItIsBakedForTheOversizedBox()
        {
            // Arrange — the same gradient and layout, panned on one element and not on the other.
            using var scope = new ReconcilerScope();
            Mount(scope, new VNode[]
            {
                V.Div(className: "bg-linear-60" + Stops + " animate-gradient"), V.Div(className: "bg-linear-60" + Stops),
            });

            // Act
            LayOut(scope.Root[0], 200f, 100f);
            LayOut(scope.Root[1], 200f, 100f);

            // Assert
            Assert.That(BackgroundOf(scope.Root[0]), Is.Not.SameAs(BackgroundOf(scope.Root[1])));
        }

        [Test]
        public void Given_ALaidOutGradient_When_APanIsAdded_Then_ItIsBakedForTheOversizedBox()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: "bg-linear-60" + Stops) };
            Mount(scope, oldTree);
            LayOut(scope.Root[0], 200f, 100f);
            var plain = BackgroundOf(scope.Root[0]);

            // Act
            scope.Reconciler.Reconcile(scope.Root, oldTree,
                new VNode[] { V.Div(className: "bg-linear-60" + Stops + " animate-gradient") });

            // Assert
            Assert.That(BackgroundOf(scope.Root[0]), Is.Not.SameAs(plain));
        }

        [Test]
        public void Given_ALaidOutPannedGradient_When_ThePanIsRemoved_Then_ItIsBakedForTheElementsBoxAgain()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: "bg-linear-60" + Stops) };
            Mount(scope, oldTree);
            LayOut(scope.Root[0], 200f, 100f);
            var plain = BackgroundOf(scope.Root[0]);
            var panned = new VNode[] { V.Div(className: "bg-linear-60" + Stops + " animate-gradient") };
            scope.Reconciler.Reconcile(scope.Root, oldTree, panned);

            // Act
            scope.Reconciler.Reconcile(scope.Root, panned, oldTree);

            // Assert
            Assert.That(BackgroundOf(scope.Root[0]), Is.SameAs(plain));
        }

        [Test]
        public void Given_ARadialCircleOfFixedRadius_When_TheWidthChangesAtTheSameAspect_Then_ItIsBakedAgain()
        {
            // Arrange — a radius in pixels covers a different share of a box twice as wide.
            using var scope = new ReconcilerScope();
            Mount(scope, new VNode[] { V.Div(className: "bg-radial-[circle_100px]" + Stops) });
            LayOut(scope.Root[0], 200f, 100f);
            var narrow = BackgroundOf(scope.Root[0]);

            // Act
            LayOut(scope.Root[0], 400f, 200f);

            // Assert
            Assert.That(BackgroundOf(scope.Root[0]), Is.Not.SameAs(narrow));
        }

        [Test]
        public void Given_ABoxKeyedBakeNobodyShowsAnymore_When_ManyOthersAreBaked_Then_ItIsDestroyedAndTheElementStillPaints()
        {
            // Arrange — a texture held by a mounted element is never evicted; one let go of is.
            using var scope = new ReconcilerScope();
            Mount(scope, new VNode[] { V.Div(className: Diagonal) });
            LayOut(scope.Root[0], 200f, 100f);
            var wide = BackgroundOf(scope.Root[0]);
            for (var i = 1; i <= 40; i++)
            {
                LayOut(scope.Root[0], 100f + (i * 40f), 100f);
            }

            // Act
            LayOut(scope.Root[0], 200f, 100f);

            // Assert
            Assert.That(wide == null && BackgroundOf(scope.Root[0]) != null, Is.True);
        }
    }
}
