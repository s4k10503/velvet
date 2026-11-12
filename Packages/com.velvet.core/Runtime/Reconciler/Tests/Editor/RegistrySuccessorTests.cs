using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a fiber mounted in place of one whose boundary mounts its output afresh, disposed while it still waits
    /// for that one's key, leaves no claim on the key: the key goes once the fiber it waited on leaves.
    /// </summary>
    [TestFixture]
    internal sealed class RegistrySuccessorTests
    {
        private static ComponentFiber s_parent;
        private static ComponentFiber s_child;

        [SetUp]
        public void SetUp()
        {
            s_parent = null;
            s_child = null;
        }

        [Component(Compiler = false)]
        private static VNode Child()
        {
            s_child = FiberAmbientStack.Current;
            return V.Label(text: "child");
        }

        [Component(Compiler = false)]
        private static VNode Parent()
        {
            s_parent = FiberAmbientStack.Current;
            return V.Component(Child, key: "child");
        }

        [Test]
        public void Given_ASuccessorDisposedWhileItWaitsForAKey_When_TheFiberHoldingTheKeyLeaves_Then_NoFiberHoldsTheKey()
        {
            // Arrange — the successor is mounted as a walk mounting the parent's output afresh mounts it
            using var mounted = V.Mount(new VisualElement(), V.Component(Parent, key: "parent"));
            var context = mounted.Root.Reconciler.Context;
            var registry = context.ComponentRegistry;
            var shown = s_child;
            registry.TryGetInlineKey(shown, out var positionKey, out var identity, out var portalScope, out var container);
            context.FibersBeingReplaced = new HashSet<ComponentFiber> { shown };
            var (successor, _) = registry.GetOrCreateInline(
                V.Component(Child, key: "child"), s_parent, positionKey, container, 0, portalScope);
            context.FibersBeingReplaced = null;
            registry.DisposeAndRemove(successor);

            // Act
            registry.DisposeAndRemove(shown);

            // Assert — a key handed on to the disposed successor reads as that fiber
            Assert.That(registry.TryGetFiberForInlineKey(s_parent, positionKey, identity, portalScope, container), Is.Null);
        }
    }
}
