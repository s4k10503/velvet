using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the props-bail memoization contract of a child <c>[Component]</c> under a re-rendering parent.
    /// <list type="bullet">
    /// <item>The <c>Memoize</c> flag is an opt-in gate: a <c>[Component(Memoize = true)]</c> child bails its
    /// re-render when its props are shallow-equal to the committed ones (per-property <c>Object.is</c>).</item>
    /// <item>A plain (non-memo) <c>[Component]</c> child re-renders whenever its parent re-renders, even when
    /// the props are shallow-equal — the bail is opt-in, not the default.</item>
    /// <item>A memoized child re-renders when any prop is no longer shallow-equal to the committed value.</item>
    /// <item>Distinct props instances carrying equal members are shallow-equal, so the bail keys on member
    /// values rather than instance identity.</item>
    /// <item><c>V.Memo(body, props, areEqual)</c> overrides the default comparison with a custom predicate:
    /// the child bails while the predicate returns true and re-renders when it returns false.</item>
    /// <item>A bailed child keeps its committed output (the new props are not adopted) until a re-render
    /// actually occurs.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. The parent
    /// owns a tick counter incremented by <c>s_parentSetTick</c>, and the child props are derived from the
    /// current tick via <c>s_childProps</c>, so a test controls exactly whether the child props change between
    /// renders. Per-fixture static fields are reset together in <see cref="SetUp"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class ComponentMemoPropsBailTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_memoChildRenderCount = 0;
            s_plainChildRenderCount = 0;
            s_parentSetTick = null;
            s_childProps = null;
            s_useAreEqual = false;
            s_compareOnlyId = false;
            s_usePlainChild = false;
        }

        [Test]
        public void Given_MemoizedChild_When_PropsShallowEqualAcrossParentReRender_Then_ChildDoesNotReRender()
        {
            // Arrange — child props are constant regardless of the parent tick
            s_childProps = _ => new ChildProps("a", 1);
            using var mounted = V.Mount(_root, V.Component(ParentRender, key: "parent"));
            Assume.That(s_memoChildRenderCount, Is.EqualTo(1), "Precondition: mount rendered the child once");

            // Act
            s_parentSetTick(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_memoChildRenderCount, Is.EqualTo(1),
                "[Component(Memoize = true)] with shallow-equal props bails the child re-render");
        }

        [Test]
        public void Given_PlainChild_When_PropsShallowEqualAcrossParentReRender_Then_ChildReRenders()
        {
            // Arrange — a plain [Component] child with constant props (no Memoize opt-in)
            s_usePlainChild = true;
            s_childProps = _ => new ChildProps("a", 1);
            using var mounted = V.Mount(_root, V.Component(ParentRender, key: "parent"));
            Assume.That(s_plainChildRenderCount, Is.EqualTo(1), "Precondition: mount rendered the child once");

            // Act
            s_parentSetTick(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_plainChildRenderCount, Is.EqualTo(2),
                "A non-memo child re-renders on every parent re-render despite shallow-equal props");
        }

        [Test]
        public void Given_MemoizedChild_When_APropChanges_Then_ChildReRenders()
        {
            // Arrange — the child's Value tracks the parent tick, so each parent re-render changes a prop
            s_childProps = tick => new ChildProps("a", tick);
            using var mounted = V.Mount(_root, V.Component(ParentRender, key: "parent"));
            Assume.That(s_memoChildRenderCount, Is.EqualTo(1), "Precondition: mount rendered the child once");

            // Act
            s_parentSetTick(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_memoChildRenderCount, Is.EqualTo(2),
                "A changed prop (Value 0 -> 5) is not shallow-equal, so the memoized child re-renders");
        }

        [Test]
        public void Given_MemoizedChild_When_FreshPropsInstanceHasEqualMembers_Then_ChildDoesNotReRender()
        {
            // Arrange — a fresh props instance every render with identical member values
            s_childProps = _ => new ChildProps("a", 1);
            using var mounted = V.Mount(_root, V.Component(ParentRender, key: "parent"));
            Assume.That(s_memoChildRenderCount, Is.EqualTo(1), "Precondition: mount rendered the child once");

            // Act
            s_parentSetTick(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_memoChildRenderCount, Is.EqualTo(1),
                "Distinct props instances with equal members are shallow-equal, so the bail keys on member values");
        }

        [Test]
        public void Given_AreEqualComparesIdOnly_When_OnlyValueChanges_Then_ChildDoesNotReRender()
        {
            // Arrange — areEqual compares only Id; Value changes every render but Id stays constant
            s_useAreEqual = true;
            s_compareOnlyId = true;
            s_childProps = tick => new ChildProps("a", tick);
            using var mounted = V.Mount(_root, V.Component(ParentRender, key: "parent"));
            Assume.That(s_memoChildRenderCount, Is.EqualTo(1), "Precondition: mount rendered the child once");

            // Act
            s_parentSetTick(9);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_memoChildRenderCount, Is.EqualTo(1),
                "areEqual(Id only) returns true while Id is unchanged, so the child bails despite a Value change");
        }

        [Test]
        public void Given_AreEqualComparesIdOnly_When_OnlyValueChanges_Then_BailedChildKeepsCommittedOutput()
        {
            // Arrange — same custom comparator scenario; assert on the committed UI rather than the render count
            s_useAreEqual = true;
            s_compareOnlyId = true;
            s_childProps = tick => new ChildProps("a", tick);
            using var mounted = V.Mount(_root, V.Component(ParentRender, key: "parent"));
            Assume.That(_root.Q<Label>(name: "child")?.text, Is.EqualTo("a:0"),
                "Precondition: the mount committed the initial child output");

            // Act
            s_parentSetTick(9);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>(name: "child")?.text, Is.EqualTo("a:0"),
                "A bailed child keeps its committed output because the new props were not adopted");
        }

        [Test]
        public void Given_AreEqualComparesIdOnly_When_IdChanges_Then_ChildReRenders()
        {
            // Arrange — areEqual compares only Id; Id changes on the next render
            s_useAreEqual = true;
            s_compareOnlyId = true;
            s_childProps = tick => new ChildProps(tick == 0 ? "a" : "b", 1);
            using var mounted = V.Mount(_root, V.Component(ParentRender, key: "parent"));
            Assume.That(s_memoChildRenderCount, Is.EqualTo(1), "Precondition: mount rendered the child once");

            // Act
            s_parentSetTick(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_memoChildRenderCount, Is.EqualTo(2),
                "areEqual(Id only) returns false when Id changes (a -> b), so the child re-renders");
        }

        #region Parent / child components

        private static int s_memoChildRenderCount;
        private static int s_plainChildRenderCount;
        private static Action<int> s_parentSetTick;
        private static Func<int, ChildProps> s_childProps;
        private static bool s_useAreEqual;
        private static bool s_compareOnlyId;
        private static bool s_usePlainChild;

        private sealed record ChildProps(string Id, int Value);

        // Opt-in memoized child: bails its re-render on shallow-equal props.
        [Component(Memoize = true)]
        private static VNode MemoChildRender(ChildProps p)
        {
            s_memoChildRenderCount++;
            return V.Label(name: "child", text: $"{p.Id}:{p.Value}");
        }

        // Plain child: no Memoize opt-in, so it re-renders whenever the parent re-renders.
        [Component]
        private static VNode PlainChildRender(ChildProps p)
        {
            s_plainChildRenderCount++;
            return V.Label(name: "child", text: $"{p.Id}:{p.Value}");
        }

        // Parent owns a tick counter that increments on every setter call (forcing a parent re-render).
        [Component]
        private static VNode ParentRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_parentSetTick = setTick;
            var props = s_childProps(tick);
            if (s_useAreEqual)
            {
                Func<ChildProps, ChildProps, bool> areEqual = s_compareOnlyId
                    ? (a, b) => a.Id == b.Id
                    : (a, b) => a == b;
                return V.Memo(MemoChildRender, props, areEqual, key: "child");
            }
            return s_usePlainChild
                ? V.Component(PlainChildRender, props, key: "child")
                : V.Component(MemoChildRender, props, key: "child");
        }

        #endregion
    }
}
