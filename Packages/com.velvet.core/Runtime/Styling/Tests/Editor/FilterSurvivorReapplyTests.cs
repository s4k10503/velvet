using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A class change that removes one filter utility re-applies the filter utilities the element keeps, so one that
    /// shared the removed token's property comes back.
    /// </summary>
    internal sealed class FilterSurvivorReapplyTests
    {
        private static string s_initial = string.Empty;
        private static StateUpdater<string> s_setClass;
        private MountedTree _mounted;

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            s_setClass = default;
        }

        [Component]
        private static VNode RenderBox()
        {
            var (cls, setClass) = Hooks.UseState(s_initial);
            s_setClass = setClass;
            return V.Div(name: "box", className: cls);
        }

        // The filter the box shows once its classes change from initial to next.
        private string FilterAfter(string initial, string next)
        {
            s_initial = initial;
            var root = new VisualElement();
            _mounted = V.Mount(root, V.Component(RenderBox));
            s_setClass.Invoke(next);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
            var filter = root.Q<VisualElement>("box").style.filter.value;
            return filter == null || filter.Count == 0
                ? "none"
                : $"{filter[0].type}:{filter[0].GetParameter(filter[0].type == FilterFunctionType.Custom ? 2 : 0).floatValue}";
        }

        [Test]
        public void Given_ABlurBracketBesideBlurNone_When_BlurNoneIsRemoved_Then_TheBracketedBlurReturns()
        {
            // Arrange
            const string initial = "blur-[2px] blur-none";

            // Act
            var filter = FilterAfter(initial, "blur-[2px]");

            // Assert
            Assert.That(filter, Is.EqualTo("Blur:2"));
        }

        [Test]
        public void Given_ADropShadowBracketBesideDropShadowNone_When_NoneIsRemoved_Then_TheBracketedShadowReturns()
        {
            // Arrange
            const string initial = "drop-shadow-[0_0_8px_#00ffff] drop-shadow-none";

            // Act
            var filter = FilterAfter(initial, "drop-shadow-[0_0_8px_#00ffff]");

            // Assert — the custom's third parameter is the deviation.
            Assert.That(filter, Is.EqualTo("Custom:8"));
        }
    }
}
