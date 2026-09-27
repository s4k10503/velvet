using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a removed AnimatePresence child's descendant Motion — one under a plain Div, so not the
    /// child's anchor — does with its exit pose: that the exit swap writes a translate pose's inline value,
    /// and that a key coming back leaves the descendant at the pose the re-added child declares, whether its
    /// exit was still playing or had completed before the render that would drop it.
    /// </summary>
    [TestFixture]
    internal sealed class PresenceDescendantExitPoseTests : MotionSimulatedPanelTestsBase
    {
        private static readonly StyleTransitionConfig s_tween = new() { DurationSec = 0.3f };

        private static readonly Dictionary<string, MotionVariant> s_slide = new()
        {
            ["rest"] = "translate-x-[0px]",
            ["gone"] = "translate-x-[60px]",
        };

        private static readonly Dictionary<string, MotionVariant> s_levels = new()
        {
            ["lit"] = "opacity-100",
            ["dim"] = new MotionVariant("opacity-50", StyleTransitionConfig.None),
            ["gone"] = "opacity-0",
        };

        private readonly record struct PresenceState(string Keys, string Label);

        private sealed class PresenceStore : Store<PresenceState>
        {
            public PresenceStore(string label) : base(new PresenceState("a", label)) { }
            public void Set(string keys, string label) => SetState(_ => new PresenceState(keys, label));
            protected override void ResetCore() => SetState(_ => new PresenceState("a", "rest"));
        }

        private static PresenceStore s_store;
        private static IReadOnlyDictionary<string, MotionVariant> s_variants;

        public override void SetUp()
        {
            base.SetUp();
            s_store = null;
            s_variants = null;
        }

        [Component]
        private static VNode PresenceHost()
        {
            var state = Hooks.UseStore(s_store, s => s);
            var children = new List<VNode>();
            foreach (var key in state.Keys)
            {
                children.Add(V.Div(key: key.ToString(), children: new VNode[]
                {
                    V.Motion(name: "inner", variants: s_variants, animate: state.Label, exit: "gone",
                        transition: s_tween),
                }));
            }
            return V.AnimatePresence(key: "presence", children: children.ToArray());
        }

        // Mounts key "a" at the label, then removes it and runs frames until its exit has swapped.
        private MountedTree MountAndRemove(PresenceStore store, IReadOnlyDictionary<string, MotionVariant> variants,
            string label)
        {
            s_store = store;
            s_variants = variants;
            var mounted = V.Mount(Root, V.Component(PresenceHost, key: "root"));
            Tick();
            store.Set(string.Empty, label);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Tick();
            Tick();
            return mounted;
        }

        // Runs frames until the element's exit completes, stopping before the frame that drains the render its
        // completion schedules.
        private void RunUntilExitCompletes(MountedTree mounted, VisualElement element)
        {
            var scheduler = mounted.Root.Reconciler.Context.StyleAnimationScheduler;
            for (var i = 0; i < 100 && scheduler.IsExiting(element); i++) Tick();
        }

        private static float TranslateX(VisualElement element)
            => element.style.translate.keyword == StyleKeyword.Undefined
                ? element.style.translate.value.x.value
                : float.NaN;

        private static string OpacityClasses(VisualElement element)
            => string.Join(" ", element.GetClasses().Where(c => c.StartsWith("opacity-")).OrderBy(c => c));

        [Test]
        public void Given_ADescendantWithATranslateExitPose_When_ItsChildIsRemoved_Then_TheExitSwapWritesThatTranslate()
        {
            // Arrange
            using var store = new PresenceStore("rest");

            // Act
            using var mounted = MountAndRemove(store, s_slide, "rest");

            // Assert
            Assert.That(TranslateX(Root.Q<VisualElement>("inner")), Is.EqualTo(60f));
        }

        // GREEN_ON_BASE(characterization): the base never writes a descendant's translate exit pose at all.
        // So a key coming back mid-exit finds the descendant still at its resting translate.
        [Test]
        public void Given_ADescendantExitingToATranslatePose_When_TheKeyReturnsMidExit_Then_ItsRestingTranslateIsWritten()
        {
            // Arrange
            using var store = new PresenceStore("rest");
            using var mounted = MountAndRemove(store, s_slide, "rest");

            // Act
            store.Set("a", "rest");
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(TranslateX(Root.Q<VisualElement>("inner")), Is.EqualTo(0f));
        }

        // GREEN_ON_BASE(characterization): the base never writes a descendant's translate exit pose at all.
        // So a key coming back after the exit completed finds the descendant still at its resting translate.
        [Test]
        public void Given_ADescendantWhoseTranslateExitCompleted_When_TheKeyReturnsBeforeTheDropRender_Then_ItsRestingTranslateIsWritten()
        {
            // Arrange — the render the exit's completion schedules is left undrained.
            using var store = new PresenceStore("rest");
            using var mounted = MountAndRemove(store, s_slide, "rest");
            RunUntilExitCompletes(mounted, Root.Q<VisualElement>("inner"));

            // Act
            store.Set("a", "rest");
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(TranslateX(Root.Q<VisualElement>("inner")), Is.EqualTo(0f));
        }

        [Test]
        public void Given_ADescendantExitingFromOneUssPose_When_TheKeyReturnsMidExitWithAnother_Then_OnlyTheNewPosesClassIsCarried()
        {
            // Arrange
            using var store = new PresenceStore("lit");
            using var mounted = MountAndRemove(store, s_levels, "lit");

            // Act
            store.Set("a", "dim");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            AdvancePast(0.3f);

            // Assert
            Assert.That(OpacityClasses(Root.Q<VisualElement>("inner")), Is.EqualTo("opacity-50"));
        }

        [Test]
        public void Given_ADescendantWhoseExitCompleted_When_TheKeyReturnsBeforeTheDropRenderWithAnotherUssPose_Then_OnlyTheNewPosesClassIsCarried()
        {
            // Arrange — the render the exit's completion schedules is left undrained.
            using var store = new PresenceStore("lit");
            using var mounted = MountAndRemove(store, s_levels, "lit");
            RunUntilExitCompletes(mounted, Root.Q<VisualElement>("inner"));

            // Act
            store.Set("a", "dim");
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(OpacityClasses(Root.Q<VisualElement>("inner")), Is.EqualTo("opacity-50"));
        }
    }
}
