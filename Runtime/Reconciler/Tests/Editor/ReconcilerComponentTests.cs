using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how reconciliation mounts, updates, and unmounts a functional <c>[Component]</c> node.
    /// <list type="bullet">
    /// <item>A ComponentNode is inline-mounted: no wrapper VisualElement is emitted, and the render's
    /// returned element occupies the component's slot directly under the parent.</item>
    /// <item>A state update re-runs the render and the new value is reflected in the mounted element.</item>
    /// <item>Disposing the mount, or reconciling the component out of the tree, removes its element from
    /// the parent.</item>
    /// <item>A ComponentNode appearing in a new tree mounts its element; one removed from the tree
    /// unmounts it.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. Per-region
    /// static fields are reset in <see cref="SetUp"/> via <c>Reset{Region}()</c> helpers.
    /// </remarks>
    [TestFixture]
    internal sealed class ReconcilerComponentTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetLabel();
        }

        [Test]
        public void Given_ComponentNode_When_Mounted_Then_RenderOutputIsTheDirectChild()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(CounterRender));

            // Assert — inline-mounted: the Label render output is the direct child, with no wrapper VE
            Assert.That(_root.ElementAt(0), Is.InstanceOf<Label>());
        }

        [Test]
        public void Given_ComponentNode_When_Mounted_Then_OccupiesExactlyOneSlot()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(CounterRender));

            // Assert
            Assert.That(_root.childCount, Is.EqualTo(1), "Inline mount emits no wrapper element");
        }

        [Test]
        public void Given_MountedComponent_When_StateUpdated_Then_NewValueReflectedInDom()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(LabelRender, key: "label"));
            Assume.That(GetLabel().text, Is.EqualTo("initial"), "Precondition: the initial render shows the seed value");
            Assume.That(s_labelSetText, Is.Not.Null, "Precondition: the render exposed its setter");

            // Act
            s_labelSetText.Invoke("updated");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(GetLabel().text, Is.EqualTo("updated"));
        }

        [Test]
        public void Given_MountedComponent_When_StateUpdated_Then_ReRendersExactlyOnce()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(LabelRender, key: "label"));
            Assume.That(s_labelRenderCount, Is.EqualTo(1), "Precondition: the first render happened once");

            // Act
            s_labelSetText.Invoke("updated");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_labelRenderCount, Is.EqualTo(2));
        }

        [Test]
        public void Given_MountedComponent_When_MountDisposed_Then_DomCleared()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(LabelRender, key: "label"));
            Assume.That(_root.childCount, Is.EqualTo(1), "Precondition: the component mounted one element");

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(_root.childCount, Is.EqualTo(0));
        }

        [Test]
        public void Given_EmptyTree_When_ComponentReconciledIn_Then_ElementMounts()
        {
            // Arrange
            var reconciler = new Reconciler();
            var newTree = new VNode[] { V.Component(CounterRender) };

            // Act
            reconciler.Reconcile(_root, Array.Empty<VNode>(), newTree);

            // Assert
            Assert.That(_root.childCount, Is.EqualTo(1));
            reconciler.Dispose();
        }

        [Test]
        public void Given_MountedComponent_When_ReconciledOutOfTree_Then_ElementUnmounts()
        {
            // Arrange
            var reconciler = new Reconciler();
            var tree = new VNode[] { V.Component(CounterRender) };
            reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);
            Assume.That(_root.childCount, Is.EqualTo(1), "Precondition: the component is mounted");

            // Act
            reconciler.Reconcile(_root, tree, Array.Empty<VNode>());

            // Assert
            Assert.That(_root.childCount, Is.EqualTo(0));
            reconciler.Dispose();
        }

        private Label GetLabel() => (Label)_root.ElementAt(0);

        #region Counter component (UseState; for mount verification)

        [Component]
        private static VNode CounterRender()
        {
            var (value, _) = Hooks.UseState(0);
            return V.Label(text: value.ToString());
        }

        #endregion

        #region Label component (UseState text; for state-update verification)

        private static int s_labelRenderCount;
        private static Action<string> s_labelSetText;

        private static void ResetLabel()
        {
            s_labelRenderCount = 0;
            s_labelSetText = null;
        }

        [Component]
        private static VNode LabelRender()
        {
            s_labelRenderCount++;
            var (text, setText) = Hooks.UseState("initial");
            s_labelSetText = setText;
            return V.Label(text: text);
        }

        #endregion
    }
}
