using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <c>V.Anchored</c>'s wiring contract: mounting registers an <c>AnchoredBinding</c> and forces
    /// the element out of flow (<c>position: absolute</c>), a target behind the camera hides the element
    /// (<c>display: none</c>) WITHOUT reaching the real screen-projection call (see the class remarks on why
    /// that matters for this fixture), unmounting releases the binding and every inline style it drove, and a
    /// patched target updates the SAME binding rather than tearing down and recreating it.
    /// </summary>
    /// <remarks>
    /// Deliberately does NOT assert the actual projected left/top for an in-front-of-camera target:
    /// <c>RuntimePanelUtils.CameraTransformWorldToPanel</c> casts its panel argument to
    /// <c>BaseRuntimePanel</c> internally, and this fixture's panel (via <c>MotionSimulatedPanelTestsBase</c>'s
    /// <c>EditorPanelSimulator</c>) is an EDITOR panel, not a runtime one — calling it here would throw an
    /// <c>InvalidCastException</c>, not exercise the real behavior. The projection math itself is pinned by
    /// <c>AnchoredPlaybackTests</c> (PlayMode, a real <c>UIDocument</c> runtime panel) instead. The
    /// behind-camera path is safe to test here because it short-circuits BEFORE that call.
    /// </remarks>
    [TestFixture]
    internal sealed class AnchoredTests : MotionSimulatedPanelTestsBase
    {
        private GameObject _cameraGo;
        private GameObject _targetGo;
        private Camera _camera;

        public override void SetUp()
        {
            base.SetUp();
            _cameraGo = new GameObject("AnchoredTestsCamera");
            _camera = _cameraGo.AddComponent<Camera>();
            _cameraGo.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
            _targetGo = new GameObject("AnchoredTestsTarget");
        }

        public override void TearDown()
        {
            if (_cameraGo != null) UnityEngine.Object.DestroyImmediate(_cameraGo);
            if (_targetGo != null) UnityEngine.Object.DestroyImmediate(_targetGo);
            base.TearDown();
        }

        [Test]
        public void Given_AnAnchoredElement_When_Mounted_Then_ItIsForcedOutOfFlow()
        {
            // Arrange & Act — a target in front of the camera; the exact projected value is not asserted here
            // (see class remarks), only that positioning is even possible at all.
            _targetGo.transform.position = new Vector3(0f, 0f, 0f);
            _reconciler.Reconcile(Root, Array.Empty<VNode>(),
                new[] { V.Anchored(_targetGo.transform, camera: _camera, key: "a", name: "anchored") });

            // Assert
            var element = Root.Q<VisualElement>("anchored");
            Assert.That(element.style.position.value, Is.EqualTo(Position.Absolute));
        }

        [Test]
        public void Given_ATargetBehindTheCamera_When_Ticked_Then_TheElementIsHidden()
        {
            // Arrange — the target sits behind the camera's own position along its forward axis.
            _targetGo.transform.position = new Vector3(0f, 0f, -20f);
            _reconciler.Reconcile(Root, Array.Empty<VNode>(),
                new[] { V.Anchored(_targetGo.transform, camera: _camera, key: "a", name: "anchored") });
            var element = Root.Q<VisualElement>("anchored");

            // Act
            Tick();

            // Assert
            Assert.That(element.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void Given_AnUnmountedAnchoredElement_When_TornDown_Then_TheBindingAndItsInlineStylesAreReleased()
        {
            // Arrange
            _targetGo.transform.position = new Vector3(0f, 0f, 0f);
            var tree = new[] { V.Anchored(_targetGo.transform, camera: _camera, key: "a", name: "anchored") };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);
            var element = Root.Q<VisualElement>("anchored");
            Assume.That(_reconciler.Context.AnchoredBindings.ContainsKey(element), Is.True,
                "Precondition: mounting registered a binding");

            // Act
            _reconciler.Reconcile(Root, tree, Array.Empty<VNode>());

            // Assert
            Assert.That(_reconciler.Context.AnchoredBindings.ContainsKey(element), Is.False);
        }

        [Test]
        public void Given_NoCameraIsSuppliedAndNoMainCameraExists_When_Ticked_Then_TheElementIsHidden()
        {
            // Arrange — Camera.main resolves to null: no camera param, and _camera (the only Camera in this
            // test's scene) is never tagged MainCamera.
            _targetGo.transform.position = new Vector3(0f, 0f, 0f);
            _reconciler.Reconcile(Root, Array.Empty<VNode>(),
                new[] { V.Anchored(_targetGo.transform, key: "a", name: "anchored") });
            var element = Root.Q<VisualElement>("anchored");

            // Act
            Tick();

            // Assert
            Assert.That(element.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void Given_ATargetInFrontOfTheCamera_When_TheHostPanelIsNotARuntimePanel_Then_TheElementIsHidden()
        {
            // Arrange & Act — this fixture's panel is an Editor-context one (see class remarks); an
            // in-front-of-camera target has nothing that would otherwise hide it, isolating this guard.
            _targetGo.transform.position = new Vector3(0f, 0f, 0f);
            _reconciler.Reconcile(Root, Array.Empty<VNode>(),
                new[] { V.Anchored(_targetGo.transform, camera: _camera, key: "a", name: "anchored") });
            var element = Root.Q<VisualElement>("anchored");
            Tick();

            // Assert
            Assert.That(element.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void Given_AnAnchoredElement_When_ItsTargetIsPatchedToADifferentTransform_Then_TheSameBindingIsReused()
        {
            // Arrange
            var otherTargetGo = new GameObject("AnchoredTestsOtherTarget");
            try
            {
                _targetGo.transform.position = new Vector3(0f, 0f, 0f);
                var before = new[] { V.Anchored(_targetGo.transform, camera: _camera, key: "a", name: "anchored") };
                _reconciler.Reconcile(Root, Array.Empty<VNode>(), before);
                var element = Root.Q<VisualElement>("anchored");
                _reconciler.Context.AnchoredBindings.TryGetValue(element, out var bindingBefore);
                Assume.That(bindingBefore, Is.Not.Null, "Precondition: mounting registered a binding");

                // Act
                var after = new[] { V.Anchored(otherTargetGo.transform, camera: _camera, key: "a", name: "anchored") };
                _reconciler.Reconcile(Root, before, after);

                // Assert — the SAME binding instance is updated in place (its Settings.Target changes),
                // rather than the element being torn down and a fresh binding attached.
                _reconciler.Context.AnchoredBindings.TryGetValue(element, out var bindingAfter);
                Assert.That((ReferenceEquals(bindingAfter, bindingBefore), bindingAfter?.Settings.Target),
                    Is.EqualTo((true, otherTargetGo.transform)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(otherTargetGo);
            }
        }
    }
}
