using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Editor.Preview
{
    /// <summary>
    /// Live UI preview for Velvet: mounts a <c>[VelvetPreview]</c> story onto this window's real UI Toolkit
    /// panel and renders it without entering Play Mode. Because the panel is a genuine one, pointer / focus
    /// events flow into the mounted tree's hooks and re-render in place; while the window is visible the editor
    /// ticks its panel scheduler each editor frame, which drains Velvet's coalesced re-renders and fires
    /// Motion / AnimatePresence timers, so animation runs live — an in-editor live preview with hot-reloading
    /// of edits.
    /// <para>
    /// Scale caveat: an EditorWindow panel has no <c>PanelSettings</c>, so this preview renders at raw
    /// editor-panel scale and cannot reproduce the game's <c>ScaleWithScreenSize</c> @ a fixed reference
    /// resolution. For scale-accurate output use the headless capture path (which sets
    /// <c>referenceResolution</c> on a real <c>PanelSettings</c>); treat this window as a live layout/behavior
    /// view, not a pixel-exact one.
    /// </para>
    /// <para>Open via Window &gt; Velvet &gt; Preview.</para>
    /// <remarks>Equivalent to Storybook's component workshop with Fast-Refresh-style live updates, for users
    /// migrating from those tools.</remarks>
    /// </summary>
    public sealed class VelvetPreviewWindow : EditorWindow
    {
        private const string WindowTitle = "Velvet Preview";
        private const string MenuPath = "Window/Velvet/Preview";
        private const string UtilitiesUssPath = "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";
        private const string LastSelectionKey = "Velvet.Preview.LastStoryId";
        private const string DarkKey = "Velvet.Preview.Dark";
        private const string BackgroundKey = "Velvet.Preview.Background";
        private const string ZoomKey = "Velvet.Preview.Zoom";
        private const string OutlineKey = "Velvet.Preview.Outline";
        private const string MeasureKey = "Velvet.Preview.Measure";
        private const string ViewportKey = "Velvet.Preview.Viewport";
        private const string ViewportWidthKey = "Velvet.Preview.ViewportW";
        private const string ViewportHeightKey = "Velvet.Preview.ViewportH";

        // Custom viewport W/H fields are clamped to this range — 1 keeps a story from collapsing to nothing, 8192
        // is generously above any real device or monitor so it never blocks an intentionally huge reference size.
        private const int MinCustomViewportSize = 1;
        private const int MaxCustomViewportSize = 8192;

        // Default custom W/H shown the first time the window opens (no remembered custom size yet) — full HD
        // landscape, just a starting point for the free-form fields.
        private const int DefaultCustomViewportWidth = 1920;
        private const int DefaultCustomViewportHeight = 1080;

        // Stage backdrops. Dark is the default — a near-black so light UI stays legible
        // and the mounted tree's own bounds read against it; Light is a neutral gray for dark UI.
        private static readonly Color DarkBackdrop = new(0.09f, 0.10f, 0.13f, 1f);
        private static readonly Color LightBackdrop = new(0.92f, 0.92f, 0.92f, 1f);

        // A thin frame drawn around the simulated viewport so its rectangle — and therefore the active resolution
        // and aspect ratio — is always visible against the stage, even when the story is transparent or smaller
        // than its canvas. The color adapts to the active backdrop (set from ApplyBackground) rather than being a
        // single fixed color — a light frame that reads on Dark is nearly invisible against Light, and vice versa.
        // Kept subtle so it reads as a bezel, not part of the story.
        private static readonly Color DarkBgViewportFrame = new(1f, 1f, 1f, 0.35f);
        private static readonly Color LightBgViewportFrame = new(0f, 0f, 0f, 0.35f);
        private static readonly Color CheckerboardViewportFrame = new(0.5f, 0.5f, 0.5f, 0.6f);

        private const string BgDark = "Dark";
        private const string BgLight = "Light";
        private const string BgCheckerboard = "Checkerboard";

        // Discrete zoom steps; "Fit" scales the canvas to fill the stage.
        private const string ZoomFit = "Fit";
        private static readonly (string Label, float Factor)[] ZoomSteps =
        {
            (ZoomFit, 0f), ("50%", 0.5f), ("100%", 1f), ("200%", 2f),
        };

        // Simulated viewports. "Full" lets the canvas fill the stage (no scope) and is the menu's only entry —
        // the fixed device presets (Mobile/Tablet/Desktop) were removed in favor of free-form entry: any reference
        // size, including the old preset sizes, is reachable by typing into the W/H fields below. "Custom" is not
        // in this list — it is entered via those fields and stores its size under
        // ViewportWidthKey/ViewportHeightKey instead of a table entry.
        private const string ViewportFull = "Full";
        private const string ViewportCustom = "Custom";
        private static readonly (string Label, float Width, float Height)[] Viewports =
        {
            (ViewportFull, 0f, 0f),
        };

        private readonly List<VelvetPreviewStory> _stories = new();
        private VelvetPreviewStory _selected;
        private VelvetPreviewHost _host;

        private ListView _list;
        private VisualElement _stage;     // fixed backdrop the scroll view / zoom box center within
        private ScrollView _stageScroll;  // pans/scrolls when the zoom box exceeds the stage viewport
        private VisualElement _zoomBox;   // layout-sized box (reference * zoom) that makes zoom affect layout, not just paint
        private VisualElement _canvas;    // the element the story actually mounts onto
        private CheckerboardBackground _checkerboard;
        private PreviewInspectOverlay _overlay;
        private PreviewControlsPanel _controls;
        private Label _statusLabel;
        private ToolbarMenu _backgroundMenu;
        private ToolbarMenu _zoomMenu;
        private ToolbarMenu _viewportMenu;
        private IntegerField _viewportWidthField;
        private IntegerField _viewportHeightField;

        // View-addon state, persisted in EditorPrefs so it survives remounts / domain reloads.
        private bool _dark;
        private string _background = BgDark;
        private string _zoom = ZoomFit;
        private bool _outline;
        private bool _measure;
        private string _viewport = ViewportFull;
        private int _viewportWidth;
        private int _viewportHeight;

        // The IsDark value the editor (or a running game) had before this window first applied its own, captured
        // once and restored on close so toggling Dark in preview never leaks to whatever else uses the theme.
        private bool _capturedIsDark;
        private bool _hasCapturedIsDark;
        // The last IsDark value this window wrote; restore only reverts if the live value still equals it, so a
        // concurrent writer (e.g. a running game) that changed the theme meanwhile is not clobbered on close.
        private bool _appliedDark;

        [MenuItem(MenuPath)]
        public static void ShowWindow()
        {
            var window = GetWindow<VelvetPreviewWindow>(false, WindowTitle, true);
            window.minSize = new Vector2(640, 400);
            window.Show();
        }

        private void OnDisable()
        {
            _host?.Dispose();
            _host = null;
            RestoreCapturedTheme();
        }

        private void CreateGUI()
        {
            LoadViewPrefs();

            var split = new TwoPaneSplitView(0, 220f, TwoPaneSplitViewOrientation.Horizontal);
            rootVisualElement.Add(split);

            split.Add(BuildSidebar());
            split.Add(BuildStageColumn());

            // The host mounts onto the canvas, which carries the utility stylesheet so utility classes resolve.
            _host = new VelvetPreviewHost(_canvas);

            ApplyBackground();
            ApplyTheme();
            RefreshStories();
        }

        private void LoadViewPrefs()
        {
            _dark = EditorPrefs.GetBool(DarkKey, false);
            _background = EditorPrefs.GetString(BackgroundKey, BgDark);
            _zoom = EditorPrefs.GetString(ZoomKey, ZoomFit);
            _outline = EditorPrefs.GetBool(OutlineKey, false);
            _measure = EditorPrefs.GetBool(MeasureKey, false);
            _viewport = EditorPrefs.GetString(ViewportKey, ViewportFull);
            _viewportWidth = ClampCustomViewportSize(EditorPrefs.GetInt(ViewportWidthKey, DefaultCustomViewportWidth));
            _viewportHeight = ClampCustomViewportSize(EditorPrefs.GetInt(ViewportHeightKey, DefaultCustomViewportHeight));

            if (!IsKnownViewportLabel(_viewport))
            {
                // A label persisted by a pre-rework session (e.g. an old preset that no longer exists) matches
                // neither the current Viewports table nor Custom. Left as-is, ViewportSize() would silently fall
                // back to (0, 0) while the toolbar kept showing the stale label — reset to Full so the label and
                // the actual reference size agree again.
                _viewport = ViewportFull;
                EditorPrefs.SetString(ViewportKey, _viewport);
            }
        }

        private static bool IsKnownViewportLabel(string label)
        {
            if (label == ViewportCustom) return true;
            foreach (var (presetLabel, _, _) in Viewports)
            {
                if (presetLabel == label) return true;
            }

            return false;
        }

        #region Sidebar
        private VisualElement BuildSidebar()
        {
            var sidebar = new VisualElement { style = { minWidth = 180f } };

            var toolbar = new Toolbar();
            var refreshButton = new ToolbarButton(RefreshStories) { text = "Refresh" };
            toolbar.Add(refreshButton);
            toolbar.Add(new ToolbarSpacer { style = { flexGrow = 1f } });
            sidebar.Add(toolbar);

            _list = new ListView(_stories, 22, MakeStoryRow, BindStoryRow)
            {
                selectionType = SelectionType.Single,
                style = { flexGrow = 1f },
            };
            _list.selectionChanged += OnSelectionChanged;
            sidebar.Add(_list);

            return sidebar;
        }

        private static VisualElement MakeStoryRow()
        {
            var label = new Label { style = { unityTextAlign = TextAnchor.MiddleLeft, paddingLeft = 8f } };
            return label;
        }

        private void BindStoryRow(VisualElement element, int index)
        {
            if (index < 0 || index >= _stories.Count) return;
            var story = _stories[index];
            ((Label)element).text = $"{story.Group}  /  {story.Name}";
        }
        #endregion

        #region Stage
        private VisualElement BuildStageColumn()
        {
            var column = new VisualElement { style = { flexGrow = 1f } };

            column.Add(BuildViewToolbar());

            _statusLabel = new Label
            {
                style =
                {
                    paddingLeft = 8f, paddingTop = 4f, paddingBottom = 4f,
                    unityFontStyleAndWeight = FontStyle.Bold,
                },
            };
            column.Add(_statusLabel);

            // Stage on top, controls below — a vertical split so the controls pane is resizable (a canvas above,
            // addons panel below). The controls pane is the fixed-size second pane.
            var vertical = new TwoPaneSplitView(1, 160f, TwoPaneSplitViewOrientation.Vertical) { style = { flexGrow = 1f } };
            column.Add(vertical);

            // The stage is the fixed backdrop; it never shrinks or grows to match the canvas — instead a
            // ScrollView inside it pans/scrolls to reach a zoom box larger than the stage. overflow: Hidden here
            // only clips the checkerboard/overlay to the stage bounds; the scroll view supplies the real clip +
            // scroll behavior for the zoomed content.
            _stage = new VisualElement
            {
                style =
                {
                    flexGrow = 1f,
                    overflow = Overflow.Hidden,
                },
            };
            vertical.Add(_stage);

            _controls = new PreviewControlsPanel();
            _controls.ArgsChanged += OnArgsChanged;
            vertical.Add(_controls);

            // Transparency grid behind the canvas, shown only for the Checkerboard background.
            _checkerboard = new CheckerboardBackground { style = { display = DisplayStyle.None } };
            _stage.Add(_checkerboard);

            // Both scrollers are Auto: a zoom box that fits the stage shows no scroller (the common case), and one
            // larger than the stage in either axis becomes pannable in that axis instead of clipping silently.
            _stageScroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal)
            {
                style = { flexGrow = 1f },
                horizontalScrollerVisibility = ScrollerVisibility.Auto,
                verticalScrollerVisibility = ScrollerVisibility.Auto,
            };
            _stage.Add(_stageScroll);

            // Center the zoom box inside the scroll view's content container. A ScrollView's content container
            // does not center by default (it is sized to its content, like a normal document flow), so this
            // wrapper opts in explicitly: flexGrow fills the viewport when the zoom box is smaller than it, and
            // alignItems/justifyContent center the box within that filled space. When the zoom box is larger than
            // the viewport in an axis, centering has no visible effect there (there is no extra space to center
            // within) and the ScrollView's scrollers take over for that axis instead.
            _stageScroll.contentContainer.style.flexGrow = 1f;
            _stageScroll.contentContainer.style.alignItems = Align.Center;
            _stageScroll.contentContainer.style.justifyContent = Justify.Center;
            // Unity's default USS gives a VerticalAndHorizontal ScrollView's content container align-self:
            // flex-start, which overrides flexGrow along the cross axis and lets it shrink-wrap vertically — so
            // justifyContent: Center above had no vertical space to center within and a small story pinned to the
            // top. A percentage min-size (not a fixed size, so the container still grows past 100% when the zoom
            // box is larger than the stage) forces it to fill the viewport in both axes while still growing to fit.
            _stageScroll.contentContainer.style.minHeight = Length.Percent(100);
            _stageScroll.contentContainer.style.minWidth = Length.Percent(100);

            // The zoom box: its LAYOUT size is the reference size times the zoom factor, so the scroll view's
            // scrollable extent matches what is actually painted. flexShrink 0 keeps it from being compressed by
            // the flex column above it (the original squeeze bug for a fixed-size story taller than the stage).
            // The frame hugs the painted story at any zoom because the zoom box carries the post-zoom layout size
            // (reference * factor) in unscaled px, so its border is a crisp 1px bezel around the simulated viewport.
            // Border colors are left at their default here and set by ApplyBackground (called once from CreateGUI
            // right after this element tree is built, and again on every background change) so the frame always
            // matches the active backdrop.
            _zoomBox = new VisualElement
            {
                style =
                {
                    flexShrink = 0f,
                    borderTopWidth = 1f, borderRightWidth = 1f, borderBottomWidth = 1f, borderLeftWidth = 1f,
                },
            };
            _stageScroll.Add(_zoomBox);

            _canvas = new VisualElement
            {
                style =
                {
                    // flexGrow 0 + flexShrink 0: the canvas is never stretched or compressed by the flex
                    // containers around it, so its declared reference size (written by ApplyCanvasSize) is always
                    // what actually lays out — the fix for a fixed-size story taller than the stage getting
                    // silently squeezed. Constant for the canvas's lifetime, so set once here rather than rewritten
                    // on every ApplyCanvasSize call.
                    flexGrow = 0f,
                    flexShrink = 0f,
                    // Scale about the top-left: the zoom box already carries the post-zoom layout size, so the
                    // canvas's own paint transform must grow from the box's origin, not re-center over it (top-center
                    // origin was the old fill-canvas convention and only made sense when zoom was paint-only).
                    transformOrigin = new TransformOrigin(0f, 0f),
                },
            };
            var utilities = AssetDatabase.LoadAssetAtPath<StyleSheet>(UtilitiesUssPath);
            if (utilities != null) _canvas.styleSheets.Add(utilities);
            _zoomBox.Add(_canvas);

            // Inspection overlay (outline / measure) drawn above everything; non-interactive so it never steals
            // the story's pointer events. It tracks the canvas subtree and re-draws when the stage resizes.
            _overlay = new PreviewInspectOverlay(_canvas)
            {
                OutlineEnabled = _outline,
                MeasureEnabled = _measure,
            };
            _stage.Add(_overlay);
            // The scroll view pans by writing contentContainer.style.translate directly (a paint-time transform),
            // which fires no GeometryChangedEvent — so without this, scrolling a zoomed-in story left the outline
            // / measure overlay frozen at its pre-scroll position until some unrelated geometry event happened to
            // fire. Subscribing to the scrollers' own valueChanged covers both drag-scrolling and programmatic
            // scrolling.
            _stageScroll.horizontalScroller.valueChanged += _ => _overlay?.Refresh();
            _stageScroll.verticalScroller.valueChanged += _ => _overlay?.Refresh();
            // The stage resize is the only geometry source that can change a Full/fill story's reference size (the
            // measured stage size); re-derive the reference and zoom box from it. ApplyCanvasSize now early-outs
            // whenever the reference size is not yet resolved (NaN before the first layout pass, or a collapsed
            // stage), so a resize tick that does not actually make the reference size resolvable simply writes
            // nothing and cannot retrigger this handler in a loop.
            _stage.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                ApplyCanvasSize(_selected);
                ApplyZoom();
                _overlay?.Refresh();
            });
            // The canvas's own geometry change (a story laying out after mount) does not affect sizing decisions
            // here — the reference size is derived from the story metadata / viewport / stage, never from the
            // canvas's own resolved size — but the overlay still needs to redraw to track it.
            _canvas.RegisterCallback<GeometryChangedEvent>(_ => _overlay?.Refresh());

            return column;
        }

        private Toolbar BuildViewToolbar()
        {
            var toolbar = new Toolbar();

            var darkToggle = new ToolbarToggle { text = "Dark", value = _dark };
            darkToggle.RegisterValueChangedCallback(evt =>
            {
                _dark = evt.newValue;
                EditorPrefs.SetBool(DarkKey, _dark);
                ApplyTheme();
            });
            toolbar.Add(darkToggle);

            _backgroundMenu = new ToolbarMenu { text = BackgroundLabel() };
            foreach (var name in new[] { BgDark, BgLight, BgCheckerboard })
            {
                var captured = name;
                _backgroundMenu.menu.AppendAction(
                    captured,
                    _ => SetBackground(captured),
                    _ => _background == captured ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
            toolbar.Add(_backgroundMenu);

            _zoomMenu = new ToolbarMenu { text = ZoomLabel() };
            foreach (var (label, _) in ZoomSteps)
            {
                var captured = label;
                _zoomMenu.menu.AppendAction(
                    captured,
                    _ => SetZoom(captured),
                    _ => _zoom == captured ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
            toolbar.Add(_zoomMenu);

            _viewportMenu = new ToolbarMenu { text = ViewportLabel() };
            foreach (var (label, width, height) in Viewports)
            {
                var captured = label;
                var capturedWidth = width;
                var capturedHeight = height;
                _viewportMenu.menu.AppendAction(
                    captured,
                    _ => SetViewportPreset(captured, capturedWidth, capturedHeight),
                    _ => _viewport == captured ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
            toolbar.Add(_viewportMenu);

            // Free-form W/H entry: picking a preset above writes its values in here (read-only feedback), and
            // editing either field switches the viewport to Custom using both fields' current values — so the
            // fields are always "what the viewport reference size currently is," regardless of how it was set.
            // isDelayed: the change event (and the remount it triggers) commits once on Enter/blur instead of
            // firing — and remounting — on every keystroke while typing a multi-digit value.
            _viewportWidthField = new IntegerField { value = _viewportWidth, style = { width = 50f }, isDelayed = true };
            _viewportWidthField.RegisterValueChangedCallback(_ => OnViewportFieldChanged());
            toolbar.Add(_viewportWidthField);

            _viewportHeightField = new IntegerField { value = _viewportHeight, style = { width = 50f }, isDelayed = true };
            _viewportHeightField.RegisterValueChangedCallback(_ => OnViewportFieldChanged());
            toolbar.Add(_viewportHeightField);

            var outlineToggle = new ToolbarToggle { text = "Outline", value = _outline };
            outlineToggle.RegisterValueChangedCallback(evt =>
            {
                _outline = evt.newValue;
                EditorPrefs.SetBool(OutlineKey, _outline);
                if (_overlay != null) _overlay.OutlineEnabled = _outline;
            });
            toolbar.Add(outlineToggle);

            var measureToggle = new ToolbarToggle { text = "Measure", value = _measure };
            measureToggle.RegisterValueChangedCallback(evt =>
            {
                _measure = evt.newValue;
                EditorPrefs.SetBool(MeasureKey, _measure);
                if (_overlay != null) _overlay.MeasureEnabled = _measure;
            });
            toolbar.Add(measureToggle);

            toolbar.Add(new ToolbarSpacer { style = { flexGrow = 1f } });
            return toolbar;
        }
        #endregion

        #region View addons
        // Captures the pre-existing theme once (so closing the window restores whatever the editor / a running
        // game had), then drives VelvetTheme so the mounted story's dark: variants re-evaluate live.
        private void ApplyTheme()
        {
            if (!_hasCapturedIsDark)
            {
                _capturedIsDark = VelvetTheme.IsDark;
                _hasCapturedIsDark = true;
            }

            VelvetTheme.IsDark = _dark;
            _appliedDark = _dark;
        }

        private void RestoreCapturedTheme()
        {
            if (!_hasCapturedIsDark) return;
            // Only revert if the theme still holds the value WE last applied. If something else changed it after
            // (a running game toggling dark), leave that value alone instead of clobbering it with our captured one.
            if (VelvetTheme.IsDark == _appliedDark) VelvetTheme.IsDark = _capturedIsDark;
            _hasCapturedIsDark = false;
        }

        private void SetBackground(string background)
        {
            _background = background;
            EditorPrefs.SetString(BackgroundKey, background);
            if (_backgroundMenu != null) _backgroundMenu.text = BackgroundLabel();
            ApplyBackground();
        }

        private void ApplyBackground()
        {
            if (_stage == null) return;
            var checkerboard = _background == BgCheckerboard;
            if (_checkerboard != null) _checkerboard.style.display = checkerboard ? DisplayStyle.Flex : DisplayStyle.None;
            _stage.style.backgroundColor = _background switch
            {
                BgLight => LightBackdrop,
                BgCheckerboard => new StyleColor(StyleKeyword.Initial),
                _ => DarkBackdrop,
            };

            // The frame's color must track the backdrop it is drawn over, or a fixed color reads on one background
            // and nearly vanishes on another (the bug this addresses: a single white@0.28 frame was invisible over
            // the Light backdrop's near-white gray).
            var frame = _background switch
            {
                BgLight => LightBgViewportFrame,
                BgCheckerboard => CheckerboardViewportFrame,
                _ => DarkBgViewportFrame,
            };
            if (_zoomBox != null)
            {
                _zoomBox.style.borderTopColor = frame;
                _zoomBox.style.borderRightColor = frame;
                _zoomBox.style.borderBottomColor = frame;
                _zoomBox.style.borderLeftColor = frame;
            }
        }

        private void SetZoom(string zoom)
        {
            _zoom = zoom;
            EditorPrefs.SetString(ZoomKey, zoom);
            if (_zoomMenu != null) _zoomMenu.text = ZoomLabel();
            ApplyZoom();
        }

        // Applies the current zoom factor to BOTH the zoom box's layout size and the canvas's paint scale. Scale
        // alone (the pre-rework behavior) only repaints the canvas larger without changing the space it occupies,
        // so the stage always centered the UNSCALED box: at 200% the painted content overflowed the stage's
        // Hidden clip with nothing to scroll to, and at 50%/Fit the centered (but unscaled) box left dead space
        // below a top-anchored paint. Driving the zoom box's layout size in step with the canvas's paint scale
        // makes the two agree, so centering is correct at any factor and the ScrollView's scrollable extent
        // matches what is actually painted.
        private void ApplyZoom()
        {
            if (_canvas == null || _zoomBox == null) return;
            var factor = _zoom == ZoomFit ? ComputeFitFactor() : ZoomFactor(_zoom);
            var (refW, refH, _) = ComputeReferenceSize(_selected);

            if (IsResolved(refW) && IsResolved(refH))
            {
                _canvas.style.scale = new Scale(new Vector3(factor, factor, 1f));

                var boxWidth = refW * factor;
                var boxHeight = refH * factor;
                // Only write when the value actually changed — this handler also runs from the stage's
                // GeometryChangedEvent, so writing unconditionally could retrigger layout every resize tick even
                // when nothing here actually moved.
                if (!Mathf.Approximately(_zoomBox.style.width.value.value, boxWidth)) _zoomBox.style.width = boxWidth;
                if (!Mathf.Approximately(_zoomBox.style.height.value.value, boxHeight)) _zoomBox.style.height = boxHeight;
            }
            // Else: the reference size is not yet resolved (NaN before the first layout pass, or a collapsed
            // stage). Leave the canvas's scale and the zoom box's size untouched rather than writing a scale/size
            // computed from NaN — that would paint the canvas at a NaN scale (effectively invisible) and, unlike a
            // wrong-but-finite value, nothing would ever compare unequal to NaN to trigger a corrective rewrite
            // once the size resolves. The stage's GeometryChangedEvent re-runs this once resolution is available.

            _overlay?.Refresh();
            // Keep the status line's resolution/zoom readout in step with the applied factor and stage size, so a
            // zoom step or a Fit recompute (e.g. the window being resized) is reflected as text, not only as layout.
            // Passes the factor/reference size already computed above instead of letting UpdateStatus recompute
            // them a second time — this runs on every stage GeometryChangedEvent tick, so recomputation was pure
            // waste.
            UpdateStatus(factor, refW, refH);
        }

        // Fit: the largest scale at which the canvas's reference size fits inside the stage, capped at 1 so Fit
        // never upscales a small story. Returns 1 until the stage has a resolved size or the reference size is not
        // yet known (the stage's GeometryChangedEvent re-runs this once both are available).
        private float ComputeFitFactor()
        {
            if (_stage == null) return 1f;
            var stageW = _stage.resolvedStyle.width;
            var stageH = _stage.resolvedStyle.height;
            if (!IsResolved(stageW) || !IsResolved(stageH)) return 1f;

            var (refW, refH, _) = ComputeReferenceSize(_selected);
            if (!IsResolved(refW) || !IsResolved(refH)) return 1f;

            return Mathf.Min(1f, Mathf.Min(stageW / refW, stageH / refH));
        }

        private static float ZoomFactor(string label)
        {
            foreach (var (name, factor) in ZoomSteps)
            {
                if (name == label) return factor <= 0f ? 1f : factor;
            }

            return 1f;
        }

        // Selecting a preset from the dropdown: store its label (so ViewportLabel/menu-check reflect it) and, for
        // a sized preset, its W/H as the "custom" numbers too, so the W/H fields immediately show the preset's
        // values rather than stale custom ones from a previous session. Full is (0, 0) — it has no size of its
        // own, so storing it would clamp to 1x1 and destroy whatever custom size was remembered; Full leaves the
        // remembered custom size and fields untouched and only changes which viewport is active.
        private void SetViewportPreset(string label, float width, float height)
        {
            if (width > 0f && height > 0f) StoreViewportSize((int)width, (int)height);

            SetViewport(label);
        }

        // Editing either W/H field: the viewport becomes Custom using both fields' current (clamped) values. A
        // non-positive or out-of-range entry is clamped rather than rejected, so the field never gets stuck showing
        // a value that cannot produce a usable canvas.
        private void OnViewportFieldChanged()
        {
            StoreViewportSize(_viewportWidthField?.value ?? _viewportWidth, _viewportHeightField?.value ?? _viewportHeight);

            SetViewport(ViewportCustom);
        }

        // Clamps and persists a custom viewport size, then reflects it into both W/H fields without re-raising
        // their change callback (which would recurse back into OnViewportFieldChanged). Shared by the preset
        // handler (a sized preset also becomes the remembered custom size) and the field-change handler.
        private void StoreViewportSize(int width, int height)
        {
            _viewportWidth = ClampCustomViewportSize(width);
            _viewportHeight = ClampCustomViewportSize(height);
            EditorPrefs.SetInt(ViewportWidthKey, _viewportWidth);
            EditorPrefs.SetInt(ViewportHeightKey, _viewportHeight);
            if (_viewportWidthField != null) _viewportWidthField.SetValueWithoutNotify(_viewportWidth);
            if (_viewportHeightField != null) _viewportHeightField.SetValueWithoutNotify(_viewportHeight);
        }

        private void SetViewport(string viewport)
        {
            _viewport = viewport;
            EditorPrefs.SetString(ViewportKey, viewport);
            if (_viewportMenu != null) _viewportMenu.text = ViewportLabel();

            // Re-apply the canvas size (which toggles the @container scope marker), then re-mount the story so its
            // descendants resolve their responsive width source against the new scope — a manipulator binds its
            // width source at attach, so the story must re-attach for the viewport to drive its breakpoints.
            ApplyCanvasSize(_selected);
            MountWithCurrentArgs(_selected);
        }

        private static int ClampCustomViewportSize(int value) =>
            Mathf.Clamp(value, MinCustomViewportSize, MaxCustomViewportSize);

        // The active viewport's reference W/H in px, or (0, 0) for Full. A known preset label returns its table
        // entry; Custom (or any unrecognized persisted label — no migration of old labels, just fall back to
        // Full-like zero) returns the W/H fields' current values only when actually in Custom mode.
        private (float Width, float Height) ViewportSize()
        {
            foreach (var (label, width, height) in Viewports)
            {
                if (label == _viewport) return (width, height);
            }

            return _viewport == ViewportCustom ? (_viewportWidth, _viewportHeight) : (0f, 0f);
        }

        private string BackgroundLabel() => "BG: " + _background;
        private string ZoomLabel() => "Zoom: " + _zoom;
        private string ViewportLabel() => "Viewport: " + _viewport;
        #endregion

        #region Story lifecycle
        private void RefreshStories()
        {
            var rememberedId = _selected?.Id ?? EditorPrefs.GetString(LastSelectionKey, null);

            _stories.Clear();
            _stories.AddRange(VelvetPreviewRegistry.DiscoverStories());
            _list?.RefreshItems();

            var restored = _stories.FindIndex(s => s.Id == rememberedId);
            if (restored < 0 && _stories.Count > 0) restored = 0;

            if (restored >= 0)
            {
                // WithoutNotify so the row highlight follows along without raising selectionChanged — the
                // explicit Select below is the single mount path; SetSelection would mount a second time.
                _list?.SetSelectionWithoutNotify(new[] { restored });
                Select(_stories[restored]);
            }
            else
            {
                Select(null);
            }
        }

        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            foreach (var item in selection)
            {
                Select(item as VelvetPreviewStory);
                return;
            }
        }

        private void Select(VelvetPreviewStory story)
        {
            _selected = story;
            if (story != null) EditorPrefs.SetString(LastSelectionKey, story.Id);

            ApplyCanvasSize(story);
            // Build the controls for this story first (fresh default args), then mount with those args so the
            // rendered tree matches the knobs shown. For a parameterless story Args is null and Mount uses the
            // plain default-args path.
            _controls?.SetStory(story);
            MountWithCurrentArgs(story);
        }

        // Mounts the story (full mount: re-runs the assembly environment), driving an args-story with the
        // controls' live instance, then re-applies the post-mount view state.
        private void MountWithCurrentArgs(VelvetPreviewStory story)
        {
            if (story != null && story.ArgsType != null) _host?.Mount(story, _controls?.Args);
            else _host?.Mount(story);

            ReapplyAfterMount();
        }

        // Updates ONLY the args of the live story, keeping the assembly environment (fonts / store / CTS) open —
        // a control edit must not tear down and rebuild the backend per keystroke. Falls back to a full mount if
        // there is no live mount to update.
        private void OnArgsChanged(object args)
        {
            if (_selected == null || _selected.ArgsType == null) return;
            if (_host == null || !_host.UpdateArgs(args)) _host?.Mount(_selected, args);
            ReapplyAfterMount();
        }

        // The single post-(re)mount step: re-apply the theme (so a remounted tree picks up dark:), recompute zoom
        // (Fit depends on the current story / viewport size), and refresh the overlay (track the new subtree).
        // ApplyZoom already refreshes the status line itself at the end, so this does not call UpdateStatus again.
        // Called from every mount path so they cannot diverge.
        private void ReapplyAfterMount()
        {
            ApplyTheme();
            ApplyZoom();
            _overlay?.Refresh();
        }

        // A stage/reference dimension is only usable once it is a positive, non-NaN number. resolvedStyle.width/
        // height IS NaN before the panel's first layout pass, and NaN <= 0f is false — so a plain "<= 0" guard lets
        // NaN slip through into a scale or an explicit size write, silently corrupting the canvas until the next
        // resize happens to overwrite it. Every read of a stage/reference size below must go through this guard.
        private static bool IsResolved(float v) => v > 0f && !float.IsNaN(v);

        // Computes the canvas's reference size (pre-zoom, in px) purely from the story metadata / viewport /
        // stage — no field is written here, so callers can probe "what would the reference size be" without
        // mutating state. Matches the pre-rework rule exactly:
        // <list type="bullet">
        // <item>A story's explicit Width/Height ALWAYS wins: an authored card footprint is shown at its real size
        // and is NOT treated as a responsive container. If only one axis is authored, the OTHER axis falls back to
        // the measured stage size (so e.g. a Width-only story still gets a sensible height instead of 0).</item>
        // <item>Else a fixed viewport (preset or custom) sizes the canvas to that reference W x H and marks it a
        // responsive scope so the mounted story's sm:/md:/... evaluate against the simulated size.</item>
        // <item>Else ("Full", no explicit size): the canvas fills the stage — its reference size IS the measured
        // stage size, and it is not a responsive scope (matches the panel root instead).</item>
        // </list>
        private (float Width, float Height, bool IsResponsiveScope) ComputeReferenceSize(VelvetPreviewStory story)
        {
            var stageW = _stage?.resolvedStyle.width ?? 0f;
            var stageH = _stage?.resolvedStyle.height ?? 0f;

            var hasExplicitSize = story != null && (story.Width > 0 || story.Height > 0);
            if (hasExplicitSize)
            {
                var refW = story.Width > 0 ? story.Width : stageW;
                var refH = story.Height > 0 ? story.Height : stageH;
                return (refW, refH, false);
            }

            var (viewportW, viewportH) = ViewportSize();
            if (viewportW > 0f && viewportH > 0f) return (viewportW, viewportH, true);

            return (stageW, stageH, false);
        }

        // Writes the canvas's reference size (pre-zoom, in px) as an EXPLICIT pixel size — never a percentage. A
        // percentage reference was the root of the old zoom-box-less design's problems: it made the canvas's
        // layout size a function of its unscaled parent, so there was no stable "100%" to multiply by a zoom
        // factor. Also toggles the @container responsive-scope marker per ComputeReferenceSize's rule.
        private void ApplyCanvasSize(VelvetPreviewStory story)
        {
            if (_canvas == null || _zoomBox == null) return;

            var (refW, refH, isResponsiveScope) = ComputeReferenceSize(story);
            // The scope marker never depends on whether the stage has actually resolved a size — it only depends
            // on story metadata / viewport mode, both known regardless of layout — so it is always safe to apply,
            // unlike the pixel size below. Toggling it unconditionally also keeps a Full selection able to clear
            // the marker even before the first layout pass.
            _canvas.EnableInClassList(VelvetResponsive.ContainerClass, isResponsiveScope);

            // Unresolved (NaN pre-layout, or a collapsed stage with no explicit/viewport size to fall back to):
            // skip the size write rather than corrupt the canvas with a NaN or 0x0 size. The stage's
            // GeometryChangedEvent re-runs this once the stage actually has a resolved size.
            if (!IsResolved(refW) || !IsResolved(refH)) return;

            _canvas.style.width = refW;
            _canvas.style.height = refH;
        }

        private void UpdateStatus() => UpdateStatus(null);

        // Overload for a caller (ApplyZoom) that already computed the zoom factor and reference size for its own
        // purposes — passing them through lets DescribeViewport skip re-deriving both (ComputeFitFactor itself
        // re-calls ComputeReferenceSize), which matters because ApplyZoom runs on every stage GeometryChangedEvent
        // tick.
        private void UpdateStatus(float factor, float refW, float refH)
        {
            (float Factor, float RefW, float RefH)? precomputed = (factor, refW, refH);
            UpdateStatus(precomputed);
        }

        private void UpdateStatus((float Factor, float RefW, float RefH)? precomputed)
        {
            if (_statusLabel == null) return;
            if (_selected == null)
            {
                _statusLabel.text = _stories.Count == 0
                    ? "No [VelvetPreview] stories found. Add [VelvetPreview] to a static method returning VNode."
                    : "Select a story.";
                return;
            }

            var error = _host?.MountError;
            _statusLabel.text = error == null
                ? $"{_selected.Group} / {_selected.Name}      {DescribeViewport(precomputed)}"
                : $"{_selected.Name} failed to mount: {error.Message}";
        }

        // A short human-readable summary of what actually drives the canvas size right now — shown in the status
        // line so a resolution or zoom change is visible as text, not only as a (sometimes subtle) layout change.
        // Mode is "Story" when the selected story's own Width/Height wins, else the viewport mode ("Full"/"Custom");
        // in Full mode the reported size is the stage the canvas fills, which is why Full always looks window-wide.
        // Accepts an optional precomputed (factor, refW, refH) so a caller that already derived these values (e.g.
        // ApplyZoom) does not force a second computation here; a caller without them (the parameterless UpdateStatus
        // path) falls back to deriving both itself.
        private string DescribeViewport((float Factor, float RefW, float RefH)? precomputed)
        {
            float factor, refW, refH;
            if (precomputed.HasValue)
            {
                (factor, refW, refH) = precomputed.Value;
            }
            else
            {
                (refW, refH, _) = ComputeReferenceSize(_selected);
                factor = _zoom == ZoomFit ? ComputeFitFactor() : ZoomFactor(_zoom);
            }

            if (!IsResolved(refW) || !IsResolved(refH)) return "sizing…";

            var pct = Mathf.RoundToInt(factor * 100f);
            var size = $"{Mathf.RoundToInt(refW)}×{Mathf.RoundToInt(refH)}";

            var hasExplicitSize = _selected != null && (_selected.Width > 0 || _selected.Height > 0);
            var mode = hasExplicitSize ? "Story" : _viewport;
            return $"{mode}  {size} · {pct}%";
        }
        #endregion
    }
}
