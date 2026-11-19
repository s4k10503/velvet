using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that StyleThemeTokens stops holding an element's variable-context watch once the element is
    /// unfollowed, so a later Follow starts a watch that has seen no context yet.
    /// </summary>
    internal sealed class ThemeTokenFollowTests
    {
        // GREEN_ON_BASE(characterization): the base drops the element's watch from its table on Unfollow.
        [Test]
        public void Given_AFollowedElement_When_Unfollowed_Then_ItsWatchIsNoLongerHeld()
        {
            // Arrange
            var element = new VisualElement();
            StyleThemeTokens.Follow(element, () => { });
            var followed = (ConditionalWeakTable<VisualElement, StyleThemeTokens.ContextWatch>)typeof(StyleThemeTokens)
                .GetField("s_followed", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
            var heldBefore = followed.TryGetValue(element, out _);

            // Act
            StyleThemeTokens.Unfollow(element);

            // Assert — null where Follow held nothing, which would leave nothing to drop.
            Assert.That(heldBefore ? followed.TryGetValue(element, out _) : (bool?)null, Is.EqualTo(false));
        }
    }
}
