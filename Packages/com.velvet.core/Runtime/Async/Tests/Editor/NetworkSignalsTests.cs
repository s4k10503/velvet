#nullable enable
using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="NetworkSignals"/>' readings: an override replaces each, and without one the
    /// application counts as visible as <c>Application.isFocused</c> reports, on every platform, and as online
    /// unless <c>Application.internetReachability</c> reports it unreachable; the visibility default itself
    /// cannot be driven from a test.
    /// </summary>
    [TestFixture]
    internal sealed class NetworkSignalsTests
    {
        [TearDown]
        public void TearDown()
        {
            NetworkSignals.IsVisible = null;
            NetworkSignals.IsOnline = null;
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
