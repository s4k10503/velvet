using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Regression coverage for the state-ghosting class <see cref="ButtonChildPoolReuseTests"/> and
    /// <see cref="LabelChildPoolReuseTests"/> pin, on the three poolable composites. <c>V.Custom&lt;Toggle&gt;</c>
    /// (and the Slider / TextField spellings) expand their children into the very container the control built
    /// its own input into, and the pool return detached nothing — so the next rent handed the leftover child
    /// back on top of the fresh content. Emptying that container is not the fix, because it would take the
    /// control's own structure with it.
    /// <para>
    /// Each case asserts the rented instance alongside what it holds: a rent that returned a DIFFERENT control
    /// holds only its own input for a reason that has nothing to do with the reset, so the child term alone
    /// would pass on a mechanism that never ran. The surviving child is compared by reference to the input the
    /// constructor made, which is what separates the fix from a <c>Clear()</c> that deletes it.
    /// </para>
    /// <para>
    /// The foreign child is inserted at the FRONT, which is where the reconciler was measured to place it: a
    /// fresh control's own sub-element is the container's only child and the expansion writes into slot 0.
    /// The pools are process-wide, so a children-bearing control left in one breaks whatever runs next — hence
    /// the clear on both sides of every case here.
    /// </para>
    /// GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class CompositeFieldChildPoolReuseTests
    {
        [SetUp]
        public void SetUp() => ClearPools();

        [TearDown]
        public void TearDown() => ClearPools();

        private static void ClearPools()
        {
            VNodePoolTestAccess.ClearTogglePoolForTest();
            VNodePoolTestAccess.ClearSliderPoolForTest();
            VNodePoolTestAccess.ClearTextFieldPoolForTest();
            VNodePoolTestAccess.ClearLabelPoolForTest();
        }

        [Test]
        public void Given_AToggleCarryingAForeignChild_When_ItIsRentedBackFromThePool_Then_ItHoldsOnlyItsOwnInput()
        {
            // Arrange — a Toggle that still holds a foreign child, as a removed V.Custom<Toggle> subtree does
            // on its way to the pool (its descendants are resource-cleaned but not detached).
            var returned = new Toggle();
            var ownInput = returned.ElementAt(0);
            returned.Insert(0, new Label("stale"));
            VNodePool.ReturnToggle(returned);

            // Act — a toggle is rented back.
            var rented = VNodePool.RentToggle();

            // Assert — it is the very instance that went in, and the one child it holds is the input its
            // constructor made, so a fresh child reconcile appends onto nothing and the control survives.
            Assert.That(
                (ReferenceEquals(rented, returned), rented.childCount, ReferenceEquals(rented.ElementAt(0), ownInput)),
                Is.EqualTo((true, 1, true)));
        }

        [Test]
        public void Given_ASliderCarryingAForeignChild_When_ItIsRentedBackFromThePool_Then_ItHoldsOnlyItsOwnInput()
        {
            // Arrange — a Slider that still holds a foreign child.
            var returned = new Slider();
            var ownInput = returned.ElementAt(0);
            returned.Insert(0, new Label("stale"));
            VNodePool.ReturnSlider(returned);

            // Act — a slider is rented back.
            var rented = VNodePool.RentSlider();

            // Assert — same instance, and only the dragger container its constructor made.
            Assert.That(
                (ReferenceEquals(rented, returned), rented.childCount, ReferenceEquals(rented.ElementAt(0), ownInput)),
                Is.EqualTo((true, 1, true)));
        }

        [Test]
        public void Given_ATextFieldCarryingAForeignChild_When_ItIsRentedBackFromThePool_Then_ItHoldsOnlyItsOwnInput()
        {
            // Arrange — a TextField that still holds a foreign child.
            var returned = new TextField();
            var ownInput = returned.ElementAt(0);
            returned.Insert(0, new Label("stale"));
            VNodePool.ReturnTextField(returned);

            // Act — a text field is rented back.
            var rented = VNodePool.RentTextField();

            // Assert — same instance, and only the text input its constructor made.
            Assert.That(
                (ReferenceEquals(rented, returned), rented.childCount, ReferenceEquals(rented.ElementAt(0), ownInput)),
                Is.EqualTo((true, 1, true)));
        }

        [Test]
        public void Given_ACustomToggleDeclaringAChild_When_ItMounts_Then_TheChildTakesTheSlotAheadOfTheControlsOwnInput()
        {
            // Arrange / Act — a V.Custom<Toggle> declaring one child is mounted.
            var root = new VisualElement();
            using var mounted = V.Mount(root,
                V.Custom<Toggle>(name: "host", children: new VNode[] { V.Label(name: "stale", text: "stale") }));
            var host = root.Q<Toggle>("host");

            // Assert — the expanded child takes slot 0, ahead of the input the constructor left there. This is
            // why the reset identifies the control's own children instead of trimming a count from an end.
            Assert.That(host.ElementAt(0).name, Is.EqualTo("stale"));
        }

        // Integration: a V.Custom<Toggle> subtree unmounts, then a plain V.Toggle rents its element back.

        private readonly record struct PhaseState(int Phase);

        private sealed class PhaseStore : Store<PhaseState>
        {
            public PhaseStore() : base(new PhaseState(0)) { }
            public void Set(int phase) => SetState(_ => new PhaseState(phase));
            protected override void ResetCore() => SetState(_ => new PhaseState(0));
        }

        private static PhaseStore s_store;

        // Phase 0 mounts a Toggle carrying a child; phase 1 unmounts it (its pooling opportunity); phase 2
        // mounts a plain V.Toggle, which rents from the pool.
        [Component]
        private static VNode Screen()
        {
            var phase = Hooks.UseStore(s_store, s => s.Phase);
            return V.Div(name: "screen", children: phase switch
            {
                0 => new VNode[]
                {
                    V.Custom<Toggle>(name: "host", children: new VNode[] { V.Label(name: "stale", text: "stale") }),
                },
                1 => Array.Empty<VNode>(),
                _ => new VNode[] { V.Toggle(name: "plain") },
            });
        }

        [Test]
        public void Given_AChildBearingToggleWasUnmounted_When_APlainToggleMounts_Then_TheRecycledElementCarriesNoLeftoverChild()
        {
            // Arrange — mount the child-bearing Toggle, then unmount it so it hits the pool-return path.
            using var store = new PhaseStore();
            s_store = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(Screen, key: "screen"));
            var scheduler = mounted.GetSchedulerForTest();
            var host = root.Q<Toggle>("host");
            var ownInput = host.Q<VisualElement>(className: BaseField<bool>.inputUssClassName);
            Assume.That(host.childCount, Is.EqualTo(2), "Precondition: the custom Toggle mounts with its child beside its own input");
            store.Set(1);
            scheduler.DrainImmediateForTest();

            // Act — a plain toggle mounts, renting from the pool.
            store.Set(2);
            scheduler.DrainImmediateForTest();

            // Assert — the plain toggle is the recycled instance and shows nothing but its own input. The
            // identity term is asserted with the rest because a rent that missed the pool holds only its own
            // input whether or not the reset detaches anything.
            var plain = root.Q<Toggle>("plain");
            Assert.That(
                (ReferenceEquals(plain, host), plain.childCount, ReferenceEquals(plain.ElementAt(0), ownInput)),
                Is.EqualTo((true, 1, true)));
        }
    }
}
