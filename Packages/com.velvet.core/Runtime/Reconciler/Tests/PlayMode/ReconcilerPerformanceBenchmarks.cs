using System;
using System.Collections;
using System.Diagnostics;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Mounts onto a live runtime panel, a <see cref="UIDocument"/>'s root, beside a floor that builds the same
    /// labels by hand onto the same panel, as <see cref="InitialMountAllocBreakdown.Bench_E_FullPipelineParity"/>
    /// builds them onto an element with no panel.
    /// <para>
    /// A case without a suffix times the build alone, and yields the frame after it outside its window. A
    /// <c>_ThroughNextFrame</c> case's window spans that frame too: the build's own time is the stopwatch's, and
    /// the frame's is what the panel recorded under the markers <see cref="PanelFrameRecorder"/> reads.
    /// Its allocation count is the main thread's over the build and the frame. Allocation is GC.Alloc sample
    /// blocks throughout.
    /// </para>
    /// <para>
    /// Each build starts on an empty panel whose last frame ran after the teardown before it. A case throws when
    /// a measured build leaves the root holding another number of elements, and a <c>_ThroughNextFrame</c> case also when
    /// the frame its window spans ran none of a phase or left a built element without a layout, so a number is
    /// not read off a window that missed the work it is named for.
    /// </para>
    /// </summary>
    [TestFixture]
    public class E2EBenchmarks
    {
        private const string PanelName = "VelvetE2EBenchmarkPanel";
        private const string TextPrefix = "e2e-item-";

        private static readonly int k_WarmupCount = BenchmarkRun.Warmup(3);
        private static readonly int k_MeasurementCount = BenchmarkRun.Measurements(10);

        private GameObject _gameObject;
        private UIDocument _uiDocument;
        private PanelSettings _panelSettings;
        private VisualElement _root;
        private Reconciler _reconciler;

        private sealed record Build(Action Run, Action Teardown);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _gameObject = new GameObject("E2EBenchmarkRoot");
            _uiDocument = _gameObject.AddComponent<UIDocument>();
            _panelSettings = TestPanelSettings.Create();
            // The panel takes its name, and the names of its profiler markers, from its settings when it is made.
            _panelSettings.name = PanelName;
            _uiDocument.panelSettings = _panelSettings;

            yield return null;

            _root = _uiDocument.rootVisualElement;
            _reconciler = new Reconciler();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _reconciler?.Dispose();
            if (_gameObject != null)
                UnityEngine.Object.Destroy(_gameObject);
            if (_panelSettings != null)
                UnityEngine.Object.Destroy(_panelSettings);
            yield return null;
        }

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its build checks hold behaviour the base already has.
        [UnityTest, Performance]
        public IEnumerator E2E_Mount_10Elements()
        {
            yield return RunBuild(Mount(10), 10);
        }

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its build checks hold behaviour the base already has.
        [UnityTest, Performance]
        public IEnumerator E2E_Mount_100Elements()
        {
            yield return RunBuild(Mount(100), 100);
        }

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its build checks hold behaviour the base already has.
        [UnityTest, Performance]
        public IEnumerator E2E_Mount_1000Elements()
        {
            yield return RunBuild(Mount(1000), 1000);
        }

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its build checks hold behaviour the base already has.
        [UnityTest, Performance]
        public IEnumerator E2E_Mount_1000Elements_ThroughNextFrame()
        {
            yield return RunThroughNextFrame(Mount(1000), 1000);
        }

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its build checks hold behaviour the base already has.
        [UnityTest, Performance]
        public IEnumerator E2E_Floor_1000Labels()
        {
            yield return RunBuild(Floor(1000), 1000);
        }

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its build checks hold behaviour the base already has.
        [UnityTest, Performance]
        public IEnumerator E2E_Floor_1000Labels_ThroughNextFrame()
        {
            yield return RunThroughNextFrame(Floor(1000), 1000);
        }

        private Build Mount(int count)
        {
            var nodes = BenchmarkHelpers.BuildLabelNodes(count, prefix: TextPrefix);
            return new Build(
                () => _reconciler.Reconcile(_root, Array.Empty<VNode>(), nodes),
                () => _reconciler.Reconcile(_root, nodes, Array.Empty<VNode>()));
        }

        // The texts BuildLabelNodes gives the mounted labels, made before any window opens.
        private Build Floor(int count)
        {
            var texts = new string[count];
            for (var i = 0; i < count; i++)
            {
                texts[i] = $"{TextPrefix}{i}";
            }
            return new Build(
                () =>
                {
                    for (var i = 0; i < texts.Length; i++)
                    {
                        var label = new Label();
                        label.text = texts[i];
                        _root.Add(label);
                    }
                },
                () => _root.Clear());
        }

        private IEnumerator RunBuild(Build build, int count)
        {
            var time = new SampleGroup("Build.Time", SampleUnit.Millisecond);
            var allocation = new SampleGroup("Build.GC", SampleUnit.Undefined);

            for (var i = 0; i < k_WarmupCount; i++)
            {
                yield return Cycle(build);
            }

            for (var i = 0; i < k_MeasurementCount; i++)
            {
                System.GC.Collect();
                var cost = WindowCost.Of(build.Run);
                Measure.Custom(time, cost.Milliseconds);
                Measure.Custom(allocation, cost.AllocationBlocks);
                RequireBuilt(count);
                yield return null;
                build.Teardown();
                yield return null;
            }

            yield return TrailingCycle(build);
        }

        private IEnumerator RunThroughNextFrame(Build build, int count)
        {
            var time = new SampleGroup("Build.Time", SampleUnit.Millisecond);
            var phases = new SampleGroup[PanelFrameRecorder.Phases.Length];
            for (var i = 0; i < phases.Length; i++)
            {
                phases[i] = new SampleGroup("Frame." + PanelFrameRecorder.Phases[i], SampleUnit.Millisecond);
            }
            var allocation = new SampleGroup("Window.GC", SampleUnit.Undefined);
            var frame = new PanelFrameRecorder(PanelName);

            for (var i = 0; i < k_WarmupCount; i++)
            {
                yield return Cycle(build);
            }

            for (var i = 0; i < k_MeasurementCount; i++)
            {
                System.GC.Collect();
                frame.Start();
                var started = Stopwatch.GetTimestamp();
                build.Run();
                var buildMs = WindowCost.ElapsedMilliseconds(started, Stopwatch.GetTimestamp());
                yield return null;
                frame.Capture();
                frame.Stop();

                if (!frame.RanEveryPhase)
                {
                    throw new InvalidOperationException($"the frame after the build ran none of a phase of {PanelName}");
                }
                RequireLaidOut(count);

                Measure.Custom(time, buildMs);
                for (var p = 0; p < phases.Length; p++)
                {
                    Measure.Custom(phases[p], frame.Milliseconds(p));
                }
                Measure.Custom(allocation, frame.AllocationBlocks);

                build.Teardown();
                yield return null;
            }

            yield return TrailingCycle(build);
        }

        private static IEnumerator Cycle(Build build)
        {
            build.Run();
            yield return null;
            build.Teardown();
            yield return null;
        }

        // One more build after the loops, whatever the counts: with BenchmarkRun's single iteration the measured
        // build is the first, so a mount renting the labels an earlier unmount pooled would go unexercised.
        private static IEnumerator TrailingCycle(Build build)
        {
            build.Run();
            yield return null;
            build.Teardown();
        }

        private void RequireBuilt(int count)
        {
            if (_root.childCount != count)
            {
                throw new InvalidOperationException($"expected {count} built elements, the panel's root holds {_root.childCount}");
            }
        }

        private void RequireLaidOut(int count)
        {
            RequireBuilt(count);
            if (float.IsNaN(_root[0].layout.width) || float.IsNaN(_root[count - 1].layout.width))
            {
                throw new InvalidOperationException("a built element has no layout after the frame that followed the build");
            }
        }
    }

    [TestFixture]
    public class ReconcilerBenchmarks
    {
        private Reconciler _reconciler;
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _reconciler = new Reconciler();
            _root = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            _reconciler.Dispose();
        }

        #region B-1-a: Initial mount (long-lived Reconciler)

        [Test, Performance]
        public void InitialMount_10Elements() => RunInitialMountBenchmark(10, 10);

        [Test, Performance]
        public void InitialMount_100Elements() => RunInitialMountBenchmark(100, 10);

        [Test, Performance]
        public void InitialMount_1000Elements() => RunInitialMountBenchmark(1000, 10);

        #endregion

        #region B-1-b: Re-reconcile — no changes

        // GREEN_ON_BASE(refactor): a benchmark asserts nothing about its numbers, so it is green wherever its body completes.
        // This change takes its counts from BenchmarkRun, and one iteration still runs the whole body.
        [Test, Performance]
        public void Reconcile_NoChange_100Elements()
        {
            var nodes = BenchmarkHelpers.BuildLabelNodes(100);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), nodes);

            Measure.Method(() =>
            {
                _reconciler.Reconcile(_root, nodes, nodes);
            })
            .GC()
            .WarmupCount(BenchmarkRun.Warmup(20))
            .MeasurementCount(BenchmarkRun.Measurements(20))
            .Run();
        }

        #endregion

        #region B-1-c: Re-reconcile — text change for all elements

        // GREEN_ON_BASE(refactor): a benchmark asserts nothing about its numbers, so it is green wherever its body completes.
        // This change takes its counts from BenchmarkRun, and one iteration still runs the whole body.
        [Test, Performance]
        public void Reconcile_AllChange_100Elements()
        {
            var oldNodes = BenchmarkHelpers.BuildLabelNodes(100, prefix: "old-");
            var newNodes = BenchmarkHelpers.BuildLabelNodes(100, prefix: "new-");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldNodes);

            Measure.Method(() =>
            {
                _reconciler.Reconcile(_root, oldNodes, newNodes);
            })
            .GC()
            .WarmupCount(BenchmarkRun.Warmup(5))
            .MeasurementCount(BenchmarkRun.Measurements(20))
            .Run();
        }

        #endregion

        #region B-1-c-2: Re-reconcile — inline-expansion path (Provider / Fragment / Component)

        // Pins the general (inline-expansion) walk, which the Label-only benchmarks above never reach:
        // a flat host-leaf array routes to the Indexed/Keyed fast path, so the expansion walk early-outs
        // before its first recursion. The allocation column is the load-bearing one — the walk's own
        // per-call state is pooled, so a steady-state re-reconcile must not allocate for it.
        // GREEN_ON_BASE(refactor): a benchmark asserts nothing about its numbers, so it is green wherever its body completes.
        // This change takes its counts from BenchmarkRun, and one iteration still runs the whole body.
        [Test, Performance]
        public void Reconcile_Expansion_NoChange_100Nodes()
        {
            var nodes = BenchmarkHelpers.BuildExpansionNodes(100);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), nodes);

            Measure.Method(() =>
            {
                _reconciler.Reconcile(_root, nodes, nodes);
            })
            .GC()
            .WarmupCount(BenchmarkRun.Warmup(5))
            .MeasurementCount(BenchmarkRun.Measurements(20))
            .Run();
        }

        // Same walk with every emitted leaf changing, so each recursion also runs the commit
        // (CommitLeaf -> PatchNode) rather than only the structural descent.
        // GREEN_ON_BASE(refactor): a benchmark asserts nothing about its numbers, so it is green wherever its body completes.
        // This change takes its counts from BenchmarkRun, and one iteration still runs the whole body.
        [Test, Performance]
        public void Reconcile_Expansion_AllChange_100Nodes()
        {
            var oldNodes = BenchmarkHelpers.BuildExpansionNodes(100, prefix: "old-");
            var newNodes = BenchmarkHelpers.BuildExpansionNodes(100, prefix: "new-");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldNodes);

            Measure.Method(() =>
            {
                _reconciler.Reconcile(_root, oldNodes, newNodes);
                _reconciler.Reconcile(_root, newNodes, oldNodes);
            })
            .GC()
            .WarmupCount(BenchmarkRun.Warmup(5))
            .MeasurementCount(BenchmarkRun.Measurements(20))
            .Run();
        }

        #endregion

        #region B-1-c-3: Time-sliced indexed diff, driven to completion through the resume entry

        // Every other benchmark passes a zero frame budget, which routes the indexed diff straight
        // through its non-budgeted path and never parks state. These two force a yield per element so
        // the parked-state save/restore and the ContinueIndexed resume entry are what gets measured.
        // GREEN_ON_BASE(refactor): a benchmark asserts nothing about its numbers, so it is green wherever its body completes.
        // This change takes its counts from BenchmarkRun, and one iteration still runs the whole body.
        [Test, Performance]
        public void Reconcile_BudgetedIndexed_AllChange_200Elements()
        {
            var oldNodes = BenchmarkHelpers.BuildLabelNodes(200, prefix: "old-");
            var newNodes = BenchmarkHelpers.BuildLabelNodes(200, prefix: "new-");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldNodes);

            Measure.Method(() =>
            {
                ReconcileToCompletionUnderBudget(oldNodes, newNodes);
                ReconcileToCompletionUnderBudget(newNodes, oldNodes);
            })
            .GC()
            .WarmupCount(BenchmarkRun.Warmup(5))
            .MeasurementCount(BenchmarkRun.Measurements(20))
            .Run();
        }

        // Grow then shrink, so the Remove and Add phases are entered through both the phase fall-through
        // and the resume entry — the Common phase alone would leave two thirds of the machine unmeasured.
        // GREEN_ON_BASE(refactor): a benchmark asserts nothing about its numbers, so it is green wherever its body completes.
        // This change takes its counts from BenchmarkRun, and one iteration still runs the whole body.
        [Test, Performance]
        public void Reconcile_BudgetedIndexed_GrowShrink_100To200Elements()
        {
            var smallNodes = BenchmarkHelpers.BuildLabelNodes(100);
            var largeNodes = BenchmarkHelpers.BuildLabelNodes(200);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), smallNodes);

            Measure.Method(() =>
            {
                ReconcileToCompletionUnderBudget(smallNodes, largeNodes);
                ReconcileToCompletionUnderBudget(largeNodes, smallNodes);
            })
            .GC()
            .WarmupCount(BenchmarkRun.Warmup(5))
            .MeasurementCount(BenchmarkRun.Measurements(20))
            .Run();
        }

        #endregion

        #region B-1-c-4: Keyed diff

        // The keyed diff, which the Label-only benchmarks above never reach: their nodes carry no key, so
        // the indexed diff takes them. NoChange passes every slot through the linear prefix without a
        // patch, AllChange patches every slot there, and Reverse leaves the prefix at once and resolves
        // every slot through the old-key map.

        // GREEN_ON_BASE(characterization): a benchmark records what the keyed diff costs rather than asserting This change takes its counts from BenchmarkRun.
        // on it, and a run on the base is the arm the branch's run is compared against.
        [Test, Performance]
        public void Reconcile_Keyed_NoChange_100Elements()
        {
            var nodes = BenchmarkHelpers.BuildKeyedLabelNodes(100);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), nodes);

            Measure.Method(() =>
            {
                _reconciler.Reconcile(_root, nodes, nodes);
            })
            .GC()
            .WarmupCount(BenchmarkRun.Warmup(20))
            .MeasurementCount(BenchmarkRun.Measurements(20))
            .Run();
        }

        // GREEN_ON_BASE(characterization): a benchmark records what the keyed diff costs rather than asserting This change takes its counts from BenchmarkRun.
        // on it, and a run on the base is the arm the branch's run is compared against.
        [Test, Performance]
        public void Reconcile_Keyed_AllChange_100Elements()
        {
            var oldNodes = BenchmarkHelpers.BuildKeyedLabelNodes(100, prefix: "old-");
            var newNodes = BenchmarkHelpers.BuildKeyedLabelNodes(100, prefix: "new-");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldNodes);

            Measure.Method(() =>
            {
                _reconciler.Reconcile(_root, oldNodes, newNodes);
                _reconciler.Reconcile(_root, newNodes, oldNodes);
            })
            .GC()
            .WarmupCount(BenchmarkRun.Warmup(5))
            .MeasurementCount(BenchmarkRun.Measurements(20))
            .Run();
        }

        // GREEN_ON_BASE(characterization): a benchmark records what the keyed diff costs rather than asserting This change takes its counts from BenchmarkRun.
        // on it, and a run on the base is the arm the branch's run is compared against.
        [Test, Performance]
        public void Reconcile_Keyed_Reverse_100Elements()
        {
            var forward = BenchmarkHelpers.BuildKeyedLabelNodes(100);
            var reversed = (VNode[])forward.Clone();
            Array.Reverse(reversed);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), forward);

            Measure.Method(() =>
            {
                _reconciler.Reconcile(_root, forward, reversed);
                _reconciler.Reconcile(_root, reversed, forward);
            })
            .GC()
            .WarmupCount(BenchmarkRun.Warmup(5))
            .MeasurementCount(BenchmarkRun.Measurements(20))
            .Run();
        }

        // The keyed counterpart of the budgeted indexed pair above, so the parked keyed state and its
        // resume entry are what gets measured.

        // GREEN_ON_BASE(characterization): a benchmark records what the keyed diff costs rather than asserting This change takes its counts from BenchmarkRun.
        // on it, and a run on the base is the arm the branch's run is compared against.
        [Test, Performance]
        public void Reconcile_BudgetedKeyed_AllChange_200Elements()
        {
            var oldNodes = BenchmarkHelpers.BuildKeyedLabelNodes(200, prefix: "old-");
            var newNodes = BenchmarkHelpers.BuildKeyedLabelNodes(200, prefix: "new-");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldNodes);

            Measure.Method(() =>
            {
                ReconcileToCompletionUnderBudget(oldNodes, newNodes);
                ReconcileToCompletionUnderBudget(newNodes, oldNodes);
            })
            .GC()
            .WarmupCount(BenchmarkRun.Warmup(5))
            .MeasurementCount(BenchmarkRun.Measurements(20))
            .Run();
        }

        #endregion

        #region B-1-d: Pooled-widget recycle (mount -> unmount -> remount)

        // Pins the primitive-element pool's recycle path: VNodePool.ReturnLabel / ReturnButton (invoked
        // when RemoveElement reclaims an unmounted Label/Button) run
        // FiberLabelPoolHelper/FiberButtonPoolHelper's ResetXForReuse, which calls
        // FiberElementPoolReset.ResetClassListAndCommon on every single recycle — an unintended per-recycle
        // heap allocation on that path is what this measurement exists to catch. The Button half carries an
        // intended one: FiberButtonPoolHelper states what the fresh Clickable buys and what it costs.
        // Held at 10 of each (well under
        // either pool's own 32-instance cap) so every element genuinely round-trips through
        // Rent/Return across the measured region instead of being dropped uncounted by a full pool.
        // GREEN_ON_BASE(refactor): a benchmark asserts nothing about its numbers, so it is green wherever its body completes.
        // This change takes its counts from BenchmarkRun, and one iteration still runs the whole body.
        [Test, Performance]
        public void PooledWidgetRecycle_LabelsAndButtons_10Elements()
        {
            var nodes = BenchmarkHelpers.BuildLabelAndButtonNodes(10);
            var empty = Array.Empty<VNode>();

            // Warms both pools first so the measured region only ever rents/returns already-pooled
            // instances, not the `new Label()` / `new Button()` construction cost of filling them.
            _reconciler.Reconcile(_root, empty, nodes);
            _reconciler.Reconcile(_root, nodes, empty);

            Measure.Method(() =>
            {
                _reconciler.Reconcile(_root, empty, nodes);
                _reconciler.Reconcile(_root, nodes, empty);
            })
            .GC()
            .WarmupCount(BenchmarkRun.Warmup(5))
            .MeasurementCount(BenchmarkRun.Measurements(20))
            .Run();
        }

        #endregion

        #region Helpers

        // The smallest positive budget: every element overruns it, so the diff yields after each one and
        // the whole list commits through ContinueReconcile. A realistic multi-millisecond budget would
        // finish a 200-element list in a single slice and measure the straight-line path instead.
        private const double YieldPerElementBudgetMs = 1e-6;

        private void ReconcileToCompletionUnderBudget(VNode[] oldNodes, VNode[] newNodes)
        {
            _reconciler.Reconcile(_root, oldNodes, newNodes, YieldPerElementBudgetMs);
            while (_reconciler.HasPendingWork)
            {
                _reconciler.ContinueReconcile(YieldPerElementBudgetMs);
            }
        }

        /// <summary>
        /// Initial mount on the long-lived Reconciler path. Production reuses one Reconciler per Page/Component,
        /// so this is the realistic metric. Reconciler ctor/Dispose is hoisted out of the measured region
        /// (via <c>[SetUp]</c>/<c>[TearDown]</c>) so the numbers reflect mount cost only. See
        /// <see cref="InitialMountAllocBreakdown"/> for the detailed breakdown.
        /// </summary>
        private void RunInitialMountBenchmark(int count, int measurements, int warmup = 3)
        {
            var nodes = BenchmarkHelpers.BuildLabelNodes(count);

            Measure.Method(() =>
            {
                var root = new VisualElement();
                _reconciler.Reconcile(root, Array.Empty<VNode>(), nodes);
            })
            .GC()
            .WarmupCount(BenchmarkRun.Warmup(warmup))
            .MeasurementCount(BenchmarkRun.Measurements(measurements))
            .Run();
        }

        #endregion
    }

    /// <summary>
    /// An initial mount of 1,000 labels onto an element with no panel, beside the same build written by hand.
    /// <para>
    /// A constructs each label, B and C also give it a text (through the constructor, and through the setter), D
    /// attaches it without one, and E does all three: the tree F and G mount through a <see cref="Reconciler"/>.
    /// G mounts through one that lives across iterations, as production reuses one, so what G records above E is
    /// Velvet's share of the mount; F constructs and disposes a Reconciler inside each window as well.
    /// <see cref="E2EBenchmarks"/> runs E's loop onto a live panel.
    /// </para>
    /// </summary>
    [TestFixture]
    public class InitialMountAllocBreakdown
    {
        private const int Count = 1000;
        private static readonly int WarmUp = BenchmarkRun.Warmup(3);
        private static readonly int Measurements = BenchmarkRun.Measurements(10);

        [Test, Performance]
        public void Bench_A_NewLabelAlone()
        {
            Measure.Method(() =>
            {
                for (var i = 0; i < Count; i++)
                {
                    _ = new Label();
                }
            })
                .GC()
                .WarmupCount(WarmUp)
                .MeasurementCount(Measurements)
                .Run();
        }

        [Test, Performance]
        public void Bench_B_NewLabelWithText()
        {
            Measure.Method(() =>
            {
                for (var i = 0; i < Count; i++)
                {
                    _ = new Label("item");
                }
            })
                .GC()
                .WarmupCount(WarmUp)
                .MeasurementCount(Measurements)
                .Run();
        }

        [Test, Performance]
        public void Bench_C_NewLabelAndSetText()
        {
            Measure.Method(() =>
            {
                for (var i = 0; i < Count; i++)
                {
                    var l = new Label();
                    l.text = "item";
                }
            })
                .GC()
                .WarmupCount(WarmUp)
                .MeasurementCount(Measurements)
                .Run();
        }

        [Test, Performance]
        public void Bench_D_NewLabelAndAddToRoot()
        {
            Measure.Method(() =>
            {
                var root = new VisualElement();
                for (var i = 0; i < Count; i++)
                {
                    var l = new Label();
                    root.Add(l);
                }
            })
                .GC()
                .WarmupCount(WarmUp)
                .MeasurementCount(Measurements)
                .Run();
        }

        [Test, Performance]
        public void Bench_E_FullPipelineParity()
        {
            Measure.Method(() =>
            {
                var root = new VisualElement();
                for (var i = 0; i < Count; i++)
                {
                    var l = new Label();
                    l.text = "item";
                    root.Add(l);
                }
            })
                .GC()
                .WarmupCount(WarmUp)
                .MeasurementCount(Measurements)
                .Run();
        }

        [Test, Performance]
        public void Bench_F_VelvetReconcile()
        {
            var nodes = BuildLabelVNodes();

            Measure.Method(() =>
            {
                var r = new Reconciler();
                var root = new VisualElement();
                r.Reconcile(root, Array.Empty<VNode>(), nodes);
                r.Dispose();
            })
                .GC()
                .WarmupCount(WarmUp)
                .MeasurementCount(Measurements)
                .Run();
        }

        [Test, Performance]
        public void Bench_G_VelvetReconcileLongLived()
        {
            var nodes = BuildLabelVNodes();

            var reconciler = new Reconciler();
            try
            {
                Measure.Method(() =>
                {
                    var root = new VisualElement();
                    reconciler.Reconcile(root, Array.Empty<VNode>(), nodes);
                })
                    .GC()
                    .WarmupCount(WarmUp)
                    .MeasurementCount(Measurements)
                    .Run();
            }
            finally
            {
                reconciler.Dispose();
            }
        }

        private static VNode[] BuildLabelVNodes()
        {
            var nodes = new VNode[Count];
            for (var i = 0; i < Count; i++)
            {
                nodes[i] = V.Label(text: "item");
            }
            return nodes;
        }
    }
}
