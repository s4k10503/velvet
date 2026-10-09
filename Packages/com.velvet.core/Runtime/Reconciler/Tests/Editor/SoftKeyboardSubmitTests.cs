using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which status a closing soft keyboard reports that makes <c>V.TextField</c>'s <c>onSubmit:</c>
    /// run. An editor opens no soft keyboard and a keyboard's status is native, so the cases read the decision
    /// alone; holding the keyboard from focus-in and reading it at focus-out is not measured here.
    /// </summary>
    internal sealed class SoftKeyboardSubmitTests
    {
        [Test]
        public void Given_ASingleLineField_When_ItsKeyboardClosesWithDone_Then_ItSubmits()
        {
            // Act
            var submits = FiberEventBindingManager.SubmitsOnKeyboardClose(false, TouchScreenKeyboard.Status.Done);

            // Assert
            Assert.That(submits, Is.True);
        }

        [TestCase(TouchScreenKeyboard.Status.Canceled)]
        [TestCase(TouchScreenKeyboard.Status.LostFocus)]
        [TestCase(TouchScreenKeyboard.Status.Visible)]
        public void Given_ASingleLineField_When_ItsKeyboardClosesWithoutFinishing_Then_ItDoesNotSubmit(
            TouchScreenKeyboard.Status status)
        {
            // Act
            var submits = FiberEventBindingManager.SubmitsOnKeyboardClose(false, status);

            // Assert
            Assert.That(submits, Is.False);
        }

        [Test]
        public void Given_AMultilineField_When_ItsKeyboardClosesWithDone_Then_ItDoesNotSubmit()
        {
            // Act
            var submits = FiberEventBindingManager.SubmitsOnKeyboardClose(true, TouchScreenKeyboard.Status.Done);

            // Assert
            Assert.That(submits, Is.False);
        }
    }
}
