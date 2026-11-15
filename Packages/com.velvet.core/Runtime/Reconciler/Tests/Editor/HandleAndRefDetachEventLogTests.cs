using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins where an imperative handle sets up among its component's layout effects, what a ref callback that returned
    /// no cleanup hears when its element goes, and where a replaced ref detaches in a commit, as one
    /// <see cref="EventLog"/> string per case, each the sequence react-reconciler 19.1 produces for the same tree.
    /// </summary>
    [TestFixtureSource(typeof(EventLogRenderer), nameof(EventLogRenderer.All))]
    internal sealed class HandleAndRefDetachEventLogTests
    {
        private static readonly Ref<object> s_handle = new();

        private readonly EventLogRenderer _renderer;

        public HandleAndRefDetachEventLogTests(EventLogRenderer renderer) => _renderer = renderer;

        [Component]
        private static VNode HandleBetweenLayoutEffects()
        {
            var (tick, _) = EventLog.UseState("Tick", 0);
            var log = EventLog.UseLog();
            EventLog.UseRender("H");
            Hooks.UseLayoutEffect(() =>
            {
                log.Add("lay:A");
                return () => log.Add("lay-x:A");
            }, null);
            Hooks.UseImperativeHandle(s_handle, () =>
            {
                log.Add("handle:H");
                return new object();
            }, null);
            Hooks.UseLayoutEffect(() =>
            {
                log.Add("lay:B");
                return () => log.Add("lay-x:B");
            }, null);
            return V.Div(name: "t" + tick);
        }

        [Component]
        private static VNode HandleOnlyBeforeALayoutEffect()
        {
            var log = EventLog.UseLog();
            Hooks.UseImperativeHandle(s_handle, () =>
            {
                log.Add("handle:H");
                return new object();
            }, new object[0]);
            Hooks.UseLayoutEffect(() =>
            {
                log.Add("lay:A");
                return (Action?)null;
            }, new object[0]);
            return V.Div();
        }

        [Component]
        private static VNode HandleAfterEveryLayoutEffect()
        {
            var log = EventLog.UseLog();
            Hooks.UseLayoutEffect(() =>
            {
                log.Add("lay:A");
                return (Action?)null;
            }, new object[0]);
            Hooks.UseImperativeHandle(s_handle, () =>
            {
                log.Add("handle:H");
                return new object();
            }, new object[0]);
            return V.Div();
        }

        [Component]
        private static VNode ReturnsNoCleanup()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            EventLog.UseRender("P");
            var log = EventLog.UseLog();
            var noCleanup = Hooks.UseCallback<Func<VisualElement, Action>>(element =>
            {
                log.Add(element == null ? "ref-null:N" : "ref:N");
                return null!;
            }, Array.Empty<object>());
            return V.Div(children: new VNode?[] { shown ? V.Div(key: "d", refCallback: noCleanup) : null });
        }

        [Component]
        private static VNode ReturnsNoCleanupThenSwaps()
        {
            var (swapped, _) = EventLog.UseState("Swapped", false);
            EventLog.UseRender("P");
            var log = EventLog.UseLog();
            var refN = Hooks.UseCallback<Func<VisualElement, Action>>(element =>
            {
                log.Add(element == null ? "ref-null:N" : "ref:N");
                return null!;
            }, Array.Empty<object>());
            var refM = Hooks.UseCallback<Func<VisualElement, Action>>(element =>
            {
                log.Add(element == null ? "ref-null:M" : "ref:M");
                return null!;
            }, Array.Empty<object>());
            return V.Div(refCallback: swapped ? refM : refN);
        }

        [Component]
        private static VNode ChildOne(bool dep)
        {
            EventLog.UseRender("C1");
            EventLog.UseInsertionAndLayoutEffects("C1", null);
            return V.Div();
        }

        [Component]
        private static VNode ChildTwo(bool dep)
        {
            EventLog.UseRender("C2");
            EventLog.UseInsertionAndLayoutEffects("C2", null);
            return V.Div();
        }

        [Component]
        private static VNode SwapsAHostRefOverChildren()
        {
            var (swapped, _) = EventLog.UseState("Swapped", false);
            EventLog.UseRender("P");
            var refA = EventLog.UseRefCallback<VisualElement>("A");
            var refB = EventLog.UseRefCallback<VisualElement>("B");
            return V.Div(refCallback: swapped ? refB : refA, children: new VNode[]
            {
                V.Component(ChildOne, swapped, key: "c1"),
                V.Component(ChildTwo, swapped, key: "c2"),
            });
        }

        [Test]
        public void Given_ALayoutEffectThenAHandleThenALayoutEffect_When_ItMounts_Then_TheHandleSetsUpBetweenThem()
        {
            // Arrange
            var log = new EventLog();

            // Act
            using var mounted = _renderer.Mount(log.Provide(V.Component(HandleBetweenLayoutEffects)));
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:H lay:A handle:H lay:B"));
        }

        [Test]
        public void Given_ALayoutEffectThenAHandleThenALayoutEffect_When_ItRerenders_Then_TheHandleSetsUpBetweenThemAfterTheCleanups()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(HandleBetweenLayoutEffects)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<int>("Tick").Invoke(1);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:H lay-x:A lay-x:B lay:A handle:H lay:B"));
        }

        [Test]
        public void Given_AHandleDeclaredBeforeEveryLayoutEffect_When_ItMounts_Then_TheHandleSetsUpFirst()
        {
            // Arrange
            var log = new EventLog();

            // Act
            using var mounted = _renderer.Mount(log.Provide(V.Component(HandleOnlyBeforeALayoutEffect)));
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("handle:H lay:A"));
        }

        [Test]
        public void Given_AHandleDeclaredAfterEveryLayoutEffect_When_ItMounts_Then_TheHandleSetsUpLast()
        {
            // Arrange
            var log = new EventLog();

            // Act
            using var mounted = _renderer.Mount(log.Provide(V.Component(HandleAfterEveryLayoutEffect)));
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("lay:A handle:H"));
        }

        [Test]
        public void Given_ARefCallbackThatReturnedNoCleanup_When_ItsElementLeaves_Then_TheCallbackHearsNull()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(ReturnsNoCleanup)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:P ref-null:N"));
        }

        [Test]
        public void Given_ARefCallbackThatReturnedNoCleanup_When_ItIsReplaced_Then_TheOldCallbackHearsNullBeforeTheNewOneAttaches()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(ReturnsNoCleanupThenSwaps)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Swapped").Invoke(true);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:P ref-null:N ref:M"));
        }

        [Test]
        public void Given_AHostWithChildComponents_When_ItsRefCallbackIsReplaced_Then_TheOldDetachesAfterTheChildrensCleanups()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(SwapsAHostRefOverChildren)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Swapped").Invoke(true);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "render:P render:C1 render:C2 ins-x:C1 ins:C1 lay-x:C1 ins-x:C2 ins:C2 lay-x:C2 ref-x:A lay:C1 lay:C2 ref:B"));
        }
    }
}
