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

    /// <summary>
    /// The preview window's zoom/layout rework: the mount canvas and its zoom box never shrink under flex
    /// pressure (a fixed-size story taller than the stage keeps its declared height instead of being squeezed to
    /// fit), and the zoom box's LAYOUT size tracks the zoom factor (not just the canvas's paint scale), so the
    /// stage's ScrollView has the correct scrollable extent at any zoom.
    /// </summary>
    /// <remarks>
    /// The canvas, zoom box, and story selection are private to the window; reached by reflection (the repo
    /// convention for verifying internals from a test assembly that lacks InternalsVisibleTo). A story is built
    /// directly via <see cref="VelvetPreviewStory"/>'s internal constructor (as <c>VelvetPreviewHostTests</c>
    /// does) so this fixture does not depend on story discovery scanning any particular assembly.
    /// </remarks>
    internal sealed class VelvetPreviewWindowZoomLayoutTests
    {
        // Taller than any reasonable test-stage height, so a squeeze bug (flex-shrink compressing the canvas to
        // fit) is unambiguous: the resolved height would land far below this if the bug were present.
        private const int TallStoryWidth = 400;
        private const int TallStoryHeight = 2000;
        private const string Marker = "zoom-layout-marker";

        private static VNode TallStory() => V.Div(name: Marker, className: "box");

        private VelvetPreviewWindow _window;

        [SetUp]
        public void SetUp()
        {
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");
            // Cleared before the window is created so CreateGUI's RefreshStories cannot restore a persisted
            // selection (e.g. a developer having clicked an example story in the live window) that would land on
            // an explicit-size story and defeat the viewport-driven assertions below.
            EditorPrefs.DeleteKey("Velvet.Preview.LastStoryId");
            EditorPrefs.SetString("Velvet.Preview.Viewport", "Full");
            EditorPrefs.SetString("Velvet.Preview.Zoom", "100%");
            _window = EditorWindow.GetWindow<VelvetPreviewWindow>();
            // Small enough that a story taller than this would be squeezed by a naive flex column if flexShrink
            // were left at its UI Toolkit default of 1.
            _window.position = new Rect(0, 0, 500, 300);
            _window.Show();
            // The window instance can be reused across tests (GetWindow finds the existing one), so its own
            // auto-selection from a previous run could still be sitting in _selected — null it explicitly, via the
            // same Select path the individual tests use to pick their own story afterward.
            SelectStory(null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_window == null) return;
            _window.Close();
            _window = null;
        }

        private static VelvetPreviewStory BuildStory(int width, int height)
        {
            var method = typeof(VelvetPreviewWindowZoomLayoutTests).GetMethod(
                nameof(TallStory), BindingFlags.Static | BindingFlags.NonPublic);
            Assume.That(method, Is.Not.Null, "fixture method 'TallStory' must exist");
            var attribute = new VelvetPreviewAttribute { Name = "TallStory", Group = "ZoomLayoutFixture", Width = width, Height = height };
            return PreviewStoryTestFactory.Build(method, attribute);
        }

        // Selects the story through the window's private Select method — the same path the sidebar list uses —
        // so ApplyCanvasSize, the mount, and the post-mount reapply all run exactly as they would interactively.
        private void SelectStory(VelvetPreviewStory story)
        {
            var select = typeof(VelvetPreviewWindow).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic);
            Assume.That(select, Is.Not.Null, "VelvetPreviewWindow.Select must exist");
            select.Invoke(_window, new object[] { story });
        }

        private void InvokePrivate(string name, params object[] args)
        {
            var method = typeof(VelvetPreviewWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assume.That(method, Is.Not.Null, $"VelvetPreviewWindow.{name} must exist");
            method.Invoke(_window, args);
        }

        private VisualElement PrivateElement(string fieldName)
        {
            var field = typeof(VelvetPreviewWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assume.That(field, Is.Not.Null, $"VelvetPreviewWindow.{fieldName} must exist");
            return (VisualElement)field.GetValue(_window);
        }

        // Forces the window's panel through a layout pass so resolvedStyle is populated; the EditMode batch
        // player loop does not tick layout on its own.
        private void ForceLayout()
        {
            var panel = _window.rootVisualElement.panel;
            Assume.That(panel, Is.Not.Null, "the window must have a live panel");
            EditorPanelTestHelpers.ForcePanelUpdate(panel);
        }

        [Test]
        public void Given_AFixedSizeStoryTallerThanTheStage_When_Selected_Then_TheCanvasKeepsItsDeclaredHeight()
        {
            // Arrange
            var story = BuildStory(TallStoryWidth, TallStoryHeight);

            // Act
            SelectStory(story);
            ForceLayout();

            // Assert — flexShrink: 0 on the canvas means the stage (300px tall) does not compress it down.
            Assert.That(PrivateElement("_canvas").resolvedStyle.height, Is.EqualTo(TallStoryHeight));
        }

        [Test]
        public void Given_ACustomViewport_When_Applied_Then_TheCanvasReferenceSizeMatchesBothAxes()
        {
            // Arrange — set the W/H toolbar widgets' values, as a user typing in them would. The change handler
            // reads the live widgets (not a plain backing field), so the widgets are what must be driven here.
            var widthField = (IntegerField)typeof(VelvetPreviewWindow)
                .GetField("_viewportWidthField", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(_window);
            var heightField = (IntegerField)typeof(VelvetPreviewWindow)
                .GetField("_viewportHeightField", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(_window);
            Assume.That(widthField, Is.Not.Null, "VelvetPreviewWindow._viewportWidthField must exist and be built");
            Assume.That(heightField, Is.Not.Null, "VelvetPreviewWindow._viewportHeightField must exist and be built");
            // SetValueWithoutNotify so setting both fields does not itself trigger two premature (and, since the
            // fields disagree mid-setup, wrong) remounts before the explicit invoke below runs the handler once
            // with both values already in place.
            widthField.SetValueWithoutNotify(620);
            heightField.SetValueWithoutNotify(410);

            // Act
            InvokePrivate("OnViewportFieldChanged");

            // Assert
            var canvas = PrivateElement("_canvas");
            Assert.That((canvas.style.width.value.value, canvas.style.height.value.value), Is.EqualTo((620f, 410f)));
        }

        [Test]
        public void Given_AZoomFactorOf200Percent_When_Applied_Then_TheZoomBoxLayoutSizeDoublesTheReferenceSize()
        {
            // Arrange — a fixed-size story gives a known, stable reference size to multiply.
            var story = BuildStory(TallStoryWidth, TallStoryHeight);
            SelectStory(story);
            ForceLayout();

            // Act
            InvokePrivate("SetZoom", "200%");

            // Assert
            Assert.That(PrivateElement("_zoomBox").style.width.value.value, Is.EqualTo(TallStoryWidth * 2f));
        }

        [Test]
        public void Given_TheStageScrollView_When_Built_Then_TheContentContainerHasAFullPercentMinHeight()
        {
            // Arrange / Act — the scroll view is built during window creation (SetUp); nothing further to drive.

            // Assert — Unity's default USS gives a VerticalAndHorizontal ScrollView's content container an
            // align-self: flex-start that shrink-wraps it vertically, leaving justifyContent: Center with no
            // vertical space to center within (a small story pinned to the top). A Percent(100) min-height is a
            // proxy for the fix here — asserting the true vertical centering needs a live layout pass.
            var minHeight = PrivateElement("_stageScroll").contentContainer.style.minHeight.value;
            Assert.That((minHeight.value, minHeight.unit), Is.EqualTo((100f, LengthUnit.Percent)));
        }

        [Test]
        public void Given_TheZoomBoxFrame_When_TheBackgroundSwitchesBetweenDarkAndLight_Then_TheFrameColorDiffers()
        {
            // Arrange — a single fixed frame color would read against one backdrop and nearly vanish against the
            // other, so the frame must be re-derived per background rather than staying constant.
            InvokePrivate("SetBackground", "Dark");
            var darkFrame = PrivateElement("_zoomBox").style.borderTopColor.value;

            // Act
            InvokePrivate("SetBackground", "Light");
            var lightFrame = PrivateElement("_zoomBox").style.borderTopColor.value;

            // Assert
            Assert.That(lightFrame, Is.Not.EqualTo(darkFrame));
        }
    }
}
