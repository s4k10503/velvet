using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a Label returned to the pool with an arbitrary radius beside a radius class, or with a radius its own
    /// rule gave it, paints no radius when a
    /// plain Label rents it, read back through <c>resolvedStyle</c> on a real panel with the bundled stylesheet attached.
    /// </summary>
    internal sealed class CornerRadiusFitPoolReusePanelTests : PanelTestBase
    {
        private static StateUpdater<string> s_setPoolState;

        private static string s_initialPoolState = "rounded";

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        public override void SetUp()
        {
            s_setPoolState = default;
            s_initialPoolState = "rounded";
            base.SetUp();
        }

        [Component]
        private static VNode RenderPoolReuse()
        {
            var (state, setState) = Hooks.UseState(s_initialPoolState);
            s_setPoolState = setState;
            return state switch
            {
                "rounded" => V.Label(name: "leaf", className: "w-[100px] h-[40px] rounded rounded-[12px]", text: "a"),
                "own-rule" => V.Label(name: "leaf", className: "w-[100px] h-[40px]", text: "a"),
                "own-rule-radius" => V.Label(name: "leaf", className: "w-[100px] h-[40px] layout-id-test-uneven", text: "a"),
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

        // GREEN_ON_BASE(characterization): the base releases the fit when a Label returns to the pool whichever way it attached.
        [Test]
        public void Given_ALabelFittedFromItsOwnStylesheetRadiusReturnedToThePool_When_RentedByAPlainLabel_Then_ItPaintsNoRadius()
        {
            // Arrange — the label gets its radius from a rule no Velvet class names, by a class swap after its first
            // layout. The pool is LIFO and nothing else rents a Label in between.
            _window.rootVisualElement.styleSheets.Add(
                UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.velvet.core/Runtime/Reconciler/Tests/Editor/LayoutIdTestRules.uss"));
            s_initialPoolState = "own-rule";
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderPoolReuse));
            var scheduler = _mounted.Root.Reconciler.Context.BatchScheduler;
            ForcePanelUpdate(_window.rootVisualElement.panel);
            var first = _window.rootVisualElement.Q<Label>("leaf");
            s_setPoolState.Invoke("own-rule-radius");
            scheduler.DrainImmediateForTest();
            ForcePanelUpdate(_window.rootVisualElement.panel);
            s_setPoolState.Invoke("hidden");
            scheduler.DrainImmediateForTest();

            // Act
            s_setPoolState.Invoke("plain");
            scheduler.DrainImmediateForTest();
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert — NaN stands for a Label that is not the first one's instance, which no radius equals.
            var recycled = _window.rootVisualElement.Q<Label>("leaf");
            Assert.That(ReferenceEquals(first, recycled) ? recycled.resolvedStyle.borderTopLeftRadius : float.NaN, Is.EqualTo(0f));
        }
    }
}
