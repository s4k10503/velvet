using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// CSS <c>clip-path</c> changes nothing about layout, so the clip-path wrapper has to leave the clipped
    /// element's box where its own classes put it: an absolutely positioned element keeps the box its edge
    /// offsets declare (and its <c>absolute inset-0</c> child fills it), and an in-flow one keeps its place in
    /// the flow. The layout expectations are derived from the declared host size and offsets; the mask case
    /// compares the wrapper's mask anchor with the element's measured box. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class ClipPathWrapperLayoutPanelTests : PanelTestBase
    {
        private const string Triangle = "clip-path-[polygon(50%_0%,100%_100%,0%_100%)]";
        private const string AbsoluteBox = "absolute left-[20px] top-[30px] right-[40px] bottom-[50px]";
        private const string InFlowBox = "w-[100px] h-[50px]";

        // Host 400x300; the four offsets above leave a 340x220 box at (20, 30).
        private static readonly Rect DeclaredAbsoluteBox = new Rect(20f, 30f, 340f, 220f);

        private static StateUpdater<int> s_setStep;
        private static Func<int, string> s_classFor;
        private static string s_hostClass;

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            s_setStep = default;
            s_classFor = _ => InFlowBox;
            s_hostClass = "";
        }

        // The card's className is chosen by the current step, so a case seeds s_classFor and advances the step to
        // drive a real reconcile patch from one class list to the next.
        [Component]
        private static VNode RenderHost()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(name: "host", className: "relative w-[400px] h-[300px] " + s_hostClass, children: new VNode[]
            {
                V.Div(name: "card", className: s_classFor(step), children: new VNode[]
                {
                    V.Div(name: "fill", className: "absolute inset-0"),
                }),
                V.Div(name: "next", className: "w-[10px] h-[10px]"),
            });
        }

        private VisualElement Named(string name) => _window.rootVisualElement.Q<VisualElement>(name);

        private void Mount(Func<int, string> classFor)
        {
            s_classFor = classFor;
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderHost));
            ForcePanelUpdate(_window.rootVisualElement.panel);
        }

        private void Step(int n)
        {
            s_setStep.Invoke(n);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
            ForcePanelUpdate(_window.rootVisualElement.panel);
        }

        private static bool IsClipWrapped(VisualElement element)
            => element.parent != null
                && element.parent.ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass);

        // Read from world bounds so the wrapper between the host and the element does not change the frame the
        // rectangle is expressed in.
        private Rect RelativeToHost(VisualElement element)
        {
            var host = Named("host").worldBound;
            var box = element.worldBound;
            return new Rect(box.x - host.x, box.y - host.y, box.width, box.height);
        }

        [Test]
        public void Given_AnAbsoluteClippedElementWithFourOffsets_When_LaidOut_Then_ItsInsetZeroChildFillsTheDeclaredBox()
        {
            // Arrange
            Mount(_ => AbsoluteBox + " " + Triangle);

            // Act
            var fill = RelativeToHost(Named("fill"));

            // Assert
            Assert.That((IsClipWrapped(Named("card")), fill), Is.EqualTo((true, DeclaredAbsoluteBox)));
        }

        [Test]
        public void Given_AnAbsoluteElement_When_ClipAddedByPatch_Then_ItsInsetZeroChildFillsTheDeclaredBox()
        {
            // Arrange
            Mount(s => s == 0 ? AbsoluteBox : AbsoluteBox + " " + Triangle);

            // Act
            Step(1);

            // Assert
            Assert.That((IsClipWrapped(Named("card")), RelativeToHost(Named("fill"))),
                Is.EqualTo((true, DeclaredAbsoluteBox)));
        }

        [Test]
        public void Given_AnAbsoluteClippedElementWithNoOffsets_When_LaidOut_Then_ItSitsAtTheHostsOrigin()
        {
            // Arrange: the host declares no alignment, so an absolute element with no offsets starts at its origin.
            Mount(_ => "absolute w-[50px] h-[40px] " + Triangle);

            // Act
            var card = RelativeToHost(Named("card"));

            // Assert
            Assert.That((IsClipWrapped(Named("card")), card), Is.EqualTo((true, new Rect(0f, 0f, 50f, 40f))));
        }

        [Test]
        public void Given_AnAbsoluteClippedElementWithNoOffsetsInAnEndAlignedHost_When_LaidOut_Then_ItSitsAtTheHostsEnd()
        {
            // Arrange: the host aligns its children to the end on both axes, which places an absolute element with
            // no offsets at its bottom-right corner.
            s_hostClass = "justify-end items-end";
            Mount(_ => "absolute w-[50px] h-[40px] " + Triangle);

            // Act
            var card = RelativeToHost(Named("card"));

            // Assert
            Assert.That((IsClipWrapped(Named("card")), card), Is.EqualTo((true, new Rect(350f, 260f, 50f, 40f))));
        }

        [Test]
        public void Given_AnAbsoluteClippedElementWithNoOffsetsInARowJustifiedToTheEnd_When_LaidOut_Then_ItSitsAtTheRowsEnd()
        {
            // Arrange: in a row, justify-end is horizontal, so an absolute element with no offsets goes to the right
            // edge and stays at the top.
            s_hostClass = "flex-row justify-end";
            Mount(_ => "absolute w-[50px] h-[40px] " + Triangle);

            // Act
            var card = RelativeToHost(Named("card"));

            // Assert
            Assert.That((IsClipWrapped(Named("card")), card), Is.EqualTo((true, new Rect(350f, 0f, 50f, 40f))));
        }

        [Test]
        public void Given_AnAbsoluteElement_When_ClipAddedByPatch_Then_TheMaskIsAnchoredAtTheElementsBox()
        {
            // Arrange
            Mount(s => s == 0 ? AbsoluteBox : AbsoluteBox + " " + Triangle);

            // Act
            Step(1);

            // Assert
            var card = Named("card");
            var binding = _mounted.Root.Reconciler.Context.ClipPathBindings[card];
            var ws = binding.Wrapper.style;
            Assert.That(
                (ws.backgroundPositionX.value.offset.value, ws.backgroundPositionY.value.offset.value),
                Is.EqualTo((card.layout.x + binding.Bounds.x, card.layout.y + binding.Bounds.y)));
        }

        // GREEN_ON_BASE(characterization): the base keeps every clip wrapper relative, so the card rejoins the flow.
        // A wrapper whose mode follows only the inner's geometry events is what reddens this.
        [Test]
        public void Given_AnAbsoluteClippedElement_When_AbsoluteIsDroppedWithoutMovingIt_Then_TheNextSiblingSitsBelowIt()
        {
            // Arrange: absolute or not, the 50x40 card sits at its wrapper's origin, so dropping `absolute`
            // leaves its own box where it was.
            Mount(s => (s == 0 ? "absolute " : "") + "w-[50px] h-[40px] " + Triangle);

            // Act
            Step(1);

            // Assert
            Assert.That((IsClipWrapped(Named("card")), RelativeToHost(Named("next")).y), Is.EqualTo((true, 40f)));
        }

        [Test]
        public void Given_AnInFlowClippedElement_When_AbsoluteIsAddedWithoutMovingIt_Then_ItsWrapperSpansTheHost()
        {
            // Arrange: under items-start the in-flow wrapper hugs the card at the host's origin, which is where
            // `absolute left-0 top-0` puts it too.
            s_hostClass = "items-start";
            Mount(s => (s == 0 ? "" : "absolute left-0 top-0 ") + "w-[50px] h-[40px] " + Triangle);

            // Act
            Step(1);

            // Assert
            var card = Named("card");
            Assert.That((IsClipWrapped(card), RelativeToHost(card.parent)),
                Is.EqualTo((true, new Rect(0f, 0f, 400f, 300f))));
        }

        [Test]
        public void Given_AnInFlowClippedElement_When_AHoverMakesItAbsoluteWithoutMovingIt_Then_ItsWrapperSpansTheHost()
        {
            // Arrange: the same no-move shape as the patch case above, with the position coming from a variant.
            s_hostClass = "items-start";
            Mount(_ => "w-[50px] h-[40px] hover:absolute hover:left-0 hover:top-0 " + Triangle);
            var card = Named("card");

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                card.SimulateEvent(over);
            }
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That((IsClipWrapped(card), RelativeToHost(card.parent)),
                Is.EqualTo((true, new Rect(0f, 0f, 400f, 300f))));
        }

        [Test]
        public void Given_AnElementMadeAbsoluteByAUserStylesheet_When_RerenderedWithoutMovingIt_Then_ItsWrapperStillSpansTheHost()
        {
            // Arrange: a stylesheet of the application's own sets the position, which no Velvet utility declares;
            // under items-start the card sits at the host's origin whichever mode its wrapper is in.
            var sheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.velvet.core/Runtime/Reconciler/Tests/Editor/ClipPathWrapperUserPosition.uss");
            _window.rootVisualElement.styleSheets.Add(sheet);
            s_hostClass = "items-start";
            Mount(s => "clip-test-overlay left-0 top-0 w-[50px] h-[40px] "
                + (s == 0 ? "bg-[#ff0000] " : "bg-[#0000ff] ") + Triangle);
            var card = Named("card");
            var mounted = RelativeToHost(card.parent);

            // Act
            Step(1);

            // Assert
            Assert.That((sheet != null, mounted, RelativeToHost(card.parent)),
                Is.EqualTo((true, new Rect(0f, 0f, 400f, 300f), new Rect(0f, 0f, 400f, 300f))));
        }

        // V.Anchored makes its element absolute with an inline position and no `absolute` class.
        [Component]
        private static VNode RenderAnchoredHost()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(name: "host", className: "relative w-[400px] h-[300px]", children: new VNode[]
            {
                V.Anchored(target: null, name: "card", className: s_classFor(step)),
            });
        }

        [Test]
        public void Given_AnAnchoredClippedElement_When_Patched_Then_ThePatchLeavesItsWrapperAbsolute()
        {
            // Arrange
            s_classFor = s => (s == 0 ? "w-[50px]" : "w-[60px]") + " h-[40px] " + Triangle;
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderAnchoredHost));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            var wrapper = Named("card").parent;
            var beforePatch = wrapper.style.position.value;

            // Act: the patch alone, before any layout pass could put a wrongly-written mode right again.
            s_setStep.Invoke(1);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That((beforePatch, wrapper.style.position.value),
                Is.EqualTo((Position.Absolute, Position.Absolute)));
        }

        // GREEN_ON_BASE(characterization): a parent's child variant still reaches its clipped child's slot.
        // A wrapper given the inner's align-self inline, over the class the variant puts on it, reddens this.
        [Test]
        public void Given_AParentChildVariantSelfEnd_When_ItsChildIsClipped_Then_TheChildSitsAtTheEnd()
        {
            // Arrange: the host is a 400px-wide column, so self-end puts a 100px card at x=300.
            s_hostClass = "[&>*]:self-end";
            Mount(_ => InFlowBox + " " + Triangle);

            // Act
            var card = RelativeToHost(Named("card"));

            // Assert
            Assert.That((IsClipWrapped(Named("card")), card), Is.EqualTo((true, new Rect(300f, 0f, 100f, 50f))));
        }

        // GREEN_ON_BASE(characterization): an in-flow clipped element keeps its slot in the flow.
        // A wrapper taken out of flow for every clipped element is what reddens this.
        [Test]
        public void Given_AnInFlowClippedElement_When_LaidOut_Then_TheNextSiblingSitsBelowIt()
        {
            // Arrange
            Mount(_ => InFlowBox + " " + Triangle);

            // Act
            var next = RelativeToHost(Named("next"));

            // Assert
            Assert.That((IsClipWrapped(Named("card")), next.y), Is.EqualTo((true, 50f)));
        }

        // GREEN_ON_BASE(characterization): a clipped element that stops being absolute returns to the flow.
        // A wrapper left out of flow after the inner's position changed is what reddens this.
        [Test]
        public void Given_AnAbsoluteClippedElement_When_AbsoluteRemovedByPatch_Then_TheNextSiblingSitsBelowIt()
        {
            // Arrange
            Mount(s => (s == 0 ? AbsoluteBox : InFlowBox) + " " + Triangle);

            // Act
            Step(1);

            // Assert
            Assert.That((IsClipWrapped(Named("card")), RelativeToHost(Named("next")).y), Is.EqualTo((true, 50f)));
        }
    }
}
