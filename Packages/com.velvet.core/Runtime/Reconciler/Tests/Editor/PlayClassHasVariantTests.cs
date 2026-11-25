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
    /// script adds, and stops matching once a play takes it away; the clip a text-overflow utility implies on a field's
    /// input box is written and taken away with it.
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

        private static readonly Dictionary<string, MotionVariant> s_ellipsisFirstPoses = new()
        {
            ["clipped"] = "text-ellipsis opacity-0",
            ["shown"] = "opacity-100",
        };

        [Test]
        public void Given_AHasClassElementAboveAMotion_When_ItsMountEnterWritesThePoseCarryingTheClass_Then_ItMatches()
        {
            // Arrange — the enter starts from "hidden".
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

        [Test]
        public void Given_AHasClassElementNamingTheBoxClipAboveAMotionTextField_When_ItsMountEnterSwapsInATextOverflowUtility_Then_ItMatches()
        {
            // Arrange — the classic enter AnimatePresence plays for a child it mounts, whose swap writes its to-class
            // and whose end takes it away.
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(name: "parent", className: "has-[.overflow-hidden]:bg-mark", children: new VNode[]
                {
                    V.AnimatePresence(children: new VNode[]
                    {
                        V.Motion(key: "m", name: "m", elementType: typeof(TextField),
                            transition: new StyleTransitionConfig
                            {
                                DurationSec = DurationSec, EnterFromClass = "opacity-0", EnterToClass = "text-ellipsis",
                            }),
                    }),
                }),
            });

            // Act — frames past the swap and short of the enter's end, which takes the to-class away.
            for (var frame = 0; frame < 4; frame++) Tick();

            // Assert — null where the play put no clip on the box.
            Assert.That(Root.Q<VisualElement>("m").Q<VisualElement>(className: StyleChildVariantClass.InputBoxClass)
                    .ClassListContains("overflow-hidden")
                ? Root.Q<VisualElement>("parent").ClassListContains("bg-mark")
                : (bool?)null, Is.True);
        }

        [Test]
        public void Given_AHasClassElementNamingTheBoxClipAboveAMotionTextFieldEnteringFromATextOverflowUtility_When_TheEnterTakesItAway_Then_ItStopsMatching()
        {
            // Arrange — the enter starts from "clipped", whose text-overflow utility clips the field's input box.
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(name: "parent", className: "has-[.overflow-hidden]:bg-mark", children: new VNode[]
                {
                    V.Motion(name: "m", elementType: typeof(TextField), variants: s_ellipsisFirstPoses,
                        initial: "clipped", animate: "shown",
                        transition: new StyleTransitionConfig { DurationSec = DurationSec }),
                }),
            });
            var matchedWhileClipped = Root.Q<VisualElement>("parent").ClassListContains("bg-mark");

            // Act
            AdvancePast(DurationSec);

            // Assert — null where "parent" did not match while the box was clipped.
            Assert.That(matchedWhileClipped ? Root.Q<VisualElement>("parent").ClassListContains("bg-mark") : (bool?)null,
                Is.False);
        }
    }
}
