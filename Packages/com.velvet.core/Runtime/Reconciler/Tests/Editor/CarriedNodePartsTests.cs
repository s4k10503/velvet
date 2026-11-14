using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A node held inside a composite value — a props record, a record in a state or memo slot, a Store's state —
    /// is left out of one render and put back by the next. When it comes back it shows the text it was built
    /// with.
    /// </summary>
    [TestFixture]
    internal sealed class CarriedNodePartsTests
    {
        private const string CarriedText = "carried";

        private sealed record Carrier(VNode Node);

        private sealed class CarrierStore : Store<Carrier>
        {
            public CarrierStore(Carrier initial) : base(initial) { }

            protected override void ResetCore() { }
        }

        private static StateUpdater<bool> s_setShown;
        private static CarrierStore s_store = null!;

        private VisualElement _root = null!;
        private MountedTree _mounted = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_setShown = default;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null!;
            s_store?.Dispose();
            s_store = null!;
        }

        private static VNode Tree(bool shown, Carrier carrier)
            => V.Div(name: "host", children: new VNode?[] { shown ? carrier.Node : V.Label(text: "stand-in") });

        [Component(Compiler = false)]
        private static VNode PropsCarrier(Carrier carrier)
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return Tree(shown, carrier);
        }

        [Component(Compiler = false)]
        private static VNode StateCarrier()
        {
            var (carrier, _) = Hooks.UseState(() => new Carrier(V.Label(text: CarriedText)));
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return Tree(shown, carrier);
        }

        [Component(Compiler = false)]
        private static VNode MemoCarrier()
        {
            var carrier = Hooks.UseMemo(() => new Carrier(V.Label(text: CarriedText)), System.Array.Empty<object>());
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return Tree(shown, carrier);
        }

        [Component(Compiler = false)]
        private static VNode StoreCarrier()
        {
            var carrier = Hooks.UseStore(s_store, state => state);
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return Tree(shown, carrier);
        }

        private string ShownAfterLeavingOutAndPuttingBack(VNode app)
        {
            _mounted = V.Mount(_root, app);
            _mounted.DrainForTest();
            s_setShown.Invoke(false);
            _mounted.DrainForTest();
            s_setShown.Invoke(true);
            _mounted.DrainForTest();
            return ((Label)_root.Q("host")[0]).text;
        }

        [Test]
        public void Given_ANodeCarriedInAPropsRecord_When_ARenderLeavesItOutAndTheNextPutsItBack_Then_ItShowsItsOwnText()
        {
            // Arrange
            var carrier = new Carrier(V.Label(text: CarriedText));

            // Act
            var shown = ShownAfterLeavingOutAndPuttingBack(V.Component(PropsCarrier, carrier));

            // Assert
            Assert.That(shown, Is.EqualTo(CarriedText));
        }

        [Test]
        public void Given_ANodeCarriedInARecordInAStateSlot_When_ARenderLeavesItOutAndTheNextPutsItBack_Then_ItShowsItsOwnText()
        {
            // Arrange
            var app = V.Component(StateCarrier);

            // Act
            var shown = ShownAfterLeavingOutAndPuttingBack(app);

            // Assert
            Assert.That(shown, Is.EqualTo(CarriedText));
        }

        [Test]
        public void Given_ANodeCarriedInARecordInAMemoSlot_When_ARenderLeavesItOutAndTheNextPutsItBack_Then_ItShowsItsOwnText()
        {
            // Arrange
            var app = V.Component(MemoCarrier);

            // Act
            var shown = ShownAfterLeavingOutAndPuttingBack(app);

            // Assert
            Assert.That(shown, Is.EqualTo(CarriedText));
        }

        [Test]
        public void Given_ANodeCarriedInAStoresState_When_ARenderLeavesItOutAndTheNextPutsItBack_Then_ItShowsItsOwnText()
        {
            // Arrange
            s_store = new CarrierStore(new Carrier(V.Label(text: CarriedText)));
            var app = V.Component(StoreCarrier);

            // Act
            var shown = ShownAfterLeavingOutAndPuttingBack(app);

            // Assert
            Assert.That(shown, Is.EqualTo(CarriedText));
        }
    }
}
