using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    [TestFixture]
    internal sealed class RouteLoaderRunnerPlayModeBenchmarks
    {
        private const int WarmupCount = 5;
        private const int MeasurementCount = 20;

        private static readonly RouteLoaderRunner SharedRunner = new();

        private static VelvetTask<object> CompletedLoader(RouteLoaderContext _, CancellationToken __) =>
            VelvetTask.FromResult<object>("loaded-data");

        private static List<RouteMatch> MakeAwaitMatch() =>
            new()
            {
                new RouteMatch
                {
                    Route = new RouteDefinition
                    {
                        Path = "test",
                        Loader = CompletedLoader,
                        LoaderMode = LoaderMode.Await,
                    },
                    Params = new Dictionary<string, string>(),
                    MatchedPath = "test",
                    RouteId = "test",
                },
            };

        [Test, Performance]
        public void RunLoadersSync_AwaitSyncCompleted()
        {
            for (var i = 0; i < 16; i++)
            {
                SharedRunner.RunLoadersSync(MakeAwaitMatch(), CancellationToken.None);
            }

            var matches = MakeAwaitMatch();
            Measure.Method(() => SharedRunner.RunLoadersSync(matches, CancellationToken.None))
                .GC()
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .Run();
        }

        [UnityTest, Performance]
        public IEnumerator RunLoadersSync_SuspendDeferredCompletion()
        {
            var groupTime = new SampleGroup("RouteLoaderRunner.SuspendDeferred.Time", SampleUnit.Millisecond);
            var groupGC = new SampleGroup("RouteLoaderRunner.SuspendDeferred.GC", SampleUnit.Undefined);

            for (var i = 0; i < WarmupCount; i++)
            {
                yield return RunSuspendDeferredRoundTrip();
            }

            for (var i = 0; i < MeasurementCount; i++)
            {
                var measurement = new SuspendDeferredMeasurement();
                yield return RunSuspendDeferredRoundTripMeasured(measurement);

                Measure.Custom(groupTime, measurement.ElapsedMs);
                Measure.Custom(groupGC, measurement.AllocBlocks);
            }
        }

        private static List<RouteMatch> MakeSuspendMatch(VelvetTask<object> task) =>
            new()
            {
                new RouteMatch
                {
                    Route = new RouteDefinition
                    {
                        Path = "test",
                        Loader = (_, __) => task,
                        LoaderMode = LoaderMode.Suspend,
                    },
                    Params = new Dictionary<string, string>(),
                    MatchedPath = "test",
                    RouteId = "test",
                },
            };

        private static IEnumerator RunSuspendDeferredRoundTrip()
        {
            var tcs = new VelvetTaskCompletionSource<object>();
            SharedRunner.RunLoadersSync(MakeSuspendMatch(tcs.Task), CancellationToken.None);
            tcs.TrySetResult("loaded-data");
            yield return null;
        }

        private static IEnumerator RunSuspendDeferredRoundTripMeasured(SuspendDeferredMeasurement measurement)
        {
            var tcs = new VelvetTaskCompletionSource<object>();
            var recorder = Recorder.Get("GC.Alloc");
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;

            var sw = Stopwatch.StartNew();
            SharedRunner.RunLoadersSync(MakeSuspendMatch(tcs.Task), CancellationToken.None);
            tcs.TrySetResult("loaded-data");
            yield return null;
            sw.Stop();

            recorder.enabled = false;
            measurement.AllocBlocks = recorder.sampleBlockCount;
            measurement.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            recorder.CollectFromAllThreads();
        }

        private sealed class SuspendDeferredMeasurement
        {
            public double ElapsedMs;
            public int AllocBlocks;
        }
    }
}
