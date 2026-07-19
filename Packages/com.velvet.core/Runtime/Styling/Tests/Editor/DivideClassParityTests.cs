using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>divide-x</c> / <c>divide-y</c> utilities, which draw a border between
    /// adjacent children (<c>&gt; * + *</c>); UITK has no <c>:first-child</c> and no <c>&gt; *</c> child
    /// combinator, so <see cref="StyleDivideManipulator"/> writes the leading border (border-left for x,
    /// border-top for y) on every child but the first — the same shape <see cref="StyleGapManipulator"/>
    /// uses for gap. Width comes from <c>divide-x</c> (1px), the <c>divide-x-{0,2,4,8}</c> scale, or the
    /// <c>divide-x-[Npx]</c> arbitrary form; color from <c>divide-{palette}</c> or <c>divide-[#hex]</c>.
    /// UITK has no border-style, so <c>divide-dashed</c> / <c>divide-dotted</c> are unsupported. GWT, one
    /// assert per case.
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
}
