using System.Threading;
using NUnit.Framework;
using Velvet.TestUtilities;

#if UNITY_EDITOR
using static Velvet.TestUtilities.VelvetTaskFrameDriverTestExtensions;
#endif

namespace Velvet.Tests
{
    [TestFixture]
    [Category("Performance")]
    internal sealed class RouterNavigationAllocationEditorTests
    {
        static async VelvetTask<int> YieldThenReturn()
        {
            await VelvetTask.Yield();
            return 42;
        }

        // `CreateRouter` is inside the measured call, so the router's construction and its Blocker's
        // registration count toward the pin. Moving the number is the answer when the change that moved it
        // is understood.
        // The tree's route ids are built at construction (id strings, the id and path lists, the id set and
        // one id array per branch), which accounts for the rise from 92 to 101 net of the id string a
        // navigation no longer concatenates. The router stands on its first entry from construction, as
        // createMemoryRouter's does, so that entry's match and location are in the pin as well: 7 of the 106,
        // measured as the drop when construction builds neither.
        static Router CreateRouter()
        {
            var router = new Router(new[]
            {
                new RouteDefinition
                {
                    Path = "/",
                    Children = new[]
                    {
                        new RouteDefinition
                        {
                            Path = "target",
                            Loader = (_, ct) => VelvetTask.FromResult((object)"loaded"),
                            LoaderMode = LoaderMode.Await,
                        },
                    },
                },
            });

            router.RouteBlockerManager.Register(
                _ => false,
                new RouteBlockerState());

            return router;
        }

        static void NavigateOnce()
        {
            using var router = CreateRouter();
            router.NavigateAsync("/target").GetAwaiter().GetResult();
        }

#if UNITY_EDITOR
        static void YieldAwaitOnce()
        {
            var task = YieldThenReturn();
            DrainEditorUpdateForTest();
            task.GetAwaiter().GetResult();
        }
#endif

        // GREEN_ON_BASE(characterization): the base already allocates what this case pins.
        // This change reads it over three windows.
        [Test]
        public void Given_WarmNavigateAsyncSteadyState_When_Navigated_Then_AllocationMatchesPinnedExpectation()
        {
            // Arrange
            for (var i = 0; i < 64; i++)
            {
                NavigateOnce();
            }

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(NavigateOnce);

            // Assert
            Assert.That(blocks, Is.EqualTo(106));
        }

#if UNITY_EDITOR
        // GREEN_ON_BASE(characterization): the base already allocates what this case pins.
        // This change reads it over three windows.
        [Test]
        public void Given_WarmYieldAwaitBeforeNavigate_When_Navigated_Then_AllocationMatchesPinnedExpectation()
        {
            // Arrange
            for (var i = 0; i < 64; i++)
            {
                YieldAwaitOnce();
            }

            for (var i = 0; i < 64; i++)
            {
                NavigateOnce();
            }

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(NavigateOnce);

            // Assert
            Assert.That(blocks, Is.EqualTo(106));
        }
#endif
    }
}
