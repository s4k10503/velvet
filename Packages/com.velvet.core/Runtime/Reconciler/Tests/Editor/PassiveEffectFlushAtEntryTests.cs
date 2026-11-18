using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that an entry starts by running the passive effects the roots left pending, whichever root it is for, as
    /// react-reconciler 19.1 keeps one root's passive effects pending and flushes them before it renders or commits
    /// anything (<c>performWorkOnRootViaSchedulerTask</c>, <c>performSyncWorkOnRoot</c>, and the loop <c>commitRoot</c>
    /// opens with): another root's update drain, the unmount of a root whose mount effect has not run, an error
    /// boundary's swap for a <c>UseFrame</c> callback's error, and the render a resolved read retries. What one root's
    /// flush throws is logged, and stands that root down rather than the entry whose start ran it. A root disposed
    /// renders nothing its pending effects force, nor, where its disposal is held inside an entry, a render held for it
    /// ahead of the unmount.
    /// </summary>
    [TestFixture]
    internal sealed class PassiveEffectFlushAtEntryTests
    {
        private sealed record Named(string Name);

        private sealed class SettableStore : Store<int>
        {
            public SettableStore() : base(0) { }

            public void Set(int value) => SetState(_ => value);

            protected override void ResetCore() => SetState(_ => 0);
        }

        private static VelvetTaskCompletionSource<string> s_read;
        private static SettableStore s_store;
        private static bool s_handleThrows;
        private static int s_forcedRenders;
        private static readonly Ref<string> s_handle = new();

        [SetUp]
        public void SetUp()
        {
            FiberStrictMode.Enabled = false;
            s_read = new VelvetTaskCompletionSource<string>();
            s_store = new SettableStore();
            s_handleThrows = false;
            s_forcedRenders = 0;
        }

        [TearDown]
        public void TearDown() => s_store.Dispose();

        private static MountedTree Mount(EventLog log, VNode tree) => V.Mount(new VisualElement(), log.Provide(tree));

        private static Action Record(EventLog log, string name)
        {
            log.Add("eff:" + name);
            return () => log.Add("eff-x:" + name);
        }

        // A passive effect over the component's own state, recording eff: on setup and eff-x: on cleanup.
        [Component]
        private static VNode Effectful(Named props)
        {
            var (value, _) = EventLog.UseState(props.Name, 0);
            var log = EventLog.UseLog();
            Hooks.UseEffect(() => Record(log, props.Name), new object[] { value });
            return V.Div();
        }

        [Component]
        private static VNode Rendered(Named props)
        {
            var (value, _) = EventLog.UseState(props.Name, 0);
            EventLog.UseRender(props.Name);
            return V.Label(text: props.Name + value);
        }

        // An effect update pending in its own root, A's, which a mount, a flush and a drain of that root leave there.
        private static MountedTree MountAnEffectUpdate(EventLog log)
        {
            var mounted = Mount(log, V.Component(Effectful, new Named("A"), key: "a"));
            mounted.FlushEffectsForTest();
            log.Setter<int>("A").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            return mounted;
        }

        // What the log holds ahead of entry, or null where it holds no entry.
        private static string EntriesBefore(EventLog log, string entry)
        {
            var entries = log.ToString().Split(' ');
            return entries.Contains(entry) ? string.Join(" ", entries.TakeWhile(logged => logged != entry)) : null;
        }

        [Test]
        public void Given_ARootWithAnEffectUpdatePending_When_AnotherRootsUpdateDrainRenders_Then_TheEffectRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var other = Mount(log, V.Component(Rendered, new Named("B"), key: "b"));
            using var pending = MountAnEffectUpdate(log);
            log.Setter<int>("B").Invoke(1);
            log.Clear();

            // Act
            other.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("eff-x:A eff:A render:B"));
        }

        [Test]
        public void Given_ARootWhoseMountEffectHasNotRun_When_ItIsDisposed_Then_TheEffectSetsUpBeforeItsCleanupRuns()
        {
            // Arrange
            var log = new EventLog();
            var mounted = Mount(log, V.Component(Effectful, new Named("C"), key: "c"));
            log.Clear();

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("eff:C eff-x:C"));
        }

        [Component(IsErrorBoundary = true)]
        private static VNode FrameBoundary()
        {
            Hooks.UseFallback(_ => V.Component(Fallback, key: "fallback"));
            return V.Component(ThrowsOnAFrame, key: "thrower");
        }

        [Component]
        private static VNode Fallback()
        {
            EventLog.UseRender("fallback");
            return V.Label(text: "fallback");
        }

        [Component]
        private static VNode ThrowsOnAFrame()
        {
            Hooks.UseFrame(_ => throw new InvalidOperationException("boom"));
            return V.Label(text: "thrower");
        }

        [Test]
        public void Given_ARootWithAnEffectUpdatePending_When_AFrameCallbacksErrorSwapsAnotherRootsBoundary_Then_TheEffectRunsBeforeTheFallbackRenders()
        {
            // Arrange — the frame callback takes its baseline on the panel's first pass, which throws nothing, and the
            // root with the pending effect is on no panel, so the panel's pass runs nothing of it
            var log = new EventLog();
            using var host = new HeadlessEditorPanelHost();
            UseFrameFakeClockHost.Reset();
            EditorPanelTestHelpers.SetPanelTimeFunction(host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            using var onPanel = V.Mount(host.Root, log.Provide(V.Component(FrameBoundary, key: "boundary")),
                CaughtErrors.Unlogged);
            onPanel.FlushEffectsForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(host.Panel);
            using var pending = MountAnEffectUpdate(log);
            log.Clear();

            // Act
            UseFrameFakeClockHost.Ms += 16;
            EditorPanelTestHelpers.DriveSchedulerOnce(host.Panel);

            // Assert — null where the boundary rendered no fallback
            Assert.That(EntriesBefore(log, "render:fallback"), Is.EqualTo("eff-x:A eff:A"));
        }

        [Component(Compiler = false)]
        private static VNode Reader()
        {
            EventLog.UseRender("reader");
            var value = Hooks.Use(() => s_read.Task, "read");
            return V.Label(text: value);
        }

        [Component]
        private static VNode ReaderBesideAnEffect()
            => V.Fragment(
                V.Component(Effectful, new Named("A"), key: "a"),
                V.Suspense(fallback: V.Label(text: "loading"), children: new VNode[] { V.Component(Reader, key: "reader") },
                    key: "suspense"));

        [Test]
        public void Given_AnEffectUpdatePendingBesideASuspendedRead_When_TheReadResolves_Then_TheEffectRunsBeforeTheReaderRendersAgain()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(ReaderBesideAnEffect, key: "host"));
            mounted.FlushEffectsForTest();
            log.Setter<int>("A").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            s_read.TrySetResult("read");

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("eff-x:A eff:A render:reader"));
        }

        // Hands the store a change from its mount effect. The re-render that change forces asks, from its handle, for
        // another render, and throws while s_handleThrows is set.
        [Component(Compiler = false)]
        private static VNode ThrowsOnItsForcedRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            var stored = Hooks.UseStore(s_store, value => value);
            if (stored > 0) s_forcedRenders++;
            Hooks.UseEffect(() =>
            {
                s_store.Set(1);
                return (Action)null;
            }, Array.Empty<object>());
            Hooks.UseImperativeHandle(s_handle, () =>
            {
                if (stored == 0 || !s_handleThrows) return "handle";
                setTick.Invoke(tick + 1);
                throw new InvalidOperationException("handle failed");
            });
            return V.Label(text: "thrower" + tick);
        }

        [Test]
        public void Given_ARootWhosePendingEffectForcesARenderThatThrows_When_AnotherRootMounts_Then_ThatRootRendersOnceAndTheMountReturns()
        {
            // Arrange — the root's mount effect is still pending
            using var thrower = V.Mount(new VisualElement(), V.Component(ThrowsOnItsForcedRender, key: "thrower"));
            s_handleThrows = true;
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: handle failed");

            // Act — the throwing render asks for another, which the mount's release would render again
            using var other = V.Mount(new VisualElement(), V.Label(text: "other"));
            s_handleThrows = false;

            // Assert — 2 where the release rendered it again, and the mount throws where nothing caught the first
            Assert.That(s_forcedRenders, Is.EqualTo(1));
        }

        // React's root.unmount() flushes the root's pending passive effects and then renders the unmount together
        // with the update they scheduled, so nothing of the root renders again.
        [Test]
        public void Given_ARootWhosePendingEffectForcesARender_When_ItIsDisposed_Then_ItRendersNothingBeforeItsUnmount()
        {
            // Arrange — the root's mount effect is still pending
            var mounted = V.Mount(new VisualElement(), V.Component(ThrowsOnItsForcedRender, key: "thrower"));

            // Act
            mounted.Dispose();

            // Assert — null where the effect never ran
            Assert.That(s_store.Current == 1 ? s_forcedRenders : (int?)null, Is.EqualTo(0));
        }

        private static int s_secondTreeRenders;
        private static MountedTree s_renderedThenDisposed;

        [Component]
        private static VNode SecondTree()
        {
            s_secondTreeRenders++;
            return V.Label(text: "second");
        }

        // Hands the tree s_renderedThenDisposed holds a new tree to render and then disposes it, from its mount effect.
        [Component]
        private static VNode RendersAnotherTreeThenDisposesIt()
        {
            Hooks.UseEffect(() =>
            {
                s_renderedThenDisposed.Render(V.Component(SecondTree, key: "second"));
                s_renderedThenDisposed.Dispose();
                return (Action)null;
            }, Array.Empty<object>());
            return V.Div();
        }

        // React's root.unmount() folds the unmount into the sync render that the root.render() before it asked for, so
        // the tree that render was handed never renders.
        [Test]
        public void Given_AnEffectThatRendersAnotherTreeAndThenDisposesIt_When_TheEffectRuns_Then_TheTreeItWasHandedNeverRenders()
        {
            // Arrange
            s_secondTreeRenders = 0;
            var target = V.Mount(new VisualElement(), V.Label(text: "first"));
            s_renderedThenDisposed = target;
            using var withTheEffect = V.Mount(new VisualElement(), V.Component(RendersAnotherTreeThenDisposesIt, key: "e"));

            // Act
            withTheEffect.FlushEffectsForTest();

            // Assert — null where the tree was not disposed
            Assert.That(target.Root.IsDisposed ? s_secondTreeRenders : (int?)null, Is.EqualTo(0));
        }

        [Test]
        public void Given_APassRecording_When_AnEntryStarts_Then_ItAssertsThatItsFlushRunsInsideThePass()
        {
            // Arrange — an entry first takes whatever earlier cases left pending, so the one below flushes nothing
            FiberExecutionContextTestAccess.RunAsEntryForTest(() => { });
            using var scope = new ReconcilerScope();
            var pass = scope.Reconciler.Context.Pass;
            LogAssert.Expect(LogType.Assert, "Passive effects ran inside an open pass.");

            // Act
            TestDelegate act = () =>
            {
                ChildListWrites.BeginRecording(pass);
                try
                {
                    FiberExecutionContext.Enter<object>(null, null, _ => { });
                }
                finally
                {
                    ChildListWrites.EndRecording(pass);
                }
            };

            // Assert
            Assert.That(act, Throws.Nothing);
        }
    }
}
