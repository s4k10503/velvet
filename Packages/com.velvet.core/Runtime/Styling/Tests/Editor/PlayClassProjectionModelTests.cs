using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// A class a running play shows, against a removal of the same class on the live class list: a payload going
    /// leaves it, as a variant rule turning off leaves the class attribute alone, and the className dropping it
    /// takes it off.
    /// </summary>
    [TestFixture]
    internal sealed class PlayClassProjectionModelTests
    {
        private static VisualElement ShowingAPlayClassBeside(string cls, long priority)
        {
            var element = new VisualElement();
            StyleClassProjection.Add(element, "my-card", StyleLayerPriority.Base);
            StyleClassProjection.Add(element, cls, priority);
            StyleAnimationClassUtils.AddClasses(element, new[] { cls }, StyleLayerPriority.Animation);
            return element;
        }

        [Test]
        public void Given_APlayShowingAClassAPayloadAlsoAdds_When_ThePayloadTurnsOff_Then_TheClassStays()
        {
            // Arrange
            var element = ShowingAPlayClassBeside("bg-blue-500", StyleLayerPriority.Hover);

            // Act
            StyleClassProjection.Remove(element, "bg-blue-500", StyleLayerPriority.Hover);

            // Assert
            Assert.IsTrue(element.ClassListContains("bg-blue-500"));
        }

        [Test]
        public void Given_APlayShowingAClassTheClassNameDeclaresImportant_When_TheClassNameDropsIt_Then_TheClassGoes()
        {
            // Arrange
            var important = StyleLayerPriority.ImportantOf(StyleLayerPriority.Base);
            var element = ShowingAPlayClassBeside("bg-blue-500", important);

            // Act
            StyleClassProjection.Remove(element, "bg-blue-500", important);

            // Assert
            Assert.IsFalse(element.ClassListContains("bg-blue-500"));
        }
    }
}
