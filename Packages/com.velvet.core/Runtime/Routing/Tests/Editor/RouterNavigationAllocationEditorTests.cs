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

        [Test]
        public void Given_WarmNavigateAsyncSteadyState_When_Navigated_Then_AllocationMatchesPinnedExpectation()
        {
            // Arrange
            for (var i = 0; i < 64; i++)
            {
                NavigateOnce();
            }

            // Act
            var blocks = GCAllocationProbe.SampleBlocksDuring(NavigateOnce);

            // Assert
            Assert.That(blocks, Is.EqualTo(92));
        }

#if UNITY_EDITOR
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
            var blocks = GCAllocationProbe.SampleBlocksDuring(NavigateOnce);

            // Assert
            Assert.That(blocks, Is.EqualTo(92));
        }
#endif
    }
}
