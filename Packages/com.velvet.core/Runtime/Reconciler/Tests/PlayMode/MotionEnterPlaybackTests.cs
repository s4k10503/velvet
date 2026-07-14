#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a standalone variant enter (initial -> animate) actually PLAYS on a real runtime
    /// panel — coverage the EditMode simulator suites cannot give, because their manual batch
    /// drains run outside the panel's timer tick, which always lands the "next frame" class swap
    /// one tick after the from-state was computed. On a runtime panel the mount itself runs inside
    /// (or right before) a timer tick, so a zero-delay swap fires before the panel has computed
    /// the from-state even once; the transition then sees no property change and the whole enter
    /// degenerates to an instant jump. A playing enter must pass through intermediate opacity.
    /// </summary>
    internal sealed class MotionEnterPlaybackTests
    {
        private static readonly Dictionary<string, string> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        private GameObject _go;
        private PanelSettings _settings;
        private MountedTree _mounted;
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
            _mounted?.Dispose();
            _mounted = null;
            if (_go != null) Object.Destroy(_go);
            if (_settings != null) Object.Destroy(_settings);
            yield return null;
        }

        [Component]
        private static VNode EnterHost()
        {
            return V.Div(name: "wrap", children: new VNode[]
            {
                V.Motion(key: "m", name: "m", variants: s_fade,
                    initial: "hidden", animate: "visible",
                    transition: new StyleTransitionConfig { DurationSec = 0.4f }),
            });
        }

        [UnityTest]
        public IEnumerator Given_AStandaloneVariantEnterOnARuntimePanel_When_FramesAdvance_Then_OpacityPassesThroughAnIntermediateValue()
        {
            // Arrange — a real UIDocument panel with the bundled utilities so opacity-0/100 resolve.
            _go = new GameObject("EnterPlayback");
            var doc = _go.AddComponent<UIDocument>();
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            yield return null;
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss");
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            doc.rootVisualElement.styleSheets.Add(sheet);

            // Act — mount, then let the panel run past the whole 0.4s enter while sampling.
            _mounted = V.Mount(doc.rootVisualElement, V.Component(EnterHost, key: "root"));
            var m = doc.rootVisualElement.Q<VisualElement>("m");
            Assume.That(m, Is.Not.Null, "Precondition: the motion mounted");
            var sawIntermediate = false;
            var deadline = Time.realtimeSinceStartupAsDouble + 1.0;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                var opacity = m.resolvedStyle.opacity;
                if (opacity > 0.05f && opacity < 0.95f)
                {
                    sawIntermediate = true;
                }
                yield return null;
            }

            // Assert — the enter tweened through the middle instead of snapping straight to the
            // animate pose (the from-state must survive one style pass so the transition can fire).
            Assert.That(sawIntermediate, Is.True);
        }
    }
}
#endif
