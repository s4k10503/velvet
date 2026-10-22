using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the application-wide network readings for every mutation fixture in this assembly, since a mutation
    /// pauses while the device reads as offline and a test run's own connectivity is none of these fixtures'
    /// business. A fixture that sets a reading restores it through <see cref="Reset"/>.
    /// </summary>
    [SetUpFixture]
    internal sealed class MutationNetworkFixture
    {
        internal static void Reset()
        {
            NetworkSignals.IsOnline = static () => true;
            NetworkSignals.IsVisible = static () => true;
        }

        [OneTimeSetUp]
        public void PinSignals() => Reset();

        [OneTimeTearDown]
        public void ReleaseSignals()
        {
            NetworkSignals.IsOnline = null;
            NetworkSignals.IsVisible = null;
        }
    }
}
