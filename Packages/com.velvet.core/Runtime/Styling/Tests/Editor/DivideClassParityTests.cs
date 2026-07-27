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
    /// Specifies the <c>divide-x</c> / <c>divide-y</c> utilities, which draw a border between
    /// adjacent children (<c>&gt; * + *</c>); UITK has no <c>:first-child</c> and no <c>&gt; *</c> child
    /// combinator, so <see cref="StyleDivideManipulator"/> writes that border on every child but the first —
    /// the same shape <see cref="StyleGapManipulator"/> uses for gap. Width comes from <c>divide-x</c> (1px),
    /// the <c>divide-x-{0,2,4,8}</c> scale, or the <c>divide-x-[Npx]</c> arbitrary form; color from
    /// <c>divide-{palette}</c> or <c>divide-[#hex]</c>. UITK has no border-style, so <c>divide-dashed</c> /
    /// <c>divide-dotted</c> are painted by <see cref="DivideDashPainter"/> on each divided child instead.
    /// Which PHYSICAL edge carries the border comes from the container's resolved direction and the
    /// <c>divide-x-reverse</c> / <c>divide-y-reverse</c> markers — see <see cref="DividerEdgeDirectionTests"/>.
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
        public void Given_DivideXRow_When_Reconciled_Then_SecondChildHasLeadingBorderWidth()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row divide-x divide-gray-200", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert — the divider sits on the left edge of the 2nd child onward.
            Assert.That(scope.Root[0][1].style.borderLeftWidth.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_DivideXRow_When_Reconciled_Then_FirstChildHasNoLeadingBorder()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row divide-x divide-gray-200", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert — the first child carries no divider (the `> * + *` rule starts at the second child).
            Assert.That(scope.Root[0][0].style.borderLeftWidth.value, Is.EqualTo(0f));
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
            Assert.That(scope.Root[0][1].style.borderLeftColor.value, Is.EqualTo(gray200));
        }

        [Test]
        public void Given_DivideYRow_When_Reconciled_Then_SecondChildHasTopBorderWidth()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-col divide-y divide-gray-200", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert — divide-y draws on the top edge.
            Assert.That(scope.Root[0][1].style.borderTopWidth.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_DivideXRow_When_ClassRemovedByPatch_Then_StaleBorderCleared()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row divide-x divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            Assume.That(scope.Root[0][1].style.borderLeftWidth.value, Is.EqualTo(1f), "Precondition: divider applied");

            // Act — patch the same container without the divide class.
            var tree2 = new VNode[] { Row("flex flex-row", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the manipulator's leading border is cleared (no ghost).
            Assert.That(scope.Root[0][1].style.borderLeftWidth.value, Is.EqualTo(0f));
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
            Assume.That(scope.Root[0][1].style.borderLeftColor.value, Is.EqualTo(gray200), "Precondition: divider colored");

            // Act — keep divide-x, drop divide-gray-200.
            var tree2 = new VNode[] { Row("flex flex-row divide-x", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the stale palette color is cleared (the divider reverts to the default border color).
            Assert.That(scope.Root[0][1].style.borderLeftColor.value, Is.Not.EqualTo(gray200));
        }

        [Test]
        public void Given_DivideXRow_When_PatchedToDivideY_Then_LeftEdgeClearedAndTopApplied()
        {
            // Arrange — a horizontal divider.
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row divide-x divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            Assume.That(scope.Root[0][1].style.borderLeftWidth.value, Is.EqualTo(1f), "Precondition: left divider applied");

            // Act — flip the axis to vertical (the manipulator clears the old edge before writing the new).
            var tree2 = new VNode[] { Row("flex flex-col divide-y divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the old left edge is cleared and the new top edge is applied.
            Assert.That(
                (scope.Root[0][1].style.borderLeftWidth.value, scope.Root[0][1].style.borderTopWidth.value),
                Is.EqualTo((0f, 1f)));
        }

        [Test]
        public void Given_DivideYScrollView_When_Reconciled_Then_ContentChildrenGetTopDivider()
        {
            // Arrange — a ScrollView redirects children into its contentContainer; the divider must land on
            // the reconciled content, not the ScrollView's internal hierarchy (mirrors the gap hardening case).
            using var scope = new ReconcilerScope();
            var children = new VNode[] { V.Div(className: "child"), V.Div(className: "child"), V.Div(className: "child") };
            var tree = new VNode[] { V.ScrollView("flex flex-col divide-y divide-gray-200", children) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var content = ((ScrollView)scope.Root[0]).contentContainer;

            // Assert — the divider sits on the 2nd content child's top edge.
            Assert.That(content[1].style.borderTopWidth.value, Is.EqualTo(1f));
        }

        #endregion

        #region End-to-end (dashed / dotted dividers)

        [Test]
        public void Given_DivideXDashedRow_When_Reconciled_Then_TheDividerGutterMatchesSolid()
        {
            // Arrange — a dashed divider must reserve the SAME layout gutter as a solid one (only the paint
            // differs), so its leading border WIDTH stays real (the color is what gets masked).
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row divide-x divide-dashed divide-gray-200", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderLeftWidth.value, Is.EqualTo(1f));
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
            Assert.That(SilhouetteFace.IsSentinel(scope.Root[0][1].style.borderLeftColor.value), Is.True);
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
        public void Given_DivideXDashedRow_When_Reconciled_Then_TheFirstChildGetsNoPaintCallback()
        {
            // Arrange — only actual divider children (the 2nd onward) get a paint; the first has no leading divider.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row divide-x divide-dashed divide-gray-200", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][0].generateVisualContent, Is.Null);
        }

        [Test]
        public void Given_DivideXDashedRow_When_FlippedToSolid_Then_TheDividerColorIsReleased()
        {
            // Arrange — a dashed divider (color masked by the sentinel).
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row divide-x divide-dashed divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            Assume.That(SilhouetteFace.IsSentinel(scope.Root[0][1].style.borderLeftColor.value), Is.True,
                "Precondition: the dashed divider masked the border color");

            // Act — flip to a solid divider; the sentinel is released back to a real color.
            var tree2 = new VNode[] { Row("flex flex-row divide-x divide-solid divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert
            Assert.That(SilhouetteFace.IsSentinel(scope.Root[0][1].style.borderLeftColor.value), Is.False);
        }

        [Test]
        public void Given_DivideXDashedKeyedLabels_When_OneChildIsRemoved_Then_TheDividerPaintCountTracksTheDividers()
        {
            // Arrange — pooled Label children (keyed) under a dashed divide: children 2 and 3 each get a paint
            // binding. Removing the last child recycles it and must shed its binding, leaving one divider.
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
            Assume.That(scope.Reconciler.Context.DivideDashBindings[scope.Root[0][1]].Color, Is.EqualTo(red),
                "Precondition: the dashed divider captured the child's initial border color");

            // Act — change the child's own border color; the divider's implicit color must follow.
            var tree2 = new VNode[] { DividerRowWithColoredChild("flex flex-row divide-x divide-dashed", "border-[#00FF00]") };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert
            Assert.That(scope.Reconciler.Context.DivideDashBindings[scope.Root[0][1]].Color, Is.EqualTo(green));
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
    /// (<c>divide-x</c> / <c>divide-y</c>), but the edge within that axis comes from the container's resolved
    /// flex direction plus the <c>divide-x-reverse</c> / <c>divide-y-reverse</c> markers: a reversed container
    /// paints its children in the opposite order, so the edge sitting between a visually adjacent pair is the
    /// axis's trailing one (<c>border-right</c> / <c>border-bottom</c>). Picking the edge from the axis alone
    /// draws one rule on the container's outer edge and leaves the adjacent pair's boundary blank. A marker and
    /// a reversed direction on the same axis combine with OR, never XOR, and the flip is strictly per axis.
    /// </summary>
    /// <remarks>
    /// The manipulator writes INLINE borders, so the applied edge is observable via <c>element.style.border*</c>
    /// without a panel or a layout tick — off-panel the direction resolves from the same class markers it
    /// prefers on a panel, so these assertions exercise the production path rather than an EditMode-only one.
    /// </remarks>
    [TestFixture]
    internal sealed class DividerEdgeDirectionTests
    {
        [Test]
        public void Given_DivideXReverseRow_When_Reconciled_Then_TheSecondChildCarriesTheTrailingBorder()
        {
            // Arrange — the marker alone, on a container that is NOT reversed: it moves the divider to the
            // trailing physical edge unconditionally.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row divide-x divide-x-reverse", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderRightWidth.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_DivideYReverseColumn_When_Reconciled_Then_TheSecondChildCarriesTheTrailingBorder()
        {
            // Arrange — the vertical twin of the marker.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-col divide-y divide-y-reverse", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderBottomWidth.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_FlexRowReverseDivideX_When_Reconciled_Then_TheSecondChildCarriesTheTrailingBorder()
        {
            // Arrange — a plain divide-x with NO marker on a reversed row. The children paint right-to-left,
            // so a left border on the second child would rule the container's own outer edge while the
            // boundary between the visually adjacent pair got nothing.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row-reverse divide-x", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderRightWidth.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_FlexColReverseDivideY_When_Reconciled_Then_TheSecondChildCarriesTheTrailingBorder()
        {
            // Arrange — the vertical twin: a plain divide-y with no marker on a reversed column.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-col-reverse divide-y", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderBottomWidth.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_FlexRowReverseWithDivideXReverse_When_Reconciled_Then_StillTheTrailingBorder()
        {
            // Arrange — the idiomatic Tailwind combination: the container's direction AND the marker both
            // independently mean "trailing". They must OR together rather than XOR (which would cancel back
            // to the leading edge).
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row-reverse divide-x divide-x-reverse", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderRightWidth.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_FlexColReverseDivideX_When_Reconciled_Then_TheHorizontalDividerStaysOnTheLeadingEdge()
        {
            // Arrange — the per-axis rule, direction half: flex-col-reverse reverses only the VERTICAL axis,
            // so a divide-x must not move.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-col-reverse divide-x", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderLeftWidth.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_FlexRowReverseDivideY_When_Reconciled_Then_TheVerticalDividerStaysOnTheLeadingEdge()
        {
            // Arrange — the symmetric case: flex-row-reverse reverses only the HORIZONTAL axis.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row-reverse divide-y", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderTopWidth.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_DivideYWithAHorizontalReverseMarker_When_Reconciled_Then_TheVerticalDividerStaysOnTheLeadingEdge()
        {
            // Arrange — the per-axis rule, marker half: a horizontal marker must not move a vertical divider.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-col divide-y divide-x-reverse", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderTopWidth.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_ALeadingDivider_When_TheReverseMarkerIsAddedByPatch_Then_TheStaleLeadingWidthIsCleared()
        {
            // Arrange — establish the leading edge first.
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row divide-x divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            Assume.That(scope.Root[0][1].style.borderLeftWidth.value, Is.EqualTo(1f),
                "Precondition: the leading (border-left) divider is established before the marker is added");

            // Act — patch in the marker: the edge flips, so the abandoned gutter must be released, not just
            // a second one added (two live gutters would inset the child from both sides).
            var tree2 = new VNode[] { Row("flex flex-row divide-x divide-x-reverse divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert
            Assert.That(scope.Root[0][1].style.borderLeftWidth.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AColoredLeadingDivider_When_TheReverseMarkerIsAddedByPatch_Then_TheStaleLeadingColorIsCleared()
        {
            // Arrange — a divider owns a width AND a color channel on its edge, so an edge flip has to
            // release both; a stale inline border color would keep tinting the abandoned edge the moment
            // anything else gives it a width.
            ColorUtility.TryParseHtmlString("#e5e7eb", out var gray200);
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row divide-x divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            Assume.That(scope.Root[0][1].style.borderLeftColor.value, Is.EqualTo(gray200),
                "Precondition: the leading divider carries the palette color before the marker is added");

            // Act
            var tree2 = new VNode[] { Row("flex flex-row divide-x divide-x-reverse divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert
            Assert.That(scope.Root[0][1].style.borderLeftColor.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AColoredReversedDivider_When_Reconciled_Then_TheTrailingEdgeCarriesTheColor()
        {
            // Arrange — the color has to follow the divider onto the edge it actually moved to, not stay on
            // the edge the axis alone would have picked.
            ColorUtility.TryParseHtmlString("#e5e7eb", out var gray200);
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row-reverse divide-x divide-gray-200", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderRightColor.value, Is.EqualTo(gray200));
        }

        [Test]
        public void Given_ATrailingDivider_When_TheReverseMarkerIsRemovedByPatch_Then_TheStaleTrailingWidthIsCleared()
        {
            // Arrange — the mirror image: establish the trailing edge first, then flip back.
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row divide-x divide-x-reverse divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            Assume.That(scope.Root[0][1].style.borderRightWidth.value, Is.EqualTo(1f),
                "Precondition: the trailing (border-right) divider is established before the marker is removed");

            // Act
            var tree2 = new VNode[] { Row("flex flex-row divide-x divide-gray-200", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert
            Assert.That(scope.Root[0][1].style.borderRightWidth.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AReversedDashedDivider_When_Reconciled_Then_ThePaintTargetsTheTrailingEdge()
        {
            // Arrange — a dashed divider reserves its gutter as a real border but paints the stroke itself,
            // so the paint has to be told the physical edge: told only the axis, it would draw the dashes
            // down the child's left edge while the reserved gutter sat on its right.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row-reverse divide-x divide-dashed divide-gray-200", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Reconciler.Context.DivideDashBindings[scope.Root[0][1]].Edge,
                Is.EqualTo(DivideEdge.Right));
        }

        [Test]
        public void Given_ADirectionClassFlipWithNoSpecChange_When_ApplyReruns_Then_TheDividerMovesToTheNewEdge()
        {
            // Arrange — establish the leading (top) edge on a column container.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-col divide-y", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = scope.Root[0];
            var manipulator = scope.Reconciler.Context.DivideManipulators[container];
            Assume.That(container[1].style.borderTopWidth.value, Is.EqualTo(1f),
                "Precondition: the column divider settled on the leading (top) edge");

            // Act — flip the class list on the live element (never through a patch, so the spec is untouched
            // and the cached signature is the only thing between this call and the new edge), then re-run
            // Apply the way a GeometryChangedEvent does. A signature that bucketed by axis instead of by
            // edge collapses Top and Bottom together and makes this a silent no-op.
            container.RemoveFromClassList("flex-col");
            container.AddToClassList("flex-col-reverse");
            manipulator.Apply();

            // Assert — the divider is now ON the trailing edge, not merely gone from the leading one.
            Assert.That(container[1].style.borderBottomWidth.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_ADirectionClassFlipWithNoSpecChange_When_ApplyReruns_Then_TheStaleLeadingBorderIsReleased()
        {
            // Arrange — the release half of the same flip. A divider that took the new edge without giving up
            // the old one rules the child on both sides and insets it by two gutters instead of one.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-col divide-y", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = scope.Root[0];
            var manipulator = scope.Reconciler.Context.DivideManipulators[container];
            Assume.That(container[1].style.borderTopWidth.value, Is.EqualTo(1f),
                "Precondition: the column divider settled on the leading (top) edge");

            // Act
            container.RemoveFromClassList("flex-col");
            container.AddToClassList("flex-col-reverse");
            manipulator.Apply();

            // Assert
            Assert.That(container[1].style.borderTopWidth.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AChildOfAReversedDivideContainer_When_ItIsMovedOut_Then_NoResidualTrailingBorder()
        {
            // Arrange — a reversed container puts the divider on the TRAILING edge, so the reset a departing
            // child gets has to reach that edge. A reset hardcoded to the leading pair leaves a reparented
            // child ruled on its right indefinitely: the container's abandoned-edge clear only walks children
            // that are still members, and nothing else revisits one that left.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row-reverse divide-x", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = scope.Root[0];
            var manipulator = scope.Reconciler.Context.DivideManipulators[container];
            var movedChild = container[2];
            Assume.That(movedChild.style.borderRightWidth.value, Is.EqualTo(1f),
                "Precondition: the child carries the trailing divider while it is in the container");

            // Act — move the child out of the divide container (a sibling reparent), then re-apply.
            container.Remove(movedChild);
            var sink = new VisualElement();
            sink.Add(movedChild);
            manipulator.Apply();

            // Assert
            Assert.That(movedChild.style.borderRightWidth.keyword, Is.EqualTo(StyleKeyword.Null));
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
            Assume.That(child.style.borderLeftWidth.value, Is.EqualTo(1f),
                "Precondition: the divider is on the left edge, not the bottom one");

            // Act — patch the divide class away, which tears the manipulator down.
            var tree2 = new VNode[] { Row("flex flex-row", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert
            Assert.That(child.style.borderBottomWidth.value, Is.EqualTo(3f));
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
    /// On-panel coverage for the divider edge. <c>flex-row-reverse</c> / <c>flex-col-reverse</c> are USS-only
    /// rules (<c>_layout.uss</c>) with no C# parse path of their own, so <c>resolvedStyle.flexDirection</c>
    /// only ever reports <c>RowReverse</c> with the bundled <c>StyleUtilities.uss</c> attached to a real
    /// panel. The first test is class-marker coverage under a live panel (the class list is preferred even
    /// there); the second is the one that proves the <c>resolvedStyle</c> FALLBACK, by withholding every
    /// direction/display class so the class scan has nothing to answer with.
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
        public void Given_AFlexRowReverseDivideContainer_When_PanelResolves_Then_TheSecondChildCarriesTheTrailingBorder()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "row", className: "flex flex-row-reverse divide-x", children: new VNode[]
                {
                    V.Label(name: "a", text: "a"),
                    V.Label(name: "b", text: "b"),
                }));
            var row = _window.rootVisualElement.Q<VisualElement>("row");
            ForcePanelUpdate(row.panel);
            using var evt = EventBase<GeometryChangedEvent>.GetPooled();
            row.SimulateEvent(evt);
            Assume.That(row.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.RowReverse),
                "Precondition: the panel resolved flex-row-reverse from the bundled USS");

            // Assert
            var b = _window.rootVisualElement.Q<Label>("b");
            Assert.That(b.resolvedStyle.borderRightWidth, Is.EqualTo(1f));
        }

        [Test]
        public void Given_ADirectionSetOnlyThroughResolvedStyle_When_PanelResolves_Then_TheDividerStillFollowsIt()
        {
            // Arrange — flex-direction set via an inline style (standing in for a custom stylesheet rule)
            // with NONE of the five direction/display classes on the element, the one case the class scan
            // cannot answer. A VisualElement is a flex container by Yoga's own default regardless of a "flex"
            // class, so the inline flex-direction alone produces a real RowReverse layout.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "row", className: "divide-x",
                    refCallback: el => { el.style.flexDirection = FlexDirection.RowReverse; return null; },
                    children: new VNode[]
                    {
                        V.Label(name: "a", text: "a"),
                        V.Label(name: "b", text: "b"),
                    }));
            var row = _window.rootVisualElement.Q<VisualElement>("row");
            ForcePanelUpdate(row.panel);
            using var evt = EventBase<GeometryChangedEvent>.GetPooled();
            row.SimulateEvent(evt);
            Assume.That(row.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.RowReverse),
                "Precondition: the panel resolved the inline flex-direction with no direction class present");

            // Assert
            var b = _window.rootVisualElement.Q<Label>("b");
            Assert.That(b.resolvedStyle.borderRightWidth, Is.EqualTo(1f));
        }
    }

    /// <summary>
    /// The on-panel convergence guard for a class-driven direction toggle. A flip between two same-size
    /// directions moves no rect (the children reorder; the container never resizes), so no
    /// <c>GeometryChangedEvent</c> is fired to correct a divider placed from a stale reading — and
    /// <c>resolvedStyle.flexDirection</c> IS stale at patch time, since the direction comes from a USS rule
    /// with no inline write and only catches up on the panel's next style pass. Reading the class list
    /// instead makes the patch's own forced re-apply converge on its own. These tests tick the panel after
    /// the toggle but deliberately never synthesize a geometry event, so a version that depended on one
    /// would leave the divider on the abandoned edge forever.
    /// </summary>
    [TestFixture]
    internal sealed class DividerEdgeRuntimeFlipTests
    {
        private const string StyleSheetPath = "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";

        private readonly record struct DirectionState(bool Reversed);

        private sealed class DirectionStore : Store<DirectionState>
        {
            public DirectionStore() : base(new DirectionState(false)) { }
            public void Set(bool reversed) => SetState(_ => new DirectionState(reversed));
            protected override void ResetCore() => SetState(_ => new DirectionState(false));
        }

        private static DirectionStore s_store;

        private EditorPanelSimulator _sim;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(200, 200) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            _sim.rootVisualElement.styleSheets.Add(sheet);
            s_store = null;
        }

        [TearDown]
        public void TearDown()
        {
            _sim?.Dispose();
            _sim = null;
        }

        private VisualElement Root => _sim.rootVisualElement;

        private void Tick() => _sim.FrameUpdateMs(16);

        [Component]
        private static VNode DividedColumn()
        {
            var reversed = Hooks.UseStore(s_store, s => s.Reversed);
            var dir = reversed ? "flex-col-reverse" : "flex-col";
            return V.Div(name: "col", className: $"flex {dir} divide-y", children: new VNode[]
            {
                V.Label(name: "a", text: "a"),
                V.Label(name: "b", text: "b"),
            });
        }

        [Test]
        public void Given_ADividedColumn_When_FlippedToColumnReverse_Then_TheStaleTopBorderIsClearedWithNoSynthesizedEvent()
        {
            // Arrange — settle on the leading (top) edge.
            using var store = new DirectionStore();
            s_store = store;
            using var mounted = V.Mount(Root, V.Component(DividedColumn, key: "col"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();
            Tick();
            var b = Root.Q<Label>("b");
            Assume.That(b.style.borderTopWidth.value, Is.EqualTo(1f),
                "Precondition: the column divider settled on the leading (top) edge");

            // Act — flip to flex-col-reverse and just tick; no synthesized geometry event.
            store.Set(true);
            scheduler.DrainImmediateForTest();
            Tick();
            Tick();

            // Assert
            Assert.That(b.style.borderTopWidth.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ADividedColumn_When_FlippedToColumnReverse_Then_TheTrailingBorderIsWrittenWithNoSynthesizedEvent()
        {
            // Arrange — the receive half on the production convergence path: releasing the abandoned edge is
            // only half of converging, and a re-apply that cleared the old edge and wrote nothing would
            // satisfy every release assertion in this fixture while leaving the container with no rules at all.
            using var store = new DirectionStore();
            s_store = store;
            using var mounted = V.Mount(Root, V.Component(DividedColumn, key: "col"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();
            Tick();
            var b = Root.Q<Label>("b");
            Assume.That(b.style.borderBottomWidth.value, Is.EqualTo(0f),
                "Precondition: the trailing edge is unwritten before the flip");

            // Act — flip to flex-col-reverse and just tick; no synthesized geometry event.
            store.Set(true);
            scheduler.DrainImmediateForTest();
            Tick();
            Tick();

            // Assert
            Assert.That(b.style.borderBottomWidth.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_AReversedDividedColumn_When_FlippedBack_Then_TheStaleBottomBorderIsClearedWithNoSynthesizedEvent()
        {
            // Arrange — the round trip: start REVERSED, so the very first layout pass (which does change the
            // rect, from nothing to something) cannot be what makes the later flip converge.
            using var store = new DirectionStore();
            s_store = store;
            store.Set(true);
            using var mounted = V.Mount(Root, V.Component(DividedColumn, key: "col"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();
            Tick();
            var b = Root.Q<Label>("b");
            Assume.That(b.style.borderBottomWidth.value, Is.EqualTo(1f),
                "Precondition: the reversed column divider settled on the trailing (bottom) edge");

            // Act — flip back to flex-col; still no synthesized geometry event.
            store.Set(false);
            scheduler.DrainImmediateForTest();
            Tick();
            Tick();

            // Assert
            Assert.That(b.style.borderBottomWidth.keyword, Is.EqualTo(StyleKeyword.Null));
        }
    }
}
