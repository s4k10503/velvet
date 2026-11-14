using System;
using System.Collections;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Frames in which no state changes, on a live runtime panel holding a mounted tree of 1,000 styled elements.
    /// Each sample is one such frame: the time the panel recorded under the markers <see cref="PanelFrameRecorder"/>
    /// reads, and the GC.Alloc blocks the main thread charged in it. Frames are paced at 60 a second,
    /// longer than the 16 ms the per-element polls here repeat at, so a sample is a frame each of them can run in;
    /// the paced wall clock is not recorded.
    /// <para>
    /// Two arms over the same elements and the same styled rows. The polling arm adds the tokens that give an
    /// element a poll of its own on the panel's scheduler, so what it records above the other arm is what those
    /// polls cost a frame in which no state changes.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class IdleFrameBenchmarks
    {
        private const string PanelName = "VelvetIdleFrameBenchmarkPanel";
        private const int Rows = 200;
        private const int ElementsPerRow = 5;
        private const int SettleFrames = 5;
        private const int FramesPerSecond = 60;

        private static readonly int WarmupFrames = BenchmarkRun.Warmup(10);
        private static readonly int MeasuredFrames = BenchmarkRun.Measurements(60);

        private GameObject _gameObject = null!;
        private PanelSettings _panelSettings = null!;
        private VisualElement _root = null!;
        private MountedTree _mounted = null!;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _gameObject = new GameObject("IdleFrameBenchmarkRoot");
            var document = _gameObject.AddComponent<UIDocument>();
            _panelSettings = TestPanelSettings.Create();
            // As E2EBenchmarks names its panel, for the same reason.
            _panelSettings.name = PanelName;
            document.panelSettings = _panelSettings;
            yield return null;
            _root = document.rootVisualElement;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _mounted?.Dispose();
            if (_gameObject != null)
                UnityEngine.Object.Destroy(_gameObject);
            if (_panelSettings != null)
                UnityEngine.Object.Destroy(_panelSettings);
            yield return null;
        }

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [UnityTest, Performance]
        public IEnumerator IdleFrames_1000StyledElements() => Run(polling: false);

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [UnityTest, Performance]
        public IEnumerator IdleFrames_1000StyledElementsWithPolls() => Run(polling: true);

        private IEnumerator Run(bool polling)
        {
            _mounted = V.Mount(_root, V.Div("flex flex-col", BuildRows(polling)));
            _mounted.FlushStateForTest();

            // The frames that lay the mount out and paint it first, which are not idle ones.
            for (var i = 0; i < SettleFrames; i++)
            {
                yield return null;
            }

            var phases = new SampleGroup[PanelFrameRecorder.Phases.Length];
            for (var i = 0; i < phases.Length; i++)
            {
                phases[i] = new SampleGroup("Frame." + PanelFrameRecorder.Phases[i], SampleUnit.Millisecond);
            }
            var allocation = new SampleGroup("Frame.GC", SampleUnit.Undefined);
            var frame = new PanelFrameRecorder(PanelName);

            using (new TargetFrameRateScope(FramesPerSecond))
            {
                frame.Start();
                // The frame Start ran in is only partly recorded.
                yield return null;
                for (var i = 0; i < WarmupFrames + MeasuredFrames; i++)
                {
                    yield return null;
                    frame.Capture();
                    if (!frame.RanEveryPhase)
                    {
                        frame.Stop();
                        throw new InvalidOperationException($"an idle frame ran none of a phase of {PanelName}");
                    }
                    if (i < WarmupFrames)
                    {
                        continue;
                    }
                    for (var p = 0; p < phases.Length; p++)
                    {
                        Measure.Custom(phases[p], frame.Milliseconds(p));
                    }
                    Measure.Custom(allocation, frame.AllocationBlocks);
                }
                frame.Stop();
            }

            var mounted = _root.Query<VisualElement>().ToList().Count - 1;
            if (mounted != 1 + Rows * ElementsPerRow)
            {
                throw new InvalidOperationException($"expected {1 + Rows * ElementsPerRow} mounted elements, the panel's root holds {mounted}");
            }
        }

        private const string RowClass =
            "flex flex-row group gap-4 divide-y divide-gray-200 pointer-events-auto [&>*]:mt-2 "
            + "hover:bg-red-500 focus:bg-red-400 active:bg-red-300 "
            + "sm:p-2 md:p-3 lg:p-4 dark:bg-red-500 "
            + "group-hover:opacity-50 peer-hover:opacity-75 "
            + "has-[:checked]:bg-red-500 has-[:focus]:text-red-500";
        private const string GridClass = "grid grid-cols-3 gap-4";
        private const string LeafClass = "hover:text-red-500";

        private static VNode[] BuildRows(bool polling)
        {
            var rowClass = RowClass;
            var gridClass = GridClass;
            var cellClass = LeafClass;
            var tailClass = LeafClass;
            if (polling)
            {
                rowClass += " @container divide-dashed";
                gridClass += " w-[calc(50%+1px)] @sm:p-2";
                cellClass += " border border-current";
                tailClass += " shadow-lg";
            }

            var rows = new VNode[Rows];
            for (var i = 0; i < Rows; i++)
            {
                rows[i] = V.Div(className: rowClass, key: "row-" + i, children: new VNode?[]
                {
                    V.Div(gridClass,
                        V.Label(className: cellClass, text: "cell-a"),
                        V.Label(className: cellClass, text: "cell-b")),
                    V.Label(className: tailClass, text: "tail"),
                });
            }
            return rows;
        }
    }
}
