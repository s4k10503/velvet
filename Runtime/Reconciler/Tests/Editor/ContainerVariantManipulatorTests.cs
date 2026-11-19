using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Which elements carry a container variant manipulator: the ones whose <c>className</c> holds a container
    /// query token, for as long as it does, and no other. The manipulator subscribes to the process-wide scope
    /// signal, so one left tracked after its element goes is held for the life of the domain.
    /// </summary>
    [TestFixture]
    internal sealed class ContainerVariantManipulatorTests : VariantCleanupTestsBase
    {
        private readonly record struct ClassState(string ClassName);

        private sealed class ClassStore : Store<ClassState>
        {
            public ClassStore(string className) : base(new ClassState(className)) { }
            public void SetClassName(string className) => SetState(_ => new ClassState(className));
            protected override void ResetCore() => SetState(_ => new ClassState(string.Empty));
        }

        private static ClassStore s_classStore;

        [Component]
        private static VNode Host()
        {
            var state = Hooks.UseStore(s_classStore, s => s);
            return V.Div(name: "host", children: new VNode[]
            {
                V.Label(name: "leaf", className: state.ClassName, text: "x"),
            });
        }

        // Mounts one leaf with className, then rewrites its className to next, reporting whether the leaf held a
        // container variant manipulator before and after.
        private (bool Before, bool After) TrackedAcrossClassChange(string className, string next)
        {
            using var store = new ClassStore(className);
            s_classStore = store;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            var ctx = mounted.Root.Reconciler.Context;
            var leaf = _root.Q<Label>("leaf");
            var before = ctx.ContainerVariantManipulators.ContainsKey(leaf);

            store.SetClassName(next);
            ctx.BatchScheduler.DrainImmediateForTest();
            return (before, ctx.ContainerVariantManipulators.ContainsKey(leaf));
        }

        [Test]
        public void Given_AContainerVariantLeafWasMounted_When_ItIsRemoved_Then_ItIsNoLongerTracked()
        {
            // Arrange
            using var mounted = MountHost(_ => V.Label(name: "leaf", className: "@md:bg-red-500", text: "x"),
                out var scheduler, out var ctx);
            var leaf = _root.Q<Label>("leaf");
            var trackedWhileMounted = ctx.ContainerVariantManipulators.ContainsKey(leaf);

            // Act
            s_store.Set(1);
            scheduler.DrainImmediateForTest();

            // Assert
            Assert.That((trackedWhileMounted, ctx.ContainerVariantManipulators.ContainsKey(leaf)),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ALeafWithOnlyAStateVariant_When_Mounted_Then_ItHoldsNoContainerVariantManipulator()
        {
            // Arrange / Act
            using var mounted = MountHost(_ => V.Label(name: "leaf", className: "hover:bg-red-500", text: "x"),
                out _, out var ctx);
            var leaf = _root.Q<Label>("leaf");

            // Assert — the state variant is tracked, so the leaf is one a manipulator would be wrongly added to.
            Assert.That((ctx.VariantManipulators.ContainsKey(leaf), ctx.ContainerVariantManipulators.ContainsKey(leaf)),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ALeafWithOnlyAViewportVariant_When_Mounted_Then_ItHoldsNoContainerVariantManipulator()
        {
            // Arrange / Act
            using var mounted = MountHost(_ => V.Label(name: "leaf", className: "md:bg-red-500", text: "x"),
                out _, out var ctx);
            var leaf = _root.Q<Label>("leaf");

            // Assert
            Assert.That(
                (ctx.ConditionalVariantManipulators.ContainsKey(leaf), ctx.ContainerVariantManipulators.ContainsKey(leaf)),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AClassNameWithoutAContainerVariant_When_ItGainsOne_Then_TheLeafIsTracked()
        {
            // Act
            var tracked = TrackedAcrossClassChange("p-2", "p-2 @md:bg-red-500");

            // Assert
            Assert.That(tracked, Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_AClassNameWithAContainerVariant_When_ItLosesIt_Then_TheLeafIsNoLongerTracked()
        {
            // Act
            var tracked = TrackedAcrossClassChange("p-2 @md:bg-red-500", "p-2");

            // Assert
            Assert.That(tracked, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AClassNameWithAContainerVariant_When_ItSwapsForAnother_Then_TheLeafStaysTracked()
        {
            // Act
            var tracked = TrackedAcrossClassChange("@md:bg-red-500", "@lg:bg-red-500");

            // Assert
            Assert.That(tracked, Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AMalformedContainerToken_When_Mounted_Then_ItIsNotTracked()
        {
            // Arrange / Act — a name off the scale is an ordinary class, beside a real query that is tracked.
            using var mounted = MountHost(
                _ => V.Div(children: new VNode[]
                {
                    V.Label(name: "bad", className: "@8xl:bg-red-500", text: "x"),
                    V.Label(name: "good", className: "@7xl:bg-red-500", text: "x"),
                }),
                out _, out var ctx);

            // Assert
            Assert.That(
                (ctx.ContainerVariantManipulators.ContainsKey(_root.Q<Label>("bad")),
                    ctx.ContainerVariantManipulators.ContainsKey(_root.Q<Label>("good"))),
                Is.EqualTo((false, true)));
        }
    }
}
