using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the two settle sweeps a changed controlled bool runs — the element-local one over the
    /// written element and the relational one offered to the stacked consumers — allocate nothing on a warm
    /// context that holds stacked consumers on many other elements.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    internal sealed class VariantSettleSweepAllocationTests
    {
        private const int Unrelated = 64;

        private static readonly Action<IVariantSettleTarget> Ignore = static _ => { };
        private static object s_sink;

        // One stacked consumer on target and one on each of Unrelated other elements.
        private static ReconcilerContext ContextWith(VisualElement target)
        {
            var context = new ReconcilerContext();
            var owner = new object();
            context.GateStackedVariant(target, owner, "hover:w-[10px]", true, StyleLayerPriority.Data, 0);
            for (var i = 0; i < Unrelated; i++)
            {
                context.GateStackedVariant(new VisualElement(), owner, "hover:w-[10px]", true, StyleLayerPriority.Data, 0);
            }
            return context;
        }

        private static int Blocks(Action once)
        {
            for (var i = 0; i < 64; i++)
            {
                once();
            }
            return GCAllocationProbe.MedianBlocksDuring(once);
        }

        // Every measured delegate allocates one object of its own, so the canary's count is the floor each
        // sweep is compared against and a probe stuck at zero cannot read as a sweep that allocates nothing.
        private static int CanaryBlocks() => Blocks(static () => s_sink = new object());

        [Test]
        public void Given_AnElementWithAStackedConsumerAmongManyOthers_When_ItIsSwept_Then_TheSweepAllocatesNothing()
        {
            // Arrange
            var target = new VisualElement();
            var context = ContextWith(target);
            var canary = CanaryBlocks();

            // Act
            var swept = Blocks(() =>
            {
                VariantSettleSweep.ForEach(target, context, Ignore);
                s_sink = new object();
            });

            // Assert
            Assert.That((canary > 0, swept), Is.EqualTo((true, canary)));
        }

        [Test]
        public void Given_ManyStackedConsumers_When_ACheckedSettleIsOfferedFromASource_Then_TheOfferAllocatesNothing()
        {
            // Arrange
            var source = new VisualElement();
            var context = ContextWith(new VisualElement());
            var canary = CanaryBlocks();

            // Act
            var offered = Blocks(() =>
            {
                VariantSettleSweep.SettleCheckedFromSource(source, context, true);
                s_sink = new object();
            });

            // Assert
            Assert.That((canary > 0, offered), Is.EqualTo((true, canary)));
        }
    }
}
