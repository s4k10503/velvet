using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    internal sealed class EaseClassTransitionPanelTests : PanelTestBase
    {
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        private sealed class Wide : Store<bool>
        {
            public Wide() : base(false) { }

            public void Set(bool value) => SetState(_ => value);

            protected override void ResetCore() => SetState(_ => false);
        }

        private static Wide s_wide;

        [Component]
        private static VNode Leaf()
        {
            var wide = Hooks.UseStore(s_wide, x => x);
            return V.Div(name: "leaf", className: "h-[10px] transition-all duration-1000 ease-[cubic-bezier(0.4,0,0.2,1)] "
                + (wide ? "w-[100px]" : "w-[0px]"));
        }

        // GREEN_ON_BASE(characterization): the base already retimes a transition an ease-[…] class names.
        // Returning no function from StyleEaseCurve.CurveFor's layer path leaves the keyword to ease it, which
        // reddens this case.
        [Test]
        public void Given_AnElementTransitioningEverythingOverASecondOnAnEaseClassCurve_When_ItsWidthClassChanges_Then_AQuarterSecondInItIsWhereTheCurveStandsThere()
        {
            // Arrange
            var now = 100.0;
            s_wide = new Wide();
            _mounted = V.Mount(_window.rootVisualElement, V.Component(Leaf, key: "root"));
            var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");
            EditorPanelTestHelpers.SetPanelTimeFunction(leaf.panel, () => now);
            ForcePanelUpdate(leaf.panel);
            ForcePanelUpdate(leaf.panel);
            var expected = StyleEaseCurve.Evaluate(new BezierCurve(0.4f, 0f, 0.2f, 1f), 0.25f);

            // Act
            s_wide.Set(true);
            _mounted.FlushStateForTest();
            ForcePanelUpdate(leaf.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(leaf.panel);
            now += 0.25;
            EditorPanelTestHelpers.DriveAnimationsOnce(leaf.panel);
            ForcePanelUpdate(leaf.panel);

            // Assert
            Assert.That(leaf.resolvedStyle.width / 100f, Is.EqualTo(expected).Within(0.02f));
        }
    }
}
