using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what applying the hides and reveals of a walk's <c>V.Suspense</c> boundaries ends with: an element an
    /// inner boundary reveals while the boundary enclosing it hides it stays hidden, a <c>V.Portal</c>'s element that
    /// a <c>z-*</c> class relocates is hidden with the rest whenever the portal mounts, a walk whose boundaries hid
    /// and revealed nothing allocates nothing applying that, and hiding twice the rows of one element reads that
    /// element no more than twice as often.
    /// </summary>
    [TestFixture]
    internal sealed class SuspenseOffscreenCommitTests
    {
        private sealed class Page
        {
            internal StateUpdater<int> Set;
            internal StateUpdater<bool> Show;
        }

        // Counts the reads of its children that ask for contentContainer; ReadsToHide checks the two it relies on.
        private sealed class CountingContainer : VisualElement
        {
            internal int Reads;

            public override VisualElement contentContainer
            {
                get
                {
                    Reads++;
                    return this;
                }
            }
        }

        private sealed record ReaderProps(int Page, int PendingAt);

        // Pending for good on the page it waits at, so its boundary keeps the fallback while that page shows.
        [Component]
        private static VNode Reader(ReaderProps props)
        {
            var value = Hooks.Use(
                () => props.Page == props.PendingAt
                    ? new VelvetTaskCompletionSource<int>().Task
                    : VelvetTask.FromResult(props.Page),
                props.Page);
            return V.Label(text: "reader:" + value);
        }

        [Component]
        private static VNode NestedHost(Page page)
        {
            var (current, set) = Hooks.UseState(0);
            page.Set = set;
            return V.Suspense(
                fallback: V.Label(text: "outer loading"),
                children: new VNode[]
                {
                    V.Suspense(
                        fallback: V.Label(text: "inner loading"),
                        children: new VNode[]
                        {
                            V.Label(name: "x", text: "x"),
                            V.Component(Reader, new ReaderProps(current, 1), key: "inner-reader"),
                        },
                        key: "inner"),
                    V.Component(Reader, new ReaderProps(current, 2), key: "outer-reader"),
                });
        }

        private sealed record PortalProps(Page Page, VisualElement Target, bool ShowLater = false);

        [Component]
        private static VNode PortalHost(PortalProps props)
        {
            var (current, set) = Hooks.UseState(0);
            var (show, setShow) = Hooks.UseState(!props.ShowLater);
            props.Page.Set = set;
            props.Page.Show = setShow;
            return V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[]
                {
                    V.Div(children: new VNode[]
                    {
                        show
                            ? V.Portal(props.Target, new VNode[] { V.Div(name: "relocated", className: "absolute z-10") })
                            : null,
                    }),
                    V.Component(Reader, new ReaderProps(current, 1), key: "reader"),
                });
        }

        [Component]
        private static VNode Row() => V.Div();

        [Component]
        private static VNode Rows(int count)
        {
            var rows = new VNode[count];
            for (var i = 0; i < count; i++) rows[i] = V.Component(Row, key: i.ToString());
            return V.Fragment(rows);
        }

        private static void GoTo(MountedTree mounted, Page page, int number)
        {
            page.Set.Invoke(number);
            mounted.DrainForTest();
        }

        // GREEN_ON_BASE(characterization): the base applies a walk's hides and reveals in the order the walk lists them.
        // The enclosing boundary decides after the inner one, so its hide is what the element ends with there.
        [Test]
        public void Given_AnElementAnInnerSuspenseHides_When_TheInnerOneRevealsItAsTheEnclosingOneSuspends_Then_ItStaysHidden()
        {
            // Arrange
            var root = new VisualElement();
            var page = new Page();
            using var mounted = V.Mount(root, V.Component(NestedHost, page));
            mounted.DrainForTest();
            GoTo(mounted, page, 1);
            var x = root.Q<Label>("x");

            // Act
            GoTo(mounted, page, 2);

            // Assert
            Assert.That(x.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void Given_APortalElementItsZClassRelocates_When_ItsSuspenseHidesThePortal_Then_TheElementIsHidden()
        {
            // Arrange
            var target = new VisualElement();
            var page = new Page();
            using var mounted = V.Mount(new VisualElement(), V.Component(PortalHost, new PortalProps(page, target)));
            mounted.DrainForTest();
            var relocated = target.Q(name: "relocated");

            // Act
            GoTo(mounted, page, 1);

            // Assert
            Assert.That(relocated.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void Given_AHiddenPrimary_When_APortalWhoseElementItsZClassRelocatesMountsInIt_Then_TheElementIsHidden()
        {
            // Arrange
            var target = new VisualElement();
            var page = new Page();
            using var mounted = V.Mount(new VisualElement(),
                V.Component(PortalHost, new PortalProps(page, target, ShowLater: true)));
            mounted.DrainForTest();
            GoTo(mounted, page, 1);

            // Act
            page.Show.Invoke(true);
            mounted.DrainForTest();

            // Assert — a portal that never mounted its element reads as no display at all
            var relocated = target.Q(name: "relocated");
            Assert.That(relocated == null ? (DisplayStyle?)null : relocated.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        // GREEN_ON_BASE(characterization): the base returns before allocating when a walk hid and revealed nothing.
        // What this pins is that applying the hides in React's order kept that.
        [Test]
        public void Given_AWalkWhoseBoundariesHidAndRevealedNothing_When_ItsChangesApply_Then_NothingIsAllocated()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Div());
            var apply = ApplyOffscreenChangesOf(mounted);
            var nothing = new GeneralPathReconciler.GeneralCommitState();
            Action applyNothing = () => apply(nothing);
            Action canary = () => GC.KeepAlive(new object());
            applyNothing();
            canary();

            // Act
            var canaryBlocks = GCAllocationProbe.MedianBlocksDuring(canary);
            var blocks = GCAllocationProbe.MedianBlocksDuring(applyNothing);

            // Assert — a probe that counts nothing for the canary cannot vouch for a zero
            Assert.That(canaryBlocks > 0 ? blocks : -1, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base hides each component without reading the element it sits in.
        // What this pins is that placing them in the walk reads that element a bounded number of times per row.
        [Test]
        public void Given_TwiceTheRowsInOneElement_When_TheirComponentsAreHidden_Then_TheElementIsReadNoMoreThanTwiceAsOften()
        {
            // Arrange
            var fewer = ReadsToHide(1000);

            // Act
            var more = ReadsToHide(2000);

            // Assert — where a count could not be taken, the excess is one no bound admits
            Assert.That(fewer < 0 || more < 0 ? int.MaxValue : more - 2 * fewer, Is.LessThanOrEqualTo(0));
        }

        // The reads of the rows' element while its rows' components are hidden, or -1 where the rows did not mount
        // in it one component each, or where a read of its child count and of a child goes uncounted.
        private static int ReadsToHide(int rows)
        {
            var container = new CountingContainer();
            using var mounted = V.Mount(container, V.Component(Rows, rows));
            mounted.DrainForTest();
            var hidden = new List<(ComponentFiber Fiber, bool Hidden)>();
            FiberTreeTraversal.Visit(mounted.Root, fiber =>
            {
                if (ReferenceEquals(fiber.MountPoint, container) && fiber.MountSlotCount == 1) hidden.Add((fiber, true));
            });
            if (hidden.Count != rows) return -1;
            var apply = ApplyOffscreenChangesOf(mounted);
            var commit = new GeneralPathReconciler.GeneralCommitState { OffscreenChanges = hidden };
            container.Reads = 0;
            _ = container.childCount;
            _ = container[0];
            if (container.Reads < 2) return -1;
            container.Reads = 0;
            apply(commit);
            return container.Reads;
        }

        private static Action<GeneralPathReconciler.GeneralCommitState> ApplyOffscreenChangesOf(MountedTree mounted)
        {
            const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
            var childPass = typeof(Reconciler).GetProperty("ChildPass", members)!.GetValue(mounted.Root.Reconciler);
            var general = typeof(ChildReconciler).GetField("_general", members)!.GetValue(childPass);
            var method = typeof(GeneralPathReconciler).GetMethod("ApplyOffscreenChanges", members)!;
            return (Action<GeneralPathReconciler.GeneralCommitState>)Delegate.CreateDelegate(
                typeof(Action<GeneralPathReconciler.GeneralCommitState>), general, method);
        }
    }
}
