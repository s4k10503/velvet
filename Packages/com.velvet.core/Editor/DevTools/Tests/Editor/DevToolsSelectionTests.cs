using NUnit.Framework;
using UnityEngine;
using Velvet.DevTools;
using Velvet.Editor.DevTools;

namespace Velvet.Tests
{
    internal sealed class DevToolsSelectionTests
    {
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

        [Test]
        public void Given_TheSecondEntrySelected_When_TheFirstLeavesTheRegistry_Then_TheSelectionStaysOnTheSecond()
        {
            // Arrange
            _window.Select(_second);

            // Act
            VelvetDevToolsRegistry.Unregister(_first);

            // Assert
            Assert.That(_window.SelectedEntry()?.Label, Is.EqualTo("Second"));
        }

        [Test]
        public void Given_TheSelectedEntryLeftTheRegistry_When_ItsFiberIsRegisteredAgain_Then_ItIsNotSelected()
        {
            // Arrange
            _window.Select(_second);
            VelvetDevToolsRegistry.Unregister(_second);

            // Act
            VelvetDevToolsRegistry.Register(_second, "Second");

            // Assert
            Assert.That(_window.SelectedEntry()?.Label, Is.Null);
        }
    }
}
