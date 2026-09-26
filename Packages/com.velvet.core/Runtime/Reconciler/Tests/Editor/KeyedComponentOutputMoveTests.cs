// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the elements a component renders move with it when its siblings move it, as in React,
    /// so what it rendered inside them keeps its state.
    /// <list type="bullet">
    /// <item>A keyed component that a keyed sibling is inserted ahead of keeps the element and the
    /// <c>V.Portal</c> it rendered, and the component inside either keeps its instance and its rows —
    /// whether the list is written into an element, under an unkeyed <c>V.Fragment</c>, or under a keyed one
    /// the host returns as its whole output.</item>
    /// <item>Two sibling components that each render an element under one key keep both.</item>
    /// <item>An element the host itself writes after those components is still matched by its position
    /// in the host's output, which the inserted sibling's element shifts.</item>
    /// </list>
    /// <see cref="ComponentSwapElementOwnershipTests"/> owns the other direction: a slot whose component
    /// changes does not hand the departing component's elements to the arriving one.
    /// </summary>
    [TestFixture]
    internal sealed class KeyedComponentOutputMoveTests
    {
        public enum Wrapper { Fragment, KeyedFragment, Element }

        public enum Output { Element, Portal }

        private static StateUpdater<int> s_setRows;
        private static StateUpdater<bool> s_setInserted;
        private static StateUpdater<int> s_setTick;
        private static int s_rowSetups;
        private static Wrapper s_wrapper;
        private static Output s_output;
        private static VisualElement s_portalTarget = null!;
        private MountedTree? _mounted;
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            s_rowSetups = 0;
            s_portalTarget = new VisualElement { name = "target" };
            _root = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
        }

        private void Flush()
        {
            _mounted!.FlushStateForTest();
            _mounted.FlushEffectsForTest();
        }

        #region Render targets

        [Component]
        private static VNode Rows()
        {
            var (rows, setRows) = Hooks.UseState(0);
            s_setRows = setRows;
            Hooks.UseLayoutEffect(() => { s_rowSetups++; return (Action)(() => { }); }, Array.Empty<object>());
            var labels = new VNode[rows];
            for (var i = 0; i < rows; i++) labels[i] = V.Label(name: "f" + i, key: "f" + i);
            return V.Fragment(labels);
        }

        [Component]
        private static VNode Holder()
        {
            var inner = new VNode[] { V.Component(Rows, key: "f") };
            return s_output == Output.Element
                ? V.Div(name: "holder", children: inner)
                : V.Portal(s_portalTarget, children: inner);
        }

        [Component]
        private static VNode Inserted() => V.Label(name: "inserted", key: "b");

        [Component(Compiler = false)]
        private static VNode InsertHost()
        {
            var (inserted, setInserted) = Hooks.UseState(false);
            s_setInserted = setInserted;
            var kids = inserted
                ? new VNode[] { V.Component(Inserted, key: "inserted"), V.Component(Holder, key: "holder") }
                : new VNode[] { V.Component(Holder, key: "holder") };
            return s_wrapper switch
            {
                Wrapper.Fragment => V.Fragment(kids),
                Wrapper.KeyedFragment => V.Fragment(kids, key: "rows"),
                _ => V.Div(name: "host", children: kids),
            };
        }

        [Component]
        private static VNode Titled() => V.Label(name: "title", key: "title");

        [Component(Compiler = false)]
        private static VNode TwoTitlesHost()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(name: "host", children: new VNode[]
            {
                V.Component(Titled, key: "a"),
                V.Component(Titled, key: "b"),
                V.Label(name: "tick", text: tick.ToString()),
            });
        }

        [Component]
        private static VNode Plain() => V.Div(name: "plain");

        [Component(Compiler = false)]
        private static VNode TrailingHost()
        {
            var (inserted, setInserted) = Hooks.UseState(false);
            s_setInserted = setInserted;
            return V.Div(name: "host", children: inserted
                ? new VNode[] { V.Component(Inserted, key: "inserted"), V.Component(Plain, key: "plain"), V.Label(name: "own") }
                : new VNode[] { V.Component(Plain, key: "plain"), V.Label(name: "own") });
        }

        #endregion

        [Test]
        public void Given_AKeyedComponentHoldingAStatefulChild_When_AKeyedSiblingIsInsertedAhead_Then_TheChildKeepsItsInstanceAndRows(
            [Values] Wrapper wrapper, [Values] Output output)
        {
            // Arrange — the rows are state the child set after mounting, so a remounted child renders none.
            s_wrapper = wrapper;
            s_output = output;
            _mounted = V.Mount(_root, V.Component(InsertHost, key: "host"));
            _mounted.FlushEffectsForTest();
            s_setRows.Invoke(1);
            Flush();

            // Act
            s_setInserted.Invoke(true);
            Flush();

            // Assert — the setup count separates a kept instance from a remount, and the rows read where the
            // child renders them separate a kept row from one a discarded Portal left on its target.
            var container = output == Output.Element ? _root.Q<VisualElement>("holder") : s_portalTarget;
            Assert.That(
                (s_rowSetups, string.Join(",", container.Children().Select(c => c.name))),
                Is.EqualTo((1, "f0")));
        }

        [Test]
        public void Given_TwoSiblingComponentsEachRenderingAnElementUnderOneKey_When_TheHostRerenders_Then_BothElementsAreKept()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(TwoTitlesHost, key: "host"));
            var host = _root.Q<VisualElement>("host");
            var before = new[] { host.ElementAt(0), host.ElementAt(1) };

            // Act
            s_setTick.Invoke(1);
            Flush();

            // Assert — the tick text rides along, since a host that never re-rendered keeps both on its own.
            Assert.That(
                (ReferenceEquals(host.ElementAt(0), before[0]), ReferenceEquals(host.ElementAt(1), before[1]),
                    _root.Q<Label>("tick").text),
                Is.EqualTo((true, true, "1")));
        }

        [Test]
        public void Given_AnUnkeyedElementTheHostWritesAfterAKeyedComponent_When_AKeyedSiblingIsInsertedAhead_Then_ItIsRebuiltAtItsNewIndex()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(TrailingHost, key: "host"));
            var host = _root.Q<VisualElement>("host");
            var own = host.Q<Label>("own");
            var plain = host.Q<VisualElement>("plain");

            // Act
            s_setInserted.Invoke(true);
            Flush();

            // Assert — React matches an unkeyed child by its index in the parent's children, and the index of
            // this one moved from 1 to 2: the count of the host's own output resumes from the host's start
            // once the child component's output ends, not from where the child's began. The component's
            // element rides along as the control that the pass reconciled rather than rebuilt the host's
            // children.
            Assert.That(
                (ReferenceEquals(host.Q<Label>("own"), own), ReferenceEquals(host.Q<VisualElement>("plain"), plain),
                    string.Join(",", host.Children().Select(c => c.name))),
                Is.EqualTo((false, true, "inserted,plain,own")));
        }
    }
}
