using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins pool-return dispatch to the exact runtime type, mirroring the exact-type checks the
    /// factory uses to rent. V.Custom&lt;T&gt; legitimately mounts user subclasses of the poolable
    /// primitives; the return side's type-pattern switch subtype-matched them into the shared pools,
    /// so an unrelated later V.Button could rent the subclass instance back — with its own fields and
    /// constructor-registered callbacks still live, since the base-type reset cannot know about them.
    /// A subclass must simply fall through un-pooled.
    /// </summary>
    [TestFixture]
    internal sealed class CustomSubclassPoolExactTypeTests
    {
        private sealed class ProbeButton : Button
        {
        }

        private readonly record struct PhaseState(int Phase);

        private sealed class PhaseStore : Store<PhaseState>
        {
            public PhaseStore() : base(new PhaseState(0)) { }
            public void Set(int phase) => SetState(_ => new PhaseState(phase));
            protected override void ResetCore() => SetState(_ => new PhaseState(0));
        }

        private static PhaseStore s_store;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
        }

        // Phase 0 mounts the subclass; phase 1 unmounts it (its only pooling opportunity);
        // phase 2 mounts a plain button that rents from the pool.
        [Component]
        private static VNode Screen()
        {
            var phase = Hooks.UseStore(s_store, s => s.Phase);
            return V.Div(name: "screen", children: phase switch
            {
                0 => new VNode[] { V.Custom<ProbeButton>(name: "probe") },
                1 => Array.Empty<VNode>(),
                _ => new VNode[] { V.Button(name: "plain", text: "plain") },
            });
        }

        [Test]
        public void Given_ACustomButtonSubclassWasUnmounted_When_APlainButtonMounts_Then_ItIsNotTheSubclassInstance()
        {
            // Arrange — mount the subclass, then unmount it so it hits the pool-return path.
            using var store = new PhaseStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(Screen, key: "screen"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            var probe = _root.Q<ProbeButton>();
            Assume.That(probe, Is.Not.Null, "Precondition: the subclass instance is mounted");
            store.Set(1);
            scheduler.DrainImmediateForTest();

            // Act — a plain button mounts, renting from the shared pool.
            store.Set(2);
            scheduler.DrainImmediateForTest();

            // Assert — the plain button is a real Button, never the recycled subclass instance.
            Assert.That(ReferenceEquals(_root.Q<VisualElement>("plain"), probe), Is.False);
        }
    }
}
