using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements.TestFramework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>divide-x</c> / <c>divide-y</c> utilities, Tailwind v4's
    /// <c>:where(&amp; &gt; :not(:last-child))</c> border; UITK has no <c>:last-child</c> and no <c>&gt; *</c>
    /// child combinator, so <see cref="StyleDivideManipulator"/> writes that border on every child but the last. Width comes from <c>divide-x</c> (1px),
    /// the <c>divide-x-{0,2,4,8}</c> scale, or the <c>divide-x-[Npx]</c> arbitrary form; color from
    /// <c>divide-{palette}</c> or <c>divide-[#hex]</c>. UITK has no border-style, so <c>divide-dashed</c> /
    /// <c>divide-dotted</c> are painted by <see cref="DivideDashPainter"/> on each divided child instead.
    /// Which PHYSICAL edge carries the border comes from the <c>divide-x-reverse</c> / <c>divide-y-reverse</c>
    /// markers alone — see <see cref="DividerEdgeDirectionTests"/>.
    /// GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class DivideClassParityTests
    {
        #region Parse

        [Test]
        public void Given_DivideX_When_Extracted_Then_HorizontalOnePixel()
        {
            // Act — bare divide-x is the 1px default on the horizontal (left-border) axis.
            var ok = StyleDivideClass.TryExtract(new[] { "divide-x" }, out var spec);

            // Assert
            Assume.That(ok, Is.True, "Precondition: recognized as a divide utility");
            Assert.That((spec.Axis, spec.Width), Is.EqualTo((DivideAxis.Horizontal, 1f)));
        }

        [Test]
        public void Given_DivideY_When_Extracted_Then_VerticalOnePixel()
        {
            // Act
            var ok = StyleDivideClass.TryExtract(new[] { "divide-y" }, out var spec);

            // Assert
            Assume.That(ok, Is.True, "Precondition: recognized as a divide utility");
            Assert.That((spec.Axis, spec.Width), Is.EqualTo((DivideAxis.Vertical, 1f)));
        }

        [Test]
        public void Given_DivideX2_When_Extracted_Then_WidthFromScale()
        {
            // Act — divide-x-2 → 2px (the divide width scale).
            var ok = StyleDivideClass.TryExtract(new[] { "divide-x-2" }, out var spec);

            // Assert
            Assume.That(ok, Is.True, "Precondition: recognized as a divide utility");
            Assert.That(spec.Width, Is.EqualTo(2f));
        }

        [Test]
        public void Given_DivideXArbitraryPixel_When_Extracted_Then_ResolvesPixelWidth()
        {
            // Act — JIT arbitrary value: divide-x-[3px].
            var ok = StyleDivideClass.TryExtract(new[] { "divide-x-[3px]" }, out var spec);

            // Assert
            Assume.That(ok, Is.True, "Precondition: recognized as a divide utility");
            Assert.That(spec.Width, Is.EqualTo(3f));
        }

        [Test]
        public void Given_DivideXArbitraryPercent_When_Extracted_Then_Declines()
        {
            // Act — a divider is a pixel border; a percentage width is not meaningful, and no other
            // divide token is present, so the element has no active divide.
            var ok = StyleDivideClass.TryExtract(new[] { "divide-x-[50%]" }, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_DivideXAndNamedColor_When_Extracted_Then_ResolvesPaletteColor()
        {
            // Arrange — divide-gray-200 needs an axis class to be active (color needs a width).
            ColorUtility.TryParseHtmlString("#e5e7eb", out var gray200); // --color-gray-200

            // Act
            StyleDivideClass.TryExtract(new[] { "divide-x", "divide-gray-200" }, out var spec);

            // Assert
            Assume.That(spec.HasColor, Is.True, "Precondition: the palette color resolved");
            Assert.That(spec.Color, Is.EqualTo(gray200));
        }

        [Test]
        public void Given_DivideXAndArbitraryColor_When_Extracted_Then_ResolvesArbitraryColor()
        {
            // Arrange
            ColorUtility.TryParseHtmlString("#aabbcc", out var expected);

            // Act — divide-[#aabbcc] arbitrary color alongside the axis class.
            StyleDivideClass.TryExtract(new[] { "divide-x", "divide-[#aabbcc]" }, out var spec);

            // Assert
            Assume.That(spec.HasColor, Is.True, "Precondition: the arbitrary color resolved");
            Assert.That(spec.Color, Is.EqualTo(expected));
        }

        [Test]
        public void Given_DivideDashed_When_Extracted_Then_Declines()
        {
            // Act — UITK has no border-style, so divide-dashed is unsupported and, with no axis token, inert.
            var ok = StyleDivideClass.TryExtract(new[] { "divide-dashed" }, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_DivideXAndDashed_When_Extracted_Then_DashedLeavesColorUnset()
        {
            // Act — divide-dashed is not a color; it must not pollute the spec when paired with an axis.
            StyleDivideClass.TryExtract(new[] { "divide-x", "divide-dashed" }, out var spec);

            // Assert
            Assert.That(spec.HasColor, Is.False);
        }

        [Test]
        public void Given_DivideXAndDashed_When_Extracted_Then_StyleIsDashed()
        {
            // Act — a dashed divider is painted (DivideDashPainter); the style rides the spec.
            StyleDivideClass.TryExtract(new[] { "divide-x", "divide-dashed" }, out var spec);

            // Assert
            Assert.That(spec.Style, Is.EqualTo(BorderLineStyle.Dashed));
        }

        [Test]
        public void Given_DivideXAndDotted_When_Extracted_Then_StyleIsDotted()
        {
            // Act
            StyleDivideClass.TryExtract(new[] { "divide-x", "divide-dotted" }, out var spec);

            // Assert
            Assert.That(spec.Style, Is.EqualTo(BorderLineStyle.Dotted));
        }

        [Test]
        public void Given_DivideXAndSolid_When_Extracted_Then_StyleIsSolid()
        {
            // Act — divide-solid is the default (a plain inline border), and a recognized reset.
            StyleDivideClass.TryExtract(new[] { "divide-x", "divide-solid" }, out var spec);

            // Assert
            Assert.That(spec.Style, Is.EqualTo(BorderLineStyle.Solid));
        }

        [Test]
        public void Given_DivideDashedThenSolid_When_Extracted_Then_SolidResetsTheStyle()
        {
            // Act — last recognized style token wins (CSS cascade), so divide-solid overrides divide-dashed.
            StyleDivideClass.TryExtract(new[] { "divide-x", "divide-dashed", "divide-solid" }, out var spec);

            // Assert
            Assert.That(spec.Style, Is.EqualTo(BorderLineStyle.Solid));
        }

        [Test]
        public void Given_LoneNamedColor_When_Extracted_Then_Inert()
        {
            // Act — a color with no divide-x / divide-y draws nothing.
            var ok = StyleDivideClass.TryExtract(new[] { "divide-gray-200" }, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_DivideXAndHorizontalReverseMarker_When_Extracted_Then_TheDividerIsReversed()
        {
            // Act — divide-x-reverse is recognized as a per-axis marker riding the spec, not skipped as an
            // unsupported divide-* token.
            StyleDivideClass.TryExtract(new[] { "divide-x", "divide-x-reverse" }, out var spec);

            // Assert
            Assert.That(spec.Reverse, Is.True);
        }

        [Test]
        public void Given_DivideYAndVerticalReverseMarker_When_Extracted_Then_TheDividerIsReversed()
        {
            // Act
            StyleDivideClass.TryExtract(new[] { "divide-y", "divide-y-reverse" }, out var spec);

            // Assert
            Assert.That(spec.Reverse, Is.True);
        }

        [Test]
        public void Given_DivideYAndHorizontalReverseMarker_When_Extracted_Then_TheCrossAxisMarkerIsDropped()
        {
            // Act — the marker names the OTHER axis than the divider resolved to, so it cannot apply: a
            // divide-y is reversed only by divide-y-reverse.
            StyleDivideClass.TryExtract(new[] { "divide-y", "divide-x-reverse" }, out var spec);

            // Assert
            Assert.That(spec.Reverse, Is.False);
        }

        [Test]
        public void Given_LoneHorizontalReverseMarker_When_Extracted_Then_Inert()
        {
            // Act — a marker with no divide-x / divide-y has no width to move, so the element has no divide.
            var ok = StyleDivideClass.TryExtract(new[] { "divide-x-reverse" }, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_DivideX2AndHorizontalReverseMarker_When_Extracted_Then_TheWidthComesFromTheAxisClass()
        {
            // Act — the marker must not be read as a width class of its own, which would silently zero the
            // divider it was meant to move.
            StyleDivideClass.TryExtract(new[] { "divide-x-2", "divide-x-reverse" }, out var spec);

            // Assert
            Assert.That(spec.Width, Is.EqualTo(2f));
        }

        [Test]
        public void Given_DivideXClass_When_HasDivideClassProbed_Then_GateReturnsTrue()
        {
            // Act — the FiberNodePatcher early-out depends on this gate recognizing the prefix.
            var has = StyleDivideClass.HasDivideClass(new[] { "divide-x" });

            // Assert
            Assert.That(has, Is.True);
        }

        #endregion

        #region End-to-end (manipulator drives child borders)

        [Test]
        public void Given_DivideXRow_When_Reconciled_Then_SecondChildHasEndBorderWidth()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row divide-x divide-gray-200", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert — the divider sits on the end (right) edge of every child but the last.
            Assert.That(scope.Root[0][1].style.borderRightWidth.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_DivideXRow_When_Reconciled_Then_LastChildHasNoEndBorder()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row divide-x divide-gray-200", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert — the last child carries no divider on either horizontal edge (Tailwind's
            // `> :not(:last-child)`).
            Assert.That((scope.Root[0][2].style.borderLeftWidth.value, scope.Root[0][2].style.borderRightWidth.value),
                Is.EqualTo((0f, 0f)));
        }

        [Test]
        public void Given_DivideXRow_When_Reconciled_Then_FirstChildHasTheEndBorder()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row divide-x divide-gray-200", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][0].style.borderRightWidth.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_DivideXNamedColorRow_When_Reconciled_Then_SecondChildHasPaletteBorderColor()
        {
            // Arrange
            ColorUtility.TryParseHtmlString("#e5e7eb", out var gray200);
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row divide-x divide-gray-200", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderRightColor.value, Is.EqualTo(gray200));
        }

        [Test]
        public void Given_DivideXNamedColorRow_When_Reconciled_Then_TheDividedChildsOtherEdgesTakeTheColorToo()
        {
            // Arrange — Tailwind's divide-{color} is `border-color` on every child but the last: all four edges.
            ColorUtility.TryParseHtmlString("#e5e7eb", out var gray200);
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row divide-x divide-gray-200", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][0].style.borderTopColor.value, Is.EqualTo(gray200));
        }

        [Test]
        public void Given_ADivideXRowWhoseChildCarriesARightBorderClass_When_Reconciled_Then_TheClassWidthWins()
        {
            // Arrange — Tailwind writes the divider width at zero specificity, so the child's own border-r-2
            // wins on its edge; the first child, which declares none, still takes the divider.
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Div(className: "flex flex-row divide-x", children: new VNode[]
                {
                    V.Div(className: "child"),
                    V.Div(className: "child border-r-2"),
                    V.Div(className: "child"),
                }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That((scope.Root[0][0].style.borderRightWidth.value, scope.Root[0][1].style.borderRightWidth.keyword),
                Is.EqualTo((1f, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ADivideRowWhoseLastChildIsAbsolute_When_Reconciled_Then_TheChildBeforeItTakesTheDivider()
        {
            // Arrange — Tailwind's `:not(:last-child)` counts an absolutely positioned last child.
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Div(className: "flex flex-row divide-x", children: new VNode[]
                {
                    V.Div(className: "child"),
                    V.Div(className: "child"),
                    V.Div(className: "absolute"),
                }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderRightWidth.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_DivideYRow_When_Reconciled_Then_SecondChildHasBottomBorderWidth()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-col divide-y divide-gray-200", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert — divide-y draws on the bottom edge.
            Assert.That(scope.Root[0][1].style.borderBottomWidth.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_DivideXRow_When_ClassRemovedByPatch_Then_StaleBorderCleared()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row divide-x divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var divided = scope.Root[0][1].style.borderRightWidth.value;

            // Act — patch the same container without the divide class.
            var tree2 = new VNode[] { Row("flex flex-row", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the manipulator's border is cleared (no ghost); the divider before the patch rides along.
            Assert.That((divided, scope.Root[0][1].style.borderRightWidth.value), Is.EqualTo((1f, 0f)));
        }

        [Test]
        public void Given_DivideXNamedColor_When_ColorClassRemovedKeepingAxis_Then_StaleBorderColorCleared()
        {
            // Arrange — a colored divider. Dropping only the color class (keeping divide-x) keeps the
            // manipulator attached (patched via UpdateSpec), so the color must be reset, not left stale.
            ColorUtility.TryParseHtmlString("#e5e7eb", out var gray200);
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row divide-x divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var colored = scope.Root[0][1].style.borderRightColor.value == gray200;

            // Act — keep divide-x, drop divide-gray-200.
            var tree2 = new VNode[] { Row("flex flex-row divide-x", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the stale palette color is cleared (the divider reverts to the default border color); the
            // color before the patch rides along.
            Assert.That((colored, scope.Root[0][1].style.borderRightColor.value == gray200), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_DivideXRow_When_PatchedToDivideY_Then_RightEdgeClearedAndBottomApplied()
        {
            // Arrange — a horizontal divider.
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row divide-x divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var divided = scope.Root[0][1].style.borderRightWidth.value;

            // Act — flip the axis to vertical (the manipulator clears the old edge before writing the new).
            var tree2 = new VNode[] { Row("flex flex-col divide-y divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the old right edge is cleared and the new bottom edge is applied; the right divider
            // before the patch rides along.
            Assert.That(
                (divided, scope.Root[0][1].style.borderRightWidth.value, scope.Root[0][1].style.borderBottomWidth.value),
                Is.EqualTo((1f, 0f, 1f)));
        }

        [Test]
        public void Given_DivideYScrollView_When_Reconciled_Then_ContentChildrenGetBottomDivider()
        {
            // Arrange — a ScrollView redirects children into its contentContainer; the divider must land on
            // the reconciled content, not the ScrollView's internal hierarchy (mirrors the gap hardening case).
            using var scope = new ReconcilerScope();
            var children = new VNode[] { V.Div(className: "child"), V.Div(className: "child"), V.Div(className: "child") };
            var tree = new VNode[] { V.ScrollView("flex flex-col divide-y divide-gray-200", children) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var content = ((ScrollView)scope.Root[0]).contentContainer;

            // Assert — the divider sits on the 2nd content child's bottom edge.
            Assert.That(content[1].style.borderBottomWidth.value, Is.EqualTo(1f));
        }

        #endregion

        #region End-to-end (dashed / dotted dividers)

        [Test]
        public void Given_DivideXDashedRow_When_Reconciled_Then_TheDividerGutterMatchesSolid()
        {
            // Arrange — a dashed divider must reserve the SAME layout gutter as a solid one (only the paint
            // differs), so its border WIDTH stays real (the color is what gets masked).
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row divide-x divide-dashed divide-gray-200", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderRightWidth.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_DivideXDashedRow_When_Reconciled_Then_TheDividerColorIsSuppressed()
        {
            // Arrange — the native border color is masked with the sentinel so only the dashed paint shows.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row divide-x divide-dashed divide-gray-200", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(SilhouetteFace.IsSentinel(scope.Root[0][1].style.borderRightColor.value), Is.True);
        }

        [Test]
        public void Given_DivideXDashedRow_When_Reconciled_Then_TheDividerChildGetsAPaintCallback()
        {
            // Arrange — the dashed divider is painted on the divided CHILD's own generateVisualContent (a
            // container paints behind its children, so a container-drawn divider would hide under an opaque child).
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row divide-x divide-dashed divide-gray-200", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].generateVisualContent, Is.Not.Null);
        }

        [Test]
        public void Given_DivideXDashedRow_When_Reconciled_Then_TheLastChildGetsNoPaintCallback()
        {
            // Arrange — only actual divider children (every one but the last) get a paint.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row divide-x divide-dashed divide-gray-200", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][2].generateVisualContent, Is.Null);
        }

        [Test]
        public void Given_DivideXDashedRow_When_FlippedToSolid_Then_TheDividerColorIsReleased()
        {
            // Arrange — a dashed divider (color masked by the sentinel).
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row divide-x divide-dashed divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var masked = SilhouetteFace.IsSentinel(scope.Root[0][1].style.borderRightColor.value);

            // Act — flip to a solid divider; the sentinel is released back to a real color.
            var tree2 = new VNode[] { Row("flex flex-row divide-x divide-solid divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the mask before the flip rides along.
            Assert.That((masked, SilhouetteFace.IsSentinel(scope.Root[0][1].style.borderRightColor.value)),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_DivideXDashedKeyedLabels_When_OneChildIsRemoved_Then_TheDividerPaintCountTracksTheDividers()
        {
            // Arrange — pooled Label children (keyed) under a dashed divide: children 1 and 2 each get a paint
            // binding. Removing the last child makes the second one last, which sheds its binding.
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { KeyedLabels("flex flex-col divide-y divide-dashed divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            Assume.That(scope.Reconciler.Context.DivideDashBindings.Count, Is.EqualTo(2),
                "Precondition: two divider children each carry a paint binding");

            // Act — drop the last keyed child.
            var tree2 = new VNode[] { KeyedLabels("flex flex-col divide-y divide-dashed divide-gray-200", 2) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — one divider remains, so exactly one paint binding survives.
            Assert.That(scope.Reconciler.Context.DivideDashBindings.Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_DivideXDashedImplicitColor_When_AChildBorderColorChanges_Then_TheDividerPaintReSyncs()
        {
            // Arrange — a dashed divider with NO divide-{color} takes its paint color from the divided child's own
            // would-be border color (here an inline border-[#hex]). That color must be re-synced each pass, not
            // captured once and cached, so a later change to the child's border color moves the divider with it.
            ColorUtility.TryParseHtmlString("#FF0000", out var red);
            ColorUtility.TryParseHtmlString("#00FF00", out var green);
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { DividerRowWithColoredChild("flex flex-row divide-x divide-dashed", "border-[#FF0000]") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            Assume.That(DivideDashPainter.PaintColor(scope.Reconciler.Context.DivideDashBindings[scope.Root[0][1]]), Is.EqualTo(red),
                "Precondition: the dashed divider captured the child's initial border color");

            // Act — change the child's own border color; the divider's implicit color must follow.
            var tree2 = new VNode[] { DividerRowWithColoredChild("flex flex-row divide-x divide-dashed", "border-[#00FF00]") };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert
            Assert.That(DivideDashPainter.PaintColor(scope.Reconciler.Context.DivideDashBindings[scope.Root[0][1]]), Is.EqualTo(green));
        }

        [Test]
        public void Given_DivideXDashedRow_When_ADividerChildIsShadowed_Then_ThatChildGetsNoDashedPaint()
        {
            // Arrange — a drop shadow owns the child's border face and repaints a solid border, so a dashed
            // divider on the same child would fight it: the dashed layer must defer to the face owner (a solid
            // divider, no paint), the same as for a skewed child and as the element-level border-dashed gate does.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { DividerRowWithColoredChild("flex flex-row divide-x divide-dashed divide-gray-200", "shadow-lg") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert — the shadowed divider child carries no dashed paint binding (it renders a solid divider).
            Assert.That(scope.Reconciler.Context.DivideDashBindings.ContainsKey(scope.Root[0][1]), Is.False);
        }

        #endregion

        [Test]
        public void Given_AColorTheChildsCodeWroteUnderASolidDivider_When_TheDividerTurnsDashed_Then_TheDashTakesIt()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var solid = new VNode[] { Row("flex flex-row divide-x", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), solid);
            var child = scope.Root[0][1];
            child.style.borderRightColor = Color.blue;

            // Act
            scope.Reconciler.Reconcile(scope.Root, solid, new VNode[] { Row("flex flex-row divide-x divide-dashed", 3) });

            // Assert
            Assert.That(DivideDashPainter.PaintColor(scope.Reconciler.Context.DivideDashBindings[child]),
                Is.EqualTo(Color.blue));
        }

        [Test]
        public void Given_AColoredSolidDivider_When_ItTurnsDashed_Then_TheDividerColorIsNotTakenForTheChildsOwn()
        {
            // Arrange — the solid divider holds its color on the edge, which the dashed one then finds there.
            using var scope = new ReconcilerScope();
            var solid = new VNode[] { Row("flex flex-row divide-x divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), solid);
            var child = scope.Root[0][1];

            // Act
            scope.Reconciler.Reconcile(scope.Root, solid,
                new VNode[] { Row("flex flex-row divide-x divide-dashed divide-gray-200", 3) });

            // Assert
            Assert.That(scope.Reconciler.Context.DivideDashBindings[child].Inline, Is.Null);
        }

        [Test]
        public void Given_AMotionDriverWritingABorderColorTheDividerGivesWayFor_When_TheDividerAppliesAgain_Then_TheDrivenColorStands()
        {
            // Arrange — border-[#0000ff] is a layer of the child's own, which the divide color's hold on its other
            // edges gives way to.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), new VNode[]
            {
                DividerRowWithColoredChild("flex flex-row divide-x divide-gray-200", "border-[#0000ff]"),
            });
            var child = scope.Root[0][1];
            var own = child.style.borderLeftColor.value;
            StyleArbitraryValueResolver.ApplyDriven(child, new ArbitraryStyle(ArbitraryProperty.BorderColor, Color.red));

            // Act
            StyleArbitraryValueResolver.NotifyClassesChanged(child);

            // Assert — the child's own color rides along, since a hold that never gave way to it writes no layer.
            Assert.That((own, child.style.borderLeftColor.value), Is.EqualTo((Color.blue, Color.red)));
        }

        [Test]
        public void Given_ADashTransitionInsideItsDelay_When_Eased_Then_ItHasNotStarted()
        {
            // Arrange
            var binding = new DivideDashChildBinding { DelaySec = 0.2f, DurationSec = 1f, Easing = EasingMode.Linear };

            // Act
            var eased = DivideDashPainter.Eased(binding, 0.1, out _);

            // Assert
            Assert.That(eased, Is.EqualTo(0f));
        }

        [Test]
        public void Given_ALinearDashTransitionHalfwayThrough_When_Eased_Then_ItIsHalfway()
        {
            // Arrange
            var binding = new DivideDashChildBinding { DelaySec = 0.25f, DurationSec = 1f, Easing = EasingMode.Linear };

            // Act
            var eased = DivideDashPainter.Eased(binding, 0.75, out _);

            // Assert
            Assert.That(eased, Is.EqualTo(0.5f));
        }

        [Test]
        public void Given_ADashTransitionPastItsEnd_When_Eased_Then_ItHasEnded()
        {
            // Arrange
            var binding = new DivideDashChildBinding { DurationSec = 1f, Easing = EasingMode.Linear };

            // Act
            var eased = DivideDashPainter.Eased(binding, 2.0, out var ended);

            // Assert
            Assert.That((eased, ended), Is.EqualTo((1f, true)));
        }

        [Test]
        public void Given_ADividedChild_When_ItGainsABorderWidthClassOfItsOwn_Then_TheDividerGivesWayToIt()
        {
            // Arrange — the class reaches the child alone, so only the container watching the child re-applies.
            // A reversed row puts the divider on the middle child's right edge under either divider rule.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row-reverse divide-x", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var child = scope.Root[0][1];
            var divided = child.style.borderRightWidth.value;

            // Act
            StyleClassProjection.Add(child, "border-r-2", StyleLayerPriority.Base);

            // Assert — the divider before the class rides along, since an edge never divided reads Null too.
            Assert.That((divided, child.style.borderRightWidth.keyword), Is.EqualTo((1f, StyleKeyword.Null)));
        }

        [Test]
        public void Given_AColoredDashedDivideRow_When_ADividedChildCarriesAnArbitraryBorderColor_Then_TheDashTakesIt()
        {
            // Arrange — the child's own color beats Tailwind's zero-specificity divide color on the dashed edge too.
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                DividerRowWithColoredChild("flex flex-row divide-x divide-dashed divide-gray-200", "border-[#ff0000]"),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(DivideDashPainter.PaintColor(scope.Reconciler.Context.DivideDashBindings[scope.Root[0][1]]), Is.EqualTo(Color.red));
        }

        // GREEN_ON_BASE(characterization): the base already paints a dashed divider in the divide color. What
        // reddens it is DivideDashPainter.PaintColor passing over the divider color.
        [Test]
        public void Given_AColoredDashedDivideRow_When_Reconciled_Then_TheDashTakesTheDividerColor()
        {
            // Arrange
            ColorUtility.TryParseHtmlString("#e5e7eb", out var gray200);
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row divide-x divide-dashed divide-gray-200", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(DivideDashPainter.PaintColor(scope.Reconciler.Context.DivideDashBindings[scope.Root[0][1]]), Is.EqualTo(gray200));
        }

        [Test]
        public void Given_AColoredDashedDivideRow_When_ADividedChildCarriesAColorClassOfItsOwn_Then_TheDividerColorStandsDown()
        {
            // Arrange — border-default outranks the zero-specificity divide color, as it does under a solid divider.
            ColorUtility.TryParseHtmlString("#e5e7eb", out var gray200);
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                DividerRowWithColoredChild("flex flex-row divide-x divide-dashed divide-gray-200", "border-default"),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(DivideDashPainter.PaintColor(scope.Reconciler.Context.DivideDashBindings[scope.Root[0][1]]), Is.Not.EqualTo(gray200));
        }

        [Test]
        public void Given_AColoredDashedDivideRow_When_AHoverPayloadGivesADividedChildABracketColor_Then_TheDashTakesIt()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                DividerRowWithColoredChild("flex flex-row divide-x divide-dashed divide-gray-200", "hover:border-[#ff0000]"),
            };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var child = scope.Root[0][1];
            var before = DivideDashPainter.PaintColor(scope.Reconciler.Context.DivideDashBindings[child]);

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                child.SimulateEvent(over);
            }

            // Assert — the color before the hover rides along, since a dash that always took red would pass too.
            Assert.That((before == Color.red, DivideDashPainter.PaintColor(scope.Reconciler.Context.DivideDashBindings[child])),
                Is.EqualTo((false, Color.red)));
        }

        [Test]
        public void Given_AColoredDashedDivideRow_When_Reconciled_Then_ADividedChildsOtherEdgesTakeTheColor()
        {
            // Arrange
            ColorUtility.TryParseHtmlString("#e5e7eb", out var gray200);
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row divide-x divide-dashed divide-gray-200", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert — the dash binding rides along, since a solid divider colors the edges through another path.
            Assert.That((scope.Reconciler.Context.DivideDashBindings.ContainsKey(scope.Root[0][1]),
                    scope.Root[0][1].style.borderTopColor.value),
                Is.EqualTo((true, gray200)));
        }

        [Test]
        public void Given_AColoredDivideRow_When_ADividedChildCarriesAnArbitraryBorderColor_Then_ThatColorWins()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { DividerRowWithColoredChild("flex flex-row divide-x divide-gray-200", "border-[#ff0000]") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert — every edge, so a divider that overwrites the color on whichever edge it sits on reddens it.
            var style = scope.Root[0][1].style;
            Assert.That((style.borderTopColor.value, style.borderRightColor.value, style.borderBottomColor.value,
                    style.borderLeftColor.value),
                Is.EqualTo((Color.red, Color.red, Color.red, Color.red)));
        }

        [Test]
        public void Given_AColoredDivideRow_When_ItsDivideClassesLeave_Then_TheChildrensOtherEdgesAreHandedBack()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row divide-x divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var colored = scope.Root[0][1].style.borderTopColor.keyword;

            // Act
            var tree2 = new VNode[] { Row("flex flex-row", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the color before the patch rides along, since an edge never colored reads Null too.
            Assert.That((colored, scope.Root[0][1].style.borderTopColor.keyword),
                Is.EqualTo((StyleKeyword.Undefined, StyleKeyword.Null)));
        }

        private static VNode DividerRowWithColoredChild(string className, string childBorderClass)
            => V.Div(className: className, children: new VNode[]
            {
                V.Div(className: "child"),
                V.Div(className: "child " + childBorderClass),
                V.Div(className: "child"),
            });

        private static VNode Row(string className, int childCount)
        {
            var children = new VNode[childCount];
            for (var i = 0; i < childCount; i++)
            {
                children[i] = V.Div(className: "child");
            }
            return V.Div(className: className, children: children);
        }

        private static VNode KeyedLabels(string className, int childCount)
        {
            var children = new VNode[childCount];
            for (var i = 0; i < childCount; i++)
            {
                children[i] = V.Label(text: "x", key: "item-" + i);
            }
            return V.Div(className: className, children: children);
        }
    }

    /// <summary>
    /// Specifies which PHYSICAL edge of a divided child carries the divider. The axis comes from the class
    /// (<c>divide-x</c> / <c>divide-y</c>), and the edge within that axis from the <c>divide-x-reverse</c> /
    /// <c>divide-y-reverse</c> marker alone, as in Tailwind v4: the end edge (<c>border-right</c> /
    /// <c>border-bottom</c>) without it, the start edge with it, whatever the container's direction.
    /// </summary>
    /// <remarks>
    /// The manipulator writes INLINE borders, so the applied edge is observable via <c>element.style.border*</c>
    /// without a panel or a layout tick.
    /// </remarks>
    [TestFixture]
    internal sealed class DividerEdgeDirectionTests
    {
        [Test]
        public void Given_ADividedRow_When_TheReconcilerRemovesADividedChild_Then_TheRemovedElementCarriesNoDivider()
        {
            // Arrange — the divide twin of the gap case: a plain Div is discarded rather than pooled, and the
            // reference stands in for user code that kept it.
            using var scope = new ReconcilerScope();
            VNode RowOf(params string[] keys)
            {
                var children = new VNode[keys.Length];
                for (var i = 0; i < keys.Length; i++)
                {
                    children[i] = V.Div(className: "child", key: keys[i]);
                }
                return V.Div(className: "flex flex-row divide-x", children: children);
            }
            var tree1 = new VNode[] { RowOf("a", "b", "c") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var removed = scope.Root[0][1];
            var divided = removed.style.borderRightWidth.value;

            // Act
            var tree2 = new VNode[] { RowOf("a", "c") };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the width it carried rides along, since an element never divided reads Null too.
            Assert.That((divided, removed.style.borderRightWidth.keyword), Is.EqualTo((1f, StyleKeyword.Null)));
        }

        [Test]
        public void Given_DivideXReverseRow_When_Reconciled_Then_TheFirstChildCarriesTheStartBorder()
        {
            // Arrange — the marker moves the divider to border-inline-start (the left edge) of every child but the last.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row divide-x divide-x-reverse", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][0].style.borderLeftWidth.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_DivideYReverseColumn_When_Reconciled_Then_TheFirstChildCarriesTheTopBorder()
        {
            // Arrange — the vertical twin of the marker.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-col divide-y divide-y-reverse", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][0].style.borderTopWidth.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_FlexRowReverseDivideX_When_Reconciled_Then_TheFirstChildCarriesTheEndBorder()
        {
            // Arrange — a plain divide-x with NO marker on a reversed row: Tailwind's divider never reads flex-direction, so
            // the border stays on the end edge of every child but the last until divide-x-reverse moves it.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row-reverse divide-x", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][0].style.borderRightWidth.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_FlexRowReverseDivideX_When_Reconciled_Then_TheLastChildCarriesNoBorder()
        {
            // Arrange — the other half of the same rule: the last child in the class list takes no divider, reversed or not.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row-reverse divide-x", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][2].style.borderRightWidth.value, Is.EqualTo(0f));
        }

        [Test]
        public void Given_FlexColReverseDivideY_When_Reconciled_Then_TheFirstChildCarriesTheBottomBorder()
        {
            // Arrange — the vertical twin: a plain divide-y with no marker on a reversed column.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-col-reverse divide-y", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][0].style.borderBottomWidth.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_FlexRowReverseWithDivideXReverse_When_Reconciled_Then_TheFirstChildCarriesTheStartBorder()
        {
            // Arrange — the idiom Tailwind documents for a reversed row: the marker moves the divider to the start edge.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row-reverse divide-x divide-x-reverse", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][0].style.borderLeftWidth.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_DivideYWithAHorizontalReverseMarker_When_Reconciled_Then_TheVerticalDividerStaysOnTheBottomEdge()
        {
            // Arrange — a horizontal marker does not move a vertical divider.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-col divide-y divide-x-reverse", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderBottomWidth.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_AnEndDivider_When_TheReverseMarkerIsAddedByPatch_Then_TheStaleEndWidthIsCleared()
        {
            // Arrange — establish the end edge first.
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row divide-x divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var width = scope.Root[0][1].style.borderRightWidth.value;

            // Act — patch in the marker: the edge flips, so the abandoned gutter must be released, not just
            // a second one added (two live gutters would inset the child from both sides).
            var tree2 = new VNode[] { Row("flex flex-row divide-x divide-x-reverse divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the width before the patch rides along, since an edge never written reads Null too.
            Assert.That((width, scope.Root[0][1].style.borderRightWidth.keyword),
                Is.EqualTo((1f, StyleKeyword.Null)));
        }

        [Test]
        public void Given_AColoredEndDivider_When_TheReverseMarkerIsAddedByPatch_Then_EveryEdgeKeepsTheColor()
        {
            // Arrange — a divide-{color} colors every edge of a divided child, as Tailwind's border-color does,
            // so handing the abandoned edge's width back must leave its color, and the flip must not strip the
            // edges the divider never sat on.
            ColorUtility.TryParseHtmlString("#e5e7eb", out var gray200);
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row divide-x divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);

            // Act
            var tree2 = new VNode[] { Row("flex flex-row divide-x divide-x-reverse divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert
            var style = scope.Root[0][1].style;
            Assert.That((style.borderTopColor.value, style.borderRightColor.value, style.borderBottomColor.value,
                    style.borderLeftColor.value),
                Is.EqualTo((gray200, gray200, gray200, gray200)));
        }

        [Test]
        public void Given_AStartDivider_When_TheReverseMarkerIsRemovedByPatch_Then_TheStaleStartWidthIsCleared()
        {
            // Arrange — the mirror image: establish the start edge, then patch the marker away.
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row divide-x divide-x-reverse divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var width = scope.Root[0][1].style.borderLeftWidth.value;

            // Act
            var tree2 = new VNode[] { Row("flex flex-row divide-x divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert
            Assert.That((width, scope.Root[0][1].style.borderLeftWidth.keyword),
                Is.EqualTo((1f, StyleKeyword.Null)));
        }

        [Test]
        public void Given_AReversedDashedStartDivider_When_Reconciled_Then_ThePaintTargetsTheStartEdge()
        {
            // Arrange — the dashed paint follows the edge the marker picks.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row divide-x divide-x-reverse divide-dashed divide-gray-200", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Reconciler.Context.DivideDashBindings[scope.Root[0][1]].Edge, Is.EqualTo(DivideEdge.Left));
        }

        [Test]
        public void Given_AChildOfAReversedDivideContainer_When_ItIsMovedOut_Then_NoResidualTrailingBorder()
        {
            // Arrange — the divider sits on the right edge, so the reset a departing child gets has to reach
            // that edge: the container's abandoned-edge clear only walks children that are still members, and
            // nothing else revisits one that left.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row-reverse divide-x", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = scope.Root[0];
            var manipulator = scope.Reconciler.Context.DivideManipulators[container];
            var movedChild = container[0];
            var divided = movedChild.style.borderRightWidth.value;

            // Act — move the child out of the divide container (a sibling reparent), then re-apply.
            container.Remove(movedChild);
            var sink = new VisualElement();
            sink.Add(movedChild);
            manipulator.Apply();

            // Assert — the divider it carried in the container rides along.
            Assert.That((divided, movedChild.style.borderRightWidth.keyword), Is.EqualTo((1f, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ADividedChildWithItsOwnBorderOnAnotherEdge_When_TheDivideIsRemoved_Then_ThatBorderSurvives()
        {
            // Arrange — a divide-x row whose second child carries its own border on an edge the divider never
            // claims. Now that a divider can land on any of the four edges, the teardown reset must cover the
            // edges this container actually used and no more, or removing the divide class silently erases a
            // border that was never the divider's to own.
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row divide-x", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var child = scope.Root[0][1];
            child.style.borderBottomWidth = 3f;
            var divided = child.style.borderRightWidth.value;

            // Act — patch the divide class away, which tears the manipulator down.
            var tree2 = new VNode[] { Row("flex flex-row", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the divider on the right edge rides along, so the bottom one is the child's own.
            Assert.That((divided, child.style.borderBottomWidth.value), Is.EqualTo((1f, 3f)));
        }

        private static VNode Row(string className, int childCount)
        {
            var children = new VNode[childCount];
            for (var i = 0; i < childCount; i++)
            {
                children[i] = V.Div(className: "child");
            }
            return V.Div(className: className, children: children);
        }
    }

    /// <summary>
    /// On-panel coverage for the divider edge with the bundled <c>StyleUtilities.uss</c> attached, so the
    /// reversed row really lays out right-to-left while the divider stays on each non-last child's end edge.
    /// </summary>
    [TestFixture]
    internal sealed class DividerEdgePanelTests : PanelTestBase
    {
        private const string StyleSheetPath = "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";

        protected override void LoadStyleSheets()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            _window.rootVisualElement.styleSheets.Add(sheet);
        }

        [Test]
        public void Given_AFlexRowReverseDivideContainer_When_PanelResolves_Then_TheFirstChildCarriesTheEndBorder()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "row", className: "flex flex-row-reverse divide-x", children: new VNode[]
                {
                    V.Label(name: "a", text: "a"),
                    V.Label(name: "b", text: "b"),
                }));
            var row = _window.rootVisualElement.Q<VisualElement>("row");

            // Act
            ForcePanelUpdate(row.panel);
            using var evt = EventBase<GeometryChangedEvent>.GetPooled();
            row.SimulateEvent(evt);

            // Assert — the resolved direction rides along: it has to be the reversed row for this to say
            // anything about a reversed container.
            var a = _window.rootVisualElement.Q<Label>("a");
            Assert.That((row.resolvedStyle.flexDirection, a.resolvedStyle.borderRightWidth),
                Is.EqualTo((FlexDirection.RowReverse, 1f)));
        }

        // The width the dash is stroked at for the middle child of a dashed row whose class is childClass.
        private (bool Dashed, float Width) MiddleDash(string childClass)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "row", className: "flex flex-row divide-x divide-dashed", children: new VNode[]
                {
                    V.Div(className: "w-[20px] h-[20px]"),
                    V.Div(name: "b", className: "w-[20px] h-[20px] " + childClass),
                    V.Div(className: "w-[20px] h-[20px]"),
                }));
            var b = _window.rootVisualElement.Q("b");
            ForcePanelUpdate(b.panel);
            var dashed = _mounted.Root.Reconciler.Context.DivideDashBindings.TryGetValue(b, out var binding);
            return (dashed, dashed ? binding.Width : 0f);
        }

        // GREEN_ON_BASE(characterization): the base strokes a dashed divider at the divider's width. What reddens
        // it is the dashed path dropping its WriteWidth, which leaves the edge no width to stroke at.
        [Test]
        public void Given_ADashedDivideRow_When_LaidOut_Then_TheDashIsAsWideAsTheDivider()
        {
            // Arrange / Act
            var dash = MiddleDash("");

            // Assert
            Assert.That(dash, Is.EqualTo((true, 1f)));
        }

        // A colored dashed row whose middle child carries childClass, beside one reference element per class in
        // referenceClasses, each carrying that class alone so the color it resolves to is read the engine's way.
        private (VisualElement Child, VisualElement[] References) MountColoredDashRow(string childClass,
            params string[] referenceClasses)
        {
            var nodes = new VNode[referenceClasses.Length + 1];
            nodes[0] = V.Div(className: "flex flex-row divide-x divide-dashed divide-gray-200", children: new VNode[]
            {
                V.Div(className: "w-[20px] h-[20px]"),
                V.Div(name: "b", className: "w-[20px] h-[20px] " + childClass),
                V.Div(className: "w-[20px] h-[20px]"),
            });
            for (var i = 0; i < referenceClasses.Length; i++)
            {
                nodes[i + 1] = V.Div(name: "ref" + i, className: "border-r " + referenceClasses[i]);
            }
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: nodes));
            var child = _window.rootVisualElement.Q("b");
            ForcePanelUpdate(child.panel);
            var found = new VisualElement[referenceClasses.Length];
            for (var i = 0; i < found.Length; i++)
            {
                found[i] = _window.rootVisualElement.Q("ref" + i);
            }
            return (child, found);
        }

        // The color the child's last paint drew its dash in.
        private Color DashColorOf(VisualElement child)
            => _mounted.Root.Reconciler.Context.DivideDashBindings[child].Color;

        [Test]
        public void Given_AColoredDashedDivideRow_When_ADividedChildCarriesAThemeColorClass_Then_TheDashTakesItsColorOnFirstBind()
        {
            // Arrange / Act
            ColorUtility.TryParseHtmlString("#e5e7eb", out var gray200);
            var (child, references) = MountColoredDashRow("border-default", "border-default");
            var theme = references[0].resolvedStyle.borderRightColor;

            // Assert — the theme color differing from the divide color rides along, since equal colors would
            // pass whichever the dash took.
            Assert.That((DashColorOf(child), theme == gray200), Is.EqualTo((theme, false)));
        }

        [Test]
        public void Given_AColoredDashedDivideRow_When_ADividedChildCarriesAPaletteBorderClass_Then_TheDashTakesItsColor()
        {
            // Arrange / Act
            var (child, references) = MountColoredDashRow("border-red-500", "border-red-500");

            // Assert
            Assert.That(DashColorOf(child), Is.EqualTo(references[0].resolvedStyle.borderRightColor));
        }

        [Test]
        public void Given_ADashTakingAThemeColorClass_When_TheChildsHoverRuleApplies_Then_TheDashTakesItsColor()
        {
            // Arrange — the pseudo-state is set directly: no pointer reaches an EditMode panel.
            var (child, references) = MountColoredDashRow("border-default hover-border-strong", "border-strong");
            var strong = references[0].resolvedStyle.borderRightColor;
            var before = DashColorOf(child);
            var pseudoStates = typeof(VisualElement).GetProperty("pseudoStates",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var hover = System.Enum.Parse(pseudoStates!.PropertyType, "Hover");

            // Act
            pseudoStates.SetValue(child, hover);
            ForcePanelUpdate(child.panel);

            // Assert — the color before the hover rides along, since a dash that always took the strong color
            // would pass too.
            Assert.That((before == strong, DashColorOf(child)), Is.EqualTo((false, strong)));
        }

        // The dash of the middle child of a dashed row without a divide color, painted once; the child runs a linear
        // border-color transition of a second after delaySec.
        private (VisualElement Child, DivideDashChildBinding Binding) MountTransitionDash(float delaySec)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(className: "flex flex-row divide-x divide-dashed", children: new VNode[]
                {
                    V.Div(className: "w-[20px] h-[20px]"),
                    V.Div(name: "b", className: "w-[20px] h-[20px]"),
                    V.Div(className: "w-[20px] h-[20px]"),
                }));
            var child = _window.rootVisualElement.Q("b");
            child.style.transitionProperty = new List<StylePropertyName> { new("border-color") };
            child.style.transitionDuration = new List<TimeValue> { new(1f, TimeUnit.Second) };
            child.style.transitionDelay = new List<TimeValue> { new(delaySec, TimeUnit.Second) };
            child.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.Linear) };
            ForcePanelUpdate(child.panel);
            return (child, _mounted.Root.Reconciler.Context.DivideDashBindings[child]);
        }

        [Test]
        public void Given_ARunningDashTransition_When_TheColorChangesBackPartWay_Then_TheWayBackIsShortened()
        {
            // Arrange — a quarter of the way to red.
            var (_, binding) = MountTransitionDash(0f);
            var start = binding.Target!.Value;
            DivideDashPainter.Advance(binding, Color.red, 100.0);

            // Act
            DivideDashPainter.Advance(binding, start, 100.25);

            // Assert
            Assert.That(binding.DurationSec, Is.EqualTo(0.25f).Within(1e-4f));
        }

        [Test]
        public void Given_ARunningDashTransition_When_TheColorChangesToAThirdOne_Then_TheNewOneRunsWhole()
        {
            // Arrange
            var (_, binding) = MountTransitionDash(0f);
            DivideDashPainter.Advance(binding, Color.red, 100.0);

            // Act
            DivideDashPainter.Advance(binding, Color.blue, 100.25);

            // Assert
            Assert.That(binding.DurationSec, Is.EqualTo(1f));
        }

        [Test]
        public void Given_ARunningDashTransitionWithANegativeDelay_When_TheColorChangesBack_Then_TheDelayIsShortenedToo()
        {
            // Arrange — 0.45 of the way to red: a quarter of a second in, plus the 0.2 second the delay skips.
            var (_, binding) = MountTransitionDash(-0.2f);
            var start = binding.Target!.Value;
            DivideDashPainter.Advance(binding, Color.red, 100.0);

            // Act
            DivideDashPainter.Advance(binding, start, 100.25);

            // Assert
            Assert.That(binding.DelaySec, Is.EqualTo(-0.09f).Within(1e-4f));
        }

        // The middle child of a dashed row without a divide color, painted once, and the colors the engine paints its
        // divided edge in from then on: each read as the child paints, ahead of the dash.
        private (VisualElement Child, List<Color> Painted) MountWatchedDash(string childClass = "")
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(className: "flex flex-row divide-x divide-dashed", children: new VNode[]
                {
                    V.Div(className: "w-[20px] h-[20px]"),
                    V.Div(name: "b", className: "w-[20px] h-[20px] " + childClass),
                    V.Div(className: "w-[20px] h-[20px]"),
                }));
            var child = _window.rootVisualElement.Q("b");
            ForcePanelUpdate(child.panel);
            var binding = _mounted.Root.Reconciler.Context.DivideDashBindings[child];
            var painted = new List<Color>();
            child.generateVisualContent -= binding.OnGenerate;
            child.generateVisualContent += _ => painted.Add(child.resolvedStyle.borderRightColor);
            child.generateVisualContent += binding.OnGenerate;
            return (child, painted);
        }

        [Test]
        public void Given_ADashedDivideRow_When_TheChildsCodeWritesItsEdgeColor_Then_ItIsMaskedBeforeItPaints()
        {
            // Arrange
            var (child, painted) = MountWatchedDash();

            // Act — a frame: the panel's scheduled items, then its styles, layout and paint.
            child.style.borderRightColor = Color.blue;
            EditorPanelTestHelpers.DriveSchedulerOnce(child.panel);
            ForcePanelUpdate(child.panel);

            // Assert — the paint rides along, since a check that never ran would leave nothing painted to read.
            Assert.That((painted.Count > 0, painted.TrueForAll(SilhouetteFace.IsSentinel), DashColorOf(child)),
                Is.EqualTo((true, true, Color.blue)));
        }

        [Test]
        public void Given_ADashedDivideRow_When_TheChildsCodeMakesItsEdgeTransparent_Then_TheDashIsTransparentToo()
        {
            // Arrange
            var (child, _) = MountWatchedDash("border-default");
            var before = DashColorOf(child);

            // Act
            child.style.borderRightColor = Color.clear;
            EditorPanelTestHelpers.DriveSchedulerOnce(child.panel);
            ForcePanelUpdate(child.panel);

            // Assert — the theme color before rides along, since a dash that was never painted would read clear too.
            Assert.That((before == Color.clear, DashColorOf(child)), Is.EqualTo((false, Color.clear)));
        }

        [Test]
        public void Given_ADashedDivideRow_When_AMotionDriverDrivesTheChildsBorderColorAndReleasesIt_Then_TheDashFollowsAndLetsGo()
        {
            // Arrange — the calls MotionSpringDriver and BezierTweenDriver make for a border-color channel.
            var (child, painted) = MountWatchedDash();
            var before = DashColorOf(child);
            StyleArbitraryValueResolver.ApplyDriven(child, new ArbitraryStyle(ArbitraryProperty.BorderColor, Color.red));
            ForcePanelUpdate(child.panel);
            var driven = DashColorOf(child);

            // Act — a driver's release: the channel's slots nulled, then the element's own layers and holds.
            StyleArbitraryValueResolver.ReleaseDriven(child, ArbitraryProperty.BorderColor);
            StyleArbitraryValueResolver.ReapplyLayeredValues(child);
            ForcePanelUpdate(child.panel);

            // Assert — the driven color rides along, since a dash that never took it would let go of nothing, and
            // every paint saw the mask on the edge, so the engine painted no solid line there.
            Assert.That((driven, DashColorOf(child) == before, painted.TrueForAll(SilhouetteFace.IsSentinel)),
                Is.EqualTo((Color.red, true, true)));
        }

        [Test]
        public void Given_ADividedChildWithABracketBorderColor_When_AMotionDriverDrivesItsBorderColor_Then_TheDashTakesTheDrivenColor()
        {
            // Arrange — border-[#e5e7eb] is a layer of the child's own, which the driver's inline write outranks.
            var (child, painted) = MountWatchedDash("border-[#e5e7eb]");

            // Act
            StyleArbitraryValueResolver.ApplyDriven(child, new ArbitraryStyle(ArbitraryProperty.BorderColor, Color.red));
            ForcePanelUpdate(child.panel);

            // Assert
            Assert.That((DashColorOf(child), painted.TrueForAll(SilhouetteFace.IsSentinel)), Is.EqualTo((Color.red, true)));
        }

        // A dashed row whose middle child is a Motion entering from border-red-500 to border-blue-500 on a spring,
        // beside reference elements carrying each.
        private (VisualElement Child, Color Red, Color Blue) MountDrivenDashRow()
        {
            var poses = new Dictionary<string, MotionVariant> { ["dim"] = "border-red-500", ["lit"] = "border-blue-500" };
            var spring = new StyleTransitionConfig { Type = TransitionType.Spring, Stiffness = 100f, Damping = 10f, Mass = 1f };
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: new VNode[]
            {
                V.Div(className: "flex flex-row divide-x divide-dashed", children: new VNode[]
                {
                    V.Div(className: "w-[20px] h-[20px]"),
                    V.Motion(name: "b", className: "w-[20px] h-[20px]", variants: poses, initial: "dim", animate: "lit",
                        transition: spring),
                    V.Div(className: "w-[20px] h-[20px]"),
                }),
                V.Div(name: "ref0", className: "border-r border-red-500"),
                V.Div(name: "ref1", className: "border-r border-blue-500"),
            }));
            var child = _window.rootVisualElement.Q("b");
            ForcePanelUpdate(child.panel);
            return (child, _window.rootVisualElement.Q("ref0").resolvedStyle.borderRightColor,
                _window.rootVisualElement.Q("ref1").resolvedStyle.borderRightColor);
        }

        [Test]
        public void Given_AMotionEnteringItsBorderColorInADashedRow_When_Mounted_Then_TheDashTakesTheDrivenColor()
        {
            // Act — the drive starts inside the child's mount, before the row's divider first applies.
            var (child, red, _) = MountDrivenDashRow();

            // Assert
            Assert.That(DashColorOf(child), Is.EqualTo(red));
        }

        [Test]
        public void Given_AMotionEnteringItsBorderColorInADashedRow_When_TheDriveIsReleased_Then_TheDashTakesTheRestingColor()
        {
            // Arrange
            var (child, _, blue) = MountDrivenDashRow();

            // Act
            _mounted.Root.Reconciler.Context.StyleAnimationScheduler.CancelEnter(child);
            EditorPanelTestHelpers.DriveSchedulerOnce(child.panel);
            ForcePanelUpdate(child.panel);

            // Assert
            Assert.That(DashColorOf(child), Is.EqualTo(blue));
        }

        // A colored dashed row whose middle child takes border-red-500 while hovered, beside a reference carrying
        // that class alone. The class reaches the child through the gesture channel, which tells the divider
        // nothing, and with all four edges held or masked no value the engine paints the child by moves.
        private (VisualElement Child, Color Red) MountGestureColoredDashRow()
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: new VNode[]
            {
                V.Div(className: "flex flex-row divide-x divide-dashed divide-gray-200", children: new VNode[]
                {
                    V.Div(className: "w-[20px] h-[20px]"),
                    V.Div(name: "b", className: "w-[20px] h-[20px]", whileHoverClass: "border-red-500"),
                    V.Div(className: "w-[20px] h-[20px]"),
                }),
                V.Div(name: "ref0", className: "border-r border-red-500"),
            }));
            var child = _window.rootVisualElement.Q("b");
            ForcePanelUpdate(child.panel);
            return (child, _window.rootVisualElement.Q("ref0").resolvedStyle.borderRightColor);
        }

        [Test]
        public void Given_ADashedDivideRow_When_AGestureClassGivesADividedChildAColor_Then_TheDashIsPaintedInIt()
        {
            // Arrange
            var (child, red) = MountGestureColoredDashRow();

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                child.SimulateEvent(over);
            }
            ForcePanelUpdate(child.panel);

            // Assert
            Assert.That(DashColorOf(child), Is.EqualTo(red));
        }

        [Test]
        public void Given_ADashPaintedInAGestureColor_When_ThePointerLeaves_Then_TheDashIsPaintedInTheDivideColorAgain()
        {
            // Arrange
            ColorUtility.TryParseHtmlString("#e5e7eb", out var gray200);
            var (child, red) = MountGestureColoredDashRow();
            using (var over = PointerOverEvent.GetPooled())
            {
                child.SimulateEvent(over);
            }
            ForcePanelUpdate(child.panel);
            var hovered = DashColorOf(child);

            // Act
            using (var leave = PointerOutEvent.GetPooled())
            {
                child.SimulateEvent(leave);
            }
            ForcePanelUpdate(child.panel);

            // Assert — the hovered color rides along, since a dash that never took red would pass too.
            Assert.That((hovered == red, DashColorOf(child)), Is.EqualTo((true, gray200)));
        }

        [Test]
        public void Given_ADashedDivideRow_When_TheChildsCodeWritesItsEdgeColorAfterMount_Then_TheDashKeepsIt()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(className: "flex flex-row divide-x divide-dashed", children: new VNode[]
                {
                    V.Div(className: "w-[20px] h-[20px]"),
                    V.Div(name: "b", className: "w-[20px] h-[20px]"),
                    V.Div(className: "w-[20px] h-[20px]"),
                }));
            var child = _window.rootVisualElement.Q("b");
            ForcePanelUpdate(child.panel);
            child.style.borderRightColor = Color.blue;

            // Act — the container applies again, as it does when the child's classes change.
            StyleArbitraryValueResolver.NotifyClassesChanged(child);
            ForcePanelUpdate(child.panel);

            // Assert
            Assert.That(DashColorOf(child), Is.EqualTo(Color.blue));
        }

        [Test]
        public void Given_ADashOnAChildWithAColorTransition_When_ItsColorClassChanges_Then_TheDashRunsFromTheOldColorToTheNew()
        {
            // Arrange
            var (child, references) = MountColoredDashRow("border-default transition-colors", "border-default",
                "border-accent");
            var oldColor = references[0].resolvedStyle.borderRightColor;
            var newColor = references[1].resolvedStyle.borderRightColor;
            StyleClassProjection.Remove(child, "border-default", StyleLayerPriority.Base);
            StyleClassProjection.Add(child, "border-accent", StyleLayerPriority.Base);
            ForcePanelUpdate(child.panel);
            var started = DashColorOf(child);

            var binding = _mounted.Root.Reconciler.Context.DivideDashBindings[child];

            // Act — past the end of the transition.
            binding.StartTime = double.NegativeInfinity;
            child.MarkDirtyRepaint();
            ForcePanelUpdate(child.panel);

            // Assert — the two classes resolving apart ride along, since equal colors would pass either way, and so
            // does the tick, which stops repainting the child once the transition has ended.
            Assert.That((started, DashColorOf(child), oldColor == newColor, binding.Tick.isActive),
                Is.EqualTo((oldColor, newColor, false, false)));
        }

        // A dash on a child carrying border-default transition-colors, part way through its transition to
        // border-accent.
        private (VisualElement Child, DivideDashChildBinding Binding, VisualElement[] References)
            MountRunningDashTransition()
        {
            var (child, references) = MountColoredDashRow("border-default transition-colors", "border-default",
                "border-accent", "border-strong");
            StyleClassProjection.Remove(child, "border-default", StyleLayerPriority.Base);
            StyleClassProjection.Add(child, "border-accent", StyleLayerPriority.Base);
            ForcePanelUpdate(child.panel);
            return (child, _mounted.Root.Reconciler.Context.DivideDashBindings[child], references);
        }

        [Test]
        public void Given_ARunningDashTransition_When_TheColorChangesWithTheTransitionRemoved_Then_TheDashTakesTheNewColorAtOnce()
        {
            // Arrange
            var (child, binding, references) = MountRunningDashTransition();
            var running = binding.Tick.isActive;
            var strong = references[2].resolvedStyle.borderRightColor;

            // Act
            StyleClassProjection.Remove(child, "transition-colors", StyleLayerPriority.Base);
            StyleClassProjection.Remove(child, "border-accent", StyleLayerPriority.Base);
            StyleClassProjection.Add(child, "border-strong", StyleLayerPriority.Base);
            ForcePanelUpdate(child.panel);

            // Assert — the transition running before the change rides along, since one that never ran would leave
            // nothing to stop.
            Assert.That((running, DashColorOf(child)), Is.EqualTo((true, strong)));
        }

        [Test]
        public void Given_ARunningDashTransition_When_ItsTransitionEntryIsRemoved_Then_TheDashLandsAtTheTarget()
        {
            // Arrange
            var (child, binding, references) = MountRunningDashTransition();
            var running = binding.Tick.isActive;
            var accent = references[1].resolvedStyle.borderRightColor;

            // Act — the tick the running transition repaints on, then the paint; the transition is made long enough
            // to be running still whatever time the run takes.
            binding.DurationSec = 1000f;
            StyleClassProjection.Remove(child, "transition-colors", StyleLayerPriority.Base);
            ForcePanelUpdate(child.panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(child.panel);
            ForcePanelUpdate(child.panel);

            // Assert
            Assert.That((running, DashColorOf(child), binding.Tick.isActive), Is.EqualTo((true, accent, false)));
        }

        [Test]
        public void Given_ARunningDashTransition_When_TheDashIsDetached_Then_ItsTickMarkerClassAndMaskAllGo()
        {
            // Arrange
            var (child, binding, _) = MountRunningDashTransition();
            var running = binding.Tick.isActive;

            // Act — and a color is written after it, which no mask should take any longer.
            DivideDashPainter.Detach(child, binding);
            child.style.borderRightColor = Color.red;
            EditorPanelTestHelpers.DriveSchedulerOnce(child.panel);

            // Assert
            Assert.That((running, binding.Tick.isActive, child.ClassListContains(DivideDashPainter.MarkerClass),
                child.style.borderRightColor.value), Is.EqualTo((true, false, false, Color.red)));
        }

        [Test]
        public void Given_ADashTakingAThemeColorClass_When_TheChildsColorClassChanges_Then_TheDashTakesTheNewColor()
        {
            // Arrange
            var (child, references) = MountColoredDashRow("border-default", "border-default", "border-accent");
            var oldColor = references[0].resolvedStyle.borderRightColor;
            var newColor = references[1].resolvedStyle.borderRightColor;
            var before = DashColorOf(child);

            // Act
            StyleClassProjection.Remove(child, "border-default", StyleLayerPriority.Base);
            StyleClassProjection.Add(child, "border-accent", StyleLayerPriority.Base);
            ForcePanelUpdate(child.panel);

            // Assert — the color before the change rides along, so a dash that never took the first class
            // cannot pass, and so do the two classes resolving apart.
            Assert.That((before, DashColorOf(child), oldColor == newColor), Is.EqualTo((oldColor, newColor, false)));
        }

        [Test]
        public void Given_ADashedDivideRow_When_ADividedChildCarriesAnArbitraryBorderWidth_Then_ThatWidthWins()
        {
            // Arrange / Act
            var dash = MiddleDash("border-r-[3px]");

            // Assert
            Assert.That(dash, Is.EqualTo((true, 3f)));
        }

        [Test]
        public void Given_ADashedDivideRow_When_ADividedChildCarriesABorderWidthClass_Then_ThatWidthIsDashed()
        {
            // Arrange / Act — Tailwind's border-r-2 takes the border style divide-dashed set.
            var dash = MiddleDash("border-r-2");

            // Assert
            Assert.That(dash, Is.EqualTo((true, 2f)));
        }
    }
}
