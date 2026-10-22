using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies an AnimatePresence child's exit pose over a composed family its own classes discard: a pose
    /// outranking the rejected value takes the family while the exit plays, as the resting pose does.
    /// </summary>
    [TestFixture]
    internal sealed class RejectedCompositeExitPoseTests : MotionSimulatedPanelTestsBase
    {
        private static readonly StyleTransitionConfig s_tween = new() { DurationSec = 0.3f };

        private static readonly Dictionary<string, MotionVariant> s_poses = new()
        {
            ["rest"] = "opacity-100",
            ["gone"] = "translate-y-4",
        };

        private sealed class ShownStore : Store<bool>
        {
            public ShownStore() : base(true) { }
            public void Hide() => SetState(_ => false);
            protected override void ResetCore() => SetState(_ => true);
        }

        private static ShownStore s_store;

        public override void SetUp()
        {
            base.SetUp();
            s_store = null;
        }

        [Component]
        private static VNode PresenceHost()
        {
            var shown = Hooks.UseStore(s_store, s => s);
            return V.AnimatePresence(key: "presence", children: shown
                ? new VNode[]
                {
                    V.Motion(key: "card", name: "card", className: "translate-y-[10]", variants: s_poses,
                        animate: "rest", exit: "gone", transition: s_tween),
                }
                : System.Array.Empty<VNode>());
        }

        // GREEN_ON_BASE(characterization): the base plays the exit pose over the own rejected value already. Handing
        // WithExitClasses an empty exit class in place of the pose's reddens it.
        [Test]
        public void Given_AnOwnRejectedTranslateAndAnExitPoseOutrankingIt_When_TheChildIsRemoved_Then_TheExitTranslates()
        {
            // Arrange
            using var store = new ShownStore();
            s_store = store;
            using var mounted = V.Mount(Root, V.Component(PresenceHost, key: "root"));
            Tick();

            // Act — two frames into a 0.3s exit.
            store.Hide();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Tick();
            Tick();

            // Assert — translate-y-4 is 16px.
            Assert.That(Root.Q<VisualElement>("card").style.translate.value.y.value, Is.EqualTo(16f));
        }
    }
}
