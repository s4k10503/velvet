using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins pool reclamation for a poolable widget that occupies its slot through a structural
    /// wrapper (ring-*/clip-path-* classes wrap the widget in a passthrough element). The ordinary
    /// removal paths returned the WRAPPER to the pool switch — a plain container that matches no
    /// pooled type — so the inner widget was silently dropped for GC instead of recycled, unlike the
    /// rollback-orphan path which already resolves the wrapper to its inner. Removal must reclaim
    /// the inner widget so pooling keeps working for the wrapped combination.
    /// </summary>
    [TestFixture]
    internal sealed class WrappedWidgetPoolReclaimTests
    {
        private readonly record struct ToggleState(bool Show);

        private sealed class ToggleStore : Store<ToggleState>
        {
            public ToggleStore() : base(new ToggleState(true)) { }
            public void Set(bool show) => SetState(_ => new ToggleState(show));
            protected override void ResetCore() => SetState(_ => new ToggleState(true));
        }

        private static ToggleStore s_store;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
        }

        [Component]
        private static VNode Avatar()
        {
            var show = Hooks.UseStore(s_store, s => s.Show);
            return V.Div(name: "host", children: show
                ? new VNode[] { V.Button(name: "avatar", className: "ring-2", text: "a") }
                : Array.Empty<VNode>());
        }

        [Test]
        public void Given_ARingWrappedButton_When_ItsSlotUnmounts_Then_TheInnerButtonIsReclaimedToThePool()
        {
            // Arrange — a mounted ring-wrapped button (the wrapper occupies the parent slot).
            using var store = new ToggleStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(Avatar, key: "avatar"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Assume.That(_root.Q<Button>("avatar"), Is.Not.Null, "Precondition: the wrapped button is mounted");
            var before = VNodePool.ButtonPoolCountForTesting;

            // Act — remove the wrapped slot through the ordinary unmount path.
            store.Set(false);
            scheduler.DrainImmediateForTest();

            // Assert — the inner button reached the pool (not the wrapper, not GC).
            Assert.AreEqual(before + 1, VNodePool.ButtonPoolCountForTesting);
        }
    }
}
