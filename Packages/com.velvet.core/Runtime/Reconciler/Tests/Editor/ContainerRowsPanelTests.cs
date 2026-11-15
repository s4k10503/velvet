using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The <see cref="ChildListWrites"/> cases that need a child on a panel: an event UI Toolkit sends from inside
    /// the call a write makes, and a write made between two passes.
    /// </summary>
    [TestFixture]
    internal sealed class ContainerRowsPanelTests : PanelTestBase
    {
        [Test]
        public void Given_AHandlerThatWritesTheDestinationFromTheChildsDetach_When_TheChildIsMovedThere_Then_TheModelHoldsBothChildren()
        {
            // Arrange
            var root = _window.rootVisualElement;
            var source = new VisualElement();
            var destination = new VisualElement();
            ChildListWrites.Add(root, source);
            ChildListWrites.Add(root, destination);
            var child = new VisualElement();
            ChildListWrites.Add(source, child);
            child.RegisterCallback<DetachFromPanelEvent>(_ => ChildListWrites.Add(destination, new VisualElement()));

            // Act
            ChildListWrites.Add(destination, child);

            // Assert
            Assert.That(ChildListWrites.Peek(destination)!.TotalCount, Is.EqualTo(destination.childCount));
        }

        [Test]
        public void Given_AWriteBetweenPassesThenAForeignReorder_When_APassBegins_Then_TheDriftIsLogged()
        {
            // Arrange
            var container = new VisualElement();
            _window.rootVisualElement.Add(container);
            ChildListWrites.Add(container, new VisualElement());
            ChildListWrites.Add(container, new VisualElement());
            container[0].BringToFront();
            LogAssert.Expect(LogType.Assert, new Regex("ContainerRows drifted"));

            // Act
            ChildListWrites.BeginPass();

            // Assert
            Assert.That(ChildListWrites.Peek(container)!.ElementAt(0), Is.SameAs(container[0]));
        }

        [Test]
        public void Given_AForeignReorderThatWasAnnounced_When_ThePassEnds_Then_NothingIsLogged()
        {
            // Arrange
            var container = new VisualElement();
            _window.rootVisualElement.Add(container);
            ChildListWrites.Add(container, new VisualElement());
            ChildListWrites.Add(container, new VisualElement());
            container[0].BringToFront();
            ChildListWrites.NoteHierarchyChange(container[0]);

            // Act
            ChildListWrites.VerifyTouched();

            // Assert
            Assert.That(ChildListWrites.Peek(container)!.ElementAt(0), Is.SameAs(container[0]));
        }
    }
}
