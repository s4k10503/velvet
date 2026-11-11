using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that making a line box allocates the box and nothing beside it. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    internal sealed class TextLineBoxAllocationTests
    {
        [Test]
        public void Given_ATextElement_When_ALineBoxIsMadeForIt_Then_TheBoxIsTheOneBlockAllocated()
        {
            // Arrange — a first box outside the windows, so the type's own initialization is not counted.
            GC.KeepAlive(new TextLineBox(new Label(), centred: false));
            Label label = null!;

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(
                () => label = new Label(),
                () => GC.KeepAlive(new TextLineBox(label, centred: false)));

            // Assert
            Assert.That(blocks, Is.EqualTo(1));
        }
    }
}
