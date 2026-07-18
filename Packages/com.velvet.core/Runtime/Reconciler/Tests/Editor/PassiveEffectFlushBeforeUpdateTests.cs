using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins React's flushPassiveEffects-before-update: a prior render's pending passive effect runs before a
    /// new discrete-event update's render, not after. A dep-change re-render schedules the effect (the EditMode
    /// scheduler never ticks, so it stays pending); a discrete click's synchronous flush must drain it first.
    /// GWT, one assert.
    /// </summary>
    [TestFixture]
    internal sealed class PassiveEffectFlushBeforeUpdateTests
    {
        private VisualElement _root;
        private MountedTree _mounted;
        private static readonly List<string> s_log = new();
        private static StateUpdater<int> s_setDep;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_log.Clear();
            s_setDep = default;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
        }

        [Component]
        private static VNode Widget()
        {
            var (dep, setDep) = Hooks.UseState(0);
            var (tick, setTick) = Hooks.UseState(0);
            s_setDep = setDep;
            s_log.Add($"render:t{tick}:d{dep}");
            Hooks.UseEffect(() =>
            {
                s_log.Add($"effect:d{dep}");
                return () => { };
            }, new object[] { dep });
            return V.Button(name: "btn", onClick: () => setTick.Invoke(t => t + 1));
        }

        [Test]
        public void Given_APendingPassiveEffect_When_ADiscreteClickReRenders_Then_TheEffectRunsBeforeTheClicksRender()
        {
            // Arrange — mounted (its mount effect already ran), then a dep change re-renders and SCHEDULES a
            // fresh passive effect (effect:d1) that the EditMode scheduler never drains, so it stays pending.
            _mounted = V.Mount(_root, V.Component(Widget, key: "w"));
            s_log.Clear();
            s_setDep.Invoke(1);
            _mounted.FlushStateForTest();
            Assume.That(s_log, Is.EqualTo(new[] { "render:t0:d1" }), "Precondition: re-rendered, effect:d1 pending");
            s_log.Clear();

            // Act — a real discrete click setStates; its FlushImmediate must flush the pending effect first.
            _root.Q<Button>("btn").SimulateClick();

            // Assert — the pending effect commits before the click's re-render (render:t1:d1), as React orders it.
            Assert.That(s_log, Is.EqualTo(new[] { "effect:d1", "render:t1:d1" }));
        }
    }
}
