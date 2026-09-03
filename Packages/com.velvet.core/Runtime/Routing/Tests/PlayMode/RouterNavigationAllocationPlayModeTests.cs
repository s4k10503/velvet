using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class RouterNavigationAllocationPlayModeTests
    {
        // Same pin as RouterNavigationAllocationEditorTests, which owns what moving it means.
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
                (_, ct) => VelvetTask.FromResult(false),
                new RouteBlockerState());

            return router;
        }

        static void NavigateOnce()
        {
            using var router = CreateRouter();
            router.NavigateAsync("/target").GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator Given_WarmNavigateAsyncSteadyState_When_Navigated_Then_AllocationMatchesPinnedExpectation()
        {
            for (var i = 0; i < 64; i++)
            {
                NavigateOnce();
            }

            var blocks = GCAllocationProbe.SampleBlocksDuring(NavigateOnce);
            Assert.That(blocks, Is.EqualTo(101));
            yield return null;
        }
    }
}
