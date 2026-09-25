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

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            s_setStep = default;
            s_classFor = _ => InFlowBox;
        }

        // The card's className is chosen by the current step, so a case seeds s_classFor and advances the step to
        // drive a real reconcile patch from one class list to the next.
        [Component]
        private static VNode RenderHost()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(name: "host", className: "relative w-[400px] h-[300px]", children: new VNode[]
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
