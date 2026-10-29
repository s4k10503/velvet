using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a context keeps the one <see cref="FiberPaintLeases"/> it was given, whatever mounts or
    /// reconciles later: the holds a Motion play has taken live in it, and a replacement would leave the class pass
    /// reading an empty one while the play still draws.
    /// </summary>
    [TestFixture]
    internal sealed class PaintLeaseOwnershipTests
    {
        private static StateUpdater<int> s_setCount;
        private static StateUpdater<bool> s_setShown;

        [SetUp]
        public void SetUp()
        {
            s_setCount = default;
            s_setShown = default;
        }

        [Component]
        private static VNode Counter()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_setCount = setCount;
            return V.Label(name: "count", text: count.ToString());
        }

        [Component]
        private static VNode Host()
        {
            var (shown, setShown) = Hooks.UseState(false);
            s_setShown = setShown;
            return V.Div(className: "shadow-sm", name: "card", children: new VNode?[]
            {
                shown ? V.Component(Counter) : null,
            });
        }

        // GREEN_ON_BASE(characterization): the base builds a fiber's helpers when it mounts, so a state update of
        // a mounted component replaces nothing there; this holds the lazy build, which moved that to the first
        // update, to the same.
        [Test]
        public void Given_APlayHoldingAnElementsPaint_When_AMountedComponentUpdatesItsOwnState_Then_TheContextKeepsItsLeases()
        {
            // Arrange
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(Host));
            mounted.FlushStateForTest();
            s_setShown.Invoke(true);
            mounted.FlushStateForTest();
            var leases = mounted.Root.Reconciler!.Context.PaintLeases;
            var hold = leases!.Open(root.Q<VisualElement>("card"), new[] { "shadow-sm" }, new[] { "shadow-lg" }, isExit: false);

            // Act
            s_setCount.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((hold != null, ReferenceEquals(leases, mounted.Root.Reconciler!.Context.PaintLeases)), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_APlayHoldingAnElementsPaint_When_AComponentMountsBelowIt_Then_TheContextKeepsItsLeases()
        {
            // Arrange
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(Host));
            mounted.FlushStateForTest();
            var leases = mounted.Root.Reconciler!.Context.PaintLeases;
            var hold = leases!.Open(root.Q<VisualElement>("card"), new[] { "shadow-sm" }, new[] { "shadow-lg" }, isExit: false);

            // Act
            s_setShown.Invoke(true);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((hold != null, ReferenceEquals(leases, mounted.Root.Reconciler!.Context.PaintLeases)), Is.EqualTo((true, true)));
        }
    }
}
