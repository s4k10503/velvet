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
        private TargetFrameRateScope _frameRateScope;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _frameRateScope = new TargetFrameRateScope(120);
            s_labelStore = null;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _frameRateScope.Dispose();
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

    /// <summary>
    /// Pins that V.Motion(layoutId:)'s FLIP tween actually plays on a real runtime panel: a rect change
    /// across a re-render passes through an intermediate inline translate on its way back to zero,
    /// instead of jump-cutting straight to the new pose the moment layout settles.
    /// </summary>
    internal sealed class MotionLayoutIdPlaybackTests
    {
        private static StateUpdater<bool> s_setMoved;

        private GameObject _go;
        private PanelSettings _settings;
        private MountedTree _mounted;
        private TargetFrameRateScope _frameRateScope;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _frameRateScope = new TargetFrameRateScope(120);
            s_setMoved = default;
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
        private static VNode SharedBoxRender()
        {
            var (moved, setMoved) = Hooks.UseState(false);
            s_setMoved = setMoved;
            return V.Div(children: new VNode[]
            {
                V.Motion(
                    name: "shared",
                    layoutId: "shared-box",
                    transition: new StyleTransitionConfig { Type = TransitionType.Spring, Stiffness = 80f, Damping = 10f, Mass = 1f },
                    className: moved
                        ? "absolute left-[200px] top-[0px] w-[100px] h-[100px]"
                        : "absolute left-[0px] top-[0px] w-[100px] h-[100px]"),
            });
        }

        [UnityTest]
        public IEnumerator Given_ALayoutIdMotionOnARuntimePanel_When_ItsRectChanges_Then_TheInlineTranslatePassesThroughAnIntermediateValueOnItsWayToZero()
        {
            // Arrange — a real UIDocument panel with the bundled utilities so left-[..]/top-[..]/w-[..]/
            // h-[..] resolve.
            _go = new GameObject("LayoutIdPlayback");
            var doc = _go.AddComponent<UIDocument>();
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            yield return null;
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss");
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            doc.rootVisualElement.styleSheets.Add(sheet);
            _mounted = V.Mount(doc.rootVisualElement, V.Component(SharedBoxRender, key: "root"));
            var element = doc.rootVisualElement.Q<VisualElement>("shared");
            Assume.That(element, Is.Not.Null, "Precondition: the Motion mounted");
            yield return null;

            // Act — move the Motion 200px right, then sample the inline translate.x across real frames.
            s_setMoved.Invoke(true);
            var sawIntermediate = false;
            var sawNearZero = false;
            var deadline = Time.realtimeSinceStartupAsDouble + 2.0;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                var t = element.resolvedStyle.translate.x;
                // The inverse pose starts near -200 (the old-minus-new position delta) and springs back
                // toward 0 — "intermediate" here means strictly between the two, not merely nonzero.
                if (t < -20f && t > -180f)
                {
                    sawIntermediate = true;
                }
                if (!sawNearZero && sawIntermediate && Mathf.Abs(t) < 1f)
                {
                    sawNearZero = true;
                    break;
                }
                yield return null;
            }

            // Assert
            Assert.That((sawIntermediate, sawNearZero), Is.EqualTo((true, true)));
        }
    }
}
#endif
