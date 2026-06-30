#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.DevTools;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the DevTools auto-attach: a <c>V.Mount</c> registers its root with
    /// <see cref="VelvetDevToolsRegistry"/> so the inspector shows the live tree with no manual
    /// <c>Register</c> call, and disposing the tree removes it again.
    /// </summary>
    [TestFixture]
    internal sealed class DevToolsAutoAttachTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            // The registry is a global static shared across the editor session; start each test from empty
            // so an assertion observes only this test's mount.
            VelvetDevToolsRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            VelvetDevToolsRegistry.Clear();
        }

        [Test]
        public void Given_TreeMounted_When_NoManualRegisterCall_Then_RootAppearsInRegistry()
        {
            // Arrange
            Assume.That(VelvetDevToolsRegistry.Entries, Is.Empty,
                "Precondition: the registry starts empty so the auto-attach is the only entry");

            // Act
            using var mounted = V.Mount(_root, V.Component(AutoAttachProbe.Render, key: "probe"));

            // Assert
            Assert.That(VelvetDevToolsRegistry.Entries.Any(e => ReferenceEquals(e.Fiber, mounted.Root)),
                Is.True);
        }

        [Test]
        public void Given_MountedTree_When_Disposed_Then_RootRemovedFromRegistry()
        {
            // Arrange
            var mounted = V.Mount(_root, V.Component(AutoAttachProbe.Render, key: "probe"));
            var root = mounted.Root;
            Assume.That(VelvetDevToolsRegistry.Entries.Any(e => ReferenceEquals(e.Fiber, root)), Is.True,
                "Precondition: the mount auto-attached the root before disposal");

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(VelvetDevToolsRegistry.Entries.Any(e => ReferenceEquals(e.Fiber, root)), Is.False);
        }

        [Test]
        public void Given_ComponentRoot_When_Mounted_Then_LabelIsComponentFunctionName()
        {
            // Arrange
            Assume.That(VelvetDevToolsRegistry.Entries, Is.Empty,
                "Precondition: the registry starts empty so the auto-attach is the only entry");

            // Act
            using var mounted = V.Mount(_root, V.Component(AutoAttachProbe.Render, key: "probe"));

            // Assert
            Assert.That(VelvetDevToolsRegistry.Entries.Single(e => ReferenceEquals(e.Fiber, mounted.Root)).Label,
                Is.EqualTo(nameof(AutoAttachProbe.Render)));
        }
    }

    internal static class AutoAttachProbe
    {
        [Component]
        public static VNode Render() => V.Label(text: "probe");
    }
}
#endif
