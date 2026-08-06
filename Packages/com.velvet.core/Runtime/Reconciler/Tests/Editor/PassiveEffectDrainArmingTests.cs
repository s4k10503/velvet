using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a mount arms the context-wide passive-effect drain. The registration is declined when the
    /// batch scheduler has no anchor yet, which keeps an anchorless context from latching a drain nothing
    /// would run — and makes the mount path's ordering load-bearing: the anchor is set during SetupMount,
    /// several lines before the render whose commit stages the first effect. Move that write after the
    /// render, or drop it from the root path, and every mount-time UseEffect goes unregistered with nothing
    /// else in the tree reporting it, so this case asserts the arming itself rather than where it landed.
    /// </summary>
    internal sealed class PassiveEffectDrainArmingTests
    {
        private VisualElement _root;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp() => _root = new VisualElement();

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
        }

        [Component]
        private static VNode EffectHost()
        {
            Hooks.UseEffect(() => () => { }, new object[] { "host" });
            return V.Label(name: "host-label");
        }

        [Test]
        public void Given_AComponentWithAPassiveEffect_When_ItMountsThroughVMount_Then_TheContextWideDrainIsAlreadyRegistered()
        {
            // Arrange — nothing beyond the mount target; the mount itself is the act.

            // Act
            _mounted = V.Mount(_root, V.Component(EffectHost, key: "host"));

            // Assert — no flush in between, so this reads the state the mount left behind.
            Assert.That(_mounted.Root.Reconciler.Context.PassiveEffectDrainScheduled, Is.True);
        }
    }
}
