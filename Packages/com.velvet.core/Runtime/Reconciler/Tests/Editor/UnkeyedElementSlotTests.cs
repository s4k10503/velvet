using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which slot an unkeyed element is matched by: the index it is written at among the children of
    /// the array, Fragment or component output that holds it.
    /// <list type="bullet">
    /// <item>A sibling written before it turning to <c>null</c> leaves it on its own element, on the walk that
    /// commits a new side holding the null and on the flat diff an old side holding one takes, sliced or
    /// not.</item>
    /// <item>An unkeyed Fragment or a component written before it rendering more elements leaves it on its
    /// own element.</item>
    /// <item>An unkeyed Fragment coming to enclose it remounts it, the Fragment taking its slot.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class UnkeyedElementSlotTests
    {
        private static StateUpdater<int> s_setCount;

        private static string Names(VisualElement container)
            => string.Join(",", container.Children().Select(child => child.name));

        [Test]
        public void Given_AnUnkeyedElementAfterASibling_When_ThatSiblingTurnsNull_Then_ItKeepsItsElement()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var oldTree = new VNode?[] { V.Div(name: "a"), V.Div(name: "b") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), oldTree);
            var b = scope.Root.Q<VisualElement>("b");

            // Act
            scope.Reconciler.Reconcile(scope.Root, oldTree, new VNode?[] { null, V.Div(name: "b") });

            // Assert — the names show the departing element went rather than the survivor's.
            Assert.That(
                (Names(scope.Root), ReferenceEquals(scope.Root.Q<VisualElement>("b"), b)),
                Is.EqualTo(("b", true)));
        }

        [TestCase(0d)]
        [TestCase(double.Epsilon)]
        public void Given_AnUnkeyedElementAfterANullSlot_When_AnElementFillsThatSlot_Then_ItKeepsItsElement(
            double frameBudgetMs)
        {
            // Arrange — the new side holds no null, so this is the flat diff rather than the walk.
            using var scope = new ReconcilerScope();
            var oldTree = new VNode?[] { null, V.Div(name: "b") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), oldTree);
            var b = scope.Root.Q<VisualElement>("b");

            // Act
            scope.Reconciler.Reconcile(
                scope.Root, oldTree, new VNode?[] { V.Div(name: "a"), V.Div(name: "b") }, frameBudgetMs);
            while (scope.Reconciler.HasPendingWork) scope.Reconciler.ContinueReconcile();

            // Assert
            Assert.That(
                (Names(scope.Root), ReferenceEquals(scope.Root.Q<VisualElement>("b"), b)),
                Is.EqualTo(("a,b", true)));
        }

        [Test]
        public void Given_AnUnkeyedElementAfterAnUnkeyedFragment_When_TheFragmentRendersMoreElements_Then_ItKeepsItsElement()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var oldTree = new VNode?[]
            {
                V.Fragment(new VNode?[] { V.Div(name: "f0") }),
                V.Div(name: "after"),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), oldTree);
            var after = scope.Root.Q<VisualElement>("after");

            // Act
            scope.Reconciler.Reconcile(scope.Root, oldTree, new VNode?[]
            {
                V.Fragment(new VNode?[] { V.Div(name: "f0"), V.Div(name: "f1") }),
                V.Div(name: "after"),
            });

            // Assert
            Assert.That(
                (Names(scope.Root), ReferenceEquals(scope.Root.Q<VisualElement>("after"), after)),
                Is.EqualTo(("f0,f1,after", true)));
        }

        [Test]
        public void Given_AnUnkeyedElement_When_AnUnkeyedFragmentComesToEncloseIt_Then_ItIsRemounted()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var oldTree = new VNode?[] { V.Div(name: "a") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), oldTree);
            var a = scope.Root.Q<VisualElement>("a");

            // Act
            scope.Reconciler.Reconcile(scope.Root, oldTree, new VNode?[]
            {
                V.Fragment(new VNode?[] { V.Div(name: "a") }),
            });

            // Assert — the names show the remount replaced the element rather than adding one beside it.
            Assert.That(
                (Names(scope.Root), ReferenceEquals(scope.Root.Q<VisualElement>("a"), a)),
                Is.EqualTo(("a", false)));
        }

        [Component]
        private static VNode Grows(int count)
            => V.Fragment(Enumerable.Range(0, count).Select(i => (VNode?)V.Div(name: "g" + i)).ToArray());

        [Component]
        private static VNode GrowingSiblingHost()
        {
            var (count, setCount) = Hooks.UseState(1);
            s_setCount = setCount;
            return V.Div(name: "host", children: new VNode?[]
            {
                V.Component<int>(Grows, count),
                V.Div(name: "after"),
            });
        }

        [Test]
        public void Given_AnUnkeyedElementAfterAComponent_When_TheComponentRendersMoreElements_Then_ItKeepsItsElement()
        {
            // Arrange
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(GrowingSiblingHost, key: "host"));
            var host = root.Q<VisualElement>("host");
            var after = host.Q<VisualElement>("after");

            // Act
            s_setCount.Invoke(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (Names(host), ReferenceEquals(host.Q<VisualElement>("after"), after)),
                Is.EqualTo(("g0,g1,after", true)));
        }
    }
}
