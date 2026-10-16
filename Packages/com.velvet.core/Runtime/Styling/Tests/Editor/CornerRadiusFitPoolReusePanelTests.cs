using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a Label returned to the pool with an arbitrary radius beside a radius class paints no radius when a
    /// plain Label rents it, read back through <c>resolvedStyle</c> on a real panel with the bundled stylesheet attached.
    /// </summary>
    internal sealed class CornerRadiusFitPoolReusePanelTests : PanelTestBase
    {
        private static StateUpdater<string> s_setPoolState;

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        public override void SetUp()
        {
            s_setPoolState = default;
            base.SetUp();
        }

        [Component]
        private static VNode RenderPoolReuse()
        {
            var (state, setState) = Hooks.UseState("rounded");
            s_setPoolState = setState;
            return state switch
            {
                "rounded" => V.Label(name: "leaf", className: "w-[100px] h-[40px] rounded rounded-[12px]", text: "a"),
                "plain" => V.Label(name: "leaf", className: "w-[100px] h-[20px]", text: "b"),
                _ => V.Div(name: "placeholder"),
            };
        }

        // GREEN_ON_BASE(characterization): the base releases the fit when a Label returns to the pool, so a recycled Label carries no radius there.
        // Dropping `CornerRadiusFit.Release(element)` from StyleArbitraryValueResolver.ClearAll reddens it: the
        // recycled Label refits the previous consumer's 12px to the 20px height it is laid out at. The class
        // beside the arbitrary radius is what makes the recycled Label resolve its style for the fit once more.
        // The bare `rounded` sorts ahead of `rounded-[12px]`, so the arbitrary radius is the one written inline.
        [Test]
        public void Given_AFittedLabelReturnedToThePool_When_RentedByAPlainLabel_Then_ItPaintsNoRadius()
        {
            // Arrange — VNodePool's Label pool is LIFO and nothing else rents a Label in between, so the plain
            // label is handed the same instance back.
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderPoolReuse));
            var scheduler = _mounted.Root.Reconciler.Context.BatchScheduler;
            ForcePanelUpdate(_window.rootVisualElement.panel);
            var first = _window.rootVisualElement.Q<Label>("leaf");
            s_setPoolState.Invoke("hidden");
            scheduler.DrainImmediateForTest();

            // Act
            s_setPoolState.Invoke("plain");
            scheduler.DrainImmediateForTest();
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert — the instance is carried beside the radius, since a label built afresh has none either.
            var recycled = _window.rootVisualElement.Q<Label>("leaf");
            Assert.That((ReferenceEquals(first, recycled), recycled.resolvedStyle.borderTopLeftRadius),
                Is.EqualTo((true, 0f)));
        }
    }
}
