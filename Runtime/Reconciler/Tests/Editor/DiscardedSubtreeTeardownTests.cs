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
    /// <item>Below the root of a removal that nothing pools afterwards, the Button click gate, the text-item
    /// manipulators and the element's event bindings leave their callbacks registered: the elements they sit on
    /// go with the subtree.</item>
    /// <item>A removal that pools its root, or that tears out a Portal range lying inside the subtree, still
    /// unregisters what the next rent could not use again. A pooled Button's click gate is not one of them: it stays
    /// registered, scrubbed, for whoever rents the Button next.</item>
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
            // A click gate registers its callbacks once a portal bridge exists in the process, so a bridge another
            // case left behind would register them in the cases that count on there being none.
            typeof(FiberCrossPanelEventDispatcher)
                .GetMethod("ResetStaticFields", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, null);
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

        // The bridge a portal puts on its target, held on the scope's root; the click gates register their callbacks
        // once one exists.
        private readonly struct HeldBridge : IDisposable
        {
            private readonly Action _release;

            public HeldBridge(ReconcilerScope scope) =>
                _release = FiberCrossPanelEventDispatcher.AttachBridge(scope.Root, ReconcilerContextProbe.Of(scope));

            public void Dispose() => _release();
        }

        private static ReconcilerScope MountedUnderABridge(VNode[] rows, out HeldBridge bridge)
        {
            var scope = new ReconcilerScope();
            bridge = new HeldBridge(scope);
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

        // GREEN_ON_BASE(characterization): the base registers every gate at its Button's first binding and leaves it
        // registered below a discarded root; the bridge held here gives this base-side count the same gates to count.
        [Test]
        public void Given_RowsOfPlainContainersHoldingButtons_When_TheyAreCleared_Then_NoClickGateCallbackIsUnregistered()
        {
            // Arrange
            var rows = PlainContainerRows();
            using var scope = MountedUnderABridge(rows, out var bridge);
            using var held = bridge;

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

        // GREEN_ON_BASE(characterization): the base keeps a pooled Button's gate registered through the clear; the
        // bridge held here gives the count the same gates to count.
        [Test]
        public void Given_PooledButtonsAsRows_When_TheyAreCleared_Then_NoClickGateCallbackIsUnregistered()
        {
            // Arrange
            var rows = ButtonRows();
            using var scope = MountedUnderABridge(rows, out var bridge);
            using var held = bridge;

            // Act
            var returned = RecordsReturnedBy(() => Clear(scope, rows), s_clickGateEvents);

            // Assert
            Assert.AreEqual(0, returned);
        }

        // GREEN_ON_BASE(characterization): the base keeps the gate registered through the clear and adopts it on the
        // rent; this holds the adopting side to one set of callbacks per Button, with the bridge held that the gate
        // registers under.
        [Test]
        public void Given_PooledButtonsAsRows_When_TheyAreClearedAndMountedAgain_Then_TheSameButtonsHoldOneSetOfClickGateCallbacks()
        {
            // Arrange
            var rows = ButtonRows();
            using var scope = MountedUnderABridge(rows, out var bridge);
            using var held = bridge;
            var first = scope.Root.Query<Button>().ToList();
            Clear(scope, rows);

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), rows);

            // Assert — the same instances come back, so a gate registered afresh would stand beside the one they kept.
            var second = scope.Root.Query<Button>().ToList();
            Assert.That(
                (second.Count(button => first.Contains(button)), second.Sum(TrickleDownCallbacks)),
                Is.EqualTo((Rows, 3 * Rows)));
        }

        // GREEN_ON_BASE(characterization): the base scrubs a pooled Button's gate and keeps it registered; the bridge
        // held here is what registers it under the change.
        [Test]
        public void Given_APooledButtonWithAPressRecorded_When_ItIsCleared_Then_ItsGateStaysRegisteredWithNoPressOrigin()
        {
            // Arrange
            var rows = ButtonRows().Take(1).ToArray();
            using var scope = MountedUnderABridge(rows, out var bridge);
            using var held = bridge;
            var button = scope.Root.Q<Button>();
            var tracked = (IDictionary)typeof(FiberEventBindingManager)
                .GetField("_clickGates", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(ReconcilerContextProbe.Of(scope).EventManager)!;
            var gate = tracked[button]!;
            ((System.Collections.Generic.ISet<int>)gate.GetType().GetField("OffChainDowns")!.GetValue(gate)!).Add(7);
            gate.GetType().GetField("Suppress")!.SetValue(gate, true);

            // Act
            Clear(scope, rows);

            // Assert
            Assert.That(GateAfterClear(button, gate), Is.EqualTo("3,0,False"));
        }

        // The gate a pooled Button keeps through a clear, rendered as the trickle-down callbacks still registered on
        // the Button, the press origins it holds and its verdict.
        private static string GateAfterClear(Button button, object gate)
        {
            var downs = (System.Collections.Generic.ISet<int>)gate.GetType().GetField("OffChainDowns")!.GetValue(gate)!;
            return $"{TrickleDownCallbacks(button)},{downs.Count},{gate.GetType().GetField("Suppress")!.GetValue(gate)}";
        }

        [Test]
        public void Given_NoPortalBridge_When_ButtonsWithAnOnClickAreMounted_Then_NoneHoldsAClickGateCallback()
        {
            // Arrange
            var rows = ButtonRows();

            // Act
            using var scope = Mounted(rows);

            // Assert
            Assert.AreEqual(0, scope.Root.Query<Button>().ToList().Sum(TrickleDownCallbacks));
        }

        // GREEN_ON_BASE(characterization): the base registers each gate when its Button binds, so the count holds
        // there before a bridge attaches as well; this holds the gates made before one to registering when it does.
        [Test]
        public void Given_ButtonsMountedBeforeAnyPortalBridge_When_ABridgeAttaches_Then_EachHoldsOneSetOfClickGateCallbacks()
        {
            // Arrange
            using var scope = Mounted(ButtonRows());
            var buttons = scope.Root.Query<Button>().ToList();

            // Act
            using var bridge = new HeldBridge(scope);

            // Assert
            Assert.AreEqual(3 * Rows, buttons.Sum(TrickleDownCallbacks));
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

        [Test]
        public void Given_APortalWhoseTargetIsInsideTheRemovedRow_When_TheRowIsCleared_Then_TheButtonItHeldKeepsOnlyItsGate()
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

            // Assert — the count while mounted is read with it, so a Button that never carried a gate does not pass for one parked.
            Assert.That((gatedWhileMounted, TrickleDownCallbacks(held)), Is.EqualTo((true, 3)));
        }

        private static FiberEventBinding[] PressBindings() => new FiberEventBinding[] { new PointerDownBinding { Handler = _ => { } } };

        private static readonly Type[] s_pointerDownEvents = { typeof(PointerDownEvent) };

        [Test]
        public void Given_RowsOfPlainContainersHoldingAChildWithAPointerDownBinding_When_TheyAreCleared_Then_NoPointerDownCallbackIsUnregistered()
        {
            // Arrange
            var rows = Enumerable.Range(0, Rows).Select(_ => (VNode)V.Div("flex flex-row", V.Div("p-1", PressBindings()))).ToArray();
            using var scope = Mounted(rows);

            // Act
            var returned = RecordsReturnedBy(() => Clear(scope, rows), s_pointerDownEvents);

            // Assert
            Assert.AreEqual(0, returned);
        }

        // GREEN_ON_BASE(characterization): the base unregisters every binding, the removal's own root included, which
        // is what the root keeps doing: only what lies below it is left registered.
        [Test]
        public void Given_RowsOfPlainContainersWithAPointerDownBindingOfTheirOwn_When_TheyAreCleared_Then_EachUnregistersIt()
        {
            // Arrange
            var rows = Enumerable.Range(0, Rows).Select(_ => (VNode)V.Div("flex flex-row", PressBindings())).ToArray();
            using var scope = Mounted(rows);

            // Act
            var returned = RecordsReturnedBy(() => Clear(scope, rows), s_pointerDownEvents);

            // Assert
            Assert.GreaterOrEqual(returned, Rows);
        }

        // GREEN_ON_BASE(characterization): the base runs each unbind action, one of which drops the Button's click-event
        // slot; this holds the bookkeeping to being dropped where the actions are left unrun.
        [Test]
        public void Given_RowsOfPlainContainersHoldingButtonsWithAClickedEventBinding_When_TheyAreCleared_Then_NothingStaysTrackedForThem()
        {
            // Arrange
            var bindings = new FiberEventBinding[] { new ClickedEventBinding { Handler = _ => { } } };
            var rows = Enumerable.Range(0, Rows).Select(_ => (VNode)V.Div("flex flex-row", V.Button("p-1", bindings))).ToArray();
            using var scope = Mounted(rows);
            var manager = ReconcilerContextProbe.Of(scope).EventManager;
            IDictionary Table(string name) => (IDictionary)typeof(FiberEventBindingManager)
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
            var trackedWhileMounted = Table("_clickedEventSlots").Count > 0 && Table("_unbindActions").Count > 0;

            // Act
            Clear(scope, rows);

            // Assert — read with the counts while mounted, so a table nothing ever filled does not pass for one emptied.
            Assert.That(
                (trackedWhileMounted, Table("_clickedEventSlots").Count, Table("_unbindActions").Count),
                Is.EqualTo((true, 0, 0)));
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
