using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what an inheritor of a layoutId Motion reads while the Motion is drawn scaled differently on each axis: the
    /// colour its projected face paints, not the clearing value the face leaves in the native slot.
    /// </summary>
    internal sealed class ProjectedCornerFaceColourReferenceTests : MotionSimulatedPanelTestsBase
    {
        private static readonly StyleTransitionConfig s_layoutSpring =
            new() { Type = TransitionType.Spring, Stiffness = 100f, Damping = 10f, Mass = 1f };

        private static StateUpdater<int> s_setStep;
        private MountedTree _tree;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            s_setStep = default;
        }

        [TearDown]
        public void DisposeTree() => _tree?.Dispose();

        // Step 0 lays the box out at 100x100 and step 1 at 400x200; the box holds one inheritor per slot kind.
        private static VNode BoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            var size = step == 0 ? "w-[100px] h-[100px]" : "w-[400px] h-[200px]";
            return V.Div(children: new VNode[]
            {
                V.Motion(name: "box", layoutId: "box", transition: s_layoutSpring,
                    className: $"left-[0px] top-[0px] {size} rounded-[20px] bg-blue-500 border-2 border-red-500",
                    children: new VNode[]
                    {
                        V.Div(name: "bg", className: "w-[10px] h-[10px] bg-inherit"),
                        V.Div(name: "border", className: "w-[10px] h-[10px] border-2 border-inherit"),
                    }),
            });
        }

        private VisualElement MountProjected(string name)
        {
            VelvetStyleUtilities.AttachTo(Root);
            _tree = V.Mount(Root, V.Component(BoxRender, key: "root"));
            Tick();
            s_setStep.Invoke(1);
            _tree.FlushStateForTest();
            Tick();
            for (var i = 0; i < 3; i++) Tick();
            return Root.Q<VisualElement>(name);
        }

        [Test]
        public void Given_ABackgroundInheritorOfAProjectedBox_When_TheFaceIsDrawn_Then_ItTakesTheFacesBackground()
        {
            VelvetPalette.TryResolveColorToken("blue-500", out var blue);
            var child = MountProjected("bg");

            Assert.That(child.style.backgroundColor.value, Is.EqualTo(blue));
        }

        [Test]
        public void Given_ABorderInheritorOfAProjectedBox_When_TheFaceIsDrawn_Then_ItTakesTheFacesBorder()
        {
            VelvetPalette.TryResolveColorToken("red-500", out var red);
            var child = MountProjected("border");

            Assert.That(child.style.borderTopColor.value, Is.EqualTo(red));
        }
    }
}
