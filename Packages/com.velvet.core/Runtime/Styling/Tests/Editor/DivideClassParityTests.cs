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

        [TestCase("!divide-x-4 divide-x-2", 4f)]
        [TestCase("divide-x-4! divide-x-2", 4f)]
        [TestCase("!divide-x-2 divide-x-4", 2f)]
        [TestCase("!divide-x-2 !divide-x-4", 4f)]
        [TestCase("divide-x-2 divide-x-4", 4f)]
        public void Given_ImportantAndPlainAxisWidths_When_Extracted_Then_ImportantWinsAndTheLastOfEqualImportanceWins(
            string className, float expectedWidth)
        {
            // Act
            StyleDivideClass.TryExtract(className.Split(' '), out var spec);

            // Assert
            Assert.That(spec.Width, Is.EqualTo(expectedWidth));
        }

        [Test]
        public void Given_ImportantVerticalDivideBeforeAPlainHorizontalOne_When_Extracted_Then_TheImportantAxisWins()
        {
            // Act
            StyleDivideClass.TryExtract(new[] { "!divide-y-2", "divide-x-4" }, out var spec);

            // Assert
            Assert.That((spec.Axis, spec.Width), Is.EqualTo((DivideAxis.Vertical, 2f)));
        }

        [Test]
        public void Given_ImportantColorBeforeAPlainColor_When_Extracted_Then_TheImportantColorWins()
        {
            // Arrange
            ColorUtility.TryParseHtmlString("#e5e7eb", out var gray200);

            // Act
            StyleDivideClass.TryExtract(new[] { "divide-x", "!divide-gray-200", "divide-red-500" }, out var spec);

            // Assert
            Assert.That(spec.Color, Is.EqualTo(gray200));
        }

        [Test]
        public void Given_ImportantDashedBeforePlainSolid_When_Extracted_Then_TheImportantStyleWins()
        {
            // Act
            StyleDivideClass.TryExtract(new[] { "divide-x", "divide-dashed!", "divide-solid" }, out var spec);

            // Assert
            Assert.That(spec.Style, Is.EqualTo(BorderLineStyle.Dashed));
        }

        [TestCase("divide-x-4 divide-gray-200", DivideImportance.None)]
        [TestCase("!divide-x-4 divide-x-2 divide-gray-200", DivideImportance.Width)]
        [TestCase("divide-x-4 divide-gray-200!", DivideImportance.Color)]
        [TestCase("!divide-x-4 !divide-gray-200", DivideImportance.Width | DivideImportance.Color)]
        [TestCase("divide-x-4 !divide-dashed", DivideImportance.None)]
        public void Given_DivideTokens_When_Extracted_Then_TheSpecCarriesTheImportanceOfTheWinningWidthAndColor(
            string className, DivideImportance expected)
        {
            // Act
            StyleDivideClass.TryExtract(className.Split(' '), out var spec);

            // Assert
            Assert.That(spec.Important, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ImportantReverseMarker_When_Extracted_Then_ItReversesAndMarksNothingImportant()
        {
            // Act
            StyleDivideClass.TryExtract(new[] { "!divide-x-reverse", "divide-x-2" }, out var spec);

            // Assert
            Assert.That((spec.Reverse, spec.Important), Is.EqualTo((true, DivideImportance.None)));
        }

        [Test]
        public void Given_AWidthWithALeadingAndATrailingBang_When_Extracted_Then_Declines()
        {
            // Act — only one modifier is stripped, so the trailing bang is left on a width that is not on the scale.
            var ok = StyleDivideClass.TryExtract(new[] { "!divide-x-4!" }, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_OnlyAnImportantDivideClass_When_HasDivideClassProbed_Then_GateReturnsTrue()
        {
            // Act
            var has = StyleDivideClass.HasDivideClass(new[] { "flex", "!divide-x" });

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

        [TestCase("flex flex-row !divide-x-4 divide-x-2")]
        [TestCase("flex flex-row divide-x-4! divide-x-2")]
        public void Given_AnImportantDivideWidthBeforeAPlainOne_When_Reconciled_Then_TheImportantWidthIsDrawn(string className)
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row(className, 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderRightWidth.value, Is.EqualTo(4f));
        }

        [TestCase("flex flex-row divide-x-2 hover:divide-x-4", 4f)]
        [TestCase("flex flex-row !divide-x-2 hover:divide-x-4", 2f)]
        [TestCase("flex flex-row !divide-x-2 hover:!divide-x-4", 4f)]
        [TestCase("flex flex-row divide-x-2 hover:divide-x-4!", 4f)]
        public void Given_AVariantDivideWidth_When_HoverStartsAndEnds_Then_TheWinningWidthAppliesAndRestores(
            string className, float hoveredWidth)
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row(className, 2) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = scope.Root[0];
            var atRest = container[0].style.borderRightWidth.value;

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                container.SimulateEvent(over);
            }
            var whileHover = container[0].style.borderRightWidth.value;
            using (var leave = PointerOutEvent.GetPooled())
            {
                container.SimulateEvent(leave);
            }
            var afterLeave = container[0].style.borderRightWidth.value;

            // Assert
            Assert.That((atRest, whileHover, afterLeave), Is.EqualTo((2f, hoveredWidth, 2f)));
        }

        [TestCase("flex flex-row !divide-x-4", "border-r-2")]
        [TestCase("flex flex-row divide-x-4!", "border-r-2")]
        [TestCase("flex flex-row !divide-x-4", "border-r-[3px]")]
        public void Given_AnImportantDivideWidth_When_ADividedChildCarriesItsOwnBorderWidth_Then_TheDivideWins(
            string className, string childBorderClass)
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { DividerRowWithColoredChild(className, childBorderClass) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderRightWidth.value, Is.EqualTo(4f));
        }

        [Test]
        public void Given_APlainDivideWidth_When_ADividedChildCarriesItsOwnBorderWidthClass_Then_TheChildKeepsIt()
        {
            // Arrange — the control for the important case above: the same row without the bang.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { DividerRowWithColoredChild("flex flex-row divide-x-4", "border-r-2") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderRightWidth.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AnImportantDivideWidth_When_ADividedChildHasAClassProjectionButOnlyAPlainBorderWidthClass_Then_TheDivideWins()
        {
            // Arrange — the unrelated important class gives the child a class projection, so the child's plain
            // border-r-2 is the only claim on the width and it is not important.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { DividerRowWithColoredChild("flex flex-row !divide-x-4", "border-r-2 !opacity-50") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderRightWidth.value, Is.EqualTo(4f));
        }

        [TestCase("flex flex-row divide-x !divide-gray-200")]
        [TestCase("flex flex-row divide-x divide-gray-200!")]
        public void Given_AnImportantDivideColor_When_ADividedChildCarriesItsOwnBorderColor_Then_TheDivideWins(
            string className)
        {
            // Arrange
            ColorUtility.TryParseHtmlString("#e5e7eb", out var gray200);
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { DividerRowWithColoredChild(className, "border-[#ff0000]") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert — every edge, as for the plain divide-{color}.
            var style = scope.Root[0][1].style;
            Assert.That((style.borderTopColor.value, style.borderRightColor.value, style.borderBottomColor.value,
                    style.borderLeftColor.value),
                Is.EqualTo((gray200, gray200, gray200, gray200)));
        }

        [Test]
        public void Given_AnImportantDivideColor_When_ADividedChildCarriesABorderColorClass_Then_TheDivideWins()
        {
            // Arrange — a bundled USS color class is what DeclaresOwn reads, unlike the inline arbitrary color above.
            ColorUtility.TryParseHtmlString("#e5e7eb", out var gray200);
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { DividerRowWithColoredChild("flex flex-row divide-x !divide-gray-200", "border-gray-300") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderRightColor.value, Is.EqualTo(gray200));
        }

        [Test]
        public void Given_APlainDivideColor_When_ADividedChildCarriesABorderColorClass_Then_TheChildKeepsIt()
        {
            // Arrange — the control for the important case above: the same row without the bang.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { DividerRowWithColoredChild("flex flex-row divide-x divide-gray-200", "border-gray-300") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderRightColor.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AnImportantDashedDivideWidth_When_ADividedChildCarriesItsOwnBorderWidthClass_Then_TheDivideWinsOnTheDashedPath()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { DividerRowWithColoredChild("flex flex-row !divide-x-4 divide-dashed", "border-r-2") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            var child = scope.Root[0][1];
            Assert.That((child.style.borderRightWidth.value, scope.Reconciler.Context.DivideDashBindings.ContainsKey(child)),
                Is.EqualTo((4f, true)));
        }

        [Test]
        public void Given_AnImportantDivideWidth_When_ADividedChildCarriesAnImportantArbitraryBorderWidth_Then_TheChildWins()
        {
            // Arrange — both declarations are !important in CSS, where the divide's :where() selector has zero
            // specificity and the child's utility class has one.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { DividerRowWithColoredChild("flex flex-row !divide-x-4", "!border-r-[3px]") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderRightWidth.value, Is.EqualTo(3f));
        }

        [Test]
        public void Given_AnImportantDivideWidth_When_ADividedChildCarriesAnImportantBorderWidthClass_Then_TheChildWins()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { DividerRowWithColoredChild("flex flex-row !divide-x-4", "!border-r-2") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderRightWidth.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AnImportantDivideColor_When_ADividedChildCarriesAnImportantBorderColor_Then_TheChildWins()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { DividerRowWithColoredChild("flex flex-row divide-x !divide-gray-200", "!border-[#ff0000]") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderRightColor.value, Is.EqualTo(Color.red));
        }

        [Test]
        public void Given_AnImportantDivideColor_When_ADividedChildCarriesAnImportantBorderColorClass_Then_TheChildWins()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { DividerRowWithColoredChild("flex flex-row divide-x !divide-gray-200", "!border-gray-300") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.borderRightColor.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AnImportantDivideWidth_When_TheChildsImportantBorderClassComesAndGoes_Then_TheWidthFollows()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var plain = new VNode[] { DividerRowWithColoredChild("flex flex-row !divide-x-4", "") };
            var owned = new VNode[] { DividerRowWithColoredChild("flex flex-row !divide-x-4", "!border-r-2") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), plain);
            var before = scope.Root[0][1].style.borderRightWidth.value;

            // Act
            scope.Reconciler.Reconcile(scope.Root, plain, owned);
            var whileOwned = scope.Root[0][1].style.borderRightWidth.keyword;
            scope.Reconciler.Reconcile(scope.Root, owned, plain);

            // Assert
            Assert.That((before, whileOwned, scope.Root[0][1].style.borderRightWidth.value),
                Is.EqualTo((4f, StyleKeyword.Null, 4f)));
        }

        [Test]
        public void Given_AnImportantDivideWidth_When_ADividedChildsImportantHoverBorderStartsAndEnds_Then_TheChildsWidthAppliesAndRestores()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { DividerRowWithColoredChild("flex flex-row !divide-x-4", "hover:!border-r-[3px]") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var child = scope.Root[0][1];
            var atRest = child.style.borderRightWidth.value;

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                child.SimulateEvent(over);
            }
            var whileHover = child.style.borderRightWidth.value;
            using (var leave = PointerOutEvent.GetPooled())
            {
                child.SimulateEvent(leave);
            }
            var afterLeave = child.style.borderRightWidth.value;

            // Assert
            Assert.That((atRest, whileHover, afterLeave), Is.EqualTo((4f, 3f, 4f)));
        }

        [Test]
        public void Given_AnImportantDivideWidth_When_PatchedToAPlainOne_Then_TheChildsOwnBorderWidthReturns()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { DividerRowWithColoredChild("flex flex-row !divide-x-4", "border-r-[3px]") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var important = scope.Root[0][1].style.borderRightWidth.value;

            // Act
            var tree2 = new VNode[] { DividerRowWithColoredChild("flex flex-row divide-x-4", "border-r-[3px]") };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the important width before the patch rides along.
            Assert.That((important, scope.Root[0][1].style.borderRightWidth.value), Is.EqualTo((4f, 3f)));
        }

        [TestCase("flex flex-row divide-x divide-gray-200 hover:divide-red-500", false)]
        [TestCase("flex flex-row divide-x !divide-gray-200 hover:divide-red-500", true)]
        [TestCase("flex flex-row divide-x !divide-gray-200 hover:!divide-red-500", false)]
        public void Given_AVariantDivideColor_When_HoverStartsAndEnds_Then_TheWinningColorAppliesAndRestores(
            string className, bool staysGray)
        {
            // Arrange
            ColorUtility.TryParseHtmlString("#e5e7eb", out var gray200);
            VelvetPalette.TryResolveColorToken("red-500", out var red500);
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row(className, 2) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = scope.Root[0];
            var atRest = container[0].style.borderRightColor.value;

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                container.SimulateEvent(over);
            }
            var whileHover = container[0].style.borderRightColor.value;
            using (var leave = PointerOutEvent.GetPooled())
            {
                container.SimulateEvent(leave);
            }
            var afterLeave = container[0].style.borderRightColor.value;

            // Assert
            Assert.That((atRest, whileHover, afterLeave), Is.EqualTo((gray200, staysGray ? gray200 : red500, gray200)));
        }

        [TestCase("flex flex-row divide-x divide-dashed hover:divide-solid", false)]
        [TestCase("flex flex-row divide-x !divide-dashed hover:divide-solid", true)]
        [TestCase("flex flex-row divide-x divide-dashed hover:!divide-solid", false)]
        public void Given_AVariantDivideStyle_When_HoverStartsAndEnds_Then_TheWinningStyleAppliesAndRestores(
            string className, bool staysDashed)
        {
            // Arrange — a dashed divider masks the native border colour, a solid one leaves it unset.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row(className, 2) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = scope.Root[0];
            bool Masked() => container[0].style.borderRightColor.value == SilhouetteFace.SuppressedColor;
            var atRest = Masked();

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                container.SimulateEvent(over);
            }
            var whileHover = Masked();
            using (var leave = PointerOutEvent.GetPooled())
            {
                container.SimulateEvent(leave);
            }
            var afterLeave = Masked();

            // Assert
            Assert.That((atRest, whileHover, afterLeave), Is.EqualTo((true, staysDashed, true)));
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

        // GREEN_ON_BASE(characterization): the base writes the divider without reading flex-wrap, as it must.
        [Test]
        public void Given_AWrappingDivideXRow_When_Reconciled_Then_EveryChildButTheLastHasTheEndBorder()
        {
            // Arrange — Tailwind's `:where(& > :not(:last-child))` selects by sibling position, so which wrapped
            // line a child sits on changes nothing.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row flex-wrap divide-x", 4) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(EdgeWidths(scope.Root[0], c => c.style.borderRightWidth.value),
                Is.EqualTo(new[] { 1f, 1f, 1f, 0f }));
        }

        // GREEN_ON_BASE(characterization): the base writes the divider without reading flex-wrap, as it must.
        [Test]
        public void Given_AWrappingDivideYColumn_When_Reconciled_Then_EveryChildButTheLastHasTheBottomBorder()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-col flex-wrap divide-y", 4) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(EdgeWidths(scope.Root[0], c => c.style.borderBottomWidth.value),
                Is.EqualTo(new[] { 1f, 1f, 1f, 0f }));
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
        public void Given_ADashedDivideRow_When_ADividedChildCarriesAnArbitraryBorderWidth_Then_ThatWidthWins()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { DividerRowWithColoredChild("flex flex-row divide-x divide-dashed", "border-r-[3px]") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert — the dash binding rides along, since a solid divider yields through another path, and so
            // does the start edge, which a divider on the wrong edge would take instead.
            var child = scope.Root[0][1];
            Assert.That((scope.Reconciler.Context.DivideDashBindings.ContainsKey(child),
                    child.style.borderRightWidth.value, child.style.borderLeftWidth.keyword),
                Is.EqualTo((true, 3f, StyleKeyword.Null)));
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

        private static float[] EdgeWidths(VisualElement container, System.Func<VisualElement, float> edge)
        {
            var widths = new float[container.childCount];
            for (var i = 0; i < widths.Length; i++)
            {
                widths[i] = edge(container[i]);
            }
            return widths;
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
    }
}
