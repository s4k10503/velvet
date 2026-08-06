using System;
using System.Threading;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    [TestFixture]
    internal sealed class UseMutationPlayModeBenchmarks
    {
        private const int WarmupCount = 5;
        private const int MeasurementCount = 20;

        private static MutationResult<int, int>? s_captured;
        private static Func<int, CancellationToken, VelvetTask<int>> s_mutationFn =
            (v, _) => VelvetTask.FromResult(v * 2);

        private VisualElement _root;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_captured = null;
            s_mutationFn = (v, _) => VelvetTask.FromResult(v * 2);
            _mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "mutation-bench"));
        }

        [TearDown]
        public void TearDown() => _mounted.Dispose();

        [Test, Performance]
        public void UseMutation_OneMutateRoundTrip()
        {
            for (var i = 0; i < 16; i++)
            {
                s_captured!.Reset();
                s_captured.MutateAsync(21).GetAwaiter().GetResult();
                _mounted.FlushStateForTest();
            }

            Measure.Method(() =>
                {
                    s_captured!.Reset();
                    s_captured.MutateAsync(21).GetAwaiter().GetResult();
                    _mounted.FlushStateForTest();
                })
                .GC()
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .Run();
        }

        [Component]
        private static VNode CaptureMutationRender()
        {
            s_captured = Hooks.UseMutation(new MutationOptions<int, int>(MutationFn: s_mutationFn));
            return V.Label(text: s_captured.Status.ToString());
        }
    }
}
