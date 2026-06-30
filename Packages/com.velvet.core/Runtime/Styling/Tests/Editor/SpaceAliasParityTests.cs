using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>space-x-*</c> / <c>space-y-*</c> alias onto Velvet's gap machinery.
    /// <c>space-*</c> is an inter-child margin (<c>&gt; * + *</c>); UITK has no such selector,
    /// and Velvet's <see cref="StyleGapManipulator"/> already writes exactly that leading margin on every
    /// child but the first, so <c>space-x-N</c> maps to <see cref="GapAxis.Horizontal"/> + the same
    /// <c>--space-*</c> scale and <c>space-y-N</c> to <see cref="GapAxis.Vertical"/>. The cheap
    /// <see cref="StyleGapClass.HasGapClass"/> gate (the patcher's early-out) must also recognize the alias
    /// or it never reaches the manipulator. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class SpaceAliasParityTests
    {
        private const float Space4 = 16f; // --space-4

        [Test]
        public void Given_SpaceX4Class_When_Parsed_Then_MapsToHorizontalGapAxis()
        {
            // Act
            var ok = StyleGapClass.TryParse("space-x-4", out var gap, out var axis);

            // Assert
            Assume.That(ok, Is.True, "Precondition: recognized as a gap utility");
            Assert.That((gap, axis), Is.EqualTo((Space4, GapAxis.Horizontal)));
        }

        [Test]
        public void Given_SpaceY2Class_When_Parsed_Then_MapsToVerticalGapAxis()
        {
            // Act
            var ok = StyleGapClass.TryParse("space-y-2", out var gap, out var axis);

            // Assert
            Assume.That(ok, Is.True, "Precondition: recognized as a gap utility");
            Assert.That((gap, axis), Is.EqualTo((8f, GapAxis.Vertical)));
        }

        [Test]
        public void Given_SpaceXClass_When_HasGapClassProbed_Then_GateReturnsTrue()
        {
            // Act — the FiberNodePatcher early-out depends on this gate recognizing the alias.
            var has = StyleGapClass.HasGapClass(new[] { "space-x-4" });

            // Assert
            Assert.That(has, Is.True);
        }

        [Test]
        public void Given_UnknownSpaceSuffix_When_Parsed_Then_DeclinesToParse()
        {
            // Act — a suffix outside the --space-* scale is not a recognized gap utility.
            var ok = StyleGapClass.TryParse("space-x-999", out _, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_FlexRowSpaceX4_When_Reconciled_Then_LeadingMarginBetweenChildren()
        {
            // Arrange — mirrors the gap-x-4 parity test: the alias must drive the manipulator end-to-end.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row space-x-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = scope.Root[0];

            // Assert — 2nd child carries the gap; the first has no leading margin.
            Assert.That(container[1].style.marginLeft.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_FlexRowSpaceX4_When_ClassRemovedByPatch_Then_StaleMarginsCleared()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row space-x-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            Assume.That(scope.Root[0][1].style.marginLeft.value.value, Is.EqualTo(Space4), "Precondition: gap applied");

            // Act — patch the same container without the space class.
            var tree2 = new VNode[] { Row("flex flex-row", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the manipulator's leading margin is cleared (no ghost via the shared gap path).
            Assert.That(scope.Root[0][1].style.marginLeft.value.value, Is.EqualTo(0f));
        }

        [Test]
        public void Given_SpaceXReverse_When_Parsed_Then_DeclinesToParse()
        {
            // Act — space-x-reverse has no gap analog; it is an intentional no-op (locked here).
            var ok = StyleGapClass.TryParse("space-x-reverse", out _, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_GapXArbitraryPixel_When_Parsed_Then_ResolvesPixelGapOnHorizontalAxis()
        {
            // Act — JIT arbitrary value: gap-x-[12px].
            var ok = StyleGapClass.TryParse("gap-x-[12px]", out var gap, out var axis);

            // Assert
            Assume.That(ok, Is.True, "Precondition: recognized as a gap utility");
            Assert.That((gap, axis), Is.EqualTo((12f, GapAxis.Horizontal)));
        }

        [Test]
        public void Given_GapArbitraryPercent_When_Parsed_Then_DeclinesToParse()
        {
            // Act — gap is a pixel inter-child margin; a percentage is not meaningful.
            var ok = StyleGapClass.TryParse("gap-[50%]", out _, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_FlexRowGapArbitrary_When_Reconciled_Then_AppliesPixelGap()
        {
            // Arrange — the arbitrary form must drive the manipulator end-to-end, like the presets.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row gap-x-[12px]", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.marginLeft.value.value, Is.EqualTo(12f));
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
}
