using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// A pooled Button keeps the <see cref="Clickable"/> it was constructed with, and the reset scrubs that
    /// Clickable's handler and pressed state in place.
    /// </summary>
    [TestFixture]
    internal sealed class ButtonClickablePoolResetTests
    {
        const BindingFlags NonPublicInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        static PropertyInfo ActiveProperty => typeof(Clickable).GetProperty("active", NonPublicInstance);

        [Test]
        public void Given_AButtonWithAClickedHandler_When_ResetForReuse_Then_TheHandlerIsGone()
        {
            // Arrange
            var button = new Button();
            button.clicked += () => { };
            var clickedField = typeof(Clickable).GetField(nameof(Clickable.clicked), NonPublicInstance);

            // Act
            FiberButtonPoolHelper.ResetButtonForReuse(button);

            // Assert
            Assert.IsNull(clickedField?.GetValue(button.clickable) ?? "field not found");
        }

        [Test]
        public void Given_AButtonWithAClickedWithEventInfoHandler_When_ResetForReuse_Then_TheHandlerIsGone()
        {
            // Arrange
            var button = new Button();
            button.clickable.clickedWithEventInfo += _ => { };
            var field = typeof(Clickable).GetField(nameof(Clickable.clickedWithEventInfo), NonPublicInstance);

            // Act
            FiberButtonPoolHelper.ResetButtonForReuse(button);

            // Assert
            Assert.IsNull(field?.GetValue(button.clickable) ?? "field not found");
        }

        [Test]
        public void Given_AButtonWhoseClickableIsPressed_When_ResetForReuse_Then_ItIsNoLongerPressed()
        {
            // Arrange
            var button = new Button();
            ActiveProperty.SetValue(button.clickable, true);

            // Act
            FiberButtonPoolHelper.ResetButtonForReuse(button);

            // Assert
            Assert.AreEqual(false, ActiveProperty.GetValue(button.clickable));
        }

        [Test]
        public void Given_AButton_When_ResetForReuse_Then_ItKeepsItsClickable()
        {
            // Arrange
            var button = new Button();
            var before = button.clickable;

            // Act
            FiberButtonPoolHelper.ResetButtonForReuse(button);

            // Assert
            Assert.AreSame(before, button.clickable);
        }
    }
}
