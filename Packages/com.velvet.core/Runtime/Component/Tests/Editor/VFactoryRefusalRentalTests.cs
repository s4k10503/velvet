using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins a NUL-key refusal to renting nothing, at the <c>V.*</c> calls that take from a pool before
    /// the node carrying the key exists. A refused call builds no node, so nothing is left to
    /// carry what it rented to a later retirement and no later call can reach it to return it. Three
    /// shapes reach that: each <c>V.ListFragment</c> overload, where C# evaluates <c>V.List(...)</c>
    /// before the <c>V.Fragment</c> call it feeds; the factories that rent a props bag or an event array
    /// ahead of building the node their key goes on; and each <c>V.List</c> overload, whose selector key
    /// is not known until an item is mapped, so the array it rented is given back instead.
    /// </summary>
    /// <remarks>
    /// What these calls MAP to stays <c>VListTests</c>'s; only what they take from the pools is read
    /// here. An argument the CALLER built is a different question again and is not pinned here either:
    /// a refusing factory has that node in hand and returns none of what it rented, and
    /// <see cref="VNodePool"/>'s rented-out identity set states why.
    /// </remarks>
    [TestFixture]
    internal sealed class VFactoryRefusalRentalTests
    {
        private const string OwnedPropsFieldName = "s_ownedProps";
        private const string OwnedEventArraysFieldName = "s_ownedSingleEventArrays";
        private const string OwnedNodeArraysFieldName = "s_ownedNodeArrays";
        private const string CountPropertyName = "Count";

        private const string NulKey = "a\0b";
        private const string AcceptedKey = "ab";

        private static readonly int[] TwoItems = { 1, 2 };

        // The accepted-key cases build nodes no reconcile will ever retire, so their rentals would
        // stay in the rented-out sets for the rest of the run. VFactoryEnumArgumentAllocTests states
        // what that costs a later fixture: a set that resizes between two allocation measurements
        // charges one side and not the other.
        private readonly List<VNode?> _unretired = new();
        private readonly List<VNode?[]> _unretiredArrays = new();

        [TearDown]
        public void ReturnWhatTheAcceptedCallsBuilt()
        {
            foreach (var node in _unretired) Retire(node);
            foreach (var array in _unretiredArrays) RetireArray(array);
            _unretired.Clear();
            _unretiredArrays.Clear();
        }

        private static void Retire(VNode? node)
        {
            switch (node)
            {
                case FragmentNode fragment:
                    RetireArray(fragment.Children);
                    break;
                case PortalNode portal:
                    for (var i = 0; i < portal.Children.Length; i++) Retire(portal.Children[i]);
                    break;
                case BaseElementNode element:
                    VNodePool.ReturnProps(element.Props);
                    VNodePool.ReturnEventArray(element.Events);
                    break;
            }
        }

        private static void RetireArray(VNode?[] nodes)
        {
            for (var i = 0; i < nodes.Length; i++) Retire(nodes[i]);
            VNodePool.ReturnNodeArray(nodes);
        }

        private static int RentedCount(string fieldName)
        {
            var field = typeof(VNodePool).GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(typeof(VNodePool).FullName, fieldName);
            }
            var set = field.GetValue(null)!;
            var count = set.GetType().GetProperty(CountPropertyName, BindingFlags.Instance | BindingFlags.Public);
            if (count == null)
            {
                throw new MissingMemberException(set.GetType().FullName, CountPropertyName);
            }
            return (int)count.GetValue(set)!;
        }

        // The refused parameter travels with the growth so one comparison covers both: a refusal
        // swapped for a silently empty Fragment rents nothing either, and the growth alone reads
        // that as the fix.
        private static (string? RefusedParam, int Props, int EventArrays, int NodeArrays) Measure(Action call)
        {
            var props = RentedCount(OwnedPropsFieldName);
            var eventArrays = RentedCount(OwnedEventArraysFieldName);
            var nodeArrays = RentedCount(OwnedNodeArraysFieldName);
            string? refusedParam = null;
            try
            {
                call();
            }
            catch (ArgumentException ex)
            {
                refusedParam = ex.ParamName;
            }
            return (refusedParam,
                RentedCount(OwnedPropsFieldName) - props,
                RentedCount(OwnedEventArraysFieldName) - eventArrays,
                RentedCount(OwnedNodeArraysFieldName) - nodeArrays);
        }

        // Neither renderer passes a props: bag of its own, so V.Draggable rents one per item.
        private static VNode RenderByItem(int item) => V.Draggable("row" + item);

        private static VNode RenderByIndex(int item, int index) => V.Draggable("row" + item + index);

        // GREEN_ON_BASE(refactor): the base refuses this key ahead of the mapping already; what changed
        // here is the event-array term the measurement gained, which reads zero on both sides.
        [Test]
        public void Given_AKeyedListFragmentWithANulKey_When_TheKeyIsRefused_Then_TheCallRentsNothing()
        {
            // Arrange + Act — two items, so a refusal after the list ran would strand both their
            // bags and the child array V.List rented for them.
            var refusal = Measure(() => V.ListFragment(TwoItems, item => item.ToString(), RenderByItem, NulKey));

            // Assert
            Assert.That(refusal, Is.EqualTo(("key", 0, 0, 0)));
        }

        // GREEN_ON_BASE(refactor): the overload above's reason, on the overload taking the index.
        [Test]
        public void Given_AnIndexedListFragmentWithANulKey_When_TheKeyIsRefused_Then_TheCallRentsNothing()
        {
            // Arrange + Act — the same, on the overload whose selector and renderer take the index.
            var refusal = Measure(() =>
                V.ListFragment(TwoItems, (item, index) => item + "-" + index, RenderByIndex, NulKey));

            // Assert
            Assert.That(refusal, Is.EqualTo(("key", 0, 0, 0)));
        }

        // GREEN_ON_BASE(characterization): the base accepts this key and rents the same three objects,
        // the event-array term this measurement gained reading zero there too. It is the control for the
        // two refusal cases above, which assert absences: a probe over a set that never moved would
        // satisfy their zeroes, and this is their call with the NUL taken out of the key.
        [Test]
        public void Given_AKeyedListFragmentWithAnAcceptedKey_When_ItBuildsTheFragment_Then_ItRentsWhatItBuiltFrom()
        {
            // Arrange + Act — the refusal cases' arrangement, minus the NUL.
            var accepted = Measure(() =>
                _unretired.Add(V.ListFragment(TwoItems, item => item.ToString(), RenderByItem, AcceptedKey)));

            // Assert
            Assert.That(accepted, Is.EqualTo(((string?)null, 2, 0, 1)));
        }

        // One call per factory that takes from a pool before the node carrying its key exists, supplying
        // whatever argument makes it rent where the rent is conditional. The accepted-key case below is
        // what says a row rents at all; the key travels as an argument so both cases read the same call.
        private static readonly (string Name, Func<string?, VNode> Build)[] FactoriesRentingBeforeTheirNode =
        {
            ("ScrollView", k => V.ScrollView(key: k, verticalScrollerVisibility: ScrollerVisibility.Auto)),
            ("Button", k => V.Button(key: k, onClick: () => { })),
            ("Label", k => V.Label(key: k, text: "x")),
            ("Slider", k => V.Slider(key: k, onValueChanged: value => { })),
            ("Toggle", k => V.Toggle(key: k, onValueChanged: value => { })),
            ("TextField", k => V.TextField(key: k, onValueChanged: value => { })),
            ("SceneView", k => V.SceneView(camera: null, key: k)),
            ("Particles", k => V.Particles(effect: null, key: k)),
            ("DropdownField", k => V.DropdownField(key: k, onValueChanged: value => { })),
            ("ListView", k => V.ListView(key: k, enabled: true)),
            ("RadioButton", k => V.RadioButton(key: k, onValueChanged: value => { })),
            ("RadioButtonGroup", k => V.RadioButtonGroup(key: k, onValueChanged: value => { })),
            ("IntegerField", k => V.IntegerField(key: k, onValueChanged: value => { })),
            ("Anchored", k => V.Anchored(target: null, key: k)),
            ("FocusScope", k => V.FocusScope(key: k)),
            ("DndContext", k => V.DndContext(key: k)),
            ("Draggable", k => V.Draggable("row", key: k)),
            ("Droppable", k => V.Droppable("row", key: k)),
            ("DragOverlay", k => V.DragOverlay(key: k)),
        };

        private static string Report(IEnumerable<string> rows) => string.Join(" ", rows);

        [Test]
        public void Given_EveryFactoryRentingBeforeItsNode_When_ANulKeyIsRefused_Then_TheCallRentsNothing()
        {
            // Arrange + Act — one report rather than one case each, so a failure names the factory
            var measured = Report(Array.ConvertAll(FactoriesRentingBeforeTheirNode, factory =>
            {
                var refusal = Measure(() => factory.Build(NulKey));
                return $"{factory.Name}={refusal.RefusedParam}/{refusal.Props}/{refusal.EventArrays}/{refusal.NodeArrays}";
            }));

            // Assert
            Assert.That(measured, Is.EqualTo(Report(Array.ConvertAll(
                FactoriesRentingBeforeTheirNode, factory => $"{factory.Name}=key/0/0/0"))));
        }

        // GREEN_ON_BASE(characterization): every row rents on the base too, its key being accepted there.
        // It is the control for the row report above, which asserts absences: a row whose call happens to
        // rent nothing satisfies that report's zeroes without the refusal having done anything.
        [Test]
        public void Given_EveryFactoryRentingBeforeItsNode_When_ItsKeyIsAccepted_Then_ItTakesFromAPool()
        {
            // Arrange + Act — the row report's calls, with the delimiter taken out of the key
            var measured = Report(Array.ConvertAll(FactoriesRentingBeforeTheirNode, factory =>
            {
                var accepted = Measure(() => _unretired.Add(factory.Build(AcceptedKey)));
                var took = accepted.Props + accepted.EventArrays + accepted.NodeArrays;
                return $"{factory.Name}={accepted.RefusedParam}/{took > 0}";
            }));

            // Assert
            Assert.That(measured, Is.EqualTo(Report(Array.ConvertAll(
                FactoriesRentingBeforeTheirNode, factory => $"{factory.Name}=/True"))));
        }

        [Test]
        public void Given_AKeyedListSelectorReturningANulKey_When_TheKeyIsRefused_Then_TheChildArrayIsGivenBack()
        {
            // Arrange + Act — the first item is mapped before its key is asked for, so its bag is out on
            // loan when the refusal fires; what the pool's rented-out set says about a renderer's node
            // leaves that where it is, and the array V.List itself took is the one that comes back.
            var refusal = Measure(() => V.List(TwoItems, item => NulKey, RenderByItem));

            // Assert
            Assert.That(refusal, Is.EqualTo(("key", 1, 0, 0)));
        }

        [Test]
        public void Given_AnIndexedListSelectorReturningANulKey_When_TheKeyIsRefused_Then_TheChildArrayIsGivenBack()
        {
            // Arrange + Act — the same, on the overload whose selector and renderer take the index.
            var refusal = Measure(() => V.List(TwoItems, (item, index) => NulKey, RenderByIndex));

            // Assert
            Assert.That(refusal, Is.EqualTo(("key", 1, 0, 0)));
        }

        // GREEN_ON_BASE(characterization): the base accepts these keys and keeps the array out on loan.
        // It is the control for the two cases above: an array the call never rented would satisfy their
        // zero without the refusal having given anything back.
        [Test]
        public void Given_AKeyedListWithAcceptedKeys_When_ItMapsTheItems_Then_TheChildArrayStaysOnLoan()
        {
            // Arrange + Act — the refusal cases' arrangement, minus the NUL.
            var accepted = Measure(() =>
            {
                var mapped = V.List(TwoItems, item => item.ToString(), RenderByItem);
                _unretiredArrays.Add(mapped);
            });

            // Assert
            Assert.That(accepted, Is.EqualTo(((string?)null, 2, 0, 1)));
        }
    }
}
