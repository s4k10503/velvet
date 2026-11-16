using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the recorded child-list writes belong to the pass that made them: a pass that starts while
    /// another's are unapplied applies them first, and the one that was recording takes over again where the other ends.
    /// </summary>
    [TestFixture]
    internal sealed class PassOwnershipTests
    {
        private Reconciler _first = null!;
        private Reconciler _second = null!;

        [SetUp]
        public void SetUp()
        {
            _first = new Reconciler();
            _second = new Reconciler();
        }

        [TearDown]
        public void TearDown()
        {
            ChildListWrites.EndRecording(_second.Context.Pass);
            ChildListWrites.EndRecording(_first.Context.Pass);
            _first.Dispose();
            _second.Dispose();
        }

        [Test]
        public void Given_APassWithARecordedInsert_When_AnotherPassBegins_Then_TheInsertIsOnTheLiveList()
        {
            // Arrange
            var parent = new VisualElement();
            parent.Add(new VisualElement());
            var child = new VisualElement();
            ChildListWrites.BeginRecording(_first.Context.Pass);
            _first.Context.HostCommit.Insert(parent, 1, child);

            // Act
            ChildListWrites.BeginRecording(_second.Context.Pass);

            // Assert
            Assert.That(parent.childCount, Is.EqualTo(2));
        }

        private MountedTree? _mounted;
        private static int s_heldRenders;

        [Component]
        private static VNode CountsItsRenders()
        {
            s_heldRenders++;
            return V.Div(name: "b");
        }

        private VNode[] TreeMountingARootInsideTheWalk(VisualElement host, Action<VisualElement>? andThen = null)
            => new VNode[]
            {
                V.ScrollView(onCreated: scroll =>
                {
                    _mounted = V.Mount(host, V.Div(name: "b"));
                    andThen?.Invoke(scroll);
                }),
            };

        [Test]
        public void Given_AMountAskedForInsideAWalk_When_TheWalkEnds_Then_TheRootHasNotRenderedInsideIt()
        {
            // Arrange
            var host = new VisualElement();
            var seen = -1;
            var tree = TreeMountingARootInsideTheWalk(host, _ => seen = host.childCount);

            // Act
            _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), tree);
            _mounted?.Dispose();

            // Assert
            Assert.That(seen, Is.Zero);
        }

        // GREEN_ON_BASE(characterization): the base rendered the root where it was asked for, once.
        [Test]
        public void Given_AHeldMountThatWasMade_When_AnotherPassEnds_Then_TheRootIsNotRenderedAgain()
        {
            // Arrange
            var host = new VisualElement();
            s_heldRenders = 0;
            _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), new VNode[]
            {
                V.ScrollView(onCreated: _ => _mounted = V.Mount(host, V.Component(CountsItsRenders))),
            });
            var rendered = s_heldRenders;

            // Act
            _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), new VNode[] { V.Div() });
            var again = s_heldRenders;
            _mounted?.Dispose();

            // Assert
            Assert.That(again, Is.EqualTo(rendered));
        }

        [Test]
        public void Given_AMountAskedForInsideAWalk_When_TheRootIsDisposedBeforeThePassEnds_Then_ItNeverRenders()
        {
            // Arrange
            s_heldRenders = 0;
            var tree = new VNode[]
            {
                V.ScrollView(onCreated: _ =>
                {
                    _mounted = V.Mount(new VisualElement(), V.Component(CountsItsRenders));
                    _mounted.Dispose();
                }),
            };

            // Act
            _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(s_heldRenders, Is.Zero);
        }

        // GREEN_ON_BASE(characterization): the base rendered the root where it was asked for, so it holds its row by the
        // time control returns; the case pins that holding the render does not leave it for a frame.
        [Test]
        public void Given_AMountAskedForInsideAWalk_When_ThePassEnds_Then_TheRootHasRenderedBeforeControlReturns()
        {
            // Arrange
            var host = new VisualElement();
            var tree = TreeMountingARootInsideTheWalk(host);

            // Act
            _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), tree);
            var rendered = host.childCount;
            _mounted?.Dispose();

            // Assert
            Assert.That(rendered, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): as for the pass that ends without a throw.
        [Test]
        public void Given_AMountAskedForInsideAWalk_When_TheApplyOfThePassThrows_Then_TheRootHasStillRendered()
        {
            // Arrange
            var host = new VisualElement();
            var patcher = new FiberNodePatcher(_first.Context);
            var tree = TreeMountingARootInsideTheWalk(host, _ => ChildListWrites.RecordPatchPre(
                new PendingPatch(patcher, new Label(), null, new ElementNode(), 0)));

            // Act
            try
            {
                _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), tree);
            }
            catch (NullReferenceException)
            {
            }
            var rendered = host.childCount;
            _mounted?.Dispose();

            // Assert
            Assert.That(rendered, Is.EqualTo(1));
        }

        [Test]
        public void Given_AnotherPassBeganOverARecordingOne_When_ItEnds_Then_TheOuterPassIsRecordingAgain()
        {
            // Arrange
            ChildListWrites.BeginRecording(_first.Context.Pass);
            ChildListWrites.BeginRecording(_second.Context.Pass);

            // Act
            ChildListWrites.EndRecording(_second.Context.Pass);

            // Assert
            Assert.That(ChildListWrites.IsRecording, Is.True);
        }

        [Test]
        public void Given_AnotherPassBeganOverARecordingOne_When_ItEnds_Then_ItsInsertIsOnTheLiveList()
        {
            // Arrange
            var parent = new VisualElement();
            var child = new VisualElement();
            ChildListWrites.BeginRecording(_first.Context.Pass);
            ChildListWrites.BeginRecording(_second.Context.Pass);
            _second.Context.HostCommit.Insert(parent, 0, child);

            // Act
            ChildListWrites.EndRecording(_second.Context.Pass);

            // Assert
            Assert.That(parent.childCount, Is.EqualTo(1));
        }

        private VNode[] TreeAskingForHeldWork(Action<ReconcilerContext> ask)
            => new VNode[] { V.ScrollView(onCreated: _ => ask(_second.Context)) };

        private void RecordAnApplyThatThrows()
            => ChildListWrites.RecordPatchPre(new PendingPatch(
                new FiberNodePatcher(_first.Context), new Label(), null, new ElementNode(), 0));

        [Test]
        public void Given_AHeldItemThatThrows_When_TheOwnersPassEndsInAnException_Then_TheOwnersExceptionPropagates()
        {
            // Arrange — the held item's exception is logged, since the owner's is the one in flight
            LogAssert.Expect(LogType.Exception, new Regex("held"));
            var tree = TreeAskingForHeldWork(context =>
            {
                ChildListWrites.HoldMount(() => throw new InvalidOperationException("held"), context);
                RecordAnApplyThatThrows();
            });

            // Act
            TestDelegate act = () => _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), tree);

            // Assert
            Assert.Throws<NullReferenceException>(act);
        }

        [Test]
        public void Given_AHeldItemThatThrows_When_ThePassEnds_Then_TheItemAfterItStillRendered()
        {
            // Arrange
            var host = new VisualElement();
            var tree = TreeAskingForHeldWork(context =>
            {
                ChildListWrites.HoldMount(() => throw new InvalidOperationException("held"), context);
                _mounted = V.Mount(host, V.Div(name: "b"));
            });

            // Act
            try
            {
                _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), tree);
            }
            catch (InvalidOperationException)
            {
            }
            var rendered = host.childCount;
            _mounted?.Dispose();

            // Assert
            Assert.That(rendered, Is.EqualTo(1));
        }

        [Test]
        public void Given_AHeldItemThatThrows_When_ThePassEnds_Then_ItsExceptionIsRethrown()
        {
            // Arrange
            var tree = TreeAskingForHeldWork(context =>
                ChildListWrites.HoldMount(() => throw new InvalidOperationException("held"), context));

            // Act
            TestDelegate act = () => _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), tree);

            // Assert
            Assert.Throws<InvalidOperationException>(act);
        }

        [Test]
        public void Given_HeldWorkThatAsksForMoreWhileItIsMade_When_ThePassEnds_Then_TheMoreRunsAfterWhatWasAheadOfIt()
        {
            // Arrange
            var log = new System.Collections.Generic.List<string>();
            var tree = TreeAskingForHeldWork(context =>
            {
                ChildListWrites.HoldMount(() =>
                {
                    log.Add("a");
                    ChildListWrites.EnterCommit();
                    ChildListWrites.HoldMount(() => log.Add("c"), context);
                    ChildListWrites.LeaveCommit();
                }, context);
                ChildListWrites.HoldMount(() => log.Add("b"), context);
            });

            // Act
            _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(string.Join(",", log), Is.EqualTo("a,b,c"));
        }

        // Held work that asks for itself again while it is made, sixty times in all: past the limit, and finite where the
        // limit is not kept.
        private VNode[] TreeAskingForItselfAgainSixtyTimes(Action? eachRun = null)
            => TreeAskingForHeldWork(context =>
            {
                var left = 60;
                Action again = null!;
                again = () =>
                {
                    eachRun?.Invoke();
                    if (--left == 0) return;
                    ChildListWrites.EnterCommit();
                    ChildListWrites.HoldMount(again, context);
                    ChildListWrites.LeaveCommit();
                };
                ChildListWrites.HoldMount(again, context);
            });

        [Test]
        public void Given_HeldWorkThatAsksForItselfAgainSixtyTimes_When_ThePassEnds_Then_TheNestedUpdateLimitIsThrown()
        {
            // Arrange
            var tree = TreeAskingForItselfAgainSixtyTimes();

            // Act
            TestDelegate act = () => _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(act, Throws.InvalidOperationException.With.Message.Contains("Maximum update depth"));
        }

        [Test]
        public void Given_HeldWorkThatAsksForItselfAgainSixtyTimes_When_ThePassEnds_Then_ItRanAsOftenAsTheLimitAllows()
        {
            // Arrange
            var runs = 0;
            var tree = TreeAskingForItselfAgainSixtyTimes(() => runs++);

            // Act
            try
            {
                _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), tree);
            }
            catch (InvalidOperationException)
            {
            }

            // Assert
            Assert.That(runs, Is.EqualTo(50));
        }

        [Test]
        public void Given_ARootDisposedByEarlierHeldWorkOfTheSameRelease_When_ItsRenderWasHeld_Then_ItNeverRenders()
        {
            // Arrange
            var host = new VisualElement();
            s_heldRenders = 0;
            var tree = TreeAskingForHeldWork(context =>
            {
                ChildListWrites.HoldMount(() => _mounted!.Dispose(), context);
                _mounted = V.Mount(host, V.Component(CountsItsRenders));
            });

            // Act
            _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(s_heldRenders, Is.Zero);
        }

        private static StateUpdater<int> s_setCounted;
        private static int s_countedRenders;

        [Component]
        private static VNode CountsItsStateRenders()
        {
            var (_, set) = Hooks.UseState(0);
            s_setCounted = set;
            s_countedRenders++;
            return V.Div();
        }

        [Test]
        public void Given_FlushesOfTwoContextsAskedForInsideAWalk_When_ThePassEnds_Then_TheSecondHasRendered()
        {
            // Arrange
            using var other = V.Mount(new VisualElement(), V.Component(CountsItsStateRenders));
            var otherContext = other.Root.Reconciler!.Context;
            var before = s_countedRenders;
            var tree = TreeAskingForHeldWork(context =>
            {
                ChildListWrites.HoldsFlush(context);
                s_setCounted.Invoke(1);
                ChildListWrites.HoldsFlush(otherContext);
            });

            // Act
            _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(s_countedRenders - before, Is.EqualTo(1));
        }

        [Test]
        public void Given_AContextWithAMountHeldWhoseFlushIsAskedForInsideAWalk_When_ThePassEnds_Then_ItHasRendered()
        {
            // Arrange
            using var other = V.Mount(new VisualElement(), V.Component(CountsItsStateRenders));
            var otherContext = other.Root.Reconciler!.Context;
            var before = s_countedRenders;
            var tree = TreeAskingForHeldWork(_ =>
            {
                ChildListWrites.HoldMount(() => { }, otherContext);
                s_setCounted.Invoke(1);
                ChildListWrites.HoldsFlush(otherContext);
            });

            // Act
            _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(s_countedRenders - before, Is.EqualTo(1));
        }

        [Test]
        public void Given_AFlushAskedForMoreTimesThanTheNestedUpdateLimitInsideAWalk_When_ThePassEnds_Then_NothingIsThrown()
        {
            // Arrange
            var tree = TreeAskingForHeldWork(context =>
            {
                for (var i = 0; i < 60; i++) ChildListWrites.HoldsFlush(context);
            });

            // Act
            TestDelegate act = () => _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(act, Throws.Nothing);
        }

        // GREEN_ON_BASE(characterization): the base rendered each root where it was asked for, so the second has rendered
        // by the time its pass returns.
        [Test]
        public void Given_TwoPassesThatEachHoldAMount_When_TheSecondEnds_Then_ItsRootHasRendered()
        {
            // Arrange
            var firstHost = new VisualElement();
            var secondHost = new VisualElement();
            _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), TreeMountingARootInsideTheWalk(firstHost));
            using var firstRoot = _mounted;

            // Act
            _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), TreeMountingARootInsideTheWalk(secondHost));
            var rendered = secondHost.childCount;
            _mounted?.Dispose();

            // Assert
            Assert.That(rendered, Is.EqualTo(1));
        }

        [Test]
        public void Given_HeldWorkThatRunsAnEntryOfItsOwn_When_ThatEntryEnds_Then_TheWorkBehindItWaitsForItToReturn()
        {
            // Arrange
            var log = new System.Collections.Generic.List<string>();
            var tree = TreeAskingForHeldWork(context =>
            {
                ChildListWrites.HoldMount(() =>
                {
                    ChildListWrites.RunEntry<object?>(null, null, _ => log.Add("entry"));
                    log.Add("after the entry");
                }, context);
                ChildListWrites.HoldMount(() => log.Add("behind"), context);
            });

            // Act
            _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(string.Join(",", log), Is.EqualTo("entry,after the entry,behind"));
        }

        private static ReconcilerContext s_holdIn = null!;

        // Holds work that throws, from inside its own first walk.
        [Component]
        private static VNode HoldsWorkThatThrows()
            => V.ScrollView(onCreated: _ => ChildListWrites.HoldMount(() => throw new InvalidOperationException("held"), s_holdIn));

        // Holds work that throws, and then writes its state from every layout effect it runs, past the nested-update limit.
        [Component]
        private static VNode HoldsWorkThatThrowsAndRunsAway()
        {
            var (n, setN) = Hooks.UseState(0);
            Hooks.UseLayoutEffect(() =>
            {
                setN.Invoke(n + 1);
                return (Action)null!;
            }, new object[] { n });
            return V.ScrollView(onCreated: _ => ChildListWrites.HoldMount(() => throw new InvalidOperationException("held"), s_holdIn));
        }

        [Test]
        public void Given_HeldWorkThatThrows_When_TheMountThatHeldItEnds_Then_ItsExceptionIsRethrown()
        {
            // Arrange
            FiberStrictMode.Enabled = false;
            s_holdIn = _second.Context;

            // Act
            TestDelegate act = () => _mounted = V.Mount(new VisualElement(), V.Component(HoldsWorkThatThrows));

            // Assert
            Assert.That(act, Throws.InvalidOperationException.With.Message.EqualTo("held"));
        }

        [Test]
        public void Given_HeldWorkThatThrows_When_TheMountThatHeldItEndsInAnExceptionOfItsOwn_Then_ItsOwnPropagates()
        {
            // Arrange — the held work's exception is logged, since the mount's is the one in flight
            FiberStrictMode.Enabled = false;
            s_holdIn = _second.Context;
            LogAssert.Expect(LogType.Exception, new Regex("held"));

            // Act
            TestDelegate act = () => _mounted = V.Mount(new VisualElement(), V.Component(HoldsWorkThatThrowsAndRunsAway));

            // Assert
            Assert.That(act, Throws.InvalidOperationException.With.Message.StartsWith("Maximum update depth exceeded"));
        }

        // GREEN_ON_BASE(characterization): the base writes at once outside a pass as well; the case pins that the
        // pass-owned state leaves that answer where it was.
        [Test]
        public void Given_NoPassIsOpen_When_AnInsertIsMadeThroughTheQueue_Then_ItIsOnTheLiveList()
        {
            // Arrange
            var parent = new VisualElement();
            var child = new VisualElement();

            // Act
            _first.Context.HostCommit.Insert(parent, 0, child);

            // Assert
            Assert.That(parent.childCount, Is.EqualTo(1));
        }
    }
}
