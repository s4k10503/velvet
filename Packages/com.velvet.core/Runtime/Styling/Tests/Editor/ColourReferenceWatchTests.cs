using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies when a colour read off a live source (<c>border-current</c>, <c>text-inherit</c>, …) is written
    /// again: on the style-resolved events of the elements its sources live on, and at no other time.
    /// </summary>
    internal sealed class ColourReferenceWatchTests : PanelTestBase
    {
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        // Restyles target through the panel's own style pass, which is what sends the event: an event sent by hand
        // would pass without the custom property UI Toolkit requires of its target.
        private void Restyle(VisualElement target)
        {
            target.style.color = target.style.color.value == Color.blue ? Color.green : Color.blue;
            ForcePanelUpdate(target.panel);
        }

        private (VisualElement Parent, VisualElement Watched, VisualElement Unrelated) Tree()
        {
            var root = _window.rootVisualElement;
            var parent = new VisualElement();
            var watched = new VisualElement();
            var unrelated = new VisualElement();
            parent.Add(watched);
            root.Add(parent);
            root.Add(unrelated);
            ForcePanelUpdate(root.panel);
            return (parent, watched, unrelated);
        }

        // A watch over the tree's watched element, settled so that the marking's own restyle is not counted.
        private (ColorReferenceWatch Watch, Counter Ticks, VisualElement Parent, VisualElement Watched, VisualElement Unrelated) Watching()
        {
            var tree = Tree();
            var ticks = new Counter();
            ColorReferenceWatch watch = null;
            watch = new ColorReferenceWatch(tree.Watched, _ =>
            {
                ticks.Count++;
                watch.Moved();
            });
            ForcePanelUpdate(tree.Watched.panel);
            ForcePanelUpdate(tree.Watched.panel);
            ticks.Count = 0;
            return (watch, ticks, tree.Parent, tree.Watched, tree.Unrelated);
        }

        private sealed class Counter
        {
            public int Count;
        }

        [Test]
        public void Given_AWatchedElement_When_ItsStylesResolve_Then_ItIsReResolved()
        {
            var w = Watching();

            Restyle(w.Watched);

            w.Watch.Stop();
            Assert.That(w.Ticks.Count, Is.GreaterThan(0));
        }

        [Test]
        public void Given_AWatchedElement_When_ItsParentsStylesResolve_Then_ItIsReResolved()
        {
            var w = Watching();

            Restyle(w.Parent);

            w.Watch.Stop();
            Assert.That(w.Ticks.Count, Is.GreaterThan(0));
        }

        [Test]
        public void Given_AWatchedElement_When_AnUnrelatedElementsStylesResolve_Then_NothingRuns()
        {
            var w = Watching();

            Restyle(w.Unrelated);

            w.Watch.Stop();
            Assert.That(w.Ticks.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_AStoppedWatch_When_ItsElementResolves_Then_NothingRuns()
        {
            var w = Watching();
            w.Watch.Stop();

            Restyle(w.Watched);

            Assert.That(w.Ticks.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_AWatchedElement_When_TheWatchStops_Then_TheMarkerLeavesBothElements()
        {
            var w = Watching();

            w.Watch.Stop();

            Assert.That(w.Watched.ClassListContains(ColorReferenceWatch.MarkerClass)
                || w.Parent.ClassListContains(ColorReferenceWatch.MarkerClass), Is.False);
        }

        [Test]
        public void Given_AWatchedElement_When_ItIsWatched_Then_TheMarkerGivesItsStyleACustomProperty()
        {
            var w = Watching();

            var marked = w.Watched.ClassListContains(ColorReferenceWatch.MarkerClass)
                && w.Parent.ClassListContains(ColorReferenceWatch.MarkerClass);

            w.Watch.Stop();
            Assert.That(marked, Is.True);
        }

        [Test]
        public void Given_UnchangedColours_When_TheWatchSamples_Then_NothingIsAllocated()
        {
            var tree = Tree();
            var watch = new ColorReferenceWatch(tree.Watched, _ => { });
            watch.Moved();

            var blocks = GCAllocationProbe.MedianBlocksDuring(() => watch.Moved());

            watch.Stop();
            Assert.That(GCAllocationCanary.BlocksForKnownArray() > 0 ? blocks : -1, Is.Zero);
        }

        [Test]
        public void Given_BorderCurrent_When_TheTextColourChanges_Then_TheBorderFollowsIt()
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "box", className: "w-[40px] h-[40px] border-2 border-current text-[#ff0000]"));
            var box = _window.rootVisualElement.Q<VisualElement>("box");
            ForcePanelUpdate(box.panel);

            box.style.color = Color.blue;
            ForcePanelUpdate(box.panel);

            Assert.That(box.style.borderTopColor.value, Is.EqualTo(Color.blue));
        }

        [Test]
        public void Given_BorderCurrent_When_Mounted_Then_TheBorderIsTheTextColour()
        {
            // The control for the case above: the border starts at the text colour, so following it is a change.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "box", className: "w-[40px] h-[40px] border-2 border-current text-[#ff0000]"));
            var box = _window.rootVisualElement.Q<VisualElement>("box");

            ForcePanelUpdate(box.panel);

            Assert.That(box.style.borderTopColor.value, Is.EqualTo(Color.red));
        }

        [Test]
        public void Given_TextInherit_When_TheParentsTextColourChanges_Then_TheChildFollowsIt()
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "parent", className: "text-[#ff0000]", children: new VNode[]
                {
                    V.Div(name: "child", className: "w-[40px] h-[40px] text-inherit"),
                }));
            var parent = _window.rootVisualElement.Q<VisualElement>("parent");
            var child = _window.rootVisualElement.Q<VisualElement>("child");
            ForcePanelUpdate(parent.panel);

            parent.style.color = Color.blue;
            ForcePanelUpdate(parent.panel);

            Assert.That(child.style.color.value, Is.EqualTo(Color.blue));
        }

        [Test]
        public void Given_BackgroundInherit_When_TheParentsBackgroundTransitionsAndEnds_Then_TheChildTakesTheEndColour()
        {
            // Arrange — a transitioning background bumps no style pass after the first, so only the transition's own
            // events and ticks reach the watch.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "parent", className: "bg-[#ff0000]", children: new VNode[]
                {
                    V.Div(name: "child", className: "w-[40px] h-[40px] bg-inherit"),
                }));
            var parent = _window.rootVisualElement.Q<VisualElement>("parent");
            var child = _window.rootVisualElement.Q<VisualElement>("child");
            ForcePanelUpdate(parent.panel);
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(parent.panel, () => now);
            parent.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { new("background-color") };
            parent.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new(2f, TimeUnit.Second) };
            parent.style.transitionTimingFunction = new System.Collections.Generic.List<EasingFunction> { new(EasingMode.Linear) };
            ForcePanelUpdate(parent.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(parent.panel);

            // Act
            parent.style.backgroundColor = Color.blue;
            ForcePanelUpdate(parent.panel);
            now += 3.0;
            EditorPanelTestHelpers.DriveAnimationsOnce(parent.panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(parent.panel);
            ForcePanelUpdate(parent.panel);

            // Assert
            Assert.That(child.style.backgroundColor.value, Is.EqualTo(Color.blue));
        }

        private VNode TwoInheritors(string parentClass, bool second = true)
            => V.Div(name: "parent", className: parentClass, children: second
                ? new VNode[]
                {
                    V.Div(name: "c1", className: "w-[40px] h-[40px] bg-inherit", key: "c1"),
                    V.Div(name: "c2", className: "w-[40px] h-[40px] bg-inherit", key: "c2"),
                }
                : new VNode[] { V.Div(name: "c1", className: "w-[40px] h-[40px] bg-inherit", key: "c1") });

        [Test]
        public void Given_TwoInheritorsOfOneParent_When_TheParentsBackgroundChanges_Then_TheFirstFollowsIt()
        {
            _mounted = V.Mount(_window.rootVisualElement, TwoInheritors("bg-[#ff0000]"));
            var parent = _window.rootVisualElement.Q<VisualElement>("parent");
            ForcePanelUpdate(parent.panel);

            _mounted.Render(TwoInheritors("bg-[#0000ff]"));
            ForcePanelUpdate(parent.panel);

            Assert.That(_window.rootVisualElement.Q<VisualElement>("c1").style.backgroundColor.value, Is.EqualTo(Color.blue));
        }

        [Test]
        public void Given_TwoInheritorsOfOneParent_When_TheParentsBackgroundChanges_Then_TheSecondFollowsIt()
        {
            _mounted = V.Mount(_window.rootVisualElement, TwoInheritors("bg-[#ff0000]"));
            var parent = _window.rootVisualElement.Q<VisualElement>("parent");
            ForcePanelUpdate(parent.panel);

            _mounted.Render(TwoInheritors("bg-[#0000ff]"));
            ForcePanelUpdate(parent.panel);

            Assert.That(_window.rootVisualElement.Q<VisualElement>("c2").style.backgroundColor.value, Is.EqualTo(Color.blue));
        }

        [Test]
        public void Given_TwoInheritorsOfOneParent_When_OneUnmounts_Then_TheOtherStillFollowsTheParent()
        {
            _mounted = V.Mount(_window.rootVisualElement, TwoInheritors("bg-[#ff0000]"));
            var parent = _window.rootVisualElement.Q<VisualElement>("parent");
            ForcePanelUpdate(parent.panel);
            _mounted.Render(TwoInheritors("bg-[#ff0000]", second: false));
            ForcePanelUpdate(parent.panel);

            _mounted.Render(TwoInheritors("bg-[#0000ff]", second: false));
            ForcePanelUpdate(parent.panel);

            Assert.That(_window.rootVisualElement.Q<VisualElement>("c1").style.backgroundColor.value, Is.EqualTo(Color.blue));
        }

        [Test]
        public void Given_AWatchedElementThatIsAlsoAParentOfAnInheritor_When_ItsTextColourChanges_Then_ItsOwnBorderFollows()
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "box", className: "w-[40px] h-[40px] border-2 border-current text-[#ff0000]", children: new VNode[]
                {
                    V.Div(name: "g", className: "w-[10px] h-[10px] bg-inherit"),
                }));
            var box = _window.rootVisualElement.Q<VisualElement>("box");
            ForcePanelUpdate(box.panel);

            box.style.color = Color.blue;
            ForcePanelUpdate(box.panel);

            Assert.That(box.style.borderTopColor.value, Is.EqualTo(Color.blue));
        }

        [Test]
        public void Given_ADescendantsTransition_When_ItRuns_Then_TheWatchIsNotReadOnTicks()
        {
            // Transition events bubble, so one under the parent is heard there; only the parent's own starts a tick.
            var tree = Tree();
            var sibling = new VisualElement();
            tree.Parent.Add(sibling);
            var ticks = new Counter();
            ColorReferenceWatch watch = null;
            watch = new ColorReferenceWatch(tree.Watched, _ =>
            {
                ticks.Count++;
                watch.Moved();
            });
            watch.Moved();
            ForcePanelUpdate(tree.Watched.panel);
            var clock = new double[] { 100.0 };
            EditorPanelTestHelpers.SetPanelTimeFunction(tree.Watched.panel, () => clock[0]);
            sibling.style.backgroundColor = Color.red;
            sibling.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { new("background-color") };
            sibling.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new(2f, TimeUnit.Second) };
            ForcePanelUpdate(sibling.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(sibling.panel);
            sibling.style.backgroundColor = Color.blue;
            ForcePanelUpdate(sibling.panel);
            ticks.Count = 0;

            clock[0] += 0.5;
            EditorPanelTestHelpers.DriveAnimationsOnce(sibling.panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(sibling.panel);
            watch.Stop();

            Assert.That(ticks.Count, Is.Zero);
        }

        [Test]
        public void Given_TwoInheritorsUnderATransitioningParent_When_OneUnmounts_Then_TheOtherStillReachesTheEndColour()
        {
            _mounted = V.Mount(_window.rootVisualElement, TwoInheritors("bg-[#ff0000]"));
            var parent = _window.rootVisualElement.Q<VisualElement>("parent");
            ForcePanelUpdate(parent.panel);
            var clock = new double[] { 100.0 };
            EditorPanelTestHelpers.SetPanelTimeFunction(parent.panel, () => clock[0]);
            parent.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { new("background-color") };
            parent.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new(2f, TimeUnit.Second) };
            parent.style.transitionTimingFunction = new System.Collections.Generic.List<EasingFunction> { new(EasingMode.Linear) };
            ForcePanelUpdate(parent.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(parent.panel);
            parent.style.backgroundColor = Color.blue;
            ForcePanelUpdate(parent.panel);
            _mounted.Render(TwoInheritors("bg-[#ff0000]", second: false));
            ForcePanelUpdate(parent.panel);

            clock[0] += 3.0;
            EditorPanelTestHelpers.DriveAnimationsOnce(parent.panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(parent.panel);
            ForcePanelUpdate(parent.panel);

            Assert.That(_window.rootVisualElement.Q<VisualElement>("c1").style.backgroundColor.value, Is.EqualTo(Color.blue));
        }

        private static VNode BorderInheritorTree(string parentClass)
            => V.Div(name: "parent", className: parentClass, children: new VNode[]
            {
                V.Div(name: "child", className: "w-[40px] h-[40px] border-2 border-inherit text-[#00ff00]"),
            });

        private VisualElement MountBorderInheritor(string parentClass)
        {
            _mounted = V.Mount(_window.rootVisualElement, BorderInheritorTree(parentClass));
            var parent = _window.rootVisualElement.Q<VisualElement>("parent");
            ForcePanelUpdate(parent.panel);
            return parent;
        }

        [Test]
        public void Given_BorderInherit_When_TheParentsBorderColourClassChanges_Then_TheChildTakesThatColour()
        {
            var parent = MountBorderInheritor("w-[80px] h-[80px] border-2 border-[#0000ff]");

            _mounted.Render(BorderInheritorTree("w-[80px] h-[80px] border-2 border-[#ff0000]"));
            ForcePanelUpdate(parent.panel);

            Assert.That(_window.rootVisualElement.Q<VisualElement>("child").style.borderTopColor.value, Is.EqualTo(Color.red));
        }

        [Test]
        public void Given_BorderInherit_When_TheParentNamesNoBorderColour_Then_TheChildTakesWhatTheParentPaints()
        {
            // The child's own text colour is green; what the parent paints is not.
            var parent = MountBorderInheritor("w-[80px] h-[80px] border-2");

            Assert.That(_window.rootVisualElement.Q<VisualElement>("child").style.borderTopColor.value,
                Is.EqualTo(parent.resolvedStyle.borderTopColor));
        }

        // Velvet writes a StyleOverrides colour inline, which raises no style pass of its own.
        private static VNode OverriddenParent(StyleOverrides styles)
            => V.Div(name: "parent", className: "w-[80px] h-[80px]", styles: styles, children: new VNode[]
            {
                V.Div(name: "child", className: "w-[40px] h-[40px] bg-inherit text-inherit"),
            });

        [Test]
        public void Given_BackgroundInherit_When_TheParentsBackgroundStyleOverrideChanges_Then_TheChildFollowsIt()
        {
            _mounted = V.Mount(_window.rootVisualElement, OverriddenParent(new StyleOverrides { BackgroundColor = Color.red }));
            var parent = _window.rootVisualElement.Q<VisualElement>("parent");
            ForcePanelUpdate(parent.panel);

            _mounted.Render(OverriddenParent(new StyleOverrides { BackgroundColor = Color.blue }));
            ForcePanelUpdate(parent.panel);

            Assert.That(_window.rootVisualElement.Q<VisualElement>("child").style.backgroundColor.value, Is.EqualTo(Color.blue));
        }

        [Test]
        public void Given_TextInherit_When_TheParentsTextColourStyleOverrideChanges_Then_TheChildFollowsIt()
        {
            _mounted = V.Mount(_window.rootVisualElement, OverriddenParent(new StyleOverrides { Color = Color.red }));
            var parent = _window.rootVisualElement.Q<VisualElement>("parent");
            ForcePanelUpdate(parent.panel);

            _mounted.Render(OverriddenParent(new StyleOverrides { Color = Color.blue }));
            ForcePanelUpdate(parent.panel);

            Assert.That(_window.rootVisualElement.Q<VisualElement>("child").style.color.value, Is.EqualTo(Color.blue));
        }

        // Two inheritors under a parent whose background transitions: the transition events once kept one entry per
        // parent, so only the last inheritor sampled and unmounting it stopped the other.
        [Test]
        public void Given_TwoInheritorsUnderATransitioningParent_When_ItEnds_Then_TheFirstReachesTheEndColour()
        {
            _mounted = V.Mount(_window.rootVisualElement, TwoInheritors("bg-[#ff0000]"));
            var parent = _window.rootVisualElement.Q<VisualElement>("parent");
            ForcePanelUpdate(parent.panel);
            var now = 100.0;
            var clock = new double[] { now };
            EditorPanelTestHelpers.SetPanelTimeFunction(parent.panel, () => clock[0]);
            parent.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { new("background-color") };
            parent.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new(2f, TimeUnit.Second) };
            parent.style.transitionTimingFunction = new System.Collections.Generic.List<EasingFunction> { new(EasingMode.Linear) };
            ForcePanelUpdate(parent.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(parent.panel);

            parent.style.backgroundColor = Color.blue;
            ForcePanelUpdate(parent.panel);
            clock[0] += 3.0;
            EditorPanelTestHelpers.DriveAnimationsOnce(parent.panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(parent.panel);
            ForcePanelUpdate(parent.panel);

            Assert.That(_window.rootVisualElement.Q<VisualElement>("c1").style.backgroundColor.value, Is.EqualTo(Color.blue));
        }

        [Test]
        public void Given_TwoInheritorsUnderATransitioningParent_When_ItEnds_Then_TheSecondReachesTheEndColour()
        {
            _mounted = V.Mount(_window.rootVisualElement, TwoInheritors("bg-[#ff0000]"));
            var parent = _window.rootVisualElement.Q<VisualElement>("parent");
            ForcePanelUpdate(parent.panel);
            var now = 100.0;
            var clock = new double[] { now };
            EditorPanelTestHelpers.SetPanelTimeFunction(parent.panel, () => clock[0]);
            parent.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { new("background-color") };
            parent.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new(2f, TimeUnit.Second) };
            parent.style.transitionTimingFunction = new System.Collections.Generic.List<EasingFunction> { new(EasingMode.Linear) };
            ForcePanelUpdate(parent.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(parent.panel);

            parent.style.backgroundColor = Color.blue;
            ForcePanelUpdate(parent.panel);
            clock[0] += 3.0;
            EditorPanelTestHelpers.DriveAnimationsOnce(parent.panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(parent.panel);
            ForcePanelUpdate(parent.panel);

            Assert.That(_window.rootVisualElement.Q<VisualElement>("c2").style.backgroundColor.value, Is.EqualTo(Color.blue));
        }

        // The parent's background fades red to blue over two seconds, the panel's clock set by the case.
        private VisualElement MountFadingParent(double[] clock, string parentClass)
        {
            _mounted = V.Mount(_window.rootVisualElement, TwoInheritors(parentClass));
            var parent = _window.rootVisualElement.Q<VisualElement>("parent");
            ForcePanelUpdate(parent.panel);
            EditorPanelTestHelpers.SetPanelTimeFunction(parent.panel, () => clock[0]);
            parent.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { new("background-color") };
            parent.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new(2f, TimeUnit.Second) };
            parent.style.transitionTimingFunction = new System.Collections.Generic.List<EasingFunction> { new(EasingMode.Linear) };
            ForcePanelUpdate(parent.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(parent.panel);
            return parent;
        }

        [Test]
        public void Given_AnInheritorUnderATransitioningParent_When_TheTransitionIsHalfway_Then_ItIsWhatTheParentShows()
        {
            var clock = new double[] { 100.0 };
            var parent = MountFadingParent(clock, "bg-[#ff0000]");
            _mounted.Render(TwoInheritors("bg-[#0000ff]"));
            ForcePanelUpdate(parent.panel);

            clock[0] += 1.0;
            EditorPanelTestHelpers.DriveAnimationsOnce(parent.panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(parent.panel);
            ForcePanelUpdate(parent.panel);

            Assert.That(_window.rootVisualElement.Q<VisualElement>("c1").style.backgroundColor.value,
                Is.EqualTo(parent.resolvedStyle.backgroundColor));
        }

        [Test]
        public void Given_AnInheritorUnderATransitioningParent_When_TheTransitionIsHalfway_Then_ItIsNotYetTheEndColour()
        {
            // The control for the case above: halfway is neither end of the fade.
            var clock = new double[] { 100.0 };
            var parent = MountFadingParent(clock, "bg-[#ff0000]");
            _mounted.Render(TwoInheritors("bg-[#0000ff]"));
            ForcePanelUpdate(parent.panel);

            clock[0] += 1.0;
            EditorPanelTestHelpers.DriveAnimationsOnce(parent.panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(parent.panel);
            ForcePanelUpdate(parent.panel);

            Assert.That(_window.rootVisualElement.Q<VisualElement>("c1").style.backgroundColor.value, Is.Not.EqualTo(Color.blue));
        }

        [Test]
        public void Given_AnInheritor_When_TheParentsBackgroundIsWrittenDirectlyToItsStyle_Then_ItFollowsOnTheNextTick()
        {
            _mounted = V.Mount(_window.rootVisualElement, TwoInheritors("bg-[#ff0000]"));
            var parent = _window.rootVisualElement.Q<VisualElement>("parent");
            ForcePanelUpdate(parent.panel);
            var clock = new double[] { 100.0 };
            EditorPanelTestHelpers.SetPanelTimeFunction(parent.panel, () => clock[0]);

            parent.style.backgroundColor = Color.blue;
            clock[0] += 1.0;
            EditorPanelTestHelpers.DriveSchedulerOnce(parent.panel);

            Assert.That(_window.rootVisualElement.Q<VisualElement>("c1").style.backgroundColor.value, Is.EqualTo(Color.blue));
        }

        [Test]
        public void Given_BackgroundInherit_When_TheParentHasAShadow_Then_TheChildTakesTheParentsBackground()
        {
            // The shadow's face suppresses the parent's own background with a sentinel and paints it itself.
            _mounted = V.Mount(_window.rootVisualElement, TwoInheritors("w-[80px] h-[80px] shadow-md bg-white"));
            var parent = _window.rootVisualElement.Q<VisualElement>("parent");

            ForcePanelUpdate(parent.panel);
            ForcePanelUpdate(parent.panel);

            Assert.That(_window.rootVisualElement.Q<VisualElement>("c1").style.backgroundColor.value, Is.EqualTo(Color.white));
        }

        [Test]
        public void Given_BorderInherit_When_TheParentIsDashed_Then_TheChildTakesTheParentsBorderColour()
        {
            VelvetPalette.TryResolveColorToken("red-500", out var red);
            var parent = MountBorderInheritor("w-[80px] h-[80px] border-dashed border-2 border-red-500");

            ForcePanelUpdate(parent.panel);

            Assert.That(_window.rootVisualElement.Q<VisualElement>("child").style.borderTopColor.value, Is.EqualTo(red));
        }

        [Test]
        public void Given_TextInherit_When_Mounted_Then_TheChildTakesTheParentsTextColour()
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "parent", className: "text-[#ff0000]", children: new VNode[]
                {
                    V.Div(name: "child", className: "w-[40px] h-[40px] text-inherit"),
                }));
            var parent = _window.rootVisualElement.Q<VisualElement>("parent");

            ForcePanelUpdate(parent.panel);

            Assert.That(_window.rootVisualElement.Q<VisualElement>("child").style.color.value, Is.EqualTo(Color.red));
        }
    }
}
