using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that an element stops being watched for the variables its classes read once the last class reading one
    /// leaves, without a panel.
    /// </summary>
    internal sealed class CustomPropertyConsumerWatchTests
    {
        private static readonly FieldInfo s_watched =
            typeof(StyleCustomProperties).GetField("s_consumerElements", BindingFlags.NonPublic | BindingFlags.Static)!;

        private static int Watched() => (int)s_watched.GetValue(null);

        private static void Toggle(VisualElement element, bool on)
            => StyleCustomProperties.Toggle(element, "bg-(--brand)", on, StyleLayerPriority.Base,
                StyleVariantPayload.NoDeclaration, null);

        // GREEN_ON_BASE(characterization): the base stops watching an element once its last variable reader leaves.
        [Test]
        public void Given_AnElementWhoseOnlyClassReadsAVariable_When_TheClassLeaves_Then_TheElementIsNoLongerWatched()
        {
            // Arrange
            var element = new VisualElement();
            var before = Watched();
            Toggle(element, true);
            var during = Watched();

            // Act
            Toggle(element, false);

            // Assert — the change the class's arrival made is the control: -1 where it started no watch.
            Assert.That(during - before == 1 ? Watched() - before : -1, Is.Zero);
        }
    }
}
