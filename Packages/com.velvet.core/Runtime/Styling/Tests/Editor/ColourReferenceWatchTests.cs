#if UNITY_EDITOR
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

        private (VisualElement parent, VisualElement watched, VisualElement unrelated) Tree()
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
            var (_, watched, _) = Tree();
            var ticks = 0;
            var watch = new ColorReferenceWatch(watched, _ => ticks++);

            Resolve(watched);

            watch.Stop();
            Assert.That(ticks, Is.EqualTo(1));
        }

        [Test]
        public void Given_AWatchedElement_When_ItsParentsStylesResolve_Then_ItIsReResolved()
        {
            var (parent, watched, _) = Tree();
            var ticks = 0;
            var watch = new ColorReferenceWatch(watched, _ => ticks++);

            Resolve(parent);

            watch.Stop();
            Assert.That(ticks, Is.EqualTo(1));
        }

        [Test]
        public void Given_AWatchedElement_When_AnUnrelatedElementsStylesResolve_Then_NothingRuns()
        {
            var (_, watched, unrelated) = Tree();
            var ticks = 0;
            var watch = new ColorReferenceWatch(watched, _ => ticks++);

            Resolve(unrelated);

            watch.Stop();
            Assert.That(ticks, Is.EqualTo(0));
        }

        [Test]
        public void Given_AStoppedWatch_When_ItsElementResolves_Then_NothingRuns()
        {
            var (_, watched, _) = Tree();
            var ticks = 0;
            new ColorReferenceWatch(watched, _ => ticks++).Stop();

            Resolve(watched);

            Assert.That(ticks, Is.EqualTo(0));
        }

        [Test]
        public void Given_UnchangedColours_When_TheWatchSamples_Then_NothingIsAllocated()
        {
            var (_, watched, _) = Tree();
            var watch = new ColorReferenceWatch(watched, _ => { });
            watch.Moved();

            var blocks = GCAllocationProbe.MedianBlocksDuring(() => watch.Moved());

            watch.Stop();
            Assert.That(blocks, Is.EqualTo(0));
        }

        [Test]
        public void Given_BorderCurrent_When_TheTextColourChanges_Then_TheBorderFollowsIt()
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "box", className: "w-[40px] h-[40px] border-2 border-current text-[#ff0000]"));
            var box = _window.rootVisualElement.Q<VisualElement>("box");
            ForcePanelUpdate(box.panel);
            var before = box.style.borderTopColor.value;

            box.style.color = Color.blue;
            ForcePanelUpdate(box.panel);

            Assert.That((before, box.style.borderTopColor.value), Is.EqualTo((Color.red, Color.blue)));
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
            var before = child.style.color.value;

            parent.style.color = Color.blue;
            ForcePanelUpdate(parent.panel);

            Assert.That((before, child.style.color.value), Is.EqualTo((Color.red, Color.blue)));
        }

        [Test]
        public void Given_AWidthBehindAVariant_When_PreflightSynced_Then_TheBorderColourLayerIsApplied()
        {
            var element = new VisualElement();

            PreflightBorderColor.Sync(element, new[] { "hover:border" });

            Assert.That(StyleArbitraryValueResolver.HasLayerAt(element, ArbitraryProperty.BorderColor, StyleLayerPriority.Preflight),
                Is.True);
        }
    }
}
#endif
