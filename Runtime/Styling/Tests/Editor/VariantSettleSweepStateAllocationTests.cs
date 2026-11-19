using System;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the element-local settle, called through the overload that carries a bool as state the
    /// way a controlled value write calls it, allocates nothing on a warm context that holds stacked consumers
    /// on many other elements. Shares <see cref="VariantSettleSweepAllocationTests"/>' arrangement.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    internal sealed class VariantSettleSweepStateAllocationTests
    {
        private static readonly Action<IVariantSettleTarget, bool> Settle =
            static (consumer, value) => consumer.SettleChecked(value);

        [Test]
        public void Given_AnElementWithAStackedConsumerAmongManyOthers_When_ItIsSweptWithAValue_Then_TheSweepAllocatesNothing()
        {
            // Arrange
            var target = new VisualElement();
            var context = VariantSettleSweepAllocationTests.ContextWith(target);
            var canary = VariantSettleSweepAllocationTests.CanaryBlocks();

            // Act
            var swept = VariantSettleSweepAllocationTests.Blocks(() =>
            {
                VariantSettleSweep.ForEach(target, context, false, Settle);
                VariantSettleSweepAllocationTests.s_sink = new object();
            });

            // Assert
            Assert.That((canary > 0, swept), Is.EqualTo((true, canary)));
        }
    }
}
