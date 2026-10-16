using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using static Velvet.Tests.PortalOffChainSupport;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which focus-out makes a <c>V.TextField</c>'s soft keyboard submit: one that reaches the field
    /// itself, not one raised in portal content the field holds without being above its call site. An editor
    /// opens no soft keyboard, so the cases stand a keyboard in and read the decision alone.
    /// </summary>
    [TestFixture]
    internal sealed class PortalOffChainFieldSubmitTests : PortalOffChainFixtureBase
    {
        private static string SubmitsPerFocusOut(TextField field, VisualElement[] targets, string keyboard,
            TouchScreenKeyboard.Status status)
        {
            var submits = new List<bool>();
            field.RegisterCallback<FocusOutEvent>(
                evt => submits.Add(FiberEventBindingManager.SubmitsOnFocusOut(field, evt, keyboard, _ => status)),
                TrickleDown.TrickleDown);
            foreach (var target in targets) DispatchFocus<FocusOutEvent>(target);
            return string.Join(",", submits);
        }

        [Test]
        public void Given_AFocusOutRaisedInPortalContentInsideATextField_When_TheKeyboardReportsDone_Then_OnlyAFocusOutOnTheFieldItselfSubmits()
        {
            // Arrange
            var content = MountTextFieldAroundTarget(_ => { });
            var field = _root.Q<TextField>("field");

            // Act
            var submits = SubmitsPerFocusOut(field, new[] { content, field }, "held", TouchScreenKeyboard.Status.Done);

            // Assert
            Assert.That(submits, Is.EqualTo("False,True"));
        }

        [Test]
        public void Given_AFocusOutOnATextField_When_TheKeyboardEndsWithoutDone_Then_ItDoesNotSubmit()
        {
            // Arrange
            MountTextFieldAroundTarget(_ => { });
            var field = _root.Q<TextField>("field");

            // Act
            var submits = SubmitsPerFocusOut(field, new VisualElement[] { field }, "held", TouchScreenKeyboard.Status.Canceled);

            // Assert
            Assert.That(submits, Is.EqualTo("False"));
        }

        [Test]
        public void Given_AFocusOutOnATextField_When_NoKeyboardWasHeld_Then_ItDoesNotSubmit()
        {
            // Arrange
            MountTextFieldAroundTarget(_ => { });
            var field = _root.Q<TextField>("field");

            // Act
            var submits = SubmitsPerFocusOut(field, new VisualElement[] { field }, null, TouchScreenKeyboard.Status.Done);

            // Assert
            Assert.That(submits, Is.EqualTo("False"));
        }
    }
}
