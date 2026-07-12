#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a runtime animate-label flip on a mounted Motion actually TWEENS on a real runtime
    /// panel when only its <see cref="StyleTransitionConfig"/> declares the timing (no transition
    /// utilities in the class list). Framer applies <c>transition</c> to every animate update; a
    /// label flip whose class diff lands instantly — because nothing wrote the config's timing to
    /// the element — snaps to the end pose without ever showing an intermediate value.
    /// </summary>
    internal sealed class MotionRuntimeSwapPlaybackTests
    {
        private static readonly Dictionary<string, string> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        private readonly record struct LabelState(string Label);

        private sealed class LabelStore : Store<LabelState>
        {
            public LabelStore() : base(new LabelState("hidden")) { }
            public void Set(string label) => SetState(_ => new LabelState(label));
            protected override void ResetCore() => SetState(_ => new LabelState("hidden"));
        }

        private static LabelStore s_labelStore;

        private GameObject _go;
        private PanelSettings _settings;
        private MountedTree _mounted;
        private LabelStore _store;
        private int _savedTargetFrameRate;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _savedTargetFrameRate = Application.targetFrameRate;
            Application.targetFrameRate = 120;
            s_labelStore = null;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Application.targetFrameRate = _savedTargetFrameRate;
            _mounted?.Dispose();
            _mounted = null;
            _store?.Dispose();
            _store = null;
            if (_go != null) Object.Destroy(_go);
            if (_settings != null) Object.Destroy(_settings);
            yield return null;
        }

        [Component]
        private static VNode SwapHost()
        {
            var label = Hooks.UseStore(s_labelStore, s => s.Label);
            return V.Div(name: "wrap", children: new VNode[]
            {
                V.Motion(key: "m", name: "m", variants: s_fade, animate: label,
                    transition: new StyleTransitionConfig { DurationSec = 0.4f }),
            });
        }

        [UnityTest]
        public IEnumerator Given_ARuntimeAnimateFlipOnARuntimePanel_When_FramesAdvance_Then_OpacityPassesThroughAnIntermediateValue()
        {
            // Arrange — a real UIDocument panel with the bundled utilities so opacity-0/100 resolve;
            // the Motion mounts resting at the hidden variant.
            _go = new GameObject("RuntimeSwapPlayback");
            var doc = _go.AddComponent<UIDocument>();
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            yield return null;
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss");
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            doc.rootVisualElement.styleSheets.Add(sheet);
            _store = new LabelStore();
            s_labelStore = _store;
            _mounted = V.Mount(doc.rootVisualElement, V.Component(SwapHost, key: "root"));
            var m = doc.rootVisualElement.Q<VisualElement>("m");
            Assume.That(m, Is.Not.Null, "Precondition: the motion mounted");
            yield return null;
            Assume.That(m.resolvedStyle.opacity, Is.LessThan(0.05f),
                "Precondition: the motion rests at the hidden variant");

            // Act — flip the label at runtime and sample past the whole 0.4s swap.
            _store.Set("visible");
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

            // Assert — the flip tweened through the middle on the config's own timing instead of
            // snapping straight to the animate pose.
            Assert.That(sawIntermediate, Is.True);
        }
    }
}
#endif
