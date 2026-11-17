using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that the components one wave renders — one drain, or one flush, mount or resumed slice a door runs — all
    /// render before any of their host writes, deletion cleanups or hook commits apply, as React renders every root an
    /// update reaches before it commits: a later component reads what an earlier one's deletion cleanup or patch had not
    /// yet changed, and a component rendered twice in the wave compares its effect dependencies against what the commit
    /// before the wave left. The guards below hold what the single pass has to keep: a component a deletion earlier in
    /// the wave takes does not render, a suspend nothing catches gives up only the renders of the component it reached, a
    /// Portal whose child suspends still hands that suspend to the component that queued it, a VirtualList's rows render
    /// into the range rather than a wave of their own, a flush the wave owes renders without draining the tier inside
    /// the wave, and an exception out of a commit leaves the next wave free to open.
    /// </summary>
    [TestFixture]
    internal sealed class OneCommitPerWaveTests
    {
        private static int s_written;
        private static Label s_label;
        private static readonly Action s_noCleanup = static () => { };
        private static readonly Func<VisualElement, Action> s_holdLabel = element =>
        {
            s_label = (Label)element;
            return s_noCleanup;
        };

        private static TransitionStarter s_start;
        private static TransitionStarter s_otherStart;
        private static Action<string> s_add;
        private static StateUpdater<int> s_setDependency;
        private static StateUpdater<int> s_setOther;
        private static StateUpdater<int> s_setRows;
        private static StateUpdater<int> s_setQueued;
        private static ComponentFiber s_holder;
        private static ComponentFiber s_rows;
        private static readonly List<string> s_queuedCommits = new();
        private static readonly List<string> s_rowLog = new();
        private static VisualElement s_portalTarget;
        private static VelvetTaskCompletionSource<string> s_read;
        private static readonly Ref<string> s_handle = new();
        private static int s_handleThrowsLeft;
        private static StateUpdater<int> s_setHandleTick;
        private static ComponentFiber s_handleOwner;

        [SetUp]
        public void SetUp()
        {
            AsyncActionsInFlightTestAccess.ResetForTest();
            s_written = 0;
            s_label = null;
            s_start = default;
            s_otherStart = default;
            s_add = null;
            s_setDependency = default;
            s_setOther = default;
            s_setRows = default;
            s_setQueued = default;
            s_holder = null;
            s_rows = null;
            s_queuedCommits.Clear();
            s_rowLog.Clear();
            s_portalTarget = new VisualElement();
            s_read = new VelvetTaskCompletionSource<string>();
            s_handle.Set(null);
            s_handleThrowsLeft = 0;
            s_setHandleTick = default;
            s_handleOwner = null;
        }

        [TearDown]
        public void TearDown() => AsyncActionsInFlightTestAccess.ResetForTest();

        #region A later component reads what the wave has not applied

        [Component]
        private static VNode WritesInItsCleanup()
        {
            Hooks.UseLayoutEffect(() => () => s_written = 1, Array.Empty<object>());
            return V.Div();
        }

        [Component]
        private static VNode Deleter()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Div(children: new VNode[] { shown ? V.Component(WritesInItsCleanup, key: "deleted") : null });
        }

        [Component]
        private static VNode CleanupReader()
        {
            EventLog.UseState("Read", 0);
            EventLog.UseRender("Reader", s_written);
            return V.Div();
        }

        [Component]
        private static VNode DeleterThenReader()
            => V.Fragment(V.Component(Deleter, key: "deleter"), V.Component(CleanupReader, key: "reader"));

        [Test]
        public void Given_AComponentThatDeletesOneWhoseLayoutCleanupWritesAValue_When_OneDrainRendersItAndALaterComponentReadingTheValue_Then_TheLaterComponentReadsTheValueFromBeforeTheCleanup()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(DeleterThenReader)));
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            log.Setter<int>("Read").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:Reader=0"));
        }

        [Component]
        private static VNode Patcher()
        {
            var (text, _) = EventLog.UseState("Text", "before");
            return V.Label(text: text, refCallback: s_holdLabel);
        }

        [Component]
        private static VNode LabelReader()
        {
            EventLog.UseState("ReadLabel", 0);
            EventLog.UseRender("LabelReader", s_label?.text);
            return V.Div();
        }

        [Component]
        private static VNode PatcherThenReader()
            => V.Fragment(V.Component(Patcher, key: "patcher"), V.Component(LabelReader, key: "reader"));

        [Test]
        public void Given_AComponentThatPatchesALabel_When_OneDrainRendersItAndALaterComponentReadingTheLabelThroughARef_Then_TheLaterComponentReadsTheTextFromBeforeThePatch()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(PatcherThenReader)));
            log.Clear();

            // Act
            log.Setter<string>("Text").Invoke("after");
            log.Setter<int>("ReadLabel").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:LabelReader=before"));
        }

        #endregion

        #region A component the wave renders twice

        // Declares the transition apart from the holder, so the render that takes the indicator down is not the
        // holder's.
        [Component(Compiler = false)]
        private static VNode Starter()
        {
            var (_, start) = Hooks.UseTransition();
            s_start = start;
            return V.Label(text: "starter");
        }

        // Writes its dependency inside the transition and holds an entry that transition owns, so the drain that lands
        // the transition settles it in this component's own flush and owes this component the render that takes the
        // entry down.
        [Component(Compiler = false)]
        private static VNode DependencyHolder()
        {
            var log = EventLog.UseLog();
            var (dependency, setDependency) = Hooks.UseState(0);
            var (shown, add) = Hooks.UseOptimistic<string, string>("base", (current, action) => current + "+" + action);
            s_setDependency = setDependency;
            s_add = add;
            Hooks.UseLayoutEffect(() =>
            {
                log.Add("lay:H");
                return () => log.Add("lay-x:H");
            }, new object[] { dependency });
            return V.Label(text: shown);
        }

        [Component(Compiler = false)]
        private static VNode StarterAndHolder()
            => V.Div(children: new VNode[]
            {
                V.Component(Starter, key: "starter"),
                V.Component(DependencyHolder, key: "holder"),
            });

        [Test]
        public void Given_AComponentWhoseTransitionChangesItsLayoutEffectsDependencyAndOwesItASettleRender_When_TheTransitionDrains_Then_ItsLayoutEffectRunsAgain()
        {
            // Arrange — the immediate drain renders the entry and leaves the dependency's update on the Transition lane
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(StarterAndHolder)));
            s_start.Invoke(() =>
            {
                s_add.Invoke("sent");
                s_setDependency.Invoke(1);
            });
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("lay-x:H lay:H"));
        }

        #endregion

        #region What the single pass keeps

        [Component]
        private static VNode DeletedChild()
        {
            EventLog.UseState("Child", 0);
            EventLog.UseRender("Child");
            return V.Div();
        }

        [Component]
        private static VNode ParentOfADeletedChild()
        {
            var (shown, _) = EventLog.UseState("ShownChild", true);
            EventLog.UseRender("Parent");
            return V.Div(children: new VNode[] { shown ? V.Component(DeletedChild, key: "child") : null });
        }

        // GREEN_ON_BASE(characterization): the base unmounts the child before the drain reaches its flush.
        // Deleting `if (fiber.IsBeingDeleted) return;` from FiberWorkLoop.FlushFiber reddens it.
        [Test]
        public void Given_AParentThatDeletesAChildWithAnUpdateOfItsOwn_When_OneDrainRendersBoth_Then_TheChildDoesNotRender()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(ParentOfADeletedChild)));
            log.Clear();

            // Act
            log.Setter<int>("Child").Invoke(1);
            log.Setter<bool>("ShownChild").Invoke(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:Parent"));
        }

        [Component]
        private static VNode Committer()
        {
            var (value, _) = EventLog.UseState("Committed", 0);
            EventLog.UseInsertionAndLayoutEffects("C", new object[] { value });
            return V.Div();
        }

        [Component(Compiler = false)]
        private static VNode SuspendsWithNothingAbove()
        {
            var (reads, _) = EventLog.UseState("Reads", false);
            Hooks.Use(() => reads ? s_read.Task : VelvetTask.FromResult("read"), reads);
            return V.Div();
        }

        [Component]
        private static VNode CommitterThenSuspender()
            => V.Fragment(V.Component(Committer, key: "committer"), V.Component(SuspendsWithNothingAbove, key: "suspender"));

        // GREEN_ON_BASE(characterization): the base closes the first component's journal before the second renders.
        // Taking FiberPassJournal.Abandon back to the start of the journal, rather than of the open segment, reddens it.
        [Test]
        public void Given_AComponentThatCommitsAndALaterOneThatSuspendsWithNothingToCatchIt_When_OneDrainRendersBoth_Then_TheFirstComponentsEffectsStillRun()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(CommitterThenSuspender)));
            log.Clear();

            // Act
            log.Setter<int>("Committed").Invoke(1);
            log.Setter<bool>("Reads").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:C ins:C lay-x:C lay:C"));
        }

        [Component]
        private static VNode ThrowsWhenTold(bool throws)
        {
            if (throws) throw new InvalidOperationException("told");
            return V.Div();
        }

        [Component(IsErrorBoundary = true, DisplayName = "Own")]
        private static VNode CatchesItsOwnRows()
        {
            var (throws, _) = EventLog.UseState("Throws", false);
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Component(ThrowsWhenTold, throws, key: "thrower");
        }

        [Component]
        private static VNode Bystander()
        {
            EventLog.UseState("Bystander", 0);
            EventLog.UseRender("Bystander");
            return V.Div();
        }

        [Component]
        private static VNode BystanderThenOwnCatch()
            => V.Fragment(V.Component(Bystander, key: "bystander"), V.Component(CatchesItsOwnRows, key: "own"));

        // GREEN_ON_BASE(characterization): the base reports the catch a boundary's own render makes.
        // Making Reconciler.BeginOrJoinPass call ChildListWrites.BeginRecording whatever the wave records reddens it,
        // since the fallback's reconcile is a further top-level reconcile of the wave.
        [Test]
        public void Given_ABoundaryWhoseOwnRenderCatchesItsChildsError_When_OneDrainRendersItAfterAnotherComponent_Then_ItReportsTheCatch()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(BystanderThenOwnCatch)),
                log.ReportingErrors());
            log.Clear();

            // Act
            log.Setter<int>("Bystander").Invoke(1);
            log.Setter<bool>("Throws").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:Bystander caught:Own:told"));
        }

        [Component(Compiler = false)]
        private static VNode PortalReader()
        {
            var text = Hooks.Use(_ => s_read.Task);
            EventLog.UseRender("Reader", text);
            return V.Label(text: text);
        }

        [Component]
        private static VNode PortalHost()
        {
            var (shown, _) = EventLog.UseState("ShownPortal", false);
            return V.Div(children: new VNode[]
            {
                shown ? V.Portal(s_portalTarget, new VNode[] { V.Component(PortalReader, key: "reader") }, key: "portal") : null,
            });
        }

        [Component]
        private static VNode BystanderThenPortalHost()
            => V.Fragment(V.Component(Bystander, key: "bystander"), V.Component(PortalHost, key: "host"));

        // GREEN_ON_BASE(characterization): the base retries the component whose pass a Portal child's suspend reached.
        // Passing a null closingRoot to FinishPass from Reconciler.FinishTopLevelPass, which drops that suspend, reddens
        // it: nothing then asks for the retry that mounts the child once the read resolves.
        [Test]
        public void Given_AComponentAndALaterOneWhosePortalsChildSuspendsWithNothingToCatchIt_When_TheReadResolves_Then_ThePortalsChildRenders()
        {
            // Arrange — the later component's Portal mounts in the wave's drain, where its child suspends
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(BystanderThenPortalHost)));
            log.Setter<int>("Bystander").Invoke(1);
            log.Setter<bool>("ShownPortal").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            s_read.TrySetResult("ready");
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:Reader=ready"));
        }

        [Component]
        private static VNode GrowingSibling()
        {
            var (rows, _) = EventLog.UseState("Rows", 1);
            var labels = new VNode[rows];
            for (var i = 0; i < rows; i++) labels[i] = V.Label(text: "a" + i);
            return V.Fragment(labels);
        }

        [Component]
        private static VNode RetextedSibling()
        {
            var (text, _) = EventLog.UseState("Retext", "b");
            return V.Label(text: text);
        }

        [Component]
        private static VNode TwoInlineSiblings()
            => V.Div(name: "siblings", children: new VNode[]
            {
                V.Component(GrowingSibling, key: "growing"),
                V.Component(RetextedSibling, key: "retexted"),
            });

        // GREEN_ON_BASE(characterization): the base places each sibling's rows in a pass of its own.
        // Making Reconciler.BeginOrJoinPass call ChildListWrites.BeginRecording whatever the wave records reddens it.
        [Test]
        public void Given_TwoInlineSiblingsInOneElement_When_OneDrainRendersBothAndTheFirstAddsARow_Then_TheElementHoldsBothOutputsInOrder()
        {
            // Arrange
            var log = new EventLog();
            var root = new VisualElement();
            using var mounted = V.Mount(root, log.Provide(V.Component(TwoInlineSiblings)));

            // Act
            log.Setter<int>("Rows").Invoke(2);
            log.Setter<string>("Retext").Invoke("b2");
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(root.Q<VisualElement>("siblings").DisplayedLabelTexts("|"), Is.EqualTo("a0|a1|b2"));
        }

        #endregion

        #region Work a wave runs inside another's

        [Component]
        private static VNode PortalRow(string id)
        {
            s_rowLog.Add("render:" + id);
            return id == "a"
                ? V.Fragment(V.Label(text: id),
                    V.Portal(s_portalTarget, new VNode[] { V.Component(PortalRowChild, key: "child") }, key: "portal"))
                : V.Label(text: id);
        }

        [Component]
        private static VNode PortalRowChild()
        {
            s_rowLog.Add("render:portal-child");
            return V.Label(text: "child");
        }

        // GREEN_ON_BASE(characterization): the base mounts a row's Portal where the range a scroll renders ends.
        // Making FiberRenderer.MountWork pass opensWave: true reddens it, through the assertion FiberWave.Open makes at
        // the depth the range's bracket has raised.
        [Test]
        public void Given_AVirtualListWhoseFirstRowHoldsAPortal_When_AScrollRendersBothRows_Then_ThePortalsChildRendersAfterBothRows()
        {
            // Arrange — a headless mount measures no viewport, so the list renders no row until the range update.
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.VirtualList(
                items: new[] { "a", "b" },
                keySelector: item => item,
                itemHeight: 50f,
                renderer: item => V.Component(PortalRow, item, key: item),
                overscan: 0));
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[root.Q<ScrollView>()];
            s_rowLog.Clear();

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 100f);

            // Assert
            Assert.That(string.Join(" ", s_rowLog), Is.EqualTo("render:a render:b render:portal-child"));
        }

        [Component(Compiler = false)]
        private static VNode OtherStarter()
        {
            var (_, start) = Hooks.UseTransition();
            s_otherStart = start;
            return V.Label(text: "other-starter");
        }

        // Holds an entry the first transition owns and an update of the second's.
        [Component(Compiler = false)]
        private static VNode StarvedHolder()
        {
            s_holder = FiberAmbientStack.Current;
            var (other, setOther) = Hooks.UseState(0);
            var (shown, add) = Hooks.UseOptimistic<string, string>("base", (current, action) => current + "+" + action);
            s_setOther = setOther;
            s_add = add;
            return V.Label(text: shown + ":" + other);
        }

        // A top-level Fragment of host nodes takes the reconciler's time-sliceable fast path.
        [Component(Compiler = false)]
        private static VNode SlicedRows()
        {
            s_rows = FiberAmbientStack.Current;
            var (tick, setTick) = Hooks.UseState(0);
            s_setRows = setTick;
            var rows = new VNode[9];
            for (var i = 0; i < rows.Length; i++) rows[i] = V.Label(text: $"{i}-{tick}");
            return V.Fragment(children: rows);
        }

        [Component(Compiler = false)]
        private static VNode QueuedBeforeTheResume()
        {
            var (value, setValue) = Hooks.UseState(0);
            s_setQueued = setValue;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_queuedCommits.Add("queued:" + value);
                return null;
            }), new object[] { value });
            return V.Label(text: "queued:" + value);
        }

        [Component(Compiler = false)]
        private static VNode ResumeOwesAStarvedHolder()
            => V.Div(children: new VNode[]
            {
                V.Component(Starter, key: "starter"),
                V.Component(OtherStarter, key: "other-starter"),
                V.Component(SlicedRows, key: "rows"),
                V.Component(StarvedHolder, key: "holder"),
                V.Component(QueuedBeforeTheResume, key: "queued"),
            });

        // GREEN_ON_BASE(characterization): the base commits the queued update in a drain the owed holder's flush runs.
        // Making FiberWorkLoop.FlushCompletedTransitionIndicator call FlushImmediateWork inside a wave reddens it,
        // through the assertion FiberWave.Open makes while the resume's pass still records.
        [Test]
        public void Given_AResumeWhoseSettleOwesAHolderAStarvedTransitionsRender_When_TheResumeCompletes_Then_AnUpdateQueuedBeforeItCommits()
        {
            // Arrange — the second transition's update leaves the holder a Transition lane its owed flush promotes,
            // which makes that flush drain transition work; the slice parks, and the update queued last waits on the
            // immediate tier.
            using var mounted = V.Mount(new VisualElement(), V.Component(ResumeOwesAStarvedHolder));
            s_otherStart.Invoke(() => s_setOther.Invoke(1));
            s_start.Invoke(() =>
            {
                s_add.Invoke("sent");
                s_setRows.Invoke(1);
            });
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_rows.FlushStateWithTinyBudgetForTest();
            s_holder.TransitionStarvationCounter = FiberWorkLoop.TransitionStarvationThreshold - 1;
            s_setQueued.Invoke(1);
            s_queuedCommits.Clear();

            // Act
            s_rows.DrainTimeSlicedReconcileForTest();

            // Assert
            Assert.That(string.Join(" ", s_queuedCommits), Is.EqualTo("queued:1"));
        }

        #endregion

        #region A commit that throws

        [Component(Compiler = false)]
        private static VNode HandleOwner()
        {
            s_handleOwner = FiberAmbientStack.Current;
            var log = EventLog.UseLog();
            var (tick, setTick) = Hooks.UseState(0);
            s_setHandleTick = setTick;
            Hooks.UseImperativeHandle(s_handle, () =>
            {
                if (tick == 0 || s_handleThrowsLeft == 0) return "handle";
                s_handleThrowsLeft--;
                throw new InvalidOperationException("handle");
            });
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                log.Add("lay:" + tick);
                return null;
            }), new object[] { tick });
            return V.Label(text: "owner:" + tick);
        }

        // GREEN_ON_BASE(characterization): the base commits a flush outside any wave, so its throw leaves nothing set.
        // Moving FiberEffects.FlushDeferredDrainLayoutEffects' clear of DeferDrainLayoutEffects after its commit reddens
        // it, through the assertion FiberWave.Open makes for the next flush.
        [Test]
        public void Given_AFlushWhoseImperativeHandleThrowsOutOfItsCommit_When_TheComponentFlushesAgain_Then_ItsLayoutEffectRuns()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(HandleOwner)));
            s_handleThrowsLeft = 1;
            s_setHandleTick.Invoke(1);
            try
            {
                FiberWorkLoop.FlushState(s_handleOwner);
            }
            catch (InvalidOperationException)
            {
            }
            log.Clear();

            // Act
            s_setHandleTick.Invoke(2);
            FiberWorkLoop.FlushState(s_handleOwner);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("lay:2"));
        }

        #endregion
    }
}
