using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the filter bounds-spacer on the drop-shadow layer: a shadowed element under an inline filter (the
    /// filter's offscreen render tree would clip the shadow bleed to the layout rect) gets a trailing spacer
    /// that widens boundingBox, and it appears / vanishes with the filter. The reconciler-safety mechanics are
    /// shared with the skew layer (SilhouetteBoundsSpacer) and pinned there. GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class ShadowFilterSpacerTests
    {
        private const string ShadowFilter = "w-[200px] h-[80px] shadow-lg hue-rotate-90";
        private const string ShadowNoFilter = "w-[200px] h-[80px] shadow-lg";

        private EditorWindow _window;
        private MountedTree _mounted;
        private static StateUpdater<string> s_setClass;

        [SetUp]
        public void SetUp()
        {
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");
            s_setClass = default;
            _window = ScriptableObject.CreateInstance<TestHostWindow>();
            _window.position = new Rect(0, 0, 800, 600);
            _window.Show();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            if (_window != null)
            {
                _window.Close();
                UnityEngine.Object.DestroyImmediate(_window);
                _window = null;
            }
        }

        [Component]
        private static VNode RenderCard()
        {
            var (cls, setClass) = Hooks.UseState(ShadowFilter);
            s_setClass = setClass;
            return V.Div(className: cls, name: "card");
        }

        private FiberBatchScheduler Scheduler => _mounted.Root.Reconciler.Context.BatchScheduler;
        private VisualElement Card => _window.rootVisualElement.Q<VisualElement>("card");
        private int SpacerCount() => Enumerable.Range(0, Card.childCount)
            .Count(i => SilhouetteBoundsSpacer.IsSpacer(Card[i]));

        private void Mount() => _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderCard));

        private void SetClass(string cls)
        {
            s_setClass.Invoke(cls);
            Scheduler.DrainImmediateForTest();
        }

        [Test]
        public void Given_AShadowedFilteredElement_When_Mounted_Then_ASpacerIsAdded()
        {
            Mount();

            Assert.That(SpacerCount(), Is.EqualTo(1));
        }

        [Test]
        public void Given_AShadowedElementWithoutFilter_When_Mounted_Then_NoSpacerIsAdded()
        {
            Mount();
            SetClass(ShadowNoFilter);

            Assert.That(SpacerCount(), Is.EqualTo(0));
        }

        [Test]
        public void Given_TheFilterRemovedOnPatch_When_Drained_Then_TheSpacerIsGone()
        {
            Mount();
            Assume.That(SpacerCount(), Is.EqualTo(1), "Precondition: the spacer was present under the filter");
            SetClass(ShadowNoFilter);

            Assert.That(SpacerCount(), Is.EqualTo(0));
        }

        [Test]
        public void Given_TheFilterAddedOnPatch_When_Drained_Then_TheSpacerAppears()
        {
            Mount();
            SetClass(ShadowNoFilter);
            Assume.That(SpacerCount(), Is.EqualTo(0), "Precondition: no spacer without a filter");
            SetClass(ShadowFilter);

            Assert.That(SpacerCount(), Is.EqualTo(1));
        }

        private sealed class TestHostWindow : EditorWindow { }
    }
}
