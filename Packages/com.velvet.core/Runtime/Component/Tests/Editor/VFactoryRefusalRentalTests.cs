using System;
using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins each <c>V.ListFragment</c> overload's NUL-key refusal to renting nothing. C# evaluates
    /// <c>V.List(...)</c> before the <c>V.Fragment</c> call it feeds, so a refusal left to that call
    /// runs after the list has rented its child array and after whatever the renderer's nodes rented
    /// — and a refused call builds no Fragment, so nothing is left to carry those to a later
    /// retirement and no later call can reach them to return them.
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
        private const string OwnedNodeArraysFieldName = "s_ownedNodeArrays";
        private const string CountPropertyName = "Count";

        private const string NulKey = "a\0b";
        private const string AcceptedKey = "ab";

        private static readonly int[] TwoItems = { 1, 2 };

        // The accepted-key case builds a Fragment no reconcile will ever retire, so its rentals would
        // stay in the rented-out sets for the rest of the run. VFactoryEnumArgumentAllocTests states
        // what that costs a later fixture: a set that resizes between two allocation measurements
        // charges one side and not the other.
        private FragmentNode? _unretired;

        [TearDown]
        public void ReturnWhatTheAcceptedCallBuilt()
        {
            if (_unretired == null) return;
            var children = _unretired.Children;
            for (var i = 0; i < children.Length; i++)
            {
                VNodePool.ReturnProps((children[i] as ElementNode)?.Props);
            }
            VNodePool.ReturnNodeArray(children);
            _unretired = null;
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
        private static (string? RefusedParam, int Props, int NodeArrays) Measure(Action call)
        {
            var props = RentedCount(OwnedPropsFieldName);
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
                RentedCount(OwnedNodeArraysFieldName) - nodeArrays);
        }

        // Neither renderer passes a props: bag of its own, so V.Draggable rents one per item.
        private static VNode RenderByItem(int item) => V.Draggable("row" + item);

        private static VNode RenderByIndex(int item, int index) => V.Draggable("row" + item + index);

        [Test]
        public void Given_AKeyedListFragmentWithANulKey_When_TheKeyIsRefused_Then_TheCallRentsNothing()
        {
            // Arrange + Act — two items, so a refusal after the list ran would strand both their
            // bags and the child array V.List rented for them.
            var refusal = Measure(() => V.ListFragment(TwoItems, item => item.ToString(), RenderByItem, NulKey));

            // Assert
            Assert.That(refusal, Is.EqualTo(("key", 0, 0)));
        }

        [Test]
        public void Given_AnIndexedListFragmentWithANulKey_When_TheKeyIsRefused_Then_TheCallRentsNothing()
        {
            // Arrange + Act — the same, on the overload whose selector and renderer take the index.
            var refusal = Measure(() =>
                V.ListFragment(TwoItems, (item, index) => item + "-" + index, RenderByIndex, NulKey));

            // Assert
            Assert.That(refusal, Is.EqualTo(("key", 0, 0)));
        }

        // GREEN_ON_BASE(characterization): the base accepts this key and rents the same three objects.
        // It is the control for the two refusal cases above, which assert absences: a probe over a set
        // that never moved would satisfy their zeroes, and this is their call with the NUL taken out of
        // the key.
        [Test]
        public void Given_AKeyedListFragmentWithAnAcceptedKey_When_ItBuildsTheFragment_Then_ItRentsWhatItBuiltFrom()
        {
            // Arrange + Act — the refusal cases' arrangement, minus the NUL.
            var accepted = Measure(() =>
                _unretired = V.ListFragment(TwoItems, item => item.ToString(), RenderByItem, AcceptedKey));

            // Assert
            Assert.That(accepted, Is.EqualTo(((string?)null, 2, 1)));
        }
    }
}
