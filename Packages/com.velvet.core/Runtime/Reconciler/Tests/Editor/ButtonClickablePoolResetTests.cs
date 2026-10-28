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
        static PropertyInfo ActiveProperty => EngineMember.ClickableActive.ResolveProperty();

        [Test]
        public void Given_AButtonWithAClickedHandler_When_ResetForReuse_Then_TheHandlerIsGone()
        {
            // Arrange
            var button = new Button();
            button.clicked += () => { };
            var clickedField = EngineMember.ClickableClicked.ResolveField();

            // Act
            FiberButtonPoolHelper.ResetButtonForReuse(button);

            // Assert
            Assert.That((clickedField != null, clickedField?.GetValue(button.clickable)), Is.EqualTo((true, (object)null)));
        }

        [Test]
        public void Given_AButtonWithAClickedWithEventInfoHandler_When_ResetForReuse_Then_TheHandlerIsGone()
        {
            // Arrange
            var button = new Button();
            button.clickable.clickedWithEventInfo += _ => { };
            var field = EngineMember.ClickableClickedWithEventInfo.ResolveField();

            // Act
            FiberButtonPoolHelper.ResetButtonForReuse(button);

            // Assert
            Assert.That((field != null, field?.GetValue(button.clickable)), Is.EqualTo((true, (object)null)));
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
