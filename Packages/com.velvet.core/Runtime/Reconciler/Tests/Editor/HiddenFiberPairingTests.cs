// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a fiber the registry disposes leaves its lookup indexes, so no later walk finds it by its
    /// key or by its wrapper element: one case per call that disposes and unregisters, each arranged so that the
    /// call's own removal from the index is the only thing taking the fiber out.
    /// </summary>
    [TestFixture]
    internal sealed class HiddenFiberPairingTests
    {
        private static IDictionary Table(ComponentRegistry registry, string name)
            => (IDictionary)typeof(ComponentRegistry).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(registry);

        private static void RegisterWrapper(ComponentRegistry registry, ComponentFiber fiber, VisualElement wrapper, object identity)
        {
            Table(registry, "_wrapperIndex")[wrapper] = fiber;
            (VisualElement, object) info = (wrapper, identity);
            Table(registry, "_wrapperFiberInfo")[fiber] = info;
        }

        // GREEN_ON_BASE(characterization): the base already removes a disposed inline fiber from its key index; no case read that table after a disposal.
        [Test]
        public void Given_AnInlineMountedFiber_When_TheRegistryDisposesIt_Then_ItIsNoLongerInTheKeyIndex()
        {
            // Arrange
            var registry = new ComponentRegistry(new ReconcilerContext());
            var fiber = new ComponentFiber();
            (ComponentFiber, VisualElement, VisualElement, object, object) key =
                (null, null, new VisualElement(), new object(), new object());
            Table(registry, "_inlineFiberToKey")[fiber] = key;

            // Act
            registry.DisposeAndRemove(fiber);

            // Assert
            Assert.That(Table(registry, "_inlineFiberToKey").Contains(fiber), Is.False);
        }

        // GREEN_ON_BASE(characterization): the base already removes a wrapper-mounted fiber from the wrapper index when Remove disposes it; no case read that table after it.
        [Test]
        public void Given_AWrapperMountedFiber_When_TheRegistryRemovesItByWrapper_Then_TheWrapperNoLongerIndexesIt()
        {
            // Arrange
            var registry = new ComponentRegistry(new ReconcilerContext());
            var wrapper = new VisualElement();
            RegisterWrapper(registry, new ComponentFiber(), wrapper, new object());

            // Act
            registry.Remove(wrapper);

            // Assert
            Assert.That(Table(registry, "_wrapperIndex").Contains(wrapper), Is.False);
        }

        // GREEN_ON_BASE(characterization): the base already removes a wrapper-mounted fiber from the wrapper index when a different identity evicts it; no case read that table after it.
        [Test]
        public void Given_AWrapperMountedFiber_When_ADifferentIdentityEvictsIt_Then_TheWrapperNoLongerIndexesIt()
        {
            // Arrange
            var registry = new ComponentRegistry(new ReconcilerContext());
            var wrapper = new VisualElement();
            RegisterWrapper(registry, new ComponentFiber(), wrapper, new object());

            // Act
            registry.RemoveIfDifferentIdentity(wrapper, new object());

            // Assert
            Assert.That(Table(registry, "_wrapperIndex").Contains(wrapper), Is.False);
        }
    }
}
