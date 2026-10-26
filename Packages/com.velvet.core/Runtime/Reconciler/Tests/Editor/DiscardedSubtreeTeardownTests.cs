using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.Pool;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which callbacks the teardown of a removed element unregisters from Unity, counted as the records
    /// those unregistrations return to Unity's pools (<see cref="UnregisterCallbackRecordPoolTests"/>).
    /// <list type="bullet">
    /// <item>Below the root of a removal that nothing pools afterwards, the Button click gate and the text-item
    /// manipulators leave their callbacks registered: the elements they sit on go with the subtree.</item>
    /// <item>A removal that pools its root, or that tears out a Portal range lying inside the subtree, unregisters
    /// as before, since those elements are rented again.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class DiscardedSubtreeTeardownTests
    {
        private const int Rows = 8;

        private static readonly Type[] s_clickGateEvents =
        {
            typeof(PointerDownEvent), typeof(PointerUpEvent), typeof(PointerCancelEvent), typeof(NavigationSubmitEvent),
        };

        private static readonly Type[] s_textItemEvents = { typeof(AttachToPanelEvent), typeof(ChangeEvent<string>) };

        [SetUp]
        public void SetUp()
        {
            VNodePoolTestAccess.ClearButtonPoolForTest();
            VNodePoolTestAccess.ClearLabelPoolForTest();
        }

        // Rows of plain containers holding a Label and a Button with a click handler, which is what gives the Button
        // a click gate: nothing pools a row, so what it holds is discarded with it.
        private static VNode[] PlainContainerRows() => Enumerable.Range(0, Rows)
            .Select(i => V.Div("flex flex-row", V.Label(text: "a" + i), V.Button(text: "b" + i, onClick: () => { })))
            .ToArray();

        private static VNode[] ButtonRows() => Enumerable.Range(0, Rows).Select(i => V.Button(text: "b" + i, onClick: () => { })).ToArray();

        private static VNode[] LabelRows() => Enumerable.Range(0, Rows).Select(i => V.Label(text: "a" + i)).ToArray();

        private static ReconcilerScope Mounted(VNode[] rows)
        {
            var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), rows);
            return scope;
        }

        private static void Clear(ReconcilerScope scope, VNode[] rows)
            => scope.Reconciler.Reconcile(scope.Root, rows, Array.Empty<VNode>());

        // The pools are emptied just before the act, so what they hold after it is what the act returned.
        private static int RecordsReturnedBy(Action act, Type[] eventTypes)
        {
            var pools = eventTypes.Select(PoolOfCallbackRecords).ToArray();
            foreach (var pool in pools) pool.GetType().GetMethod("Clear")!.Invoke(pool, null);
            act();
            return pools.Sum(pool => (int)pool.GetType().GetProperty("CountInactive")!.GetValue(pool)!);
        }

        internal static object PoolOfCallbackRecords(Type eventType)
        {
            var record = typeof(VisualElement).Assembly
                .GetType("UnityEngine.UIElements.EventCallbackFunctor`1", throwOnError: true)!
                .MakeGenericType(eventType);
            return typeof(GenericPool<>).MakeGenericType(record)
                .GetField("s_Pool", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        }

        [Test]
        public void Given_RowsOfPlainContainersHoldingButtons_When_TheyAreCleared_Then_NoClickGateCallbackIsUnregistered()
        {
            // Arrange
            var rows = PlainContainerRows();
            using var scope = Mounted(rows);

            // Act
            var returned = RecordsReturnedBy(() => Clear(scope, rows), s_clickGateEvents);

            // Assert
            Assert.AreEqual(0, returned);
        }

        [Test]
        public void Given_RowsOfPlainContainersHoldingTextLeaves_When_TheyAreCleared_Then_NoTextItemCallbackIsUnregistered()
        {
            // Arrange
            var rows = PlainContainerRows();
            using var scope = Mounted(rows);

            // Act
            var returned = RecordsReturnedBy(() => Clear(scope, rows), s_textItemEvents);

            // Assert
            Assert.AreEqual(0, returned);
        }

        // GREEN_ON_BASE(characterization): the base unregisters every callback, so a pooled Button root returns at least one click-gate record there as well.
        [Test]
        public void Given_PooledButtonsAsRows_When_TheyAreCleared_Then_EachUnregistersItsClickGate()
        {
            // Arrange
            var rows = ButtonRows();
            using var scope = Mounted(rows);

            // Act
            var returned = RecordsReturnedBy(() => Clear(scope, rows), s_clickGateEvents);

            // Assert
            Assert.GreaterOrEqual(returned, Rows);
        }

        // GREEN_ON_BASE(characterization): the base unregisters every callback, so a pooled Label root returns at least one text-item record there as well.
        [Test]
        public void Given_PooledLabelsAsRows_When_TheyAreCleared_Then_EachUnregistersItsTextItemCallbacks()
        {
            // Arrange
            var rows = LabelRows();
            using var scope = Mounted(rows);

            // Act
            var returned = RecordsReturnedBy(() => Clear(scope, rows), s_textItemEvents);

            // Assert
            Assert.GreaterOrEqual(returned, Rows);
        }

        // GREEN_ON_BASE(characterization): the base drops the click gates along with their callbacks; this holds the bookkeeping to that.
        [Test]
        public void Given_RowsOfPlainContainersHoldingButtons_When_TheyAreCleared_Then_NoClickGateStaysTracked()
        {
            // Arrange
            var rows = PlainContainerRows();
            using var scope = Mounted(rows);
            var gates = (IDictionary)typeof(FiberEventBindingManager)
                .GetField("_clickGates", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(ReconcilerContextProbe.Of(scope).EventManager)!;
            var trackedWhileMounted = gates.Count > 0;

            // Act
            Clear(scope, rows);

            // Assert — the count while mounted is read with it, so a gate nothing ever tracked does not pass for one dropped.
            Assert.That((trackedWhileMounted, gates.Count), Is.EqualTo((true, 0)));
        }

        // GREEN_ON_BASE(characterization): the base removes the manipulators from their side table as well; this holds the bookkeeping to that.
        [Test]
        public void Given_RowsOfPlainContainersHoldingTextLeaves_When_TheyAreCleared_Then_NoFlexMinSizeManipulatorStaysTracked()
        {
            // Arrange
            var rows = PlainContainerRows();
            using var scope = Mounted(rows);
            var manipulators = ReconcilerContextProbe.Of(scope).FlexMinSizeManipulators;
            var trackedWhileMounted = manipulators.Count > 0;

            // Act
            Clear(scope, rows);

            // Assert — read with the count while mounted, for the reason above.
            Assert.That((trackedWhileMounted, manipulators.Count), Is.EqualTo((true, 0)));
        }

        // GREEN_ON_BASE(characterization): the base unregisters every callback, so the Button a Portal holds is unwired there as well.
        [Test]
        public void Given_APortalWhoseTargetIsInsideTheRemovedRow_When_TheRowIsCleared_Then_TheButtonItHeldIsUnwired()
        {
            // Arrange — the target is the row's first child and the Portal's placeholder its second, so the
            // target's descendants are visited before the placeholder tears the Button out of it.
            using var scope = new ReconcilerScope();
            var rowWithSlot = new VNode[] { V.Div("flex flex-row", V.Div(name: "slot")) };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), rowWithSlot);
            var slot = scope.Root.Q<VisualElement>("slot");
            var rowWithPortal = new VNode[]
            {
                V.Div("flex flex-row", V.Div(name: "slot"), V.Portal(slot, children: new VNode?[] { V.Button(text: "held", onClick: () => { }) })),
            };
            scope.Reconciler.Reconcile(scope.Root, rowWithSlot, rowWithPortal);
            var held = slot.Q<Button>();
            var gatedWhileMounted = TrickleDownCallbacks(held) > 0;

            // Act
            Clear(scope, rowWithPortal);

            // Assert — the count while mounted is read with it, so a Button that never carried a gate does not pass for one unwired.
            Assert.That((gatedWhileMounted, TrickleDownCallbacks(held)), Is.EqualTo((true, 0)));
        }

        // The click gate registers its pointer-down, pointer-up and submit callbacks in the trickle-down phase.
        private static int TrickleDownCallbacks(VisualElement element)
        {
            var registry = typeof(CallbackEventHandler)
                .GetField("m_CallbackRegistry", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(element);
            if (registry == null) return 0;
            var list = registry.GetType().GetField("m_TrickleDownCallbacks", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(registry)!;
            return (int)list.GetType().GetProperty("Count")!.GetValue(list)!;
        }
    }

    /// <summary>
    /// Pins the engine behaviour <see cref="DiscardedSubtreeTeardownTests"/> counts through: unregistering a
    /// callback from a VisualElement leaves its record in a pool that keeps it.
    /// </summary>
    [TestFixture]
    internal sealed class UnregisterCallbackRecordPoolTests
    {
        // GREEN_ON_BASE(characterization): the engine fact holds with or without the change; the cases above count through it.
        [Test]
        public void Given_CallbacksOfOneEventTypeOnSeveralElements_When_TheyAreUnregistered_Then_EachLeavesItsRecordInThePool()
        {
            // Arrange
            const int count = 5;
            var elements = Enumerable.Range(0, count).Select(_ => new VisualElement()).ToArray();
            EventCallback<PointerCancelEvent> callback = _ => { };
            foreach (var element in elements) element.RegisterCallback(callback);
            var pool = DiscardedSubtreeTeardownTests.PoolOfCallbackRecords(typeof(PointerCancelEvent));
            pool.GetType().GetMethod("Clear")!.Invoke(pool, null);

            // Act
            foreach (var element in elements) element.UnregisterCallback(callback);

            // Assert
            Assert.AreEqual(count, (int)pool.GetType().GetProperty("CountInactive")!.GetValue(pool)!);
        }
    }
}
