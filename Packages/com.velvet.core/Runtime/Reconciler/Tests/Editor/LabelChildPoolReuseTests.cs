using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Regression coverage for the same state-ghosting class <see cref="ButtonChildPoolReuseTests"/> pins, on
    /// the Label pool. <c>V.Label</c> declares no children, but <c>V.Custom&lt;Label&gt;</c> does: it mounts an
    /// exact pooled <see cref="Label"/> and expands its children into the element itself, so a released Label
    /// carried them into the pool and the next <c>V.Label</c> rent handed them back on top of the fresh
    /// content. The pool is process-wide, so driving the unfixed path leaves a children-bearing Label behind
    /// for whatever runs next — hence the clear on both sides of every case here.
    /// <para>
    /// The rented instance is asserted alongside the child count: a rent that returned a DIFFERENT Label is
    /// childless for a reason that has nothing to do with the reset, so the count alone would pass on a
    /// mechanism that never ran.
    /// </para>
    /// GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class LabelChildPoolReuseTests
    {
        [SetUp]
        public void SetUp() => VNodePoolTestAccess.ClearLabelPoolForTest();

        [TearDown]
        public void TearDown() => VNodePoolTestAccess.ClearLabelPoolForTest();

        [Test]
        public void Given_ALabelCarryingAChild_When_ResetForReuse_Then_ItHasNoChildren()
        {
            // Arrange — a label that still holds a child, as a removed V.Custom<Label> subtree does on its
            // way to the pool (its descendants are resource-cleaned but not detached).
            var label = new Label("host");
            label.Add(new Label("stale"));
            Assume.That(label.childCount, Is.EqualTo(1), "Precondition: the label starts with one child");

            // Act — it is reset for reuse.
            FiberLabelPoolHelper.ResetLabelForReuse(label);

            // Assert — the child is gone, so the recycled label matches a freshly constructed one.
            Assert.That(label.childCount, Is.EqualTo(0));
        }

        [Test]
        public void Given_ALabelWithAChildWasReturnedToThePool_When_RentedAgain_Then_TheSameInstanceComesBackChildless()
        {
            // Arrange — a label that still holds a child is returned to the emptied pool.
            var returned = new Label("host");
            returned.Add(new Label("stale"));
            VNodePool.ReturnLabel(returned);

            // Act — a label is rented back.
            var rented = VNodePool.RentLabel("fresh");

            // Assert — it is the very instance that went in, and it is childless, so a fresh child reconcile
            // cannot append onto leftover content.
            Assert.That((ReferenceEquals(rented, returned), rented.childCount), Is.EqualTo((true, 0)));
        }

        // Integration: a V.Custom<Label> subtree unmounts, then a plain V.Label rents its element back.

        private readonly record struct PhaseState(int Phase);

        private sealed class PhaseStore : Store<PhaseState>
        {
            public PhaseStore() : base(new PhaseState(0)) { }
            public void Set(int phase) => SetState(_ => new PhaseState(phase));
            protected override void ResetCore() => SetState(_ => new PhaseState(0));
        }

        private static PhaseStore s_store;

        // Phase 0 mounts a Label carrying a child; phase 1 unmounts it (its pooling opportunity); phase 2
        // mounts a plain V.Label, which rents from the pool.
        [Component]
        private static VNode Screen()
        {
            var phase = Hooks.UseStore(s_store, s => s.Phase);
            return V.Div(name: "screen", children: phase switch
            {
                0 => new VNode[]
                {
                    V.Custom<Label>(name: "host", children: new VNode[] { V.Label(name: "stale", text: "stale") }),
                },
                1 => Array.Empty<VNode>(),
                _ => new VNode[] { V.Label(name: "plain", text: "plain") },
            });
        }

        [Test]
        public void Given_AChildBearingLabelWasUnmounted_When_APlainLabelMounts_Then_TheRecycledElementCarriesNoLeftoverChild()
        {
            // Arrange — mount the child-bearing Label, then unmount it so it hits the pool-return path.
            using var store = new PhaseStore();
            s_store = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(Screen, key: "screen"));
            var scheduler = mounted.GetSchedulerForTest();
            var host = root.Q<Label>("host");
            Assume.That(host?.childCount, Is.EqualTo(1), "Precondition: the custom Label mounts with its child");
            store.Set(1);
            scheduler.DrainImmediateForTest();

            // Act — a plain label mounts, renting from the pool.
            store.Set(2);
            scheduler.DrainImmediateForTest();

            // Assert — the plain label is the recycled instance and shows nothing but its own text. The
            // identity term is asserted with the count because a rent that missed the pool is childless
            // whether or not the reset detaches anything.
            var plain = root.Q<Label>("plain");
            Assert.That((ReferenceEquals(plain, host), plain.childCount), Is.EqualTo((true, 0)));
        }
    }

    /// <summary>
    /// Pins which poolable primitives may have their child container emptied on pool return. Button and Label
    /// construct nothing into it, so <c>Clear()</c> there can only remove a previous tenant's content; Toggle,
    /// Slider and TextField each build a sub-element into that same container (their <c>contentContainer</c>
    /// is the element itself), so the same call would delete the control's own structure. This fails if a
    /// UI Toolkit version changes those baselines, which is what makes it safe for
    /// <c>FiberPrimitiveElementPool</c> to treat the five differently.
    /// </summary>
    [TestFixture]
    internal sealed class PoolableWidgetChildBaselineTests
    {
        [Test]
        public void Given_FreshlyConstructedPoolablePrimitives_When_TheirChildContainersAreCounted_Then_OnlyTheCompositesArrivePopulated()
        {
            // Arrange — one freshly constructed instance of each poolable primitive.
            var widgets = new VisualElement[]
            {
                new Button(), new Label(), new Toggle(), new Slider(), new TextField(),
            };

            // Act — count what each one already holds in the container children are placed into.
            var counts = Array.ConvertAll(widgets, w => FiberNodePatcher.GetChildContainer(w).childCount);

            // Assert — the two clearable types start empty; the three composites do not.
            Assert.That(counts, Is.EqualTo(new[] { 0, 0, 1, 1, 1 }));
        }
    }
}
