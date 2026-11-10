using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a face stash released to the native chrome and taken again lands on the colour the rules give the
    /// face then, rather than running a transition from the colour it held before the release.
    /// </summary>
    internal sealed class SilhouetteFaceRestashTests : PanelTestBase
    {
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        // GREEN_ON_BASE(characterization): the base lands a re-taken face on the background its rules give it now.
        [Test]
        public void Given_AFaceReleasedWhileItsBackgroundRuleChanged_When_ItIsTakenAgain_Then_ItShowsTheNewBackground()
        {
            // Arrange — a colour transition, which a carry that did not land would run the new colour on.
            VelvetPalette.TryResolveColorToken("blue-500", out var blue);
            var element = new VisualElement { style = { width = 40f, height = 40f } };
            element.AddToClassList("bg-red-500");
            element.AddToClassList("transition-colors");
            element.AddToClassList("duration-1000");
            _window.rootVisualElement.Add(element);
            ForcePanelUpdate(element.panel);
            var stash = new SilhouetteFaceStash();
            stash.TryStash(element);
            stash.Release(element);
            element.RemoveFromClassList("bg-red-500");
            element.AddToClassList("bg-blue-500");
            ForcePanelUpdate(element.panel);

            // Act
            stash.TryStash(element);

            // Assert
            Assert.That(stash.CurrentBg, Is.EqualTo(blue));
        }
    }
}
