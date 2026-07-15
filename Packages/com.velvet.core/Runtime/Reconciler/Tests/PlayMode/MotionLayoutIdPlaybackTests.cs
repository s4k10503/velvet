#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
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
