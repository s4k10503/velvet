#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the UI Toolkit behaviour FiberNodePatcher.ResolveInlineHold is built on: an inline style write
    /// animates on the transition the element carries at the moment of the write, so a transition written
    /// inline after it in the same frame does not time it. If the first two elements start tweening, the
    /// engine times such a write by the later transition, and the hold may no longer be needed.
    /// </summary>
    internal sealed class InlineStyleTransitionTimingTests
    {
        private GameObject _go;
        private PanelSettings _settings;
        private TargetFrameRateScope _frameRateScope;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _frameRateScope = new TargetFrameRateScope(120);
            yield break;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _frameRateScope.Dispose();
            if (_go != null) Object.Destroy(_go);
            if (_settings != null) Object.Destroy(_settings);
            yield return null;
        }

        private static void WriteTransition(VisualElement element, int durationMs)
        {
            element.style.transitionProperty = new List<StylePropertyName> { new("all") };
            element.style.transitionDuration = new List<TimeValue> { new(durationMs, TimeUnit.Millisecond) };
            element.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.Linear) };
        }

        private static VisualElement AddBox(VisualElement root)
        {
            var box = new VisualElement();
            box.style.position = Position.Absolute;
            box.style.width = 50;
            box.style.height = 50;
            root.Add(box);
            return box;
        }

        private static bool IsBetween(float x) => x > 15f && x < 285f;

        // GREEN_ON_BASE(characterization): UI Toolkit's own write-order behaviour, which the hold relies on.
        // No production code runs in it, so the base and the branch run the same engine. Measured: writing the
        // transition first on the first two elements as well makes all three tween, which reddens it.
        [UnityTest]
        public IEnumerator Given_ThreeElements_When_AnInlineTranslateIsWrittenBeforeOrAfterAnInlineTransitionInOneFrame_Then_OnlyTheOneWrittenAfterTweens()
        {
            // Arrange — the second element already carries a 1ms transition, resolved a frame ago.
            _go = new GameObject("InlineStyleTransitionTiming");
            var doc = _go.AddComponent<UIDocument>();
            _settings = TestPanelSettings.Create();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            yield return null;
            var untimed = AddBox(doc.rootVisualElement);
            var shortTimed = AddBox(doc.rootVisualElement);
            var transitionFirst = AddBox(doc.rootVisualElement);
            WriteTransition(shortTimed, 1);
            yield return null;

            // Act — every element gets the same 300ms transition and translate in one frame; only the third
            // gets the transition first.
            untimed.style.translate = new Translate(300, 0);
            WriteTransition(untimed, 300);
            shortTimed.style.translate = new Translate(300, 0);
            WriteTransition(shortTimed, 300);
            WriteTransition(transitionFirst, 300);
            transitionFirst.style.translate = new Translate(300, 0);
            var seen = (untimed: false, shortTimed: false, transitionFirst: false);
            var deadline = Time.realtimeSinceStartupAsDouble + 0.6;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                seen.untimed |= IsBetween(untimed.resolvedStyle.translate.x);
                seen.shortTimed |= IsBetween(shortTimed.resolvedStyle.translate.x);
                seen.transitionFirst |= IsBetween(transitionFirst.resolvedStyle.translate.x);
                yield return null;
            }

            // Assert
            Assert.That(seen, Is.EqualTo((false, false, true)));
        }
    }
}
#endif
