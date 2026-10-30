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
            var watch = new ColorReferenceWatch(tree.Watched, _ => ticks.Count++);
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

            parent.style.backgroundColor = Color.blue;
            ForcePanelUpdate(parent.panel);

            Assert.That(_window.rootVisualElement.Q<VisualElement>("c1").style.backgroundColor.value, Is.EqualTo(Color.blue));
        }

        [Test]
        public void Given_TwoInheritorsOfOneParent_When_TheParentsBackgroundChanges_Then_TheSecondFollowsIt()
        {
            _mounted = V.Mount(_window.rootVisualElement, TwoInheritors("bg-[#ff0000]"));
            var parent = _window.rootVisualElement.Q<VisualElement>("parent");
            ForcePanelUpdate(parent.panel);

            parent.style.backgroundColor = Color.blue;
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

            parent.style.backgroundColor = Color.blue;
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
        public void Given_ADescendantsTransition_When_ItRuns_Then_TheWatchDoesNotSample()
        {
            // Transition events bubble, so one under the parent is heard there; only the parent's own counts.
            var tree = Tree();
            var sibling = new VisualElement();
            tree.Parent.Add(sibling);
            var watch = new ColorReferenceWatch(tree.Watched, _ => { });
            ForcePanelUpdate(tree.Watched.panel);
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(tree.Watched.panel, () => now);
            sibling.style.backgroundColor = Color.red;
            sibling.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { new("background-color") };
            sibling.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new(2f, TimeUnit.Second) };
            ForcePanelUpdate(sibling.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(sibling.panel);
            sibling.style.backgroundColor = Color.blue;
            ForcePanelUpdate(sibling.panel);
            now += 0.5;
            EditorPanelTestHelpers.DriveAnimationsOnce(sibling.panel);

            var sampling = watch.Sampling;
            watch.Stop();

            Assert.That(sampling, Is.False);
        }

        private VisualElement MountBorderInheritor(string parentClass)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "parent", className: parentClass, children: new VNode[]
                {
                    V.Div(name: "child", className: "w-[40px] h-[40px] border-2 border-inherit text-[#00ff00]"),
                }));
            var parent = _window.rootVisualElement.Q<VisualElement>("parent");
            ForcePanelUpdate(parent.panel);
            return parent;
        }

        [Test]
        public void Given_BorderInherit_When_TheParentsBorderColourIsInline_Then_TheChildTakesThatColour()
        {
            var parent = MountBorderInheritor("w-[80px] h-[80px] border-2");

            parent.style.borderTopColor = Color.red;
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
