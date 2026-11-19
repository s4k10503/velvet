using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the variant registry walks a changed controlled bool sets off — the element-local settle
    /// over the written element, the relational offer to the stacked consumers, and the retarget at the end of
    /// the pass — allocate nothing on a warm context that holds stacked consumers on many other elements, and
    /// that a whole pass writing a controlled Toggle costs what the same pass costs with none of them.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    internal sealed class VariantSettleSweepAllocationTests
    {
        internal const int Unrelated = 64;

        private static readonly Action<IVariantSettleTarget> Ignore = static _ => { };
        internal static object s_sink;

        private static void OpenUnrelated(ReconcilerContext context, object owner)
        {
            for (var i = 0; i < Unrelated; i++)
            {
                context.GateStackedVariant(new VisualElement(), owner, "hover:w-[10px]", true, StyleLayerPriority.Data, 0);
            }
        }

        // One stacked consumer on target and one on each of Unrelated other elements.
        internal static ReconcilerContext ContextWith(VisualElement target)
        {
            var context = new ReconcilerContext();
            var owner = new object();
            context.GateStackedVariant(target, owner, "hover:w-[10px]", true, StyleLayerPriority.Data, 0);
            OpenUnrelated(context, owner);
            return context;
        }

        internal static int Blocks(Action once)
        {
            for (var i = 0; i < 64; i++)
            {
                once();
            }
            return GCAllocationProbe.MedianBlocksDuring(once);
        }

        // Every measured delegate allocates one object of its own, so the canary's count is the floor each
        // walk is compared against and a probe stuck at zero cannot read as a walk that allocates nothing.
        internal static int CanaryBlocks() => Blocks(static () => s_sink = new object());

        // A pass that writes a controlled Toggle's value, measured on a context holding Unrelated stacked
        // consumers on elements outside the tree, or none. The VNodes are built once, outside every window.
        private static int ControlledWriteBlocks(bool withUnrelated)
        {
            using var scope = new ReconcilerScope();
            var renderedUnchecked = new VNode[] { V.Toggle(name: "toggle", value: false) };
            var renderedChecked = new VNode[] { V.Toggle(name: "toggle", value: true) };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), renderedUnchecked);
            if (withUnrelated)
            {
                OpenUnrelated(scope.Reconciler.Context, new object());
            }

            void Uncheck() => scope.Reconciler.Reconcile(scope.Root, renderedChecked, renderedUnchecked);
            void Check()
            {
                scope.Reconciler.Reconcile(scope.Root, renderedUnchecked, renderedChecked);
                s_sink = new object();
            }

            for (var i = 0; i < 64; i++)
            {
                Check();
                Uncheck();
            }
            return GCAllocationProbe.MedianBlocksDuring(Uncheck, Check);
        }

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

        [Test]
        public void Given_ManyStackedConsumers_When_ThePassEndRetargetsThem_Then_TheRetargetAllocatesNothing()
        {
            // Arrange
            var context = ContextWith(new VisualElement());
            var canary = CanaryBlocks();

            // Act
            var retargeted = Blocks(() =>
            {
                StyleRelationalVariantManipulator.RetargetAll(context);
                s_sink = new object();
            });

            // Assert
            Assert.That((canary > 0, retargeted), Is.EqualTo((true, canary)));
        }

        // The pass is compared with itself rather than with zero: whatever else a reconcile allocates is not
        // this fixture's to pin, and a zero bound would hold all of it alongside the registry walks.
        [Test]
        public void Given_ManyUnrelatedStackedConsumers_When_AControlledToggleIsWritten_Then_ThePassCostsWhatItCostsWithNone()
        {
            // Arrange & Act
            var none = ControlledWriteBlocks(withUnrelated: false);
            var many = ControlledWriteBlocks(withUnrelated: true);

            // Assert — the empty count rides along, because two equal numbers say nothing if the probe
            // measured nothing at all.
            Assert.That((none > 0, many), Is.EqualTo((true, none)));
        }
    }
}
