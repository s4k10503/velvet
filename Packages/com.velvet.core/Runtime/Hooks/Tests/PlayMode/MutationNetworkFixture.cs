using NUnit.Framework;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Pins the application-wide network readings for the mutation benchmarks, which a device that reads as
    /// offline would otherwise leave paused.
    /// </summary>
    [SetUpFixture]
    internal sealed class MutationNetworkFixture
    {
        [OneTimeSetUp]
        public void PinSignals()
        {
            NetworkSignals.IsOnline = static () => true;
            NetworkSignals.IsVisible = static () => true;
        }

        [OneTimeTearDown]
        public void ReleaseSignals()
        {
            NetworkSignals.IsOnline = null;
            NetworkSignals.IsVisible = null;
        }
    }
}
