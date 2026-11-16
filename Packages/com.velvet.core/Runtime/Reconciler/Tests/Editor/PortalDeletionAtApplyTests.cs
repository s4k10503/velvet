using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins how a container's deletion of a row holding a Portal splits what removing the Portal does
    /// (<c>DeletionCommit.DetachPortals</c>): its range leaves the model where the deletion decides, so a row
    /// the walk reaches afterwards reads the slots the removal leaves, and its world-space host is destroyed where the
    /// pass applies its recorded writes, after the renders of the walk.
    /// </summary>
    [TestFixture]
    internal sealed class PortalDeletionAtApplyTests
    {
        private sealed record Named(string Name);

        private sealed record Target(VisualElement Element);

        private static Func<string> s_read;
        private MountedTree _mounted;
        private HeadlessEditorPanelHost _host;

        [SetUp]
        public void SetUp() => s_read = null;

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            s_read = null;
        }

        [Component]
        private static VNode Probed(Named props)
        {
            EventLog.UseInsertionAndLayoutEffects(props.Name, Array.Empty<object>());
            return V.Div();
        }

        // Renders again whenever its flag changes, which a row after the deleted one does within the same pass, and
        // records what the case reads of the context at that point.
        [Component]
        private static VNode Watcher(bool shown)
        {
            EventLog.UseRender("W", s_read?.Invoke());
            return V.Div(name: shown ? "watching" : "watched");
        }

        [Component]
        private static VNode HoldsAProbedPortal(Target target)
            => V.Div(children: new VNode[]
            {
                V.Portal(target.Element, new VNode[] { V.Component(Probed, new Named("D"), key: "d") }, key: "p"),
            });

        [Component]
        private static VNode HoldsAPortal(Target target)
            => V.Div(children: new VNode[] { V.Portal(target.Element, new VNode[] { V.Div(name: "one") }, key: "p1") });

        [Component]
        private static VNode HoldsTwoPortals(Target target)
            => V.Div(children: new VNode[]
            {
                V.Portal(target.Element, new VNode[] { V.Div(name: "one") }, key: "p1"),
                V.Portal(target.Element, new VNode[] { V.Div(name: "one-b") }, key: "p1b"),
            });

        // Bails where its props are equal, so the Portal it renders is not patched while the pass runs.
        [Component(Memoize = true)]
        private static VNode StaysPut(Target target)
            => V.Portal(target.Element, new VNode[] { V.Div(name: "two") }, key: "p2");

        [Component]
        private static VNode HoldsAWorldSpacePanel()
            => V.Div(children: new VNode[]
            {
                V.WorldSpace(Vector3.zero, key: "ws", children: new VNode[] { V.Div(name: "in-world") }),
            });

        // The deleted row and the watcher after it sit in elements of their own, so the watcher renders in a walk of a
        // container after the one that decides the deletion.
        [Component]
        private static VNode PortalBesideAWatcher(Target target)
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Div(children: new VNode[]
            {
                V.Div(key: "a", children: new VNode[] { shown ? V.Component(HoldsAProbedPortal, target, key: "h") : null }),
                V.Div(key: "b", children: new VNode[] { V.Component(Watcher, shown, key: "w") }),
            });
        }

        private sealed record Held(string Kind, VisualElement Target);

        private static VNode RowOf(string kind) => kind switch
        {
            "label" => V.Label(text: "x"),
            "hover" => V.Div(className: "hover:bg-red-500", name: "hovered"),
            _ => V.Component(Probed, new Named("D"), key: "d"),
        };

        [Component]
        private static VNode HoldsAPortalOfKind(Held held)
            => V.Div(name: "holder", children: new VNode[] { V.Portal(held.Target, new VNode[] { RowOf(held.Kind) }, key: "p") });

        [Component]
        private static VNode KindBesideAWatcher(Held held)
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Div(children: new VNode[]
            {
                V.Div(key: "a", children: new VNode[] { shown ? V.Component(HoldsAPortalOfKind, held, key: "h") : null }),
                V.Div(key: "b", children: new VNode[] { V.Component(Watcher, shown, key: "w") }),
            });
        }

        // The Portals share a target, the second of them held by a component that bails where its props are equal.
        [Component]
        private static VNode PortalsOnOneTarget(Target target)
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Div(children: new VNode[]
            {
                V.Div(key: "a", children: new VNode[] { shown ? V.Component(HoldsAPortal, target, key: "h") : null }),
                V.Div(key: "c", children: new VNode[] { V.Component(StaysPut, target, key: "s") }),
                V.Div(key: "b", children: new VNode[] { V.Component(Watcher, shown, key: "w") }),
            });
        }

        [Component]
        private static VNode TwoPortalsAheadOfOne(Target target)
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Div(children: new VNode[]
            {
                V.Div(key: "a", children: new VNode[] { shown ? V.Component(HoldsTwoPortals, target, key: "h") : null }),
                V.Div(key: "c", children: new VNode[] { V.Component(StaysPut, target, key: "s") }),
                V.Div(key: "b", children: new VNode[] { V.Component(Watcher, shown, key: "w") }),
            });
        }

        [Component]
        private static VNode WorldSpaceBesideAWatcher()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Div(children: new VNode[]
            {
                V.Div(key: "a", children: new VNode[] { shown ? V.Component(HoldsAWorldSpacePanel, key: "h") : null }),
                V.Div(key: "b", children: new VNode[] { V.Component(Watcher, shown, key: "w") }),
            });
        }

        private static string Names(VisualElement parent) => string.Join("|", parent.Children().Select(child => child.name));

        private ReconcilerContext Mounted(EventLog log, VNode tree, VisualElement mountAt)
        {
            _mounted = V.Mount(mountAt, log.Provide(tree));
            _mounted.DrainForTest();
            log.Clear();
            return _mounted.Root.Reconciler.Context;
        }

        [Test]
        public void Given_ADeletedRowHoldingAPortalAndARowAfterItThatRendersAgain_When_TheParentStopsRenderingIt_Then_ThePortalsCleanupsRunAfterThatRender()
        {
            // Arrange
            var log = new EventLog();
            Mounted(log, V.Component(PortalBesideAWatcher, new Target(new VisualElement())), new VisualElement());

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            _mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:W= ins-x:D lay-x:D"));
        }

        // GREEN_ON_BASE(refactor): the base moves the later Portal's range where it decides too.
        [Test]
        public void Given_ADeletedRowHoldingAPortalAheadOfAnotherOnTheTarget_When_ARowAfterBothRendersInThePass_Then_TheOthersRangeStartsWhereTheRemovalLeavesIt()
        {
            // Arrange
            var log = new EventLog();
            var ctx = Mounted(log, V.Component(PortalsOnOneTarget, new Target(new VisualElement())), new VisualElement());
            s_read = () => ctx.PortalState.Stored.Values.Max(range => range.SlotStart).ToString();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            _mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:W=0"));
        }

        // GREEN_ON_BASE(refactor): the base moves each range as the one before it leaves, with its rows gone.
        [Test]
        public void Given_TwoPortalsInADeletedRowAheadOfAnotherOnTheTarget_When_ARowAfterAllRendersInThePass_Then_TheOthersRangeStartsWhereTheRemovalsLeaveIt()
        {
            // Arrange
            var log = new EventLog();
            var ctx = Mounted(log, V.Component(TwoPortalsAheadOfOne, new Target(new VisualElement())), new VisualElement());
            s_read = () => ctx.PortalState.Stored.Values.Max(range => range.SlotStart).ToString();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            _mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:W=0"));
        }

        // GREEN_ON_BASE(refactor): the base drops the Portal's entry where it decides too.
        [Test]
        public void Given_ADeletedRowHoldingAPortalAheadOfAnotherOnTheTarget_When_ThePassEnds_Then_OnlyTheOtherPortalHoldsAnEntry()
        {
            // Arrange
            var log = new EventLog();
            var ctx = Mounted(log, V.Component(PortalsOnOneTarget, new Target(new VisualElement())), new VisualElement());

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            _mounted.DrainForTest();

            // Assert
            Assert.That(ctx.PortalState.Count, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(refactor): the base removes the Portal's rows from the target in the same pass.
        [Test]
        public void Given_ADeletedRowHoldingAPortalAheadOfAnotherOnTheTarget_When_ThePassEnds_Then_OnlyTheOthersRowsAreLeftOnTheTarget()
        {
            // Arrange
            var log = new EventLog();
            var target = new VisualElement();
            Mounted(log, V.Component(PortalsOnOneTarget, new Target(target)), new VisualElement());

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            _mounted.DrainForTest();

            // Assert
            Assert.That(Names(target), Is.EqualTo("two"));
        }

        // GREEN_ON_BASE(refactor): the base releases the bridge in the same pass too.
        [Test]
        public void Given_ADeletedRowHoldingTheOnlyPortalOnATarget_When_ThePassEnds_Then_TheTargetKeepsNoBridge()
        {
            // Arrange
            var log = new EventLog();
            var ctx = Mounted(log, V.Component(KindBesideAWatcher, new Held("probed", new VisualElement())), new VisualElement());
            var bound = ctx.SamePanelPortalBridges.Count;

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            _mounted.DrainForTest();

            // Assert — a target that never held a bridge reads as its own value, which the expected one is not
            Assert.That(bound > 0 ? ctx.SamePanelPortalBridges.Count : -1, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(refactor): the base returns the Portal's rows to their pools in the same pass too.
        [Test]
        public void Given_ADeletedRowHoldingAPortalOfALabel_When_ThePassEnds_Then_TheLabelWasResetForReuse()
        {
            // Arrange — a pool with room, which resets what it takes
            for (var i = 0; i < 40; i++) VNodePool.RentLabel(string.Empty);
            var log = new EventLog();
            var target = new VisualElement();
            Mounted(log, V.Component(KindBesideAWatcher, new Held("label", target)), new VisualElement());
            var label = (Label)target[0];

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            _mounted.DrainForTest();

            // Assert
            Assert.That(label.text, Is.EqualTo(string.Empty));
        }

        // GREEN_ON_BASE(refactor): the base releases the Portal's rows in the same pass too.
        [Test]
        public void Given_ADeletedRowHoldingAPortalOfAHoveredElement_When_ThePassEnds_Then_TheElementHoldsFewerCallbacks()
        {
            // Arrange
            var log = new EventLog();
            var target = new VisualElement();
            Mounted(log, V.Component(KindBesideAWatcher, new Held("hover", target)), new VisualElement());
            var row = target[0];
            var before = CallbackRegistryProbe.BubbleUpCallbackCount(row);

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            _mounted.DrainForTest();

            // Assert — a row that held no callback to begin with is not fewer
            Assert.That(CallbackRegistryProbe.BubbleUpCallbackCount(row) < before, Is.True);
        }

        // GREEN_ON_BASE(refactor): the base takes down in the walk every removal that holds a Portal, wherever its target is.
        [Test]
        public void Given_ADeletedRowHoldingAPortalIntoAnElementInsideIt_When_TheParentStopsRenderingIt_Then_ThePortalsCleanupsRunBeforeARowAfterItRenders()
        {
            // Arrange
            var log = new EventLog();
            var target = new VisualElement();
            var mountAt = new VisualElement();
            Mounted(log, V.Component(KindBesideAWatcher, new Held("probed", target)), mountAt);
            mountAt.Q<VisualElement>("holder").Add(target);

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            _mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:D lay-x:D render:W="));
        }

        [Test]
        public void Given_ADeletedRowHoldingAWorldSpacePanelAndARowAfterItThatRendersAgain_When_TheParentStopsRenderingIt_Then_ItsHostIsStillBoundAtThatRender()
        {
            // Arrange
            _host = new HeadlessEditorPanelHost();
            var log = new EventLog();
            var ctx = Mounted(log, V.Component(WorldSpaceBesideAWatcher), _host.Root);
            s_read = () => ctx.WorldSpaceBindings.Count.ToString();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            _mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:W=1"));
        }

        // GREEN_ON_BASE(refactor): the base destroys the host in the same pass too.
        [Test]
        public void Given_ADeletedRowHoldingAWorldSpacePanel_When_ThePassEnds_Then_ItsHostIsNoLongerBound()
        {
            // Arrange
            _host = new HeadlessEditorPanelHost();
            var log = new EventLog();
            var ctx = Mounted(log, V.Component(WorldSpaceBesideAWatcher), _host.Root);

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            _mounted.DrainForTest();

            // Assert
            Assert.That(ctx.WorldSpaceBindings.Count, Is.EqualTo(0));
        }
    }
}
