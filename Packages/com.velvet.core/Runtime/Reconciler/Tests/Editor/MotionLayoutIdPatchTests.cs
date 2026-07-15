using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies V.Motion(layoutId:)'s FLIP behavior end to end through a real (simulated) panel and
    /// reconcile pass: when a Motion's resolved layout rect changes across a re-render while carrying
    /// the same layoutId, MotionLayoutIdDriver applies an inverse inline transform immediately after
    /// layout settles at the new rect, then springs it back to zero over subsequent ticks — instead of
    /// jump-cutting straight to the new pose.
    /// </summary>
    [TestFixture]
    internal sealed class MotionLayoutIdPatchTests : MotionSimulatedPanelTestsBase
    {
        private static StateUpdater<bool> s_setMoved;

        public override void SetUp()
        {
            base.SetUp();
            s_setMoved = default;
        }

        [Component]
        private static VNode SharedBoxRender()
        {
            var (moved, setMoved) = Hooks.UseState(false);
            s_setMoved = setMoved;
            return V.Div(children: new VNode[]
            {
                V.Motion(
                    name: "shared",
                    layoutId: "shared-box",
                    transition: new StyleTransitionConfig { Type = TransitionType.Spring, Stiffness = 100f, Damping = 10f, Mass = 1f },
                    className: moved
                        ? "absolute left-[200px] top-[0px] w-[100px] h-[100px]"
                        : "absolute left-[0px] top-[0px] w-[100px] h-[100px]"),
            });
        }

        [Test]
        public void Given_ALayoutIdMotion_When_ItsRectChangesAcrossARerender_Then_AnInverseTranslateAppliesImmediatelyAfterLayoutSettles()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(SharedBoxRender, key: "root"));
            Tick();
            var element = Root.Q<VisualElement>("shared");
            Assume.That(element, Is.Not.Null, "Precondition: the Motion mounted");

            // Act — move the Motion 200px to the right; wait one tick for GeometryChangedEvent to fire and
            // the driver to apply the inverse pose.
            s_setMoved.Invoke(true);
            mounted.FlushStateForTest();
            Tick();

            // Assert — the element is pinned at (roughly) its OLD screen position via an inline translate,
            // even though the resolved layout already moved it 200px right (translate.x ~= -200).
            Assert.That(element.style.translate.value.x.value, Is.LessThan(-50f));
        }

        [Test]
        public void Given_ALayoutIdMotion_When_SeveralTicksElapseAfterARectChange_Then_TheInverseTranslateSettlesToZero()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(SharedBoxRender, key: "root"));
            Tick();
            var element = Root.Q<VisualElement>("shared");
            s_setMoved.Invoke(true);
            mounted.FlushStateForTest();
            Tick();
            Assume.That(element.style.translate.value.x.value, Is.LessThan(-50f),
                "Precondition: the inverse pose applied after the rect change");

            // Act — let the spring settle.
            AdvancePast(2f);

            // Assert — the inline translate override is cleared once the spring settles (StyleKeyword.Null),
            // reporting back to StyleKeyword.Auto / 0 the way MotionSpringDriver.ClearInlineOverrides always
            // leaves a settled channel.
            Assert.That(element.style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
        }
    }
}
