using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.DevTools;
using Velvet.Editor.DevTools;

namespace Velvet.Tests
{
    internal sealed class DevToolsSelectionTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private VelvetDevToolsWindow _window;
        private ComponentFiber _first;
        private ComponentFiber _second;
        private ComponentFiber _third;

        [SetUp]
        public void SetUp()
        {
            VelvetDevToolsRegistry.Clear();
            _first = new ComponentFiber();
            _second = new ComponentFiber();
            _third = new ComponentFiber();
            VelvetDevToolsRegistry.Register(_first, "First");
            VelvetDevToolsRegistry.Register(_second, "Second");
            VelvetDevToolsRegistry.Register(_third, "Third");
            _window = ScriptableObject.CreateInstance<VelvetDevToolsWindow>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_window);
            VelvetDevToolsRegistry.Clear();
        }

        // What the row toggle does: set the selected fiber, then read it.
        private void Select(ComponentFiber fiber)
        {
            typeof(VelvetDevToolsWindow).GetField("_selectedFiber", Hidden).SetValue(_window, fiber);
            typeof(VelvetDevToolsWindow).GetMethod("RefreshSelectedComponent", Hidden).Invoke(_window, null);
        }

        [Test]
        public void Given_AMountedRoot_When_Selected_Then_TheInspectorReadsItsTree()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Div(name: "probe"));

            // Act
            Select(mounted.Root);

            // Assert
            var text = (string)typeof(VelvetDevToolsWindow).GetField("_cachedVNodeText", Hidden).GetValue(_window);
            Assert.That(text, Does.Contain("probe"));
        }

        [Test]
        public void Given_TheSecondEntrySelected_When_TheFirstLeavesTheRegistry_Then_TheSelectionStaysOnTheSecond()
        {
            // Arrange
            Select(_second);

            // Act
            VelvetDevToolsRegistry.Unregister(_first);

            // Assert
            Assert.That(_window.SelectedEntry()?.Label, Is.EqualTo("Second"));
        }

        [Test]
        public void Given_TheSelectedEntryLeftTheRegistry_When_ItsFiberIsRegisteredAgain_Then_ItIsNotSelected()
        {
            // Arrange
            Select(_second);
            VelvetDevToolsRegistry.Unregister(_second);

            // Act
            VelvetDevToolsRegistry.Register(_second, "Second");

            // Assert
            Assert.That(_window.SelectedEntry()?.Label, Is.Null);
        }
    }
}
