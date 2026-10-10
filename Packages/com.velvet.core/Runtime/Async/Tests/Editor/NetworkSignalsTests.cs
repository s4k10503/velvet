#nullable enable
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="NetworkSignals"/>' readings: an override replaces each, and without one the
    /// application counts as visible as <c>Application.isFocused</c> reports, on every platform, and as online
    /// unless <c>Application.internetReachability</c> reports it unreachable.
    /// </summary>
    [TestFixture]
    internal sealed class NetworkSignalsTests
    {
        private static readonly FieldInfo IsFocusedField =
            typeof(ApplicationVisibility).GetField("s_isFocused", BindingFlags.NonPublic | BindingFlags.Static)!;

        private Func<bool> _isFocused = null!;

        [SetUp]
        public void SetUp()
        {
            _isFocused = (Func<bool>)IsFocusedField.GetValue(null)!;
        }

        [TearDown]
        public void TearDown()
        {
            IsFocusedField.SetValue(null, _isFocused);
            NetworkSignals.IsVisible = null;
            NetworkSignals.IsOnline = null;
        }

        [Test]
        public void Given_NoOverrideAndTheApplicationHasLostFocus_When_VisibilityIsRead_Then_TheApplicationIsNotVisible()
        {
            // Arrange
            IsFocusedField.SetValue(null, (Func<bool>)(() => false));

            // Act
            var visible = NetworkSignals.ReadVisible();

            // Assert
            Assert.That(visible, Is.False, "Without an override the application is visible as long as it has focus, on every platform");
        }

        [Test]
        public void Given_NoOverrideAndTheApplicationHasFocus_When_VisibilityIsRead_Then_TheApplicationIsVisible()
        {
            // Arrange
            IsFocusedField.SetValue(null, (Func<bool>)(() => true));

            // Act
            var visible = NetworkSignals.ReadVisible();

            // Assert
            Assert.That(visible, Is.True, "A focused application is visible");
        }

        [Test]
        public void Given_NoOverride_When_TheDefaultReadingIsTaken_Then_ItIsWhatApplicationIsFocusedReports()
        {
            // Act
            var reading = _isFocused();

            // Assert
            Assert.That(reading, Is.EqualTo(Application.isFocused), "The default reading is Unity's focus state");
        }

        [Test]
        public void Given_NoOverride_When_TheConnectionIsRead_Then_ItIsWhatReachabilityReports()
        {
            // Act
            var online = NetworkSignals.ReadOnline();

            // Assert
            Assert.That(online, Is.EqualTo(Application.internetReachability != NetworkReachability.NotReachable),
                "Without an override the reading is Unity's reachability");
        }

        [Test]
        public void Given_AVisibilityOverride_When_VisibilityIsRead_Then_TheOverrideAnswers()
        {
            // Arrange
            NetworkSignals.IsVisible = () => false;

            // Act
            var visible = NetworkSignals.ReadVisible();

            // Assert
            Assert.That(visible, Is.False, "An override replaces the default reading");
        }

        [Test]
        public void Given_AnOnlineOverride_When_TheConnectionIsRead_Then_TheOverrideAnswers()
        {
            // Arrange
            NetworkSignals.IsOnline = () => false;

            // Act
            var online = NetworkSignals.ReadOnline();

            // Assert
            Assert.That(online, Is.False, "An override replaces the default reading");
        }
    }
}
