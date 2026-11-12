using System;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a <c>V.VirtualList</c> range rendered outside any pass leaves no Portal named behind it: the
    /// Portal it names for its own rows is not the one a later list's rows are stamped with
    /// (<c>ComponentFiber.OwningPortalPlaceholder</c>).
    /// </summary>
    [TestFixture]
    internal sealed class DetachedItemPortalStampTests
    {
        private static ComponentFiber s_hostlessRow;

        [SetUp]
        public void SetUp() => s_hostlessRow = null;

        [Component(Compiler = false)]
        private static VNode HostedRow() => V.Label(text: "hosted");

        [Component(Compiler = false)]
        private static VNode HostlessRow()
        {
            s_hostlessRow = FiberAmbientStack.Current;
            return V.Label(text: "hostless");
        }

        [Component(Compiler = false)]
        private static VNode ListHost()
            => V.VirtualList(
                items: new[] { "A" },
                keySelector: item => item,
                itemHeight: 50f,
                renderer: _ => V.Component(HostedRow, key: "row"),
                overscan: 0);

        // GREEN_ON_BASE(characterization): the base stamps no list row with a Portal, whatever renders before it.
        [Test]
        public void Given_AListInAPortalRenderedOutsideAPass_When_AListNoComponentHostsRendersAfterIt_Then_ItsRowNamesNoPortal()
        {
            // Arrange — a reconciler no component roots mounts a hosted list in a Portal and a list beside the Portal
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var target = new VisualElement();
            reconciler.Reconcile(root, Array.Empty<VNode>(), new VNode[]
            {
                V.Portal(target, new VNode[] { V.Component(ListHost, key: "host") }, key: "portal"),
                V.VirtualList(
                    items: new[] { "B" },
                    keySelector: item => item,
                    itemHeight: 50f,
                    renderer: _ => V.Component(HostlessRow, key: "row"),
                    overscan: 0),
            });
            var lists = reconciler.Context.VirtualListControllers;
            lists[target.Q<ScrollView>()].UpdateVisibleRange(scrollY: 0f, viewportHeight: 50f);

            // Act
            lists[root.Q<ScrollView>()].UpdateVisibleRange(scrollY: 0f, viewportHeight: 50f);

            // Assert — a row that never rendered names no Portal either, so that reading is a sentinel.
            Assert.That(s_hostlessRow == null ? "no row rendered"
                    : s_hostlessRow.OwningPortalPlaceholder == null ? "no Portal" : "a Portal",
                Is.EqualTo("no Portal"));
        }
    }
}
