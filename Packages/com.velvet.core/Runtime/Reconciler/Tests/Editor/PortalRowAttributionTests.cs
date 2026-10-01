using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which portal the synthetic-bubbling walk attributes a target's row to where two recorded
    /// ranges on the target both hold it: the one starting last, whichever the table yields first.
    /// </summary>
    internal sealed class PortalRowAttributionTests
    {
        [Test]
        public void Given_TwoRangesHoldingOneRow_When_TheInnerIsRecordedFirst_Then_TheRowIsTheInnerPortals()
        {
            // Arrange — the inner range is entered into the table ahead of the outer one.
            var reconciler = new Reconciler();
            var target = new VisualElement();
            for (var i = 0; i < 3; i++)
            {
                target.Add(new VisualElement());
            }
            var inner = new VisualElement();
            var outer = new VisualElement();
            reconciler.Context.PortalState[inner] = new PortalSlotInfo(target, SlotStart: 1, SlotLength: 2);
            reconciler.Context.PortalState[outer] = new PortalSlotInfo(target, SlotStart: 0, SlotLength: 3);
            var portalHoldingRow = typeof(FiberCrossPanelEventDispatcher).GetMethod(
                "PortalHoldingRow", BindingFlags.Static | BindingFlags.NonPublic);

            // Act
            var holder = portalHoldingRow?.Invoke(null, new object[] { target[2], target, reconciler.Context });
            reconciler.Dispose();

            // Assert
            Assert.That(holder, Is.SameAs(inner));
        }
    }
}
