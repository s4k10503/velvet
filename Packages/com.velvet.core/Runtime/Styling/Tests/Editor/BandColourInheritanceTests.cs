using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a ring, ring-offset or outline <c>-inherit</c> resolves to through the colours each ancestor records:
    /// an ancestor that inherits every family passes its parent's colour on, and with nothing recorded above, each
    /// family takes its property's initial value.
    /// </summary>
    [TestFixture]
    internal sealed class BandColourInheritanceTests
    {
        private static BandColourInheritance.Own Inherits => new(false, ColorReference.Inherit, Color.clear);

        // GREEN_ON_BASE(characterization): the base walks past an ancestor that inherits every band colour.
        [Test]
        public void Given_AParentInheritingEveryFamily_When_AChildsRingInherits_Then_ItTakesTheGrandparentsRingColour()
        {
            // Arrange
            var grandparent = new VisualElement();
            var parent = new VisualElement();
            var child = new VisualElement();
            grandparent.Add(parent);
            parent.Add(child);
            var none = new BandColourInheritance.Own(false, ColorReference.None, Color.clear);
            BandColourInheritance.Record(grandparent, new BandColourInheritance.Own(true, ColorReference.None, Color.red),
                none, none);
            BandColourInheritance.Record(parent, Inherits, Inherits, Inherits);

            // Act
            var resolved = BandColourInheritance.Resolve(child, BandColourInheritance.Family.Ring);

            // Assert
            Assert.That(resolved, Is.EqualTo(Color.red));
        }

        // GREEN_ON_BASE(characterization): the base falls back to white for an inherited ring-offset colour.
        [Test]
        public void Given_NoAncestorRecordsAColour_When_ARingOffsetInherits_Then_ItTakesWhite()
        {
            // Arrange — Tailwind registers --tw-ring-offset-color with #fff as its initial value.
            var parent = new VisualElement();
            var child = new VisualElement();
            parent.Add(child);

            // Act
            var resolved = BandColourInheritance.Resolve(child, BandColourInheritance.Family.Offset);

            // Assert
            Assert.That(resolved, Is.EqualTo(Color.white));
        }
    }
}
