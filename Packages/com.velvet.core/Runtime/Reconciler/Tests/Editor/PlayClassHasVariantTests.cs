using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies a <c>has-[.class]:</c> element against a class a play writes onto a descendant on the panel's
    /// frames, outside any pass: a <see cref="V.Motion"/> whose mount enter holds its pose back and swaps it in on a
    /// later frame. The element matches once the play has written the class, as <c>:has()</c> matches a class a
    /// script adds.
    /// </summary>
    [TestFixture]
    internal sealed class PlayClassHasVariantTests : MotionSimulatedPanelTestsBase
    {
        private const float DurationSec = 0.3f;

        private static readonly Dictionary<string, MotionVariant> s_poses = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "on opacity-100",
        };

        [Test]
        public void Given_AHasClassElementAboveAMotion_When_ItsMountEnterWritesThePoseCarryingTheClass_Then_ItMatches()
        {
            // Arrange — the enter starts from "hidden", so the render leaves "on" off the Motion.
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(name: "parent", className: "has-[.on]:bg-mark", children: new VNode[]
                {
                    V.Motion(name: "m", variants: s_poses, initial: "hidden", animate: "visible",
                        transition: new StyleTransitionConfig { DurationSec = DurationSec }),
                }),
            });

            // Act
            AdvancePast(DurationSec);

            // Assert — null where the play has not written the class.
            Assert.That(Root.Q<VisualElement>("m").ClassListContains("on")
                ? Root.Q<VisualElement>("parent").ClassListContains("bg-mark")
                : (bool?)null, Is.True);
        }
    }
}
