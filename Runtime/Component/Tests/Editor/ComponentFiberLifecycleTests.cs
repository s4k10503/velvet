using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the fiber lifecycle contract for function components mounted via <c>V.Mount</c>.
    /// <list type="bullet">
    /// <item>Mounting builds a root fiber that sits at the top of the tree and has no parent.</item>
    /// <item>A function component referenced through <c>V.Component</c> is chained as the root fiber's child, with the
    /// child's parent pointing back at the root and a render body delegate attached to it.</item>
    /// <item>A nested <c>V.Component</c> inside a render body links as a grandchild, forming a parent/child/grandchild
    /// chain whose back-references are consistent at every level.</item>
    /// <item>Disposing the mounted tree unmounts the root, clearing the rendered DOM and detaching the child fiber.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Each <c>V.Mount</c> creates a fresh root fiber, so there is no fiber-reuse-on-remount concept to verify. Uses the
    /// <c>[Component] static VNode</c> render-target pattern.
    /// </remarks>
    [TestFixture]
    internal sealed class ComponentFiberLifecycleTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp() => _root = new VisualElement();

        [Test]
        public void Given_FreshReconciler_When_ComponentMounted_Then_RootFiberIsCreated()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));

            // Assert
            Assert.That(mounted.Root, Is.Not.Null, "V.Mount creates a root fiber");
        }

        [Test]
        public void Given_FreshReconciler_When_ComponentMounted_Then_RootFiberHasNoParent()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));

            // Assert
            Assert.That(mounted.Root.Parent, Is.Null, "The root fiber sits at the top of the tree and has no parent");
        }

        [Test]
        public void Given_MountedComponent_When_Inspected_Then_ChildFiberPointsBackToRoot()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            var rootFiber = mounted.Root;
            Assume.That(rootFiber.Child, Is.Not.Null, "Precondition: the user component is chained as the root's child");

            // Assert
            Assert.That(rootFiber.Child.Parent, Is.SameAs(rootFiber), "The child fiber's parent points back to the root");
        }

        [Test]
        public void Given_MountedComponent_When_Inspected_Then_ChildFiberHasRenderBody()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            var rootFiber = mounted.Root;
            Assume.That(rootFiber.Child, Is.Not.Null, "Precondition: the user component is chained as the root's child");

            // Assert
            Assert.That(rootFiber.Child.Body, Is.Not.Null, "The child fiber carries a render body delegate");
        }

        [Test]
        public void Given_NestedComponents_When_Mounted_Then_GrandchildLinksBackToParent()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ParentWithChildRender, key: "parent"));
            var parentFiber = mounted.Root.Child;
            Assume.That(parentFiber, Is.Not.Null, "Precondition: the parent component is chained under the root");
            Assume.That(parentFiber.Child, Is.Not.Null, "Precondition: the nested component is chained under the parent");

            // Assert
            Assert.That(parentFiber.Child.Parent, Is.SameAs(parentFiber),
                "A three-level fiber chain links each grandchild back to its parent");
        }

        [Test]
        public void Given_MountedComponent_When_Disposed_Then_RenderedDomIsCleared()
        {
            // Arrange
            var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            Assume.That(_root.childCount, Is.GreaterThan(0), "Precondition: the component is present in the DOM after mount");

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(_root.childCount, Is.EqualTo(0), "Disposing the mounted tree unmounts the root and clears the DOM");
        }

        [Test]
        public void Given_MountedComponent_When_Disposed_Then_ChildFiberIsDetached()
        {
            // Arrange
            var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            var rootFiber = mounted.Root;
            Assume.That(rootFiber.Child, Is.Not.Null, "Precondition: the child fiber is linked after mount");

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(rootFiber.Child, Is.Null, "Disposing the mounted tree detaches the child fiber via the unmount path");
        }

        #region Render targets (function components)

        [Component]
        private static VNode SimpleRender() => V.Label(text: "x");

        [Component]
        private static VNode ChildRender() => V.Label(text: "child");

        [Component]
        private static VNode ParentWithChildRender()
            => V.Component(ChildRender, key: "child");

        #endregion
    }
}
