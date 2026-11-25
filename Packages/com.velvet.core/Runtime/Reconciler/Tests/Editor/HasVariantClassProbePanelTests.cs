using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies a <c>has-[.class]:</c> element against the probe a variant payload of a class of the application's own
    /// sends out beside the element it reached, which carries the class until the panel's next layout pass: the probe is
    /// no descendant of the application's, so the payload alone still leaves the element unmatched, as
    /// <c>:has(.foo)</c> does not match a class only <c>hover:foo</c> applies.
    /// </summary>
    internal sealed class HasVariantClassProbePanelTests : PanelTestBase
    {
        private static StateUpdater<int> s_render;

        public override void SetUp()
        {
            base.SetUp();
            s_render = default;
        }

        [Component(Compiler = false)]
        private static VNode Holder()
        {
            var (_, setRender) = Hooks.UseState(0);
            s_render = setRender;
            return V.Div(name: "holder", className: "has-[.lit]:bg-mark", children: new VNode[]
            {
                V.Div(name: "lamp", className: "hover:lit"),
            });
        }

        [Test]
        public void Given_AHasClassElementAboveAHoveredPayloadOfTheClass_When_TheElementRendersWhileTheProbeStands_Then_ItDoesNotMatch()
        {
            // Arrange — the first hover's payload of lit, a class of the application's own, sends a probe out beside
            // "lamp"; no layout pass runs here, so it stands.
            _mounted = V.Mount(_window.rootVisualElement, V.Component(Holder, key: "holder"));
            var holder = _window.rootVisualElement.Q<VisualElement>("holder");
            using (var over = PointerOverEvent.GetPooled())
            {
                _window.rootVisualElement.Q<VisualElement>("lamp").SimulateEvent(over);
            }
            var probeStands = holder.Q<VisualElement>(className: StyleUserClassProperties.MarkerClass) != null;

            // Act — "holder" renders again, and its own pass reads its descendants.
            s_render.Invoke(previous => previous + 1);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — null where no probe stood.
            Assert.That(probeStands ? holder.ClassListContains("bg-mark") : (bool?)null, Is.False);
        }
    }
}
