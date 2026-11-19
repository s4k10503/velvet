using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the element-local settle does not visit a stacked manipulator an earlier settle in the
    /// same walk detached.
    /// </summary>
    [TestFixture]
    internal sealed class VariantSettleSweepVisitTests
    {
        // GREEN_ON_BASE(characterization): the base already skips a manipulator detached mid-walk.
        [Test]
        public void Given_ACheckedStackWhoseLeafIsDark_When_TheSettleUnchecksIt_Then_TheDarkManipulatorItDetachedIsNotVisited()
        {
            // Arrange — checked:dark: on a checked Toggle: the checked manipulator is registered first, and its
            // open leaf registers the dark one after it. Unchecking closes that leaf, and a dark inner does not
            // survive an outer close, so the dark manipulator is detached while the walk is still listing it.
            var context = new ReconcilerContext();
            var toggle = new Toggle();
            toggle.SetValueWithoutNotify(true);
            context.GateStackedVariant(toggle, new object(), "checked:dark:w-[10px]", true, StyleLayerPriority.Data, 0);
            var dark = context.StackedVariantManipulators.Single(kv => kv.Key.inner == StyleVariantKind.Dark).Value;
            toggle.SetValueWithoutNotify(false);
            var visited = new List<IVariantSettleTarget>();

            // Act
            VariantSettleSweep.ForEach(toggle, context, consumer =>
            {
                visited.Add(consumer);
                consumer.SettleChecked(false);
            });

            // Assert — the detach rides along, so a dark manipulator the walk never had to skip cannot read as
            // one it skipped.
            Assert.That((dark.target == null, visited.Contains(dark)), Is.EqualTo((true, false)));
        }
    }
}
