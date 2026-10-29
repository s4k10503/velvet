using System;
using System.Diagnostics;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine.Profiling;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// The keyed operation set of the js-framework-benchmark suite, driven through <see cref="KeyedBenchmarkApp"/>.
    /// <para>
    /// Each case measures one dispatch plus the synchronous flush that commits it onto the mounted elements.
    /// The root is a <see cref="VisualElement"/> with no panel, as in <c>ReconcilerBenchmarks</c>, so the
    /// measurement holds no layout pass. Time is the stopwatch over the window; allocation is the count of
    /// GC.Alloc sample blocks the window charges to the running thread, the recorder the other async
    /// benchmarks read. The arrangement that puts the rows in the state an operation starts from runs before
    /// the window.
    /// </para>
    /// <para>
    /// After every iteration, measured or not, the elements are compared with the state they should show
    /// and the number of row renders with the number the operation should cause, and a difference throws.
    /// A benchmark whose operation did not reach the code it is named for would otherwise report a number
    /// with nothing behind it.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class JsFrameworkKeyedBenchmarks
    {
        private static readonly int WarmupCount = BenchmarkRun.Warmup(3);
        private static readonly int MeasurementCount = BenchmarkRun.Measurements(10);
        private static readonly int LargeWarmupCount = BenchmarkRun.Warmup(1);
        private static readonly int LargeMeasurementCount = BenchmarkRun.Measurements(5);

        private const int Rows = 1000;
        private const int ManyRows = 10000;
        private const int SelectedIndex = 500;
        private const int RemovedIndex = 4;

        private sealed record Operation(
            Action Arrange,
            Func<KeyedAction> Next,
            int RowsAfter,
            int RowRendersAfter);

        private VisualElement _root = null!;
        private MountedTree _mounted = null!;

        [SetUp]
        public void SetUp()
        {
            KeyedBenchmarkApp.Reset();
            _root = new VisualElement();
            _mounted = V.Mount(_root, V.Component(KeyedBenchmarkApp.RenderApp));
            _mounted.FlushStateForTest();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted.Dispose();
        }

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [Test, Performance]
        public void Create_1000Rows()
        {
            Execute(
                new Operation(() => Apply(Clear()), () => RunRows(Rows), Rows, Rows),
                WarmupCount,
                MeasurementCount);
        }

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [Test, Performance]
        public void Replace_AllOf1000Rows()
        {
            Execute(
                new Operation(() => EnsureRows(Rows), () => RunRows(Rows), Rows, Rows),
                WarmupCount,
                MeasurementCount);
        }

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [Test, Performance]
        public void PartialUpdate_EveryTenthOf1000Rows()
        {
            Execute(
                new Operation(
                    () => EnsureRows(Rows),
                    () => new KeyedAction(KeyedActionKind.Update),
                    Rows,
                    Rows / 10),
                WarmupCount,
                MeasurementCount);
        }

        // The arrangement selects the first row, so the measured selection both highlights one row and
        // un-highlights another.
        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [Test, Performance]
        public void Select_OneOf1000Rows()
        {
            Execute(
                new Operation(
                    () =>
                    {
                        EnsureRows(Rows);
                        Apply(Select(0));
                    },
                    () => Select(SelectedIndex),
                    Rows,
                    2),
                WarmupCount,
                MeasurementCount);
        }

        // Rows 2 and 999 trade places; neither row's props change, so no row renders.
        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [Test, Performance]
        public void Swap_Rows2And999Of1000()
        {
            Execute(
                new Operation(
                    () => EnsureRows(Rows),
                    () => new KeyedAction(KeyedActionKind.Swap),
                    Rows,
                    0),
                WarmupCount,
                MeasurementCount);
        }

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [Test, Performance]
        public void Remove_OneOf1000Rows()
        {
            Execute(
                new Operation(
                    () => Apply(RunRows(Rows)),
                    () => new KeyedAction(KeyedActionKind.Remove, Id: KeyedBenchmarkApp.State.Data[RemovedIndex].Id),
                    Rows - 1,
                    0),
                WarmupCount,
                MeasurementCount);
        }

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [Test, Performance]
        public void Create_10000Rows()
        {
            Execute(
                new Operation(() => Apply(Clear()), () => RunRows(ManyRows), ManyRows, ManyRows),
                LargeWarmupCount,
                LargeMeasurementCount);
        }

        // The 1000 rows already shown keep their props, so only the appended rows render.
        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [Test, Performance]
        public void Append_1000RowsTo1000()
        {
            Execute(
                new Operation(
                    () => Apply(RunRows(Rows)),
                    () => new KeyedAction(KeyedActionKind.Add, Count: Rows),
                    2 * Rows,
                    Rows),
                WarmupCount,
                MeasurementCount);
        }

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [Test, Performance]
        public void Clear_1000Rows()
        {
            Execute(
                new Operation(() => Apply(RunRows(Rows)), Clear, 0, 0),
                WarmupCount,
                MeasurementCount);
        }

        private static KeyedAction RunRows(int count) => new(KeyedActionKind.Run, Count: count);

        private static KeyedAction Clear() => new(KeyedActionKind.Clear);

        private static KeyedAction Select(int index)
            => new(KeyedActionKind.Select, Id: KeyedBenchmarkApp.State.Data[index].Id);

        private void Apply(KeyedAction action)
        {
            KeyedBenchmarkApp.Dispatch(action);
            _mounted.FlushStateForTest();
        }

        private void EnsureRows(int count)
        {
            if (KeyedBenchmarkApp.State.Data.Length != count)
            {
                Apply(RunRows(count));
            }
        }

        private void Execute(Operation operation, int warmup, int measurements)
        {
            var time = new SampleGroup("Op.Time", SampleUnit.Millisecond);
            var allocation = new SampleGroup("Op.GC", SampleUnit.Undefined);

            for (var i = 0; i < warmup; i++)
            {
                RunIteration(operation, null, null);
            }

            for (var i = 0; i < measurements; i++)
            {
                RunIteration(operation, time, allocation);
            }

            // One more iteration after the measured ones, whatever the counts, for the reason
            // StyleManipulatorReconcileBenchmarks.Mount_100StyledRows gives its extra cycle.
            RunIteration(operation, null, null);
        }

        private void RunIteration(Operation operation, SampleGroup? time, SampleGroup? allocation)
        {
            operation.Arrange();
            var action = operation.Next();
            KeyedBenchmarkApp.RowRenders = 0;

            var recorder = Recorder.Get("GC.Alloc");
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;

            var sw = Stopwatch.StartNew();
            KeyedBenchmarkApp.Dispatch(action);
            _mounted.FlushStateForTest();
            sw.Stop();

            recorder.enabled = false;
            var blocks = recorder.sampleBlockCount;
            recorder.CollectFromAllThreads();

            VerifyTree(operation);

            if (time != null && allocation != null)
            {
                Measure.Custom(time, sw.Elapsed.TotalMilliseconds);
                Measure.Custom(allocation, blocks);
            }
        }

        private void VerifyTree(Operation operation)
        {
            var data = KeyedBenchmarkApp.State.Data;
            var buttons = _root.Query<Button>().ToList();
            if (data.Length != operation.RowsAfter || buttons.Count != 2 * operation.RowsAfter)
            {
                throw new InvalidOperationException(
                    $"expected {operation.RowsAfter} rows, the state holds {data.Length} and the tree {buttons.Count / 2}");
            }

            for (var i = 0; i < data.Length; i++)
            {
                if (buttons[2 * i].text != data[i].Label)
                {
                    throw new InvalidOperationException($"row {i} shows '{buttons[2 * i].text}', the state holds '{data[i].Label}'");
                }
            }

            if (KeyedBenchmarkApp.RowRenders != operation.RowRendersAfter)
            {
                throw new InvalidOperationException(
                    $"expected {operation.RowRendersAfter} row renders, saw {KeyedBenchmarkApp.RowRenders}");
            }
        }
    }
}
