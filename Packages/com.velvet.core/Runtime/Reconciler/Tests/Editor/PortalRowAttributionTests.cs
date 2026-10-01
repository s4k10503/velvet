using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which portal a target's row is attributed to where two recorded ranges on the target both
    /// hold it: the one starting last, whichever the table yields first. The synthetic-bubbling walk and the
    /// focus navigator's logical parent both read it.
    /// </summary>
    internal sealed class PortalRowAttributionTests
    {
        private Reconciler _reconciler;
        private VisualElement _target;
        private VisualElement _inner;
        private VisualElement _outer;

        [SetUp]
        public void SetUp()
        {
            _reconciler = new Reconciler();
            _target = new VisualElement();
            for (var i = 0; i < 3; i++)
            {
                _target.Add(new VisualElement());
            }
            _inner = new VisualElement();
            _outer = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            AttachedContexts().Remove(_reconciler.Context);
            _reconciler.Dispose();
        }

        private static List<ReconcilerContext> AttachedContexts()
            => (List<ReconcilerContext>)typeof(FiberFocusNavigator)
                .GetField("s_attachedContexts", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null);

        [Test]
        public void Given_TwoRangesHoldingOneRow_When_TheInnerIsRecordedFirst_Then_TheRowIsTheInnerPortals()
        {
            // Arrange — the inner range is entered into the table ahead of the outer one.
            _reconciler.Context.PortalState[_inner] = new PortalSlotInfo(_target, SlotStart: 1, SlotLength: 2);
            _reconciler.Context.PortalState[_outer] = new PortalSlotInfo(_target, SlotStart: 0, SlotLength: 3);

            // Act
            var holder = _reconciler.Context.PortalHoldingRow(_target[2], _target);

            // Assert
            Assert.That(holder, Is.SameAs(_inner));
        }

        [Test]
        public void Given_TwoRangesHoldingOneRow_When_TheOuterIsRecordedFirst_Then_TheFocusNavigatorsLogicalParentIsTheInnerPortal()
        {
            // Arrange — the outer range is entered first, so a lookup taking the first range holding the row
            // answers the outer portal. The navigator reads only the contexts attached to a panel.
            _reconciler.Context.PortalState[_outer] = new PortalSlotInfo(_target, SlotStart: 0, SlotLength: 3);
            _reconciler.Context.PortalState[_inner] = new PortalSlotInfo(_target, SlotStart: 1, SlotLength: 2);
            AttachedContexts().Add(_reconciler.Context);

            // Act
            var parent = FiberFocusNavigator.LogicalParentOf(_target[2]);

            // Assert
            Assert.That(parent, Is.SameAs(_inner));
        }
    }
}
