using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that consulting a registered Blocker that lets the attempt through costs nothing beyond
    /// what a pass over no registration costs, so an application registering one does not pay for it on every
    /// navigation.
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
                manager.Register(_ => false, new RouteBlockerState());
            }
            return manager;
        }

        // The argument is built inside the measured call, as the router builds one for every navigation it
        // consults a Blocker about; it is also what lets the pass over none read above zero.
        private static int Blocks(int blockers)
        {
            var manager = Manager(blockers);
            void Once() => manager.Check(new BlockerFunctionArgs(), () => { });
            for (var i = 0; i < 64; i++)
            {
                Once();
            }
            return GCAllocationProbe.SampleBlocksDuring(Once);
        }

        [Test]
        public void Given_APassOverOneRegistration_When_Compared_To_APassOverNone_Then_TheCostIsTheSame()
        {
            // Arrange & Act
            var none = Blocks(0);
            var one = Blocks(1);

            // Assert — the empty count rides along, because two equal numbers say nothing if the probe
            // measured nothing at all.
            Assert.That((none > 0, one), Is.EqualTo((true, none)));
        }
    }
}
