using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// The clip wrapper carries its element's classes, so whatever a USS longhand does on the wrapper has been
    /// decided for every longhand: moved from the element, held inert, owned by the mask, or left to the classes.
    /// A longhand the table does not name would reach the wrapper undecided. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class ClipPathLayoutBoxRoleTests
    {
        [Test]
        public void Given_EveryUssLonghand_When_TheClipWrapperRolesAreRead_Then_EachLonghandHasExactlyOneRole()
        {
            // Arrange
            var entries = (Array)typeof(ClipPathLayoutBox)
                .GetField("s_entries", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);
            var longhandField = entries.GetType().GetElementType().GetField("Longhand");
            var counts = Enum.GetValues(typeof(StyleLonghand)).Cast<StyleLonghand>().ToDictionary(l => l, _ => 0);

            // Act
            foreach (var entry in entries)
            {
                counts[(StyleLonghand)longhandField.GetValue(entry)]++;
            }

            // Assert
            var wrong = counts.Where(pair => pair.Value != 1).Select(pair => pair.Key + "=" + pair.Value);
            Assert.That(string.Join(", ", wrong), Is.Empty);
        }
    }
}
