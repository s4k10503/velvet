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
                (_, ct) => VelvetTask.FromResult(false),
                new RouteBlockerState());

            return router;
        }
    }
}
