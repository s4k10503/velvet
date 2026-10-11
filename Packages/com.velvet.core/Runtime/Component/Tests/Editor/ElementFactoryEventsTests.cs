using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the <c>events:</c> parameter the element factories take, through a mounted tree: a binding
    /// handed to a factory other than <c>V.Motion</c> reaches its element, a factory's own callback and the
    /// caller's bindings both fire, its own first, unless they are one delegate, which fires once, and a
    /// later render replaces or removes what an earlier one bound. It also reads <c>V.VirtualList</c>'s array
    /// on the list's ScrollView, and the one click a button's <see cref="ClickedEventBinding"/>s share.
    /// <see cref="ElementFactoryEventsInventoryTests"/> holds the factories returning an element node to
    /// the rule; these read what a mounted element does with it.
    /// </summary>
    [TestFixture]
    internal sealed class ElementFactoryEventsTests
    {
        private VisualElement _root;

        private static readonly List<string> s_log = new();
        private static StateUpdater<int> s_setPhase;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_log.Clear();
            s_setPhase = default;
        }

        private static void Dispatch(VisualElement target)
        {
            using var evt = PointerDownEvent.GetPooled();
            target.SimulateEvent(evt);
        }

        [Test]
        public void Given_AShorthandWithEventsAndAnEmptyChildrenArray_When_Built_Then_ItHoldsTheChildrenTheShorthandWithoutEventsHolds()
        {
            // Arrange — a new array, so only the empty-children reading can make the two nodes share it.
            var events = new FiberEventBinding[] { new PointerDownBinding { Handler = _ => { } } };

            // Act
            var node = V.Div("p-4", events, new VNode[0]);

            // Assert
            Assert.That(ReferenceEquals(node.Children, V.Div("p-4").Children), Is.True);
        }

        [Test]
        public void Given_ASceneViewDeclaringAPointerDownBinding_When_APointerDownReachesIt_Then_TheHandlerRuns()
        {
            // Arrange — the shape that used to need a V.Motion wrapper animating nothing.
            using var mounted = V.Mount(_root, V.SceneView(null, name: "view", events: new FiberEventBinding[]
            {
                new PointerDownBinding { Handler = _ => s_log.Add("down") },
            }));

            // Act
            Dispatch(_root.Q<SceneViewElement>("view"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("down"));
        }

        [Test]
        public void Given_AButtonWithOnClickAndAClickedBindingInEvents_When_Clicked_Then_OnClickRunsAndThenTheBinding()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Button(
                name: "button",
                onClick: () => s_log.Add("onClick"),
                events: new FiberEventBinding[] { new ClickedBinding { Handler = () => s_log.Add("events") } }));

            // Act
            _root.Q<Button>("button").SimulateClick();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("onClick,events"));
        }

        [Test]
        public void Given_AToggleWithOnValueChangedAndAChangeBindingInEvents_When_ItsValueChanges_Then_OnValueChangedRunsAndThenTheBinding()
        {
            // Arrange — the value-change sibling of the click case above: these two bind through the
            // element's change callbacks rather than through Button.clicked.
            using var mounted = V.Mount(_root, V.Toggle(
                name: "toggle",
                onValueChanged: _ => s_log.Add("onValueChanged"),
                events: new FiberEventBinding[] { new ChangeEventBinding<bool> { Handler = _ => s_log.Add("events") } }));

            // Act
            _root.Q<Toggle>("toggle").SimulateChange(true);

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("onValueChanged,events"));
        }

        private static void LogClick() => s_log.Add("click");

        [Test]
        public void Given_AButtonGivenOneDelegateAsOnClickAndInEvents_When_Clicked_Then_ItRunsOnce()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Button(
                name: "button",
                onClick: LogClick,
                events: new FiberEventBinding[] { new ClickedBinding { Handler = LogClick } }));

            // Act
            _root.Q<Button>("button").SimulateClick();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("click"));
        }

        [Test]
        public void Given_TwoClickedEventBindingsTheFirstOfWhichPrevents_When_Clicked_Then_TheSecondSeesThePreventedClick()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Button(name: "button", events: new FiberEventBinding[]
            {
                new ClickedEventBinding { Handler = click => click.PreventDefault() },
                new ClickedEventBinding { Handler = click => s_log.Add(click.DefaultPrevented.ToString()) },
            }));

            // Act
            _root.Q<Button>("button").SimulateClick();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("True"));
        }

        [Test]
        public void Given_AClickedEventBindingThatPrevents_When_ClickedTwice_Then_EachClickArrivesUnprevented()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Button(name: "button", events: new FiberEventBinding[]
            {
                new ClickedEventBinding
                {
                    Handler = click =>
                    {
                        s_log.Add(click.DefaultPrevented.ToString());
                        click.PreventDefault();
                    },
                },
            }));
            var button = _root.Q<Button>("button");

            // Act
            button.SimulateClick();
            button.SimulateClick();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("False,False"));
        }

        private static readonly int[] Rows = { 1, 2, 3 };

        private static VNode Row(int row) => V.Label(text: row.ToString());

        [Component]
        private static VNode SwappingListHandler()
        {
            var (phase, setPhase) = Hooks.UseState(0);
            s_setPhase = setPhase;
            return V.VirtualList(Rows, row => row.ToString(), 20f, Row, events: new FiberEventBinding[]
            {
                new PointerDownBinding { Handler = _ => s_log.Add(phase == 0 ? "first" : "second") },
            });
        }

        [Test]
        public void Given_AVirtualListDeclaringAPointerDownBinding_When_APointerDownReachesItsScrollView_Then_TheHandlerRuns()
        {
            // Arrange — a VirtualList is not an element node; its ScrollView is built by the list's own path.
            using var mounted = V.Mount(_root, V.Component(SwappingListHandler));

            // Act
            Dispatch(_root.Q<ScrollView>());

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("first"));
        }

        [Test]
        public void Given_AVirtualListWhoseHandlerARenderReplaces_When_APointerDownReachesItsScrollView_Then_OnlyTheNewHandlerRuns()
        {
            // Arrange — the list patches its ScrollView in place rather than through the element path.
            using var mounted = V.Mount(_root, V.Component(SwappingListHandler));
            s_setPhase.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            Dispatch(_root.Q<ScrollView>());

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("second"));
        }

        [Component]
        private static VNode SwappingHandler()
        {
            var (phase, setPhase) = Hooks.UseState(0);
            s_setPhase = setPhase;
            return V.Div(name: "target", events: phase switch
            {
                0 => new FiberEventBinding[] { new PointerDownBinding { Handler = _ => s_log.Add("first") } },
                1 => new FiberEventBinding[] { new PointerDownBinding { Handler = _ => s_log.Add("second") } },
                _ => null,
            });
        }

        [Test]
        public void Given_ADivWhosePointerDownHandlerARenderReplaces_When_APointerDownReachesIt_Then_OnlyTheNewHandlerRuns()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SwappingHandler));
            s_setPhase.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            Dispatch(_root.Q<VisualElement>("target"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("second"));
        }

        [Test]
        public void Given_ADivWhosePointerDownHandlerARenderDrops_When_APointerDownReachesIt_Then_OnlyTheEarlierDispatchRanIt()
        {
            // Arrange — a dispatch before the drop, whose entry in the log is what tells a dropped handler
            // from one that was never bound.
            using var mounted = V.Mount(_root, V.Component(SwappingHandler));
            var target = _root.Q<VisualElement>("target");
            Dispatch(target);

            // Act
            s_setPhase.Invoke(2);
            mounted.FlushStateForTest();
            Dispatch(target);

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("first"));
        }
    }
}
