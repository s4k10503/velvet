using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    [TestFixture]
    internal sealed class FiberAsyncResourcePlayModeBenchmarks
    {
        private const int WarmupCount = 5;
        private const int MeasurementCount = 20;

        private static readonly object ResourceKey = new();
        private static Func<CancellationToken, VelvetTask<int>> s_factory;

        private VisualElement _root;

        [SetUp]
        public void SetUp() => _root = new VisualElement();

        [UnityTest, Performance]
        public IEnumerator HooksUse_SuspendResumeRoundTrip()
        {
            var groupTime = new SampleGroup("HooksUse.SuspendResume.Time", SampleUnit.Millisecond);
            var groupGC = new SampleGroup("HooksUse.SuspendResume.GC", SampleUnit.Undefined);

            for (var i = 0; i < WarmupCount; i++)
            {
                yield return RunHooksUseSuspendResumeRoundTrip(_root);
            }

            for (var i = 0; i < MeasurementCount; i++)
            {
                var measurement = new HooksUseMeasurement();
                yield return RunHooksUseSuspendResumeRoundTripMeasured(_root, measurement);

                Measure.Custom(groupTime, measurement.ElapsedMs);
                Measure.Custom(groupGC, measurement.AllocBlocks);
            }
        }

        [Component]
        private static VNode HooksUseBenchmarkRender()
            => V.Label(text: Hooks.Use(s_factory, ResourceKey).ToString());

        private static IEnumerator RunHooksUseSuspendResumeRoundTrip(VisualElement root)
        {
            s_factory = async ct =>
            {
                await VelvetTask.Yield();
                return 42;
            };

            using var mounted = V.Mount(
                root,
                V.Component(HooksUseBenchmarkRender, key: Guid.NewGuid().ToString()));
            mounted.FlushStateForTest();
            yield return null;
            mounted.FlushStateForTest();
        }

        private static IEnumerator RunHooksUseSuspendResumeRoundTripMeasured(
            VisualElement root,
            HooksUseMeasurement measurement)
        {
            s_factory = async ct =>
            {
                await VelvetTask.Yield();
                return 42;
            };

            var recorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;

            var sw = Stopwatch.StartNew();
            using var mounted = V.Mount(
                root,
                V.Component(HooksUseBenchmarkRender, key: Guid.NewGuid().ToString()));
            mounted.FlushStateForTest();
            yield return null;
            mounted.FlushStateForTest();
            sw.Stop();

            recorder.enabled = false;
            measurement.AllocBlocks = recorder.sampleBlockCount;
            measurement.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            recorder.CollectFromAllThreads();
        }

        private sealed class HooksUseMeasurement
        {
            public double ElapsedMs;
            public int AllocBlocks;
        }
    }
}
