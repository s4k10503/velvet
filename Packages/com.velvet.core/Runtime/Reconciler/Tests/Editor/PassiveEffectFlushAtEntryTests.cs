using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that an entry of any root starts by running the passive effects every root left pending, as
    /// react-reconciler 19.1 keeps one root's passive effects pending and flushes them before it renders or commits
    /// anything (<c>performWorkOnRootViaSchedulerTask</c>, <c>performSyncWorkOnRoot</c>, and the loop <c>commitRoot</c>
    /// opens with): another root's update drain, one whose walk renders a <c>V.VirtualList</c> range, the unmount of a
    /// root whose mount effect has not run, an error boundary's swap for a <c>UseFrame</c> callback's error, and the
    /// render a resolved read retries.
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

        private static SettableStore s_store;
        private static VelvetTaskCompletionSource<string> s_read;

        [SetUp]
        public void SetUp()
        {
            FiberStrictMode.Enabled = false;
            s_store = new SettableStore();
            s_read = new VelvetTaskCompletionSource<string>();
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

        // Hands its state to the store from its effect once the state has changed.
        [Component]
        private static VNode StoreWriter()
        {
            var (value, _) = EventLog.UseState("A", 0);
            var log = EventLog.UseLog();
            Hooks.UseEffect(() =>
            {
                if (value > 0)
                {
                    log.Add("eff:A");
                    s_store.Set(value);
                }
                return (Action)null;
            }, new object[] { value });
            return V.Div();
        }

        [Component]
        private static VNode StoreReader()
        {
            var value = Hooks.UseStore(s_store, stored => stored);
            EventLog.UseRender("A");
            return V.Label(text: "store" + value);
        }

        [Component]
        private static VNode WriterAndReader()
            => V.Fragment(V.Component(StoreWriter, key: "writer"), V.Component(StoreReader, key: "reader"));

        // Its update hands the list a new set of items, whose rows its walk renders.
        [Component]
        private static VNode ListHost()
        {
            var (shifted, _) = EventLog.UseState("Items", 0);
            EventLog.UseRender("B");
            var items = shifted == 0 ? new[] { "a", "b", "c", "d" } : new[] { "e", "f", "g", "h" };
            return V.Div(children: new VNode[]
            {
                V.VirtualList(items: items, keySelector: item => item, itemHeight: 50f,
                    renderer: item => V.Label(text: item, key: item), overscan: 0, key: "list"),
            });
        }

        [Test]
        public void Given_ARootWhosePendingEffectChangesAStoreItReads_When_AnotherRootsDrainRendersAListRange_Then_TheEffectAndTheReadersRenderComeFirst()
        {
            // Arrange — the list's viewport is set as the layout pass EditMode does not run would measure it
            var log = new EventLog();
            var listRoot = new VisualElement();
            using var other = V.Mount(listRoot, log.Provide(V.Component(ListHost, key: "host")));
            var controller = other.Root.Reconciler.Context.VirtualListControllers[listRoot.Q<ScrollView>()];
            var viewport = typeof(FiberVirtualListController).GetField(
                    "_viewportHeight", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(FiberVirtualListController).FullName, "_viewportHeight");
            viewport.SetValue(controller, 100f);
            using var pending = Mount(log, V.Component(WriterAndReader, key: "a"));
            pending.FlushEffectsForTest();
            log.Setter<int>("A").Invoke(1);
            pending.GetSchedulerForTest().DrainImmediateForTest();
            log.Setter<int>("Items").Invoke(1);
            log.Clear();

            // Act
            other.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — null where the drain rendered no row of the new items
            var rowRendered = listRoot.Query<Label>().ToList().Any(label => label.text == "e");
            Assert.That(rowRendered ? EntriesBefore(log, "render:B") : null, Is.EqualTo("eff:A render:A"));
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
