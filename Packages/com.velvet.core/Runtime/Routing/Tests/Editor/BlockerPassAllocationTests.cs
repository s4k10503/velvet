using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a blocker pass costs the same whether the manager holds registrations or none.
    /// The pass walks a snapshot so a decision taken in it may unregister, and a snapshot taken per
    /// pass is an allocation on every navigation an application registers a blocker for.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    internal sealed class BlockerPassAllocationTests
    {
        private static RouteBlockerManager Manager(int blockers)
        {
            var manager = new RouteBlockerManager();
            for (var index = 0; index < blockers; index++)
            {
                manager.Register((_, __) => UniTask.FromResult(false), new RouteBlockerState());
            }
            return manager;
        }

        private static int Blocks(int blockers)
        {
            var manager = Manager(blockers);
            var attempt = new NavigationAttempt();
            void Once() => manager.CheckAsync(attempt, () => { }).GetAwaiter().GetResult();
            for (var i = 0; i < 64; i++)
            {
                Once();
            }
            return GCAllocationProbe.SampleBlocksDuring(Once);
        }

        [Test]
        public void Given_APassOverTwoRegistrations_When_Compared_To_APassOverNone_Then_TheCostIsTheSame()
        {
            // Arrange & Act
            var none = Blocks(0);
            var two = Blocks(2);

            // Assert — the empty count rides along, because two equal numbers say nothing if the probe
            // measured nothing at all.
            Assert.That((none > 0, two), Is.EqualTo((true, none)));
        }
    }
}
