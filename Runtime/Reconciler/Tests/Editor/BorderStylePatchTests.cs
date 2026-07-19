using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>border-dashed</c> / <c>border-dotted</c> className → dashed-outline reconciler contract.
    /// The outline is wrapper-less (the element's own <c>generateVisualContent</c>): a bordered element keeps its
    /// DOM slot, gets a tracked <see cref="BorderStyleBinding"/>, suppresses only the border COLOR (the width
    /// stays real so layout is unchanged), and detaches cleanly. Skew and shadow each own the whole face (they
    /// repaint a solid border), so a skewed / shadowed element tracks NO border-style binding (the border stays
    /// solid — a documented v1 limitation). A pooled element sheds the binding so a recycle cannot ghost a
    /// dashed outline. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class BorderStylePatchTests : VariantCleanupTestsBase
    {
        private static void Mount(ReconcilerScope scope, VNode[] tree)
            => scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

        [Test]
        public void Given_ABorderDashedClass_When_Reconciled_Then_ABindingIsTracked()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "border-dashed", name: "card") });

            // Assert
            Assert.That(scope.Reconciler.Context.BorderStyleBindings.Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_ABorderDashedClass_When_Reconciled_Then_TheElementIsNotWrapped()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "border-dashed", name: "card") });

            // Assert — the outline paints on the element itself; the DOM slot holds the element directly.
            Assert.That(scope.Root[0].name, Is.EqualTo("card"));
        }

        [Test]
        public void Given_ABorderDashedColoredElement_When_Mounted_Then_TheBorderColorReadsTheSentinel()
        {
            // Arrange — an inline border color (the border-[…] case) is captured, then masked so only the dashed
            // repaint shows. The width is never masked, so the box keeps reserving its border gutter.
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "border-dashed border-[#FFFFFF]", name: "card") });

            // Assert
            Assert.That(SilhouetteFace.IsSentinel(scope.Root[0].style.borderLeftColor.value), Is.True);
        }

        [Test]
        public void Given_ASkewedBorderDashedElement_When_Reconciled_Then_NoBorderStyleBindingIsTracked()
        {
            // Arrange — skew owns the whole face (it repaints a solid border as part of the sheared silhouette),
            // so the dashed layer defers: border-dashed + skew keeps a solid border (a documented limitation).
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "-skew-x-6 border-dashed", name: "card") });

            // Assert
            Assert.That(scope.Reconciler.Context.BorderStyleBindings.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_AShadowedBorderDashedElement_When_Reconciled_Then_NoBorderStyleBindingIsTracked()
        {
            // Arrange — the upright drop shadow also owns the face (it repaints a solid border over its shadow
            // quad), so the dashed layer defers to it too.
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "shadow-md border-dashed", name: "card") });

            // Assert
            Assert.That(scope.Reconciler.Context.BorderStyleBindings.Count, Is.EqualTo(0));
        }

        // Face handoff: adding skew / shadow to an already border-dashed element in ONE patch moves face
        // ownership to the skew / shadow silhouette. The dashed layer defers (detaches) LAST in the patch, but it
        // must hand the captured border color to the new owner and leave the suppression in place — a plain
        // release would null the shared inline border slot the new owner's suppression now depends on, re-exposing
        // the native rectangular border behind the sheared / shadowed face.

        [Test]
        public void Given_ABorderDashedColoredElement_When_SkewIsAddedInOnePatch_Then_TheSkewFaceAdoptsTheBorderColor()
        {
            // Arrange — a dashed border captures its inline color and masks the native border with the sentinel.
            ColorUtility.TryParseHtmlString("#FF0000", out var red);
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: "border-dashed border-[#FF0000]", name: "card") };
            Mount(scope, oldTree);
            Assume.That(scope.Reconciler.Context.BorderStyleBindings.Count, Is.EqualTo(1), "Precondition: the dashed border is tracked");

            // Act — add skew to the same element in a single reconcile; the skew silhouette takes the face.
            scope.Reconciler.Reconcile(scope.Root, oldTree,
                new VNode[] { V.Div(className: "-skew-x-6 border-dashed border-[#FF0000]", name: "card") });

            // Assert — the sheared face repaints the handed-off border color (its own capture read only the mask).
            Assert.That(scope.Reconciler.Context.SkewBindings[scope.Root[0]].BorderColor, Is.EqualTo(red));
        }

        [Test]
        public void Given_ABorderDashedColoredElement_When_SkewIsAddedInOnePatch_Then_TheBorderColorStaysSuppressed()
        {
            // Arrange — the dashed layer suppressed the native border with the sentinel.
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: "border-dashed border-[#FF0000]", name: "card") };
            Mount(scope, oldTree);
            Assume.That(SilhouetteFace.IsSentinel(scope.Root[0].style.borderLeftColor.value), Is.True,
                "Precondition: the border color is suppressed");

            // Act — hand the face to skew in one patch.
            scope.Reconciler.Reconcile(scope.Root, oldTree,
                new VNode[] { V.Div(className: "-skew-x-6 border-dashed border-[#FF0000]", name: "card") });

            // Assert — the suppression survives the handoff (releasing it would re-expose the native rectangle).
            Assert.That(SilhouetteFace.IsSentinel(scope.Root[0].style.borderLeftColor.value), Is.True);
        }

        [Test]
        public void Given_ABorderDashedColoredElement_When_ShadowIsAddedInOnePatch_Then_TheShadowFaceAdoptsTheBorderColor()
        {
            // Arrange — an upright drop shadow also owns the whole face (it repaints a solid border over its
            // shadow quad), so it is the second layer this handoff must serve.
            ColorUtility.TryParseHtmlString("#FF0000", out var red);
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: "border-dashed border-[#FF0000]", name: "card") };
            Mount(scope, oldTree);
            Assume.That(scope.Reconciler.Context.BorderStyleBindings.Count, Is.EqualTo(1), "Precondition: the dashed border is tracked");

            // Act — add a shadow to the same element in a single reconcile.
            scope.Reconciler.Reconcile(scope.Root, oldTree,
                new VNode[] { V.Div(className: "shadow-md border-dashed border-[#FF0000]", name: "card") });

            // Assert — the shadow's repainted upright face carries the handed-off border color.
            Assert.That(scope.Reconciler.Context.ShadowBindings[scope.Root[0]].Face.BorderColor, Is.EqualTo(red));
        }

        [Test]
        public void Given_ABorderDashedElement_When_TheClassIsRemovedByPatch_Then_TheBindingIsUntracked()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: "border-dashed", name: "card") };
            Mount(scope, oldTree);
            Assume.That(scope.Reconciler.Context.BorderStyleBindings.Count, Is.EqualTo(1), "Precondition: bound");

            // Act
            scope.Reconciler.Reconcile(scope.Root, oldTree, new VNode[] { V.Div(className: "w-full", name: "card") });

            // Assert
            Assert.That(scope.Reconciler.Context.BorderStyleBindings.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_ABorderDashedElement_When_TheTreeUnmounts_Then_NoBindingRemains()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: "border-dashed", name: "card") };
            Mount(scope, oldTree);

            // Act
            scope.Reconciler.Reconcile(scope.Root, oldTree, Array.Empty<VNode>());

            // Assert
            Assert.That(scope.Reconciler.Context.BorderStyleBindings.Count, Is.EqualTo(0));
        }

        // Pool reuse: the dashed-outline paint is a generateVisualContent delegate, not a style property, so the
        // pool reset cannot scrub it — only FiberElementCleaner's teardown detaches it. Without that teardown a
        // recycled element ghosts the prior consumer's binding (and its paint callback).

        [Component]
        private static VNode PoolHost()
        {
            var mode = Hooks.UseStore(s_store, s => s.Mode);
            VNode child = mode == 0
                ? V.Button(name: "b", className: "border-dashed border-[#FFFFFF]", text: "x")
                : mode == 2
                    ? V.Button(name: "b", text: "x")
                    : (VNode)V.Fragment(Array.Empty<VNode>());
            return V.Div(name: "host", children: new VNode[] { child });
        }

        [Test]
        public void Given_ABorderDashedElementWasRemoved_When_APlainOneIsRecreatedFromThePool_Then_NoStaleBindingLingers()
        {
            // Arrange — a border-dashed button mounted (binding tracked), then removed and returned to the pool.
            using var store = new ModeStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(PoolHost, key: "host"));
            var ctx = mounted.Root.Reconciler.Context;
            var scheduler = ctx.BatchScheduler;
            Assume.That(ctx.BorderStyleBindings.Count, Is.EqualTo(1), "Precondition: the dashed button is tracked");
            store.Set(1);
            scheduler.DrainImmediateForTest();

            // Act — a plain button (no border-dashed) renting the pooled button back.
            store.Set(2);
            scheduler.DrainImmediateForTest();

            // Assert — the recycled button carries no leftover border-style binding.
            Assert.That(ctx.BorderStyleBindings.ContainsKey(_root.Q<Button>("b")), Is.False);
        }
    }
}
