using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// What a <see cref="ClickedEventBinding"/> leaves on its <see cref="Button"/> once unbound: neither its
    /// handler nor the callback that opens each click's <see cref="ClickedEvent"/>, and no slot a later binding
    /// would share, including on a button the pool hands to another element.
    /// </summary>
    [TestFixture]
    internal sealed class ClickedEventUnbindTests
    {
        private static readonly List<string> s_log = new();

        private FiberEventBindingManager _manager;

        [SetUp]
        public void SetUp()
        {
            _manager = new FiberEventBindingManager();
            s_log.Clear();
            s_store = null;
        }

        [TearDown]
        public void TearDown() => _manager.Clear();

        private static int ClickCallbacks(Button button)
        {
            var clicked = typeof(Clickable).GetField(nameof(Clickable.clicked), BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (clicked.GetValue(button.clickable) as Delegate)?.GetInvocationList().Length ?? 0;
        }

        private static ClickedEventBinding Logs(string entry) => new() { Handler = _ => s_log.Add(entry) };

        [Test]
        public void Given_AClickedEventBindingUnboundAndAnotherBound_When_Clicked_Then_OnlyTheLaterOneRuns()
        {
            // Arrange
            var button = new Button();
            _manager.Bind(button, Logs("first"));
            _manager.UnbindAll(button);
            _manager.Bind(button, Logs("second"));

            // Act
            button.SimulateClick();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("second"));
        }

        [Test]
        public void Given_TheOnlyClickedEventBinding_When_Unbound_Then_NoneOfItsClickCallbacksStays()
        {
            // Arrange
            var button = new Button();
            var before = ClickCallbacks(button);
            _manager.Bind(button, Logs("first"));
            var whileBound = ClickCallbacks(button);

            // Act
            _manager.UnbindAll(button);

            // Assert — the bound reading is folded in, since a binding that added nothing would satisfy the
            // unbound one on its own. The binding adds its handler and the callback opening each click.
            Assert.That((whileBound - before, ClickCallbacks(button) - before), Is.EqualTo((2, 0)));
        }

        [Test]
        public void Given_AClickAPreventingBindingPreventedThenUnbound_When_ANewBindingSeesTheNextClick_Then_ItArrivesUnprevented()
        {
            // Arrange — the first click leaves its event prevented, which a slot kept past the unbind would
            // hand to the next binding.
            var button = new Button();
            _manager.Bind(button, new ClickedEventBinding { Handler = click => click.PreventDefault() });
            button.SimulateClick();
            _manager.UnbindAll(button);
            _manager.Bind(button, new ClickedEventBinding { Handler = click => s_log.Add(click.DefaultPrevented.ToString()) });

            // Act
            button.SimulateClick();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("False"));
        }

        private static PhaseStore s_store;

        private readonly record struct PhaseState(int Phase);

        private sealed class PhaseStore : Store<PhaseState>
        {
            public PhaseStore() : base(new PhaseState(0)) { }
            public void Set(int phase) => SetState(_ => new PhaseState(phase));
            protected override void ResetCore() => SetState(_ => new PhaseState(0));
        }

        // Phase 0 mounts a button logging "first"; phase 1 unmounts it, returning it to the pool; phase 2
        // mounts a button logging "second", which the pool hands the same instance.
        [Component]
        private static VNode Screen()
        {
            var phase = Hooks.UseStore(s_store, s => s.Phase);
            return V.Div(children: phase switch
            {
                0 => new VNode[] { V.Button(name: "first", events: new FiberEventBinding[] { Logs("first") }) },
                1 => Array.Empty<VNode>(),
                _ => new VNode[] { V.Button(name: "second", events: new FiberEventBinding[] { Logs("second") }) },
            });
        }

        [Test]
        public void Given_AButtonWithAClickedEventBindingReturnedToThePool_When_AnotherButtonRentsIt_Then_TheOldHandlerDoesNotRun()
        {
            // Arrange
            var root = new VisualElement();
            using var store = new PhaseStore();
            s_store = store;
            using var mounted = V.Mount(root, V.Component(Screen, key: "screen"));
            var scheduler = mounted.GetSchedulerForTest();
            var first = root.Q<Button>("first");
            store.Set(1);
            scheduler.DrainImmediateForTest();
            store.Set(2);
            scheduler.DrainImmediateForTest();
            var second = root.Q<Button>("second");

            // Act
            second.SimulateClick();

            // Assert — the identity is folded in, since a button the pool did not hand back would carry no old
            // handler to leave behind.
            Assert.That((ReferenceEquals(first, second), string.Join(",", s_log)), Is.EqualTo((true, "second")));
        }
    }
}
