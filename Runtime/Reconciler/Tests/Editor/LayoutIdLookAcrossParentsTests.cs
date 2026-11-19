using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The look a layoutId holder hands on when another holder joins its id under a parent reconciled after the one the
    /// holder's own changes are in, so that the join reads the holder in the render that changed it, before a style pass.
    /// GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class LayoutIdLookAcrossParentsTests : MotionSimulatedPanelTestsBase
    {
        private static readonly StyleTransitionConfig s_atOnce = new() { DurationSec = 0f };
        private static readonly StyleTransitionConfig s_slowTween = new() { DurationSec = 1f, Easing = EasingMode.Linear };

        private static StateUpdater<int> s_setStep;
        // The classes "a" carries of its own.
        private static string s_aClasses;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            (s_setStep, s_aClasses) = (default, "");
        }

        // "a" holds "card" in the first parent, moving at once. Step 1 mounts "b", turned 45 degrees, beside it under the
        // id on a one-second tween, and step 3 keeps it there; step 2 takes "b" away and mounts "c" under the id in the
        // second parent.
        [Component]
        private static VNode TwoParentsRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            var first = new List<VNode>
            {
                V.Motion(key: "a", name: "a", layoutId: "card", transition: s_atOnce,
                    className: $"absolute left-[0px] top-[0px] w-[100px] h-[100px] {s_aClasses}"),
            };
            if (step == 1 || step == 3)
            {
                first.Add(V.Motion(key: "b", name: "b", layoutId: "card", transition: s_slowTween,
                    className: "absolute left-[300px] top-[0px] w-[100px] h-[100px] rotate-45"));
            }
            var second = step == 2
                ? new VNode[]
                {
                    V.Motion(key: "c", name: "c", layoutId: "card", transition: s_slowTween,
                        className: "absolute left-[0px] top-[200px] w-[100px] h-[100px]"),
                }
                : System.Array.Empty<VNode>();
            return V.Div(children: new VNode[]
            {
                V.Div(key: "first", className: "absolute w-[600px] h-[150px]", children: first.ToArray()),
                V.Div(key: "second", className: "absolute top-[150px] w-[600px] h-[300px]", children: second),
            });
        }

        // GREEN_ON_BASE(characterization): the base keeps no record of a rotate a projection hands back, and mixes "c" in
        // from a's none here. The record this branch keeps must not stand in for a's own when "c" joins in the render that
        // hands "a" the lead back.
        [TestCase("", "")]
        [TestCase("transition-all duration-1000 ease-linear", "translate-y-[40px]")]
        public void Given_AHolderThatTookTheLeadBackAtOnce_When_AnotherJoinsUnderAnotherParentInThatRender_Then_ItMixesFromItsOwnRotate(
            string aClasses, string late)
        {
            // Arrange — the bundled sheet; "a" carrying aClasses; "b" takes the id from "a", mixing its rotate in from a's
            // none, and "a" is drawn over "b" at b's rotate; some 0.2 s in "a" takes late, which a translate on a transition
            // carries on past the render that ends a's draw over "b", and three frames on.
            VelvetStyleUtilities.AttachTo(Root);
            s_aClasses = aClasses;
            using var mounted = V.Mount(Root, V.Component(TwoParentsRender, key: "root"));
            Tick();
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            for (var i = 0; i < 12; i++) Tick();
            s_aClasses = $"{aClasses} {late}";
            s_setStep.Invoke(3);
            mounted.FlushStateForTest();
            for (var i = 0; i < 3; i++) Tick();

            // Act — one render takes "b" away, which hands "a" the lead back, landing at once, and mounts "c" under the id,
            // which takes it from "a"; the frame after.
            s_setStep.Invoke(2);
            mounted.FlushStateForTest();
            Tick();

            // Assert — "c" mixes its rotate in from a's own none, rather than from the rotate "a" was drawn at over "b".
            var c = Root.Q<VisualElement>("c");
            var from = mounted.Root.Reconciler.Context.LayoutIdProjections.TryGetValue(c, out var projection) ? projection.FromLook : null;
            Assert.That(from?.Rotate ?? float.NaN, Is.EqualTo(0f).Within(0.5f));
        }
    }
}
