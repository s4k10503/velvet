using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what applying the hides and reveals of a walk's <c>V.Suspense</c> boundaries ends with: an element an
    /// inner boundary reveals while the boundary enclosing it hides it stays hidden, a <c>V.Portal</c>'s element
    /// that a <c>z-*</c> class relocates is hidden with the rest whenever the portal mounts, and a Label that a layout
    /// cleanup of the hide returns to the pool, through an error boundary's fallback swap, keeps no hide there. A Label
    /// that swap keeps in place and reveals stays displayed with its ref set.
    /// </summary>
    [TestFixture]
    internal sealed class SuspenseOffscreenCommitTests
    {
        private sealed class Page
        {
            internal StateUpdater<int> Set;
            internal StateUpdater<bool> Show;
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
        private static VNode ThrowingCleanupItem()
        {
            Hooks.UseLayoutEffect(() => () => throw new InvalidOperationException("hide cleanup"), Array.Empty<object>());
            return V.Label(text: "item");
        }

        // Nothing here renders a Label but the primary, so the pool holds only what the fallback swap returns.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode CatchingBoundary(Page page)
        {
            Hooks.UseFallback(_ => V.Div(name: "caught"));
            return V.Component(CatchingHost, page, key: "host");
        }

        [Component]
        private static VNode CatchingHost(Page page)
        {
            var (current, set) = Hooks.UseState(0);
            page.Set = set;
            return V.Suspense(
                fallback: V.Div(name: "loading"),
                children: new VNode[]
                {
                    V.Component(ThrowingCleanupItem, key: "item"),
                    V.Component(Reader, new ReaderProps(current, 1), key: "reader"),
                });
        }

        private sealed record SwapProps(Page Page, bool ThrowerFirst);

        private sealed record SwappingBoundaryProps(int Page, bool ThrowerFirst);

        private static VisualElement s_xRef;

        private static readonly Func<VisualElement, Action> s_xRefCallback = element =>
        {
            s_xRef = element;
            return () => s_xRef = null;
        };

        private static VNode X() => V.Label(key: "x", name: "x", text: "x", refCallback: s_xRefCallback);

        // Its fallback is the same Suspense at the same position, holding the same keyed Label.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode SwappingBoundary(SwappingBoundaryProps props)
        {
            Hooks.UseFallback(_ => V.Suspense(fallback: V.Div(name: "loading"), children: new VNode[] { X() }));
            var thrower = V.Component(ThrowingCleanupItem, key: "thrower");
            var reader = V.Component(Reader, new ReaderProps(props.Page, 1), key: "reader");
            return V.Suspense(
                fallback: V.Div(name: "loading"),
                children: props.ThrowerFirst ? new VNode[] { thrower, X(), reader } : new VNode[] { X(), thrower, reader });
        }

        [Component]
        private static VNode SwapHost(SwapProps props)
        {
            var (current, set) = Hooks.UseState(0);
            props.Page.Set = set;
            return V.Component(SwappingBoundary, new SwappingBoundaryProps(current, props.ThrowerFirst));
        }

        private static MountedTree MountSwap(VisualElement root, Page page, bool throwerFirst)
        {
            var mounted = V.Mount(root, V.Component(SwapHost, new SwapProps(page, throwerFirst)),
                new MountOptions { OnCaughtError = (_, _) => { } });
            mounted.DrainForTest();
            return mounted;
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

        // GREEN_ON_BASE(characterization): the base hides each element before the layout cleanups of what it hides.
        // So the reset of a Label the fallback swap returns to the pool comes after the hide and clears it.
        [Test]
        public void Given_AHiddenLabelWhoseComponentsLayoutCleanupSwapsInABoundarysFallback_When_TheLabelIsRentedAgain_Then_ItIsDisplayed()
        {
            // Arrange
            VNodePoolTestAccess.ClearLabelPoolForTest();
            var root = new VisualElement();
            var page = new Page();
            using var mounted = V.Mount(root, V.Component(CatchingBoundary, page),
                new MountOptions { OnCaughtError = (_, _) => { } });
            mounted.DrainForTest();
            var primaryLabels = root.Query<Label>().ToList();
            GoTo(mounted, page, 1);

            // Act
            var rented = VNodePool.RentLabel("next");

            // Assert — a Label the swap did not return to the pool reads as no display at all
            Assert.That(primaryLabels.Contains(rented) ? rented.style.display.value : (DisplayStyle?)null,
                Is.EqualTo(DisplayStyle.Flex));
        }

        // GREEN_ON_BASE(characterization): the base hides each element before the layout cleanups of what it hides.
        // So the reveal the fallback swap's own commit makes is the last write x takes.
        [Test]
        public void Given_ABoundaryWhoseFallbackIsTheSameSuspense_When_ALayoutCleanupOfItsHideSwapsTheFallbackIn_Then_TheLabelItKeepsIsDisplayed()
        {
            // Arrange
            var root = new VisualElement();
            var page = new Page();
            using var mounted = MountSwap(root, page, throwerFirst: false);
            var x = root.Q<Label>("x");

            // Act
            GoTo(mounted, page, 1);

            // Assert — a Label the swap did not keep in place reads as no display at all
            var after = root.Q<Label>("x");
            Assert.That(ReferenceEquals(after, x) ? after.style.display.value : (DisplayStyle?)null,
                Is.EqualTo(DisplayStyle.Flex));
        }

        // GREEN_ON_BASE(characterization): the base detaches x's ref at the hide and the swap's reveal attaches it again.
        // The hide comes before every layout cleanup of what it hides, so the reveal is the last to reach the ref.
        [Test]
        public void Given_ABoundaryWhoseFallbackIsTheSameSuspenseAndALayoutCleanupAheadOfALabel_When_TheCleanupSwapsTheFallbackIn_Then_TheLabelKeepsItsRef()
        {
            // Arrange
            s_xRef = null;
            var root = new VisualElement();
            var page = new Page();
            using var mounted = MountSwap(root, page, throwerFirst: true);
            var x = root.Q<Label>("x");

            // Act
            GoTo(mounted, page, 1);

            // Assert — a Label the swap did not keep in place reads as no ref at all
            Assert.That(ReferenceEquals(root.Q<Label>("x"), x) ? s_xRef : null, Is.SameAs(x));
        }
    }
}
