using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies ReconcilerContext.RelationalVariantSources, the index from a relational source element to the
    /// source sets hooking it: an element no set hooks any more leaves the index, so a pooled element is not
    /// held by it, and a source a later render keeps is hooked once rather than once per render. GWT, one
    /// assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class RelationalSourceIndexPanelTests : PanelTestBase
    {
        internal readonly record struct Marks(bool NearMarked, int Renders);

        private sealed class MarksStore : Store<Marks>
        {
            public MarksStore(Marks initial) : base(initial) { }
            public void Update(System.Func<Marks, Marks> change) => SetState(change);
            protected override void ResetCore() => SetState(_ => default);
        }

        private static MarksStore s_store;

        public override void TearDown()
        {
            base.TearDown();
            s_store?.Dispose();
            s_store = null;
        }

        [Component]
        private static VNode Screen()
        {
            var s = Hooks.UseStore(s_store, x => x);
            return V.Div(
                "container",
                V.Toggle(key: "far", name: "far", className: "peer", value: true),
                V.Toggle(key: "near", name: "near", className: s.NearMarked ? "peer" : "", value: false),
                V.Label(key: "child", name: "child", text: s.Renders.ToString(), className: "peer-checked:bg-on"));
        }

        private ReconcilerContext Mount()
        {
            s_store = new MarksStore(new Marks(NearMarked: true, Renders: 0));
            _mounted = V.Mount(_window.rootVisualElement, V.Component(Screen, key: "screen"));
            return _mounted.Root.Reconciler.Context;
        }

        private void Change(System.Func<Marks, Marks> change)
        {
            s_store.Update(change);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        [Test]
        public void Given_AHookedPeer_When_ItLosesThePeerClass_Then_TheIndexNoLongerHoldsIt()
        {
            // Arrange
            var ctx = Mount();
            var near = _window.rootVisualElement.Q<Toggle>("near");
            var heldBefore = ctx.RelationalVariantSources.ContainsKey(near);

            // Act
            Change(s => s with { NearMarked = false });

            // Assert
            Assert.That((heldBefore, ctx.RelationalVariantSources.ContainsKey(near)), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_TheNearestHookedPeer_When_LaterRendersKeepIt_Then_ItIsHookedOnce()
        {
            // Arrange
            var ctx = Mount();
            var near = _window.rootVisualElement.Q<Toggle>("near");

            // Act
            Change(s => s with { Renders = 1 });
            Change(s => s with { Renders = 2 });

            // Assert — the render count is a term: both renders must have run.
            Assert.That(
                (_window.rootVisualElement.Q<Label>("child").text, ctx.RelationalVariantSources[near].Count),
                Is.EqualTo(("2", 1)));
        }
    }
}
