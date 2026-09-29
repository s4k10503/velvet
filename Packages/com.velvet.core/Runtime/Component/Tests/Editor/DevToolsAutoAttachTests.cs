#if UNITY_EDITOR
using System.Linq;
using System.Threading;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.DevTools;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class DevToolsAutoAttachTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
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
        public void Given_ComponentRoot_When_Mounted_Then_LabelIsTheComponentsName()
        {
            // Arrange
            Assume.That(VelvetDevToolsRegistry.Entries, Is.Empty,
                "Precondition: the registry starts empty so the auto-attach is the only entry");

            // Act
            using var mounted = V.Mount(_root, V.Component(AutoAttachProbe.Render, key: "probe"));

            // Assert
            Assert.That(VelvetDevToolsRegistry.Entries.Single(e => ReferenceEquals(e.Fiber, mounted.Root)).Label,
                Is.EqualTo("AutoAttachProbe.Render"));
        }

        // GREEN_ON_BASE(characterization): the base also labels a root node naming no method by its target.
        // The shared name lookup has to keep returning nothing for such a node for this to keep passing.
        [Test]
        public void Given_ARootNodeNamingNoMethod_When_Mounted_Then_LabelFallsBackToTheTargetName()
        {
            // Arrange — a hand-built node with no body and an identity that is not a method; the registry
            // refuses it and logs, and the label still has to come from somewhere
            _root.name = "host-target";
            var node = new ComponentNode { Body = null, Identity = "no-method" };
            LogAssert.Expect(LogType.Exception, new Regex(@"ArgumentException: ComponentNode\.Body must not be null"));

            // Act
            using var mounted = V.Mount(_root, node);

            // Assert
            Assert.That(VelvetDevToolsRegistry.Entries.Single(e => ReferenceEquals(e.Fiber, mounted.Root)).Label,
                Is.EqualTo("host-target"));
        }

        [Test]
        public void Given_PropsComponentRoot_When_Mounted_Then_LabelIsTheComponentsNameRatherThanItsClosure()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(AutoAttachPropsProbe.Render, "probe", key: "props-probe"));

            // Assert
            Assert.That(VelvetDevToolsRegistry.Entries.Single(e => ReferenceEquals(e.Fiber, mounted.Root)).Label,
                Is.EqualTo("AutoAttachPropsProbe.Render"));
        }
    }

    internal sealed class DevToolsRegistryLifetimeTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            VelvetDevToolsRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            VelvetDevToolsRegistry.Clear();
        }

        [Test]
        public void Given_AnInteriorFiberRegisteredByHand_When_ItsTreeIsDisposed_Then_ItLeavesTheRegistry()
        {
            // Arrange
            var mounted = V.Mount(_root, V.Div("", V.Component(InteriorProbe.Render, key: "interior")));
            var interior = InteriorProbe.Fiber;
            VelvetDevToolsRegistry.Register(interior, "Page");

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(VelvetDevToolsRegistry.Entries.Any(e => ReferenceEquals(e.Fiber, interior)), Is.False);
        }

        [Test]
        public void Given_ADisposedFiber_When_Registered_Then_ItIsNotAdded()
        {
            // Arrange
            var mounted = V.Mount(_root, V.Component(AutoAttachProbe.Render, key: "probe"));
            var root = mounted.Root;
            mounted.Dispose();

            // Act
            VelvetDevToolsRegistry.Register(root, "Late");

            // Assert
            Assert.That(VelvetDevToolsRegistry.Entries.Any(e => ReferenceEquals(e.Fiber, root)), Is.False);
        }

        [Test]
        public void Given_ARegisteredFiber_When_RegisteredAgain_Then_ItKeepsItsRegistrationTime()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(AutoAttachProbe.Render, key: "probe"));
            var registeredAt = VelvetDevToolsRegistry.Entries.Single(e => ReferenceEquals(e.Fiber, mounted.Root))
                .RegisteredAt;
            Thread.Sleep(20);

            // Act
            VelvetDevToolsRegistry.Register(mounted.Root, "Renamed");

            // Assert
            Assert.That(
                VelvetDevToolsRegistry.Entries.Single(e => ReferenceEquals(e.Fiber, mounted.Root)).RegisteredAt,
                Is.EqualTo(registeredAt));
        }

        // GREEN_ON_BASE(characterization): the base's replacement entry carries the new label too.
        [Test]
        public void Given_ARegisteredFiber_When_RegisteredAgain_Then_ItCarriesTheNewLabel()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(AutoAttachProbe.Render, key: "probe"));

            // Act
            VelvetDevToolsRegistry.Register(mounted.Root, "Renamed");

            // Assert
            Assert.That(
                VelvetDevToolsRegistry.Entries.Single(e => ReferenceEquals(e.Fiber, mounted.Root)).Label,
                Is.EqualTo("Renamed"));
        }
    }

    internal static class InteriorProbe
    {
        public static ComponentFiber Fiber;

        [Component]
        public static VNode Render()
        {
            Fiber = FiberAmbientStack.Current;
            return V.Label(text: "interior");
        }
    }

    internal static class AutoAttachProbe
    {
        [Component]
        public static VNode Render() => V.Label(text: "probe");
    }

    internal static class AutoAttachPropsProbe
    {
        [Component]
        public static VNode Render(string text) => V.Label(text: text);
    }
}
#endif
