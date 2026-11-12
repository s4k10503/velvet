using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the passes a patch runs again on each element (gap, grid, divide, box-shadow, gradient) build
    /// nothing to keep a relative-length poll up while their classes measure no length, so starting none: what each
    /// call allocates is what the same call does with the poll left out of it. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    internal sealed class RelativeLengthPollAllocationTests
    {
        private Reconciler _reconciler;

        [SetUp]
        public void SetUp() => _reconciler = new Reconciler();

        [TearDown]
        public void TearDown()
        {
            // The gradient case binds outside any mount, so its texture is handed back here.
            foreach (var pair in _reconciler.Context.GradientBackgrounds)
            {
                GradientBackground.Detach(pair.Key, pair.Value);
            }
            _reconciler.Dispose();
        }

        private static int WarmBlocks(Action once)
        {
            for (var i = 0; i < 64; i++)
            {
                once();
            }
            return GCAllocationProbe.MedianBlocksDuring(once);
        }

        // A probe stuck at zero satisfies the zero guards below without measuring anything; this canary is what makes
        // their zeros mean something.
        // GREEN_ON_BASE(characterization): the probe already counts a 16-byte array allocation on the base, which is all this case asserts.
        [Test]
        public void Given_ADelegateAllocatingAKnownArray_When_Probed_Then_TheProbeCountsIt()
        {
            // Act
            var blocks = GCAllocationCanary.BlocksForKnownArray();

            // Assert
            Assert.That(blocks, Is.GreaterThan(0));
        }

        // On no element the update applies nothing, so the poll's upkeep is all it can allocate.
        [Test]
        public void Given_AGapManipulatorOnNoElement_When_ItsSpecIsUpdated_Then_NothingIsAllocated()
        {
            // Arrange
            var spec = StyleGapClass.Extract(new[] { "gap-4" });
            var manipulator = new StyleGapManipulator(_reconciler.Context, spec);

            // Act
            var blocks = WarmBlocks(() => manipulator.UpdateGap(spec));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_AGridManipulatorOnNoElement_When_ItsSpecIsUpdated_Then_NothingIsAllocated()
        {
            // Arrange
            var spec = new GridSpec(3, 16f, 16f, default);
            var manipulator = new StyleGridManipulator(_reconciler.Context, spec);

            // Act
            var blocks = WarmBlocks(() => manipulator.UpdateSpec(spec));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        // On an element, so the update also registers the divider's resnap with the panel-scale watch; a reapply is the
        // same update with neither the poll nor the watch in it.
        [Test]
        public void Given_ADivideManipulatorOnAnElement_When_ItsSpecIsUpdated_Then_ItAllocatesWhatAReapplyDoes()
        {
            // Arrange
            StyleDivideClass.TryExtract(new[] { "divide-y" }, out var spec);
            var manipulator = new StyleDivideManipulator(spec, _reconciler.Context);
            new VisualElement().AddManipulator(manipulator);
            var reapply = WarmBlocks(manipulator.Reapply);

            // Act
            var update = WarmBlocks(() => manipulator.UpdateSpec(spec));

            // Assert
            Assert.That(update, Is.EqualTo(reapply));
        }

        // Off a panel the sync resolves the radius from the classes, of which none is a rounded-*.
        [Test]
        public void Given_AShadowAttachedOffAPanel_When_ItsLayersAreSyncedAgain_Then_NothingIsAllocated()
        {
            // Arrange
            var element = new VisualElement();
            var classNames = new[] { "shadow-sm" };
            StyleShadowClass.TryExtract(classNames, out var layers);
            var binding = BoxShadowSilhouette.Attach(element, layers, classNames);
            var request = new BoxShadowSyncRequest { Layers = layers, ClassNames = classNames };

            // Act
            var blocks = WarmBlocks(() => BoxShadowSilhouette.Sync(element, binding, request));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_AGradientBoundToAnElement_When_PatchedWithItsClassesAgain_Then_ItAllocatesWhatReadingThemDoes()
        {
            // Arrange
            var element = new VisualElement();
            var classNames = new[] { "bg-gradient-to-r", "from-blue-500", "to-blue-900" };
            var applier = new FiberGradientApplier(_reconciler.Context);
            applier.ApplyGradientOnCreate(element, classNames);
            var read = WarmBlocks(() =>
            {
                _ = StyleGradientClass.HasGradientClass(classNames)
                    && StyleGradientClass.TryExtract(classNames, element, out _, out _);
            });

            // Act
            var patch = WarmBlocks(() => applier.ApplyGradientOnPatch(element, classNames));

            // Assert
            Assert.That(patch, Is.EqualTo(read));
        }
    }
}
