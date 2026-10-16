using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a scheduled render runs the passive effects earlier commits left pending before it renders,
    /// as React flushes them before it renders again.
    /// <list type="bullet">
    /// <item>A component whose mount effect is still pending when an update removes it runs that effect before the
    /// update renders, then its cleanup.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class PassiveFlushBeforeRenderTests
    {
        private static readonly List<string> s_log = new();
        private static StateUpdater<bool> s_setShown;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_log.Clear();
            s_setShown = default;
        }

        [Test]
        public void Given_AMountEffectStillPending_When_AScheduledUpdateRemovesItsComponent_Then_ItRunsAndIsCleanedUp()
        {
            // Arrange — no passive flush between the mount and the update
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));

            // Act
            s_setShown.Invoke(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("host render true,effect,host render false,cleanup"));
        }

        [Component(Compiler = false)]
        private static VNode EffectRender()
        {
            Hooks.UseEffect((Func<Action>)(() =>
            {
                s_log.Add("effect");
                return () => s_log.Add("cleanup");
            }), Array.Empty<object>());
            return V.Label(text: "effect");
        }

        [Component(Compiler = false)]
        private static VNode HostRender()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            s_log.Add("host render " + (shown ? "true" : "false"));
            return V.Div(children: new VNode[] { shown ? V.Component(EffectRender, key: "effect") : null });
        }
    }
}
