using System;
using System.Threading;
using NUnit.Framework;
using Unity.PerformanceTesting;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    [TestFixture]
    internal sealed class RouterNavigationPlayModeBenchmarks
    {
        private const int WarmupCount = 5;
        private const int MeasurementCount = 20;

        // GREEN_ON_BASE(refactor): the benchmark times the same navigation as it did before the branch.
        // Its Blocker takes the synchronous predicate, since the branch removes the asynchronous one.
        [Test, Performance]
        public void NavigateAsync_LoaderAndBlocker()
        {
            Action instrumentCanary = static () => GC.KeepAlive(new byte[16]);
            GCAllocationProbe.SampleBlocksDuring(instrumentCanary);

            for (var i = 0; i < 64; i++)
            {
                using var router = CreateRouter();
                router.NavigateAsync("/target").GetAwaiter().GetResult();
            }

            Measure.Method(() =>
                {
                    using var router = CreateRouter();
                    router.NavigateAsync("/target").GetAwaiter().GetResult();
                })
                .GC()
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .Run();
        }

        private static Router CreateRouter()
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
    }
}
