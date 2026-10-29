using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies when a fiber's Reconciler builds the parts a reconcile runs through: a Reconciler that does not own
    /// its context builds them when one of its members first needs them, so a component mounted inline, whose
    /// render its parent's pass reconciles, builds none.
    /// </summary>
    [TestFixture]
    internal sealed class ReconcilerLazyPartsTests
    {
        private static StateUpdater<int> s_setCount;

        [SetUp]
        public void SetUp() => s_setCount = default;

        [Component]
        private static VNode ParentRender() => V.Div(children: new VNode[] { V.Component(ChildRender) });

        [Component]
        private static VNode ChildRender()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_setCount = setCount;
            return V.Label(name: "count", text: count.ToString());
        }

        private static object? ChildReconcilerOf(ComponentFiber fiber)
            => typeof(Reconciler)
                .GetField("_childReconciler", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(fiber.Reconciler);

        [Test]
        public void Given_AnInlineMountedChildComponent_When_ItsParentMountsIt_Then_ItsReconcilerHasBuiltNoChildReconciler()
        {
            // Arrange
            var root = new VisualElement();

            // Act
            using var mounted = V.Mount(root, V.Component(ParentRender));
            mounted.FlushStateForTest();

            // Assert
            Assert.IsNull(ChildReconcilerOf(mounted.Root.Child!));
        }

        // GREEN_ON_BASE(characterization): the base builds the parts at construction, so a fiber that reconciles its
        // own rows finds them there; this holds the lazy build to reaching them when it first does.
        [Test]
        public void Given_AnInlineMountedChildComponent_When_ItUpdatesItsOwnState_Then_ItsRowsShowTheNewState()
        {
            // Arrange
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(ParentRender));
            mounted.FlushStateForTest();

            // Act
            s_setCount.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual("1", root.Q<Label>("count").text);
        }
    }
}
