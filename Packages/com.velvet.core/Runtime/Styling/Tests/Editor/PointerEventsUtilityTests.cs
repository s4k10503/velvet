using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    internal readonly record struct PointerEventsFlag(bool On);

    internal sealed class PointerEventsFlagStore : Store<PointerEventsFlag>
    {
        public PointerEventsFlagStore(bool on) : base(new PointerEventsFlag(on)) { }

        public void Set(bool on) => SetState(_ => new PointerEventsFlag(on));

        protected override void ResetCore() => SetState(_ => new PointerEventsFlag(false));
    }

    internal static class PickingModes
    {
        // Every element of the raw subtree, root first, in hierarchy order.
        public static string Of(VisualElement root)
        {
            var modes = new List<string>();
            Collect(root, modes);
            return string.Join(",", modes);
        }

        // The modes the raw subtree holds, each once.
        public static string DistinctOf(VisualElement root)
            => string.Join(",", Of(root).Split(',').Distinct().OrderBy(mode => mode, System.StringComparer.Ordinal));

        private static void Collect(VisualElement element, List<string> into)
        {
            into.Add(element.pickingMode.ToString());
            for (var i = 0; i < element.hierarchy.childCount; i++)
            {
                Collect(element.hierarchy[i], into);
            }
        }
    }

    /// <summary>
    /// Pins what <c>pointer-events-none</c> and <c>pointer-events-auto</c> write onto an element's subtree and what
    /// they hand back, read off <see cref="VisualElement.pickingMode"/>; <see cref="PickingModeEngineTests"/> pins
    /// what the engine does with those values.
    /// </summary>
    internal sealed class PointerEventsUtilityTests
    {
        private static PointerEventsFlagStore s_flag;
        private static PointerEventsFlagStore s_show;
        private static VisualElement s_layer;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_flag = null;
            s_show = null;
            s_layer = null;
        }

        [Test]
        public void Given_APointerEventsNoneContainerHoldingAScrollView_When_Mounted_Then_EveryElementOfItsRawSubtreeIgnoresPicking()
        {
            // Arrange — a scroll view redirects its children into an inner container, so its scrollers are parts
            // of the raw hierarchy that no content-container walk reaches.
            using var mounted = V.Mount(_root, V.Div(name: "scope", className: "pointer-events-none",
                children: new VNode[]
                {
                    V.ScrollView(name: "scroll", children: new VNode[] { V.Label(name: "row", text: "row") }),
                }));
            var scope = _root.Q<VisualElement>("scope");
            var scroll = _root.Q<ScrollView>("scroll");

            // Act
            var modes = PickingModes.DistinctOf(scope);

            // Assert
            Assert.That((modes, scroll.verticalScroller.pickingMode), Is.EqualTo(("Ignore", PickingMode.Ignore)));
        }

        [Test]
        public void Given_APointerEventsAutoChildInsideANoneScope_When_Mounted_Then_ItsSubtreeKeepsPickingUntilANestedNone()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Div(name: "scope", className: "pointer-events-none",
                children: new VNode[]
                {
                    V.Div(name: "auto", className: "pointer-events-auto", children: new VNode[]
                    {
                        V.Div(name: "plain"),
                        V.Div(name: "again", className: "pointer-events-none",
                            children: new VNode[] { V.Div(name: "leaf") }),
                    }),
                }));

            // Act
            var modes = string.Join("/", new[] { "scope", "auto", "plain", "again", "leaf" }
                .Select(name => _root.Q<VisualElement>(name).pickingMode));

            // Assert
            Assert.That(modes, Is.EqualTo("Ignore/Position/Position/Ignore/Ignore"));
        }

        [Test]
        public void Given_ImportantPointerEventsUtilities_When_Mounted_Then_AnImportantOneOutranksALaterUnmarkedOneAndTheLaterOfTwoImportantOnesWins()
        {
            // Arrange — a reading that took the last token whatever its weight answers Position for the first
            // element, and one that kept the first important token answers Position for the second.
            using var mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.Div(name: "over-unmarked", className: "!pointer-events-none pointer-events-auto"),
                V.Div(name: "over-important", className: "!pointer-events-auto !pointer-events-none"),
            }));

            // Act
            var overUnmarked = _root.Q<VisualElement>("over-unmarked").pickingMode;
            var overImportant = _root.Q<VisualElement>("over-important").pickingMode;

            // Assert
            Assert.That((overUnmarked, overImportant), Is.EqualTo((PickingMode.Ignore, PickingMode.Ignore)));
        }

        [Component]
        private static VNode SliderScope()
        {
            var on = Hooks.UseStore(s_flag, s => s.On);
            return V.Div(name: "scope", className: on ? "pointer-events-none" : null,
                children: new VNode[] { V.Slider(name: "slider") });
        }

        [Test]
        public void Given_AScopeAroundASlider_When_ItsUtilityIsRemoved_Then_EveryElementGetsBackTheModeItHadBefore()
        {
            // Arrange — a slider's own root ignores picking from its constructor, which is the mode a blanket
            // restore to Position would lose.
            using var flag = new PointerEventsFlagStore(false);
            s_flag = flag;
            using var mounted = V.Mount(_root, V.Component(SliderScope, key: "scope"));
            var scheduler = mounted.GetSchedulerForTest();
            var slider = _root.Q<Slider>("slider");
            var before = PickingModes.Of(slider);
            flag.Set(true);
            scheduler.DrainImmediateForTest();
            var held = PickingModes.DistinctOf(slider);

            // Act
            flag.Set(false);
            scheduler.DrainImmediateForTest();

            // Assert
            Assert.That((held, PickingModes.Of(slider) == before, slider.pickingMode),
                Is.EqualTo(("Ignore", true, PickingMode.Ignore)));
        }

        [Component]
        private static VNode LateChild()
        {
            var on = Hooks.UseStore(s_flag, s => s.On);
            return on ? V.Div(name: "late") : null;
        }

        [Test]
        public void Given_ANoneScope_When_AComponentInsideItMountsAChildWithoutTheScopeRendering_Then_TheChildIgnoresPicking()
        {
            // Arrange — the store is read by the inner component alone, so the scope's own element is not patched
            // by the render that mounts the child.
            using var flag = new PointerEventsFlagStore(false);
            s_flag = flag;
            using var mounted = V.Mount(_root, V.Div(name: "scope", className: "pointer-events-none",
                children: new VNode[] { V.Component(LateChild, key: "late") }));
            var scheduler = mounted.GetSchedulerForTest();

            // Act
            flag.Set(true);
            scheduler.DrainImmediateForTest();

            // Assert
            Assert.That(_root.Q<VisualElement>("late")?.pickingMode, Is.EqualTo(PickingMode.Ignore));
        }

        [Component]
        private static VNode Ticker()
        {
            var on = Hooks.UseStore(s_flag, s => s.On);
            return V.Div(name: "ticker", className: on ? "tick-on" : "tick-off");
        }

        [Test]
        public void Given_AHeldChildMovedOutOfItsScope_When_TheTreeRendersTwiceMore_Then_ItTakesPointersAgain()
        {
            // Arrange — the move is the one a refCallback can make; nothing tears the child down.
            using var flag = new PointerEventsFlagStore(false);
            s_flag = flag;
            using var mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.Div(name: "scope", className: "pointer-events-none", children: new VNode[] { V.Div(name: "kid") }),
                V.Component(Ticker, key: "ticker"),
            }));
            var scheduler = mounted.GetSchedulerForTest();
            var kid = _root.Q<VisualElement>("kid");
            var held = kid.pickingMode;
            new VisualElement().Add(kid);

            // Act — the first render lets go of the child; the second is the walk after that one, which must not
            // take it back.
            flag.Set(true);
            scheduler.DrainImmediateForTest();
            flag.Set(false);
            scheduler.DrainImmediateForTest();

            // Assert
            Assert.That((held, kid.pickingMode), Is.EqualTo((PickingMode.Ignore, PickingMode.Position)));
        }

        [Component]
        private static VNode ShownScope()
        {
            var show = Hooks.UseStore(s_show, s => s.On);
            return show
                ? V.Div(name: "scope", className: "pointer-events-none", children: new VNode[] { V.Div(name: "kid") })
                : null;
        }

        [Test]
        public void Given_AHeldChildMovedOutOfItsScope_When_TheScopeUnmountsBeforeAnyRender_Then_ItTakesPointersAgain()
        {
            // Arrange — no render runs between the move and the unmount, so the scope's teardown is the only
            // place left to let go of the child.
            using var show = new PointerEventsFlagStore(true);
            s_show = show;
            using var mounted = V.Mount(_root, V.Component(ShownScope, key: "scope"));
            var kid = _root.Q<VisualElement>("kid");
            var held = kid.pickingMode;
            new VisualElement().Add(kid);

            // Act
            show.Set(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That((held, kid.pickingMode), Is.EqualTo((PickingMode.Ignore, PickingMode.Position)));
        }

        [Component]
        private static VNode AutoUnderNone()
        {
            var on = Hooks.UseStore(s_flag, s => s.On);
            return V.Div(name: "scope", className: "pointer-events-none", children: new VNode[]
            {
                V.Div(name: "child", className: on ? "pointer-events-auto" : null),
            });
        }

        [Test]
        public void Given_AnAutoChildInsideANoneScope_When_ItsUtilityIsRemoved_Then_TheEnclosingScopeTakesIt()
        {
            // Arrange
            using var flag = new PointerEventsFlagStore(true);
            s_flag = flag;
            using var mounted = V.Mount(_root, V.Component(AutoUnderNone, key: "scope"));
            var child = _root.Q<VisualElement>("child");
            var asAuto = child.pickingMode;

            // Act
            flag.Set(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That((asAuto, child.pickingMode), Is.EqualTo((PickingMode.Position, PickingMode.Ignore)));
        }

        [Component]
        private static VNode NoneOrAuto()
        {
            var on = Hooks.UseStore(s_flag, s => s.On);
            return V.Div(name: "switch", className: on ? "pointer-events-auto" : "pointer-events-none");
        }

        [Test]
        public void Given_AnElementCarryingPointerEventsNone_When_ItSwitchesToAuto_Then_ItTakesPointersAgain()
        {
            // Arrange
            using var flag = new PointerEventsFlagStore(false);
            s_flag = flag;
            using var mounted = V.Mount(_root, V.Component(NoneOrAuto, key: "switch"));
            var element = _root.Q<VisualElement>("switch");
            var asNone = element.pickingMode;

            // Act
            flag.Set(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That((asNone, element.pickingMode), Is.EqualTo((PickingMode.Ignore, PickingMode.Position)));
        }

        [Component]
        private static VNode ShownButton()
        {
            var show = Hooks.UseStore(s_show, s => s.On);
            return show ? V.Button(name: "button", className: "pointer-events-none", text: "go") : null;
        }

        [Component]
        private static VNode RentedButtonScope()
        {
            var on = Hooks.UseStore(s_flag, s => s.On);
            return V.Div(className: on ? "pointer-events-none" : null,
                children: new VNode[] { V.Button(name: "again", text: "again") });
        }

        [Test]
        public void Given_APointerEventsNoneButtonThatUnmounted_When_AnotherTreeRentsItIntoAScopeThatThenEnds_Then_ItTakesPointers()
        {
            // Arrange — the button is the scope's own element, and the pool hands it back out. The pool return
            // writes Position over the button whatever holds are left on it, so the second tree puts it under a
            // scope of its own and ends that scope: a hold the teardown left behind keeps it Ignore past that end.
            using var show = new PointerEventsFlagStore(true);
            using var flag = new PointerEventsFlagStore(true);
            s_show = show;
            s_flag = flag;
            using var mounted = V.Mount(_root, V.Component(ShownButton, key: "button"));
            var button = _root.Q<Button>("button");
            var held = button.pickingMode;
            show.Set(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var elsewhere = new VisualElement();
            using var other = V.Mount(elsewhere, V.Component(RentedButtonScope, key: "rented"));
            var again = elsewhere.Q<Button>("again");
            // A scope root the teardown left registered would stop the second scope's walk at the button.
            var inSecondScope = again.pickingMode;

            // Act
            flag.Set(false);
            other.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — that the rented button is the same instance rides along, since a freshly built one would
            // take pointers whatever the teardown did.
            Assert.That((held, ReferenceEquals(again, button), inSecondScope, again.pickingMode),
                Is.EqualTo((PickingMode.Ignore, true, PickingMode.Ignore, PickingMode.Position)));
        }

        [Component]
        private static VNode SwappedButton()
        {
            var on = Hooks.UseStore(s_flag, s => s.On);
            return V.Div(className: "pointer-events-none", children: new VNode[]
            {
                on ? V.Button(key: "second", name: "second", text: "b") : V.Button(key: "first", name: "first", text: "a"),
            });
        }

        [Test]
        public void Given_AHeldButtonInsideANoneScope_When_ARenderReplacesItInItsSlot_Then_TheButtonThePoolHandsBackIgnoresPicking()
        {
            // Arrange — a different key in the same slot is removed before its replacement is created, so the
            // replacement is rented from the pool the removal just returned the first button to.
            using var flag = new PointerEventsFlagStore(false);
            s_flag = flag;
            using var mounted = V.Mount(_root, V.Component(SwappedButton, key: "swap"));
            var first = _root.Q<Button>("first");

            // Act
            flag.Set(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var second = _root.Q<Button>("second");

            // Assert — that the pool handed back the same instance rides along, since a freshly built button is
            // taken by the scope whatever the removal left on the first one.
            Assert.That((ReferenceEquals(second, first), second?.pickingMode),
                Is.EqualTo((true, (PickingMode?)PickingMode.Ignore)));
        }

        [Test]
        public void Given_ATreeMountedIntoAnotherTreesNoneScope_When_ItMountsAndLaterAddsAChild_Then_BothIgnorePickingWithoutTheOuterTreeRendering()
        {
            // Arrange — the outer tree never renders after its own mount, so only the inner tree's passes can
            // reach the outer scope.
            using var show = new PointerEventsFlagStore(false);
            s_show = show;
            using var outer = V.Mount(_root, V.Div(name: "scope", className: "pointer-events-none",
                children: new VNode[] { V.Div(name: "host") }));

            // Act — one element arrives with the inner tree's mount, the other with a render inside a drain.
            using var inner = V.Mount(_root.Q<VisualElement>("host"), V.Div(children: new VNode[]
            {
                V.Div(name: "mounted"),
                V.Component(ShownToggle, key: "toggle"),
            }));
            var mounted = _root.Q<VisualElement>("mounted").pickingMode;
            show.Set(true);
            inner.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That((mounted, _root.Q<Toggle>("toggle")?.pickingMode),
                Is.EqualTo((PickingMode.Ignore, (PickingMode?)PickingMode.Ignore)));
        }

        [Component]
        private static VNode PortalOpener()
        {
            var open = Hooks.UseStore(s_show, s => s.On);
            return V.Div(children: new VNode[]
            {
                V.Portal(s_layer, children: open
                    ? new VNode[] { V.Div(name: "portalled-first"), V.Div(name: "portalled-second") }
                    : new VNode[] { V.Div(name: "portalled-first") }),
            });
        }

        [Test]
        public void Given_APortalIntoAnotherTreesNoneScope_When_ItMountsAndLaterAddsAChild_Then_BothIgnorePickingWithoutTheOuterTreeRendering()
        {
            // Arrange — the portalling tree is mounted outside the scope, so only the Portal's target sits in it,
            // and the outer tree never renders after its own mount.
            using var show = new PointerEventsFlagStore(false);
            s_show = show;
            using var outer = V.Mount(_root, V.Div(name: "scope", className: "pointer-events-none",
                children: new VNode[] { V.Div(name: "layer") }));
            s_layer = _root.Q<VisualElement>("layer");

            // Act — the first child arrives with the Portal's mount, the second with a patch of its children.
            using var portalling = V.Mount(new VisualElement(), V.Component(PortalOpener, key: "opener"));
            var first = s_layer.Q<VisualElement>("portalled-first")?.pickingMode;
            show.Set(true);
            portalling.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That((first, s_layer.Q<VisualElement>("portalled-second")?.pickingMode),
                Is.EqualTo(((PickingMode?)PickingMode.Ignore, (PickingMode?)PickingMode.Ignore)));
        }

        private static PickingMode? RowMode(ScrollView scrollView, string text)
            => scrollView.Query<Label>().ToList().FirstOrDefault(label => label.text == text)?.pickingMode;

        [Test]
        public void Given_AVirtualListInsideANoneScope_When_ItRendersRowsFromItsGeometryAndScrollEntries_Then_TheRowsIgnorePicking()
        {
            // Arrange — a list of 100 rows 20 tall. Its geometry and scroll callbacks both render through
            // UpdateVisibleRange, outside any reconcile pass; it is called with the viewport they would measure.
            var items = Enumerable.Range(0, 100).Select(i => "item-" + i).ToArray();
            using var mounted = V.Mount(_root, V.Div(className: "pointer-events-none", children: new VNode[]
            {
                V.VirtualList(items, item => item, itemHeight: 20f, renderer: item => V.Label(text: item), overscan: 0),
            }));
            var scrollView = _root.Q<ScrollView>();
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[scrollView];

            // Act
            controller.UpdateVisibleRange(400f, 200f);
            var afterGeometry = RowMode(scrollView, "item-21");
            controller.UpdateVisibleRange(1000f, 200f);
            var afterScroll = RowMode(scrollView, "item-51");

            // Assert
            Assert.That((afterGeometry, afterScroll),
                Is.EqualTo(((PickingMode?)PickingMode.Ignore, (PickingMode?)PickingMode.Ignore)));
        }

        [Test]
        public void Given_AZIndexedChildOfANoneScope_When_ItIsHoistedIntoItsLayerContainer_Then_ItIgnoresPicking()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Div(name: "scope", className: "pointer-events-none",
                children: new VNode[] { V.Div(name: "floating", className: "absolute z-10 w-[10px] h-[10px]") }));

            // Act
            var floating = _root.Q<VisualElement>("floating");

            // Assert — that the child was hoisted rides along, since one left in its own slot is reached either way.
            Assert.That((FiberZLayerCoordinator.IsLayerContainer(floating.parent), floating.pickingMode),
                Is.EqualTo((true, PickingMode.Ignore)));
        }

        [Component]
        private static VNode ShownToggle()
        {
            var show = Hooks.UseStore(s_show, s => s.On);
            return show ? V.Toggle(name: "toggle") : null;
        }

        [Test]
        public void Given_AToggleAnotherTreesScopeHolds_When_ItsOwnTreeUnmountsIt_Then_TheToggleItRentsOutAgainTakesItsOwnModes()
        {
            // Arrange — the toggle is mounted by an inner tree under an element of the outer tree's scope, so the
            // outer tree, which never renders again, is not where the toggle is torn down. The modes it should get
            // back are read off a toggle mounted outside any scope first.
            string before;
            var probeHost = new VisualElement();
            using (V.Mount(probeHost, V.Toggle(name: "probe")))
            {
                before = PickingModes.Of(probeHost.Q<Toggle>("probe"));
            }
            using var show = new PointerEventsFlagStore(true);
            s_show = show;
            using var outer = V.Mount(_root, V.Div(name: "scope", className: "pointer-events-none",
                children: new VNode[] { V.Div(name: "host") }));
            using var inner = V.Mount(_root.Q<VisualElement>("host"), V.Component(ShownToggle, key: "toggle"));
            var toggle = _root.Q<Toggle>("toggle");
            var held = PickingModes.DistinctOf(toggle);
            show.Set(false);
            inner.GetSchedulerForTest().DrainImmediateForTest();

            // Act
            var elsewhere = new VisualElement();
            using var third = V.Mount(elsewhere, V.Toggle(name: "again"));
            var again = elsewhere.Q<Toggle>("again");

            // Assert — that the rented toggle is the same instance rides along, since a freshly built one would
            // carry its own modes whatever the teardown did.
            Assert.That((held, ReferenceEquals(again, toggle), PickingModes.Of(again) == before),
                Is.EqualTo(("Ignore", true, true)));
        }
    }

    /// <summary>
    /// Pins the pointer-events behaviour that needs a real panel: a click-through pick, a variant toggling the
    /// utility on a live element, and keyboard focus inside an ignored subtree.
    /// </summary>
    internal sealed class PointerEventsPanelTests : PanelTestBase
    {
        private bool _darkBefore;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            _darkBefore = VelvetTheme.IsDark;
            VelvetTheme.IsDark = false;
        }

        [TearDown]
        public override void TearDown()
        {
            VelvetTheme.IsDark = _darkBefore;
            base.TearDown();
        }

        [Test]
        public void Given_APointerEventsNoneOverlayInFrontOfAnElement_When_PickedOverTheOverlaysChild_Then_TheElementBehindIsPicked()
        {
            // Arrange — sizes and the overlap are arbitrary values, which resolve to inline style, so the panel
            // needs no stylesheet.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: new VNode[]
            {
                V.Div(name: "behind", className: "w-[100px] h-[100px]"),
                V.Div(name: "overlay", className: "w-[100px] h-[100px] mt-[-100px] pointer-events-none",
                    children: new VNode[] { V.Div(name: "overlay-child", className: "w-[50px] h-[50px]") }),
            }));
            var behind = _window.rootVisualElement.Q<VisualElement>("behind");
            var overlayChild = _window.rootVisualElement.Q<VisualElement>("overlay-child");
            ForcePanelUpdate(behind.panel);
            ForcePanelUpdate(behind.panel);
            var point = overlayChild.worldBound.center;

            // Act
            var picked = behind.panel.PickAll(point, null);

            // Assert — that the overlay's child covers the point rides along, since elements laid out apart would
            // also pick the one behind.
            Assert.That((overlayChild.worldBound.Contains(point), picked == behind), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ADarkVariantPointerEventsNone_When_TheThemeTurnsDark_Then_TheMountedElementIgnoresPicking()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "scope", className: "dark:pointer-events-none",
                children: new VNode[] { V.Div(name: "kid") }));
            var kid = _window.rootVisualElement.Q<VisualElement>("kid");
            var light = kid.pickingMode;

            // Act
            VelvetTheme.IsDark = true;

            // Assert
            Assert.That((light, kid.pickingMode), Is.EqualTo((PickingMode.Position, PickingMode.Ignore)));
        }

        [Test]
        public void Given_PointerEventsNoneBesideADarkVariantAuto_When_TheThemeTurnsDark_Then_TheElementTakesPointersAgain()
        {
            // Arrange — the theme flip re-derives the element outside any reconcile pass.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "switch", className: "pointer-events-none dark:pointer-events-auto"));
            var element = _window.rootVisualElement.Q<VisualElement>("switch");
            var light = element.pickingMode;

            // Act
            VelvetTheme.IsDark = true;

            // Assert
            Assert.That((light, element.pickingMode), Is.EqualTo((PickingMode.Ignore, PickingMode.Position)));
        }

        [Test]
        public void Given_AnImportantNoneBesideADarkVariantImportantAuto_When_TheThemeTurnsDark_Then_TheVariantsImportantAutoWins()
        {
            // Arrange — the variant's payload has to keep its bang to outrank the element's own important token.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "switch", className: "!pointer-events-none dark:!pointer-events-auto"));
            var element = _window.rootVisualElement.Q<VisualElement>("switch");
            var light = element.pickingMode;

            // Act
            VelvetTheme.IsDark = true;

            // Assert
            Assert.That((light, element.pickingMode), Is.EqualTo((PickingMode.Ignore, PickingMode.Position)));
        }

        [Test]
        public void Given_DarkVariantAutosInsideAnotherTreesNoneScope_When_TheThemeTurnsDark_Then_TheyTakePointersWithoutTheOuterTreeRendering()
        {
            // Arrange — one tree is mounted inside the outer scope, the other outside it with a Portal into it, so
            // the first is reached from its mount target and the second only from its Portal's target.
            var outside = new VisualElement();
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "scope", className: "pointer-events-none",
                children: new VNode[] { V.Div(name: "host"), V.Div(name: "layer") }));
            var root = _window.rootVisualElement;
            using var direct = V.Mount(root.Q<VisualElement>("host"),
                V.Div(name: "direct", className: "dark:pointer-events-auto"));
            using var portalling = V.Mount(outside, V.Div(children: new VNode[]
            {
                V.Portal(root.Q<VisualElement>("layer"), children: new VNode[]
                {
                    V.Div(name: "portalled", className: "dark:pointer-events-auto"),
                }),
            }));
            var directElement = root.Q<VisualElement>("direct");
            var portalledElement = root.Q<VisualElement>("portalled");
            var light = (directElement.pickingMode, portalledElement.pickingMode);

            // Act
            VelvetTheme.IsDark = true;

            // Assert
            Assert.That((light, (directElement.pickingMode, portalledElement.pickingMode)),
                Is.EqualTo(((PickingMode.Ignore, PickingMode.Ignore), (PickingMode.Position, PickingMode.Position))));
        }

        [Test]
        public void Given_AButtonInsideAPointerEventsNoneScope_When_Mounted_Then_ItIgnoresPickingAndCanStillTakeFocus()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Div(className: "pointer-events-none",
                children: new VNode[] { V.Button(name: "button", text: "go") }));
            var button = _window.rootVisualElement.Q<Button>("button");
            ForcePanelUpdate(button.panel);

            // Act
            var canGrabFocus = button.canGrabFocus;

            // Assert
            Assert.That((button.pickingMode, canGrabFocus), Is.EqualTo((PickingMode.Ignore, true)));
        }
    }
}
