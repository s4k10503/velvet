// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Velvet;
using Velvet.TestUtilities;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="FiberEventBindingManager"/>, which attaches and removes UIToolkit
    /// event callbacks for declarative event bindings.
    /// <list type="bullet">
    /// <item>A discrete user-input binding (pointer down/up, key, focus, click, value change) runs its handler
    /// with <see cref="FiberWorkLoop.IsInDiscreteEvent"/> set, and the flag is restored once the handler returns,
    /// so a state update the handler triggers takes the Urgent lane.</item>
    /// <item>A continuous binding (pointer move/enter/leave, wheel, geometry) runs its handler with the discrete
    /// flag unset.</item>
    /// <item>A click binding dispatches to its handler; typed change bindings dispatch the changed value (float,
    /// bool, string) to their handler.</item>
    /// <item>Binding a null element is tolerated and binds nothing.</item>
    /// <item>Binding the same delegate twice for one element collapses to a single registration.</item>
    /// <item><c>UnbindAll</c> removes every handler for an element and is idempotent; <c>Clear</c> unbinds every
    /// element, after which a per-element unbind is a no-op.</item>
    /// <item><c>HasSameBindings</c> reports equality by delegate identity and count: same delegate(s) is equal,
    /// a different delegate or a count mismatch is not, and an unbound element equals both null and an empty set.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class EventBindingManagerTests
    {
        private FiberEventBindingManager _manager;

        [SetUp]
        public void SetUp()
        {
            // IsInDiscreteEvent is a process-global static; RunDiscrete restores it via finally, but reset it here
            // too so the discrete/continuous assertions never depend on a prior test's teardown order.
            FiberWorkLoop.IsInDiscreteEvent = false;
            _manager = new FiberEventBindingManager();
        }

        [TearDown]
        public void TearDown()
        {
            _manager.Clear();
        }

        [Test]
        public void Given_DiscretePointerDownBinding_When_EventDispatched_Then_HandlerRunsInsideDiscreteEvent()
        {
            // Arrange
            var element = new VisualElement();
            var inDiscreteEventDuringHandler = false;
            _manager.Bind(element, new PointerDownBinding
            {
                Handler = _ => inDiscreteEventDuringHandler = FiberWorkLoop.IsInDiscreteEvent,
            });

            // Act
            using var evt = PointerDownEvent.GetPooled();
            element.SimulateEvent(evt);

            // Assert
            Assert.IsTrue(inDiscreteEventDuringHandler,
                "A discrete pointer-down handler runs with IsInDiscreteEvent set");
        }

        [Test]
        public void Given_DiscretePointerDownBinding_When_HandlerReturns_Then_DiscreteFlagIsRestored()
        {
            // Arrange
            var element = new VisualElement();
            _manager.Bind(element, new PointerDownBinding { Handler = _ => { } });

            // Act
            using var evt = PointerDownEvent.GetPooled();
            element.SimulateEvent(evt);

            // Assert
            Assert.IsFalse(FiberWorkLoop.IsInDiscreteEvent, "The discrete flag is restored after the handler returns");
        }

        [Test]
        public void Given_ContinuousPointerMoveBinding_When_EventDispatched_Then_HandlerRunsOutsideDiscreteEvent()
        {
            // Arrange
            var element = new VisualElement();
            var inDiscreteEventDuringHandler = true;
            _manager.Bind(element, new PointerMoveBinding
            {
                Handler = _ => inDiscreteEventDuringHandler = FiberWorkLoop.IsInDiscreteEvent,
            });

            // Act
            using var evt = PointerMoveEvent.GetPooled();
            element.SimulateEvent(evt);

            // Assert
            Assert.IsFalse(inDiscreteEventDuringHandler,
                "A continuous pointer-move handler runs without IsInDiscreteEvent");
        }

        [Test]
        public void Given_ClickedBinding_When_ButtonClicked_Then_HandlerIsInvoked()
        {
            // Arrange
            var button = new Button();
            var invoked = false;
            _manager.Bind(button, new ClickedBinding { Handler = () => invoked = true });

            // Act
            button.SimulateClick();

            // Assert
            Assert.That(invoked, Is.True);
        }

        [Test]
        public void Given_ChangeEventFloatBinding_When_ValueChanged_Then_HandlerReceivesValue()
        {
            // Arrange
            var slider = new Slider();
            float receivedValue = 0;
            _manager.Bind(slider, new ChangeEventBinding<float> { Handler = v => receivedValue = v });

            // Act
            slider.SimulateChange(0.7f);

            // Assert
            Assert.That(receivedValue, Is.EqualTo(0.7f));
        }

        [Test]
        public void Given_ChangeEventBoolBinding_When_ValueChanged_Then_HandlerReceivesValue()
        {
            // Arrange
            var toggle = new Toggle();
            bool? receivedValue = null;
            _manager.Bind(toggle, new ChangeEventBinding<bool> { Handler = v => receivedValue = v });

            // Act
            toggle.SimulateChange(true);

            // Assert
            Assert.That(receivedValue, Is.True);
        }

        [Test]
        public void Given_ChangeEventStringBinding_When_ValueChanged_Then_HandlerReceivesValue()
        {
            // Arrange
            var textField = new TextField();
            string? receivedValue = null;
            _manager.Bind(textField, new ChangeEventBinding<string> { Handler = v => receivedValue = v });

            // Act
            textField.SimulateChange("hello");

            // Assert
            Assert.That(receivedValue, Is.EqualTo("hello"));
        }

        [Test]
        public void Given_NullElement_When_Bound_Then_DoesNotThrow()
        {
            // Act + Assert
            Assert.DoesNotThrow(() => _manager.Bind(null, new ClickedBinding { Handler = () => { } }));
        }

        [Test]
        public void Given_ElementWithTwoBindings_When_UnbindAllCalledTwice_Then_IsIdempotent()
        {
            // Arrange
            var button = new Button();
            _manager.Bind(button, new ClickedBinding { Handler = () => { } });
            _manager.Bind(button, new ClickedBinding { Handler = () => { } });

            // Act + Assert — the second unbind is a no-op, not an error
            _manager.UnbindAll(button);
            Assert.DoesNotThrow(() => _manager.UnbindAll(button));
        }

        [Test]
        public void Given_MultipleBoundElements_When_Cleared_Then_SubsequentUnbindIsNoOp()
        {
            // Arrange
            var button1 = new Button();
            var button2 = new Button();
            _manager.Bind(button1, new ClickedBinding { Handler = () => { } });
            _manager.Bind(button2, new ClickedBinding { Handler = () => { } });

            // Act
            _manager.Clear();

            // Assert
            Assert.DoesNotThrow(() => _manager.UnbindAll(button1));
        }

        [Test]
        public void Given_SameDelegateBoundTwice_When_QueriedWithSingleBinding_Then_HasSameBindings()
        {
            // Arrange — a duplicate Bind of the same delegate collapses to one registration
            var button = new Button();
            Action handler = () => { };
            _manager.Bind(button, new ClickedBinding { Handler = handler });
            _manager.Bind(button, new ClickedBinding { Handler = handler });

            // Act
            var singleEvent = new FiberEventBinding[] { new ClickedBinding { Handler = handler } };

            // Assert
            Assert.That(_manager.HasSameBindings(button, singleEvent), Is.True);
        }

        #region HasSameBindings

        [Test]
        public void Given_BoundDelegate_When_ComparedWithSameDelegate_Then_HasSameBindings()
        {
            // Arrange
            var button = new Button();
            Action handler = () => { };
            _manager.Bind(button, new ClickedBinding { Handler = handler });

            // Act
            var newEvents = new FiberEventBinding[] { new ClickedBinding { Handler = handler } };

            // Assert
            Assert.That(_manager.HasSameBindings(button, newEvents), Is.True);
        }

        [Test]
        public void Given_BoundDelegate_When_ComparedWithDifferentDelegate_Then_NotSameBindings()
        {
            // Arrange
            var button = new Button();
            _manager.Bind(button, new ClickedBinding { Handler = () => { } });

            // Act
            var newEvents = new FiberEventBinding[] { new ClickedBinding { Handler = () => { } } };

            // Assert
            Assert.That(_manager.HasSameBindings(button, newEvents), Is.False);
        }

        [Test]
        public void Given_UnboundElement_When_ComparedWithNull_Then_HasSameBindings()
        {
            // Arrange
            var button = new Button();

            // Act + Assert
            Assert.That(_manager.HasSameBindings(button, null), Is.True);
        }

        [Test]
        public void Given_UnboundElement_When_ComparedWithEmptySet_Then_HasSameBindings()
        {
            // Arrange
            var button = new Button();

            // Act + Assert
            Assert.That(_manager.HasSameBindings(button, System.Array.Empty<FiberEventBinding>()), Is.True);
        }

        [Test]
        public void Given_SingleBoundDelegate_When_ComparedWithTwoBindings_Then_NotSameBindings()
        {
            // Arrange
            var button = new Button();
            Action handler = () => { };
            _manager.Bind(button, new ClickedBinding { Handler = handler });

            // Act
            var newEvents = new FiberEventBinding[]
            {
                new ClickedBinding { Handler = handler },
                new ClickedBinding { Handler = handler },
            };

            // Assert
            Assert.That(_manager.HasSameBindings(button, newEvents), Is.False);
        }

        #endregion

        #region Parameterized: typed event binding round-trip and HasSameBindings

        private static IEnumerable<TestCaseData> BindRoundTripCases()
        {
            yield return MakeRoundTripCase<PointerDownEvent>(h => new PointerDownBinding { Handler = h }, "PointerDown");
            yield return MakeRoundTripCase<PointerUpEvent>(h => new PointerUpBinding { Handler = h }, "PointerUp");
            yield return MakeRoundTripCase<PointerMoveEvent>(h => new PointerMoveBinding { Handler = h }, "PointerMove");
            yield return MakeRoundTripCase<PointerEnterEvent>(h => new PointerEnterBinding { Handler = h }, "PointerEnter");
            yield return MakeRoundTripCase<PointerLeaveEvent>(h => new PointerLeaveBinding { Handler = h }, "PointerLeave");
            yield return MakeRoundTripCase<WheelEvent>(h => new WheelBinding { Handler = h }, "Wheel");
            yield return MakeRoundTripCase<KeyDownEvent>(h => new KeyDownBinding { Handler = h }, "KeyDown");
            yield return MakeRoundTripCase<KeyUpEvent>(h => new KeyUpBinding { Handler = h }, "KeyUp");
            yield return MakeRoundTripCase<FocusInEvent>(h => new FocusInBinding { Handler = h }, "FocusIn");
            yield return MakeRoundTripCase<FocusOutEvent>(h => new FocusOutBinding { Handler = h }, "FocusOut");
            yield return MakeRoundTripCase<FocusEvent>(h => new FocusBinding { Handler = h }, "Focus");
            yield return MakeRoundTripCase<BlurEvent>(h => new BlurBinding { Handler = h }, "Blur");
            yield return MakeRoundTripCase<GeometryChangedEvent>(h => new GeometryChangedBinding { Handler = h }, "GeometryChanged");
        }

        private static IEnumerable<TestCaseData> SameDelegateCases()
        {
            // One representative per binding category.
            yield return MakeSameDelegateCase<PointerDownEvent>(h => new PointerDownBinding { Handler = h }, "PointerDown");
            yield return MakeSameDelegateCase<WheelEvent>(h => new WheelBinding { Handler = h }, "Wheel");
            yield return MakeSameDelegateCase<KeyDownEvent>(h => new KeyDownBinding { Handler = h }, "KeyDown");
            yield return MakeSameDelegateCase<FocusInEvent>(h => new FocusInBinding { Handler = h }, "FocusIn");
            yield return MakeSameDelegateCase<GeometryChangedEvent>(h => new GeometryChangedBinding { Handler = h }, "GeometryChanged");
        }

        private static TestCaseData MakeRoundTripCase<TEvent>(Func<EventCallback<TEvent>, FiberEventBinding> factory, string name)
            where TEvent : EventBase<TEvent>, new()
        {
            EventCallback<TEvent> handler = _ => { };
            var binding = factory(handler);
            return new TestCaseData(binding).SetName($"Given_{name}Binding_When_BoundThenUnbound_Then_DoesNotThrow");
        }

        private static TestCaseData MakeSameDelegateCase<TEvent>(Func<EventCallback<TEvent>, FiberEventBinding> factory, string name)
            where TEvent : EventBase<TEvent>, new()
        {
            // Two bindings share the same Handler delegate; HasSameBindings must recognize them as equal.
            EventCallback<TEvent> handler = _ => { };
            var first = factory(handler);
            var second = factory(handler);
            return new TestCaseData(first, second).SetName($"Given_{name}BindingSameDelegate_When_Compared_Then_HasSameBindings");
        }

        [TestCaseSource(nameof(BindRoundTripCases))]
        public void Given_TypedEventBinding_When_BoundThenUnbound_Then_DoesNotThrow(FiberEventBinding binding)
        {
            // Arrange
            var element = new VisualElement();
            _manager.Bind(element, binding);

            // Act + Assert
            Assert.DoesNotThrow(() => _manager.UnbindAll(element));
        }

        [TestCaseSource(nameof(SameDelegateCases))]
        public void Given_TypedEventBindingSameDelegate_When_Compared_Then_HasSameBindings(FiberEventBinding first, FiberEventBinding second)
        {
            // Arrange
            var element = new VisualElement();
            _manager.Bind(element, first);

            // Act
            var newEvents = new FiberEventBinding[] { second };

            // Assert
            Assert.That(_manager.HasSameBindings(element, newEvents), Is.True);
        }

        #endregion
    }
}
