using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.Editor.Preview;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The preview window's Viewport addon: selecting a custom W/H pair (via the toolbar fields) sizes the mount
    /// canvas to that reference size and marks it a responsive scope (<c>@container</c>) so a mounted story's
    /// <c>sm:</c>/<c>md:</c>… evaluate against the simulated width; <b>Full</b> (the only remaining menu preset —
    /// device presets were removed in favor of free-form W/H entry) removes the marker and restores the fill
    /// behavior.
    /// </summary>
    /// <remarks>
    /// The viewport selection handlers (<c>SetViewport</c>/<c>OnViewportFieldChanged</c>) and the mount canvas
    /// are private to the window; the tests reach them by reflection (the repo convention for verifying internals
    /// from a test assembly that lacks InternalsVisibleTo).
    /// </remarks>
    internal sealed class VelvetPreviewWindowViewportTests
    {
        private const float CustomWidth = 500f;
        private const float CustomHeight = 900f;
        private const string FullViewport = "Full";
        private const string CustomViewport = "Custom";

        private VelvetPreviewWindow _window;

        [SetUp]
        public void SetUp()
        {
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");
            // Cleared before the window is created so CreateGUI's RefreshStories cannot restore a persisted
            // selection (e.g. a developer having clicked an example story in the live window) that would land on
            // an explicit-size story and defeat every assertion below that assumes the viewport drives the canvas.
            EditorPrefs.DeleteKey("Velvet.Preview.LastStoryId");
            EditorPrefs.SetString("Velvet.Preview.Viewport", FullViewport);
            _window = EditorWindow.GetWindow<VelvetPreviewWindow>();
            _window.Show();
            // The window instance can be reused across tests (GetWindow finds the existing one), so its own
            // auto-selection from a previous run could still be sitting in _selected — null it explicitly rather
            // than relying on the cleared EditorPrefs key alone.
            ResetSelection();
        }

        // Restores the pre-example baseline: no story selected, so ApplyCanvasSize lets the viewport drive the
        // canvas instead of an explicit-size story's own Width/Height.
        private void ResetSelection()
        {
            var select = typeof(VelvetPreviewWindow).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic);
            Assume.That(select, Is.Not.Null, "VelvetPreviewWindow.Select must exist");
            select.Invoke(_window, new object[] { null });
        }

        [TearDown]
        public void TearDown()
        {
            if (_window == null) return;
            _window.Close();
            _window = null;
        }

        private void SetViewport(string viewport)
        {
            var method = typeof(VelvetPreviewWindow).GetMethod(
                "SetViewport", BindingFlags.Instance | BindingFlags.NonPublic);
            Assume.That(method, Is.Not.Null, "VelvetPreviewWindow.SetViewport must exist");
            method.Invoke(_window, new object[] { viewport });
        }

        // Drives the same path the W/H toolbar fields use: set the actual IntegerField widgets' values (as a user
        // typing in them would) via reflection, then invoke the field-changed handler so the viewport switches to
        // Custom using those values. OnViewportFieldChanged reads the live widgets, not a settable backing field,
        // so the widgets themselves — not some intermediate int — are what must be driven here.
        private void SetCustomViewport(int width, int height)
        {
            SetPrivateFieldWidgetValue("_viewportWidthField", width);
            SetPrivateFieldWidgetValue("_viewportHeightField", height);
            var method = typeof(VelvetPreviewWindow).GetMethod(
                "OnViewportFieldChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            Assume.That(method, Is.Not.Null, "VelvetPreviewWindow.OnViewportFieldChanged must exist");
            method.Invoke(_window, null);
        }

        private void SetPrivateFieldWidgetValue(string fieldName, int value)
        {
            var field = typeof(VelvetPreviewWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assume.That(field, Is.Not.Null, $"VelvetPreviewWindow.{fieldName} must exist");
            var widget = (IntegerField)field.GetValue(_window);
            Assume.That(widget, Is.Not.Null, $"VelvetPreviewWindow.{fieldName} must be built by the time the test runs");
            // WithoutNotify: setting both fields must not itself raise the change callback (which would remount
            // once per field, before both values are actually in place) — SetCustomViewport invokes the handler
            // itself exactly once, after both widgets already hold their new values.
            widget.SetValueWithoutNotify(value);
        }

        private VisualElement Canvas()
        {
            var field = typeof(VelvetPreviewWindow).GetField(
                "_canvas", BindingFlags.Instance | BindingFlags.NonPublic);
            Assume.That(field, Is.Not.Null, "VelvetPreviewWindow._canvas must exist");
            return (VisualElement)field.GetValue(_window);
        }

        private VisualElement ZoomBox()
        {
            var field = typeof(VelvetPreviewWindow).GetField(
                "_zoomBox", BindingFlags.Instance | BindingFlags.NonPublic);
            Assume.That(field, Is.Not.Null, "VelvetPreviewWindow._zoomBox must exist");
            return (VisualElement)field.GetValue(_window);
        }

        private IntegerField ViewportField(string fieldName)
        {
            var field = typeof(VelvetPreviewWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assume.That(field, Is.Not.Null, $"VelvetPreviewWindow.{fieldName} must exist");
            var widget = (IntegerField)field.GetValue(_window);
            Assume.That(widget, Is.Not.Null, $"VelvetPreviewWindow.{fieldName} must be built by the time the test runs");
            return widget;
        }

        private int PrivateInt(string fieldName)
        {
            var field = typeof(VelvetPreviewWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assume.That(field, Is.Not.Null, $"VelvetPreviewWindow.{fieldName} must exist");
            return (int)field.GetValue(_window);
        }

        // Drives the same path the preset dropdown uses (SetViewportPreset), rather than SetViewport, so the
        // Full-preset-clobbers-custom-size regression is exercised through its real entry point.
        private void SetViewportPreset(string label, float width, float height)
        {
            var method = typeof(VelvetPreviewWindow).GetMethod(
                "SetViewportPreset", BindingFlags.Instance | BindingFlags.NonPublic);
            Assume.That(method, Is.Not.Null, "VelvetPreviewWindow.SetViewportPreset must exist");
            method.Invoke(_window, new object[] { label, width, height });
        }

        [Test]
        public void Given_ACustomWidthAndHeight_When_TheFieldsChange_Then_TheCanvasWidthMatches()
        {
            // Arrange / Act — simulates editing the W/H toolbar fields to an arbitrary resolution.
            SetCustomViewport((int)CustomWidth, (int)CustomHeight);

            // Assert
            Assert.That(Canvas().style.width.value.value, Is.EqualTo(CustomWidth));
        }

        [Test]
        public void Given_ACustomWidthAndHeight_When_TheFieldsChange_Then_TheCanvasHeightMatches()
        {
            // Arrange / Act
            SetCustomViewport((int)CustomWidth, (int)CustomHeight);

            // Assert
            Assert.That(Canvas().style.height.value.value, Is.EqualTo(CustomHeight));
        }

        [Test]
        public void Given_ACustomViewport_When_Selected_Then_TheCanvasIsMarkedAsAResponsiveScope()
        {
            // Arrange / Act
            SetCustomViewport((int)CustomWidth, (int)CustomHeight);

            // Assert
            Assert.That(Canvas().ClassListContains(VelvetResponsive.ContainerClass), Is.True);
        }

        [Test]
        public void Given_ACustomViewport_When_SwitchedBackToFull_Then_TheScopeMarkerIsRemoved()
        {
            // Arrange — a custom viewport first marks the canvas.
            SetCustomViewport((int)CustomWidth, (int)CustomHeight);
            Assume.That(Canvas().ClassListContains(VelvetResponsive.ContainerClass), Is.True,
                "Precondition: the custom viewport marked the canvas");

            // Act
            SetViewport(FullViewport);

            // Assert
            Assert.That(Canvas().ClassListContains(VelvetResponsive.ContainerClass), Is.False);
        }

        [Test]
        public void Given_ACustomWidthAndHeight_When_TheFieldsChange_Then_TheViewportModeBecomesCustom()
        {
            // Arrange / Act
            SetCustomViewport((int)CustomWidth, (int)CustomHeight);

            // Assert
            var field = typeof(VelvetPreviewWindow).GetField("_viewport", BindingFlags.Instance | BindingFlags.NonPublic);
            Assume.That(field, Is.Not.Null, "VelvetPreviewWindow._viewport must exist");
            Assert.That((string)field.GetValue(_window), Is.EqualTo(CustomViewport));
        }

        [Test]
        public void Given_AZoomFactorOf200Percent_When_Applied_Then_TheZoomBoxWidthIsTwiceTheReferenceWidth()
        {
            // Arrange — a known reference width via a custom viewport, then zoom to 200%.
            SetCustomViewport((int)CustomWidth, (int)CustomHeight);
            var setZoom = typeof(VelvetPreviewWindow).GetMethod("SetZoom", BindingFlags.Instance | BindingFlags.NonPublic);
            Assume.That(setZoom, Is.Not.Null, "VelvetPreviewWindow.SetZoom must exist");

            // Act
            setZoom.Invoke(_window, new object[] { "200%" });

            // Assert
            Assert.That(ZoomBox().style.width.value.value, Is.EqualTo(CustomWidth * 2f));
        }

        [Test]
        public void Given_TheViewportToolbar_When_Built_Then_BothWidthAndHeightFieldsAreDelayed()
        {
            // Arrange / Act — the fields are built during window creation (SetUp); nothing further to drive.

            // Assert — isDelayed so the change event (and the remount it triggers) commits once on Enter/blur
            // rather than firing per keystroke while typing a multi-digit value.
            Assert.That((ViewportField("_viewportWidthField").isDelayed, ViewportField("_viewportHeightField").isDelayed),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ACustomViewportSize_When_TheFullPresetIsSelected_Then_TheRememberedCustomSizeIsPreserved()
        {
            // Arrange — a custom size stored via the toolbar fields.
            SetCustomViewport((int)CustomWidth, (int)CustomHeight);
            Assume.That(PrivateInt("_viewportWidth"), Is.EqualTo((int)CustomWidth), "Precondition: the custom width was stored");

            // Act — Full is (0, 0): it must not clamp through and clobber the remembered custom size.
            SetViewportPreset(FullViewport, 0f, 0f);

            // Assert
            Assert.That((PrivateInt("_viewportWidth"), PrivateInt("_viewportHeight")), Is.EqualTo(((int)CustomWidth, (int)CustomHeight)));
        }

        [Test]
        public void Given_AnUnrecognizedPersistedViewportLabel_When_TheWindowOpens_Then_TheViewportResetsToFull()
        {
            // Arrange — a label a pre-rework session could have persisted, matching neither the current preset
            // table nor Custom.
            _window.Close();
            EditorPrefs.SetString("Velvet.Preview.Viewport", "Mobile (375)");

            // Act
            _window = EditorWindow.GetWindow<VelvetPreviewWindow>();
            _window.Show();

            // Assert
            var field = typeof(VelvetPreviewWindow).GetField("_viewport", BindingFlags.Instance | BindingFlags.NonPublic);
            Assume.That(field, Is.Not.Null, "VelvetPreviewWindow._viewport must exist");
            Assert.That((string)field.GetValue(_window), Is.EqualTo(FullViewport));
        }
    }
}
