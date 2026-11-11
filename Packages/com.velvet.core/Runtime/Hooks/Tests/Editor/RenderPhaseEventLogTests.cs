using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins render-phase semantics — which components render for an update and what each render sees — as one
    /// <see cref="EventLog"/> string per case, each one what React 19 logs for the same tree.
    /// </summary>
    [TestFixture]
    internal sealed class RenderPhaseEventLogTests
    {
        private static readonly ComponentContext<int> Value = ComponentContext<int>.Create(0);

        [Component]
        private static VNode StatefulParent()
        {
            var (count, _) = EventLog.UseState("P", 0);
            EventLog.UseRender("P");
            return V.Div(children: new VNode[] { V.Component(StatefulChild, count) });
        }

        [Component]
        private static VNode StatefulChild(int parentCount)
        {
            EventLog.UseState("C", 0);
            EventLog.UseRender("C");
            return V.Div();
        }

        [Component]
        private static VNode Slots()
        {
            var log = EventLog.UseLog();
            var (a, _) = EventLog.UseState("a", 0);
            var held = Hooks.UseRef<string>(() => "r0");
            var memo = Hooks.UseMemo(() =>
            {
                log.Add("memo");
                return "m0";
            }, Array.Empty<object>());
            var (b, _) = EventLog.UseState("b", 0);
            Hooks.UseLayoutEffect(() =>
            {
                held.Set("r1");
                return (Action)null;
            }, Array.Empty<object>());
            log.Add($"render:a={a},r={held.Current},m={memo},b={b}");
            return V.Div();
        }

        [Component]
        private static VNode Snapshot()
        {
            var log = EventLog.UseLog();
            var (value, _) = EventLog.UseState("v", 0);
            Hooks.UseLayoutEffect(() =>
            {
                log.Add("lay:" + value);
                return () => log.Add("lay-x:" + value);
            }, new object[] { value });
            Hooks.UseEffect(() =>
            {
                log.Add("eff:" + value);
                return () => log.Add("eff-x:" + value);
            }, new object[] { value });
            return V.Div();
        }

        [Component]
        private static VNode ProviderRoot()
        {
            var (value, _) = EventLog.UseState("Root", 0);
            EventLog.UseRender("Root");
            return V.Provider(Value, value, new VNode[] { V.Component(MemoizedMiddle) });
        }

        [Component(Memoize = true)]
        private static VNode MemoizedMiddle()
        {
            EventLog.UseRender("Middle");
            return V.Div(children: new VNode[] { V.Component(Consumer) });
        }

        [Component]
        private static VNode Consumer()
        {
            var value = Hooks.UseContext(Value);
            EventLog.UseRender("Consumer=" + value);
            return V.Div();
        }

        // GREEN_ON_BASE(characterization): the drain renders a parent ahead of a child queued before it.
        [Test]
        public void Given_AChildDirtiedBeforeItsParentInOneBatch_When_Drained_Then_TheParentRendersBeforeTheChildAndTheChildRendersOnce()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(StatefulParent)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<int>("C").Invoke(1);
            log.Setter<int>("P").Invoke(1);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:P render:C"));
        }

        // GREEN_ON_BASE(characterization): hook slots keyed by call order, which a render/commit split must keep.
        [Test]
        public void Given_HooksOfDifferentKindsInOneComponent_When_TheFirstHooksValueChanges_Then_TheLaterSlotsKeepTheirValues()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(Slots)));
            mounted.DrainForTest();
            log.Setter<int>("b").Invoke(7);
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<int>("a").Invoke(1);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:a=1,r=r1,m=m0,b=7"));
        }

        // GREEN_ON_BASE(characterization): each effect cleanup reads its own render's value, which a split must keep.
        [Test]
        public void Given_EffectsClosingOverAStateValue_When_TheNextRenderCommits_Then_EachCleanupReadsTheValueOfTheRenderThatSetItUp()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(Snapshot)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<int>("v").Invoke(1);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("lay-x:0 lay:1 eff-x:0 eff:1"));
        }

        // GREEN_ON_BASE(characterization): an equal set renders none of the children, which a split must keep.
        [Test]
        public void Given_AStateJustChangedToAValue_When_ThatValueIsSetAgain_Then_TheComponentRendersAtMostOnceMoreAndItsChildDoesNot()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(StatefulParent)));
            mounted.DrainForTest();
            log.Setter<int>("P").Invoke(1);
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<int>("P").Invoke(1);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("").Or.EqualTo("render:P"));
        }

        // GREEN_ON_BASE(characterization): context reaching a consumer past a memoized parent, which a split must keep.
        [Test]
        public void Given_AProviderAboveAMemoizedChildWhoseChildConsumes_When_TheValueChanges_Then_OnlyTheProviderOwnerAndTheConsumerRender()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(ProviderRoot)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<int>("Root").Invoke(1);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:Root render:Consumer=1"));
        }
    }
}
