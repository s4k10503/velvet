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

        private static void Resolve(VisualElement target)
        {
            using var evt = CustomStyleResolvedEvent.GetPooled();
            evt.target = target;
            target.SendEvent(evt);
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

        [Test]
        public void Given_AWatchedElement_When_ItsStylesResolve_Then_ItIsReResolved()
        {
            var tree = Tree();
            var ticks = 0;
            var watch = new ColorReferenceWatch(tree.Watched, _ => ticks++);

            Resolve(tree.Watched);

            watch.Stop();
            Assert.That(ticks, Is.EqualTo(1));
        }

        [Test]
        public void Given_AWatchedElement_When_ItsParentsStylesResolve_Then_ItIsReResolved()
        {
            var tree = Tree();
            var ticks = 0;
            var watch = new ColorReferenceWatch(tree.Watched, _ => ticks++);

            Resolve(tree.Parent);

            watch.Stop();
            Assert.That(ticks, Is.EqualTo(1));
        }

        [Test]
        public void Given_AWatchedElement_When_AnUnrelatedElementsStylesResolve_Then_NothingRuns()
        {
            var tree = Tree();
            var ticks = 0;
            var watch = new ColorReferenceWatch(tree.Watched, _ => ticks++);

            Resolve(tree.Unrelated);

            watch.Stop();
            Assert.That(ticks, Is.EqualTo(0));
        }

        [Test]
        public void Given_AStoppedWatch_When_ItsElementResolves_Then_NothingRuns()
        {
            var tree = Tree();
            var ticks = 0;
            new ColorReferenceWatch(tree.Watched, _ => ticks++).Stop();

            Resolve(tree.Watched);

            Assert.That(ticks, Is.EqualTo(0));
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
