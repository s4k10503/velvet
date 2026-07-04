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

        // Stage backdrops. Dark is the default — a near-black so light UI stays legible
        // and the mounted tree's own bounds read against it; Light is a neutral gray for dark UI.
        private static readonly Color DarkBackdrop = new(0.09f, 0.10f, 0.13f, 1f);
        private static readonly Color LightBackdrop = new(0.92f, 0.92f, 0.92f, 1f);

        private const string BgDark = "Dark";
        private const string BgLight = "Light";
        private const string BgCheckerboard = "Checkerboard";

        // Discrete zoom steps; "Fit" scales the canvas to fill the stage.
        private const string ZoomFit = "Fit";
        private static readonly (string Label, float Factor)[] ZoomSteps =
        {
            (ZoomFit, 0f), ("50%", 0.5f), ("100%", 1f), ("200%", 2f),
        };

        // Simulated viewports. "Full" lets the canvas fill the stage (no scope); a fixed
        // preset sizes the canvas to that reference width and makes it a responsive scope so the mounted story's
        // sm:/md:/... evaluate against the simulated width. Reference px mirror common device breakpoints.
        private const string ViewportFull = "Full";
        private static readonly (string Label, float Width)[] Viewports =
        {
            (ViewportFull, 0f), ("Mobile (375)", 375f), ("Tablet (768)", 768f), ("Desktop (1280)", 1280f),
        };

        private readonly List<VelvetPreviewStory> _stories = new();
        private VelvetPreviewStory _selected;
        private VelvetPreviewHost _host;

        private ListView _list;
        private VisualElement _stage;     // fixed backdrop the canvas centers within
        private VisualElement _canvas;    // the element the story actually mounts onto
        private CheckerboardBackground _checkerboard;
        private PreviewInspectOverlay _overlay;
        private PreviewControlsPanel _controls;
        private Label _statusLabel;
        private ToolbarMenu _backgroundMenu;
        private ToolbarMenu _zoomMenu;
        private ToolbarMenu _viewportMenu;

        // View-addon state, persisted in EditorPrefs so it survives remounts / domain reloads.
        private bool _dark;
        private string _background = BgDark;
        private string _zoom = ZoomFit;
        private bool _outline;
        private bool _measure;
        private string _viewport = ViewportFull;

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

            _stage = new VisualElement
            {
                style =
                {
                    flexGrow = 1f,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                    overflow = Overflow.Hidden,
                },
            };
            // Fit recomputes against the stage's measured size, so it must re-run whenever the stage resizes.
            _stage.RegisterCallback<GeometryChangedEvent>(_ => ApplyZoom());
            vertical.Add(_stage);

            _controls = new PreviewControlsPanel();
            _controls.ArgsChanged += OnArgsChanged;
            vertical.Add(_controls);

            // Transparency grid behind the canvas, shown only for the Checkerboard background.
            _checkerboard = new CheckerboardBackground { style = { display = DisplayStyle.None } };
            _stage.Add(_checkerboard);

            _canvas = new VisualElement
            {
                style =
                {
                    flexGrow = 1f, width = Length.Percent(100f), height = Length.Percent(100f),
                    // Scale about the top-center so zooming grows the canvas downward from a stable anchor.
                    transformOrigin = new TransformOrigin(Length.Percent(50f), Length.Percent(0f)),
                },
            };
            var utilities = AssetDatabase.LoadAssetAtPath<StyleSheet>(UtilitiesUssPath);
            if (utilities != null) _canvas.styleSheets.Add(utilities);
            _stage.Add(_canvas);

            // Inspection overlay (outline / measure) drawn above the canvas; non-interactive so it never steals
            // the story's pointer events. It tracks the canvas subtree and re-draws when the stage resizes.
            _overlay = new PreviewInspectOverlay(_canvas)
            {
                OutlineEnabled = _outline,
                MeasureEnabled = _measure,
            };
            _stage.Add(_overlay);
            // Re-draw the overlay when the stage resizes OR the canvas relayouts (a story laying out after mount,
            // or a zoom change altering the canvas transform) so outlines track the current geometry. The canvas
            // also re-runs Fit: a viewport-driven canvas WIDTH change (not a stage resize) must recompute the fit
            // scale, which only the canvas's own geometry change reports.
            _stage.RegisterCallback<GeometryChangedEvent>(_ => _overlay?.Refresh());
            _canvas.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                ApplyZoom();
                _overlay?.Refresh();
            });

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
            foreach (var (label, _) in Viewports)
            {
                var captured = label;
                _viewportMenu.menu.AppendAction(
                    captured,
                    _ => SetViewport(captured),
                    _ => _viewport == captured ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
            toolbar.Add(_viewportMenu);

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
        }

        private void SetZoom(string zoom)
        {
            _zoom = zoom;
            EditorPrefs.SetString(ZoomKey, zoom);
            if (_zoomMenu != null) _zoomMenu.text = ZoomLabel();
            ApplyZoom();
        }

        private void ApplyZoom()
        {
            if (_canvas == null) return;
            var factor = _zoom == ZoomFit ? ComputeFitFactor() : ZoomFactor(_zoom);
            _canvas.style.scale = new Scale(new Vector3(factor, factor, 1f));
        }

        // Fit: the largest scale at which the canvas's reference size fits inside the stage. Uses the story's
        // explicit Width/Height when set, else the canvas's measured layout; returns 1 until the stage and
        // canvas have a resolved size (the GeometryChangedEvent re-runs this once they do).
        private float ComputeFitFactor()
        {
            if (_stage == null || _canvas == null) return 1f;
            var stageW = _stage.resolvedStyle.width;
            var stageH = _stage.resolvedStyle.height;
            if (stageW <= 0f || stageH <= 0f) return 1f;

            var canvasW = _selected is { Width: > 0 } ? _selected.Width : _canvas.resolvedStyle.width;
            var canvasH = _selected is { Height: > 0 } ? _selected.Height : _canvas.resolvedStyle.height;
            if (canvasW <= 0f || canvasH <= 0f) return 1f;

            return Mathf.Min(1f, Mathf.Min(stageW / canvasW, stageH / canvasH));
        }

        private static float ZoomFactor(string label)
        {
            foreach (var (name, factor) in ZoomSteps)
            {
                if (name == label) return factor <= 0f ? 1f : factor;
            }

            return 1f;
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

        // The fixed viewport width in reference px, or 0 for Full.
        private float ViewportWidth()
        {
            foreach (var (label, width) in Viewports)
            {
                if (label == _viewport) return width;
            }

            return 0f;
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
        // (Fit depends on the current story / viewport size), refresh the overlay (track the new subtree), and
        // refresh the status line. Called from every mount path so they cannot diverge.
        private void ReapplyAfterMount()
        {
            ApplyTheme();
            ApplyZoom();
            _overlay?.Refresh();
            UpdateStatus();
        }

        // Sizes the mount canvas. A story's explicit Width/Height ALWAYS wins: an authored card footprint is
        // shown at its real size and is NOT treated as a responsive container (the viewport simulation does not
        // apply to a fixed-size story, so no @container marker). Only a fill-canvas story (no explicit size)
        // honors a fixed viewport: the canvas takes the simulated width and becomes a responsive scope
        // (@container) so the mounted story's sm:/md:/... evaluate against that width. With Full viewport and no
        // explicit size, the canvas fills the stage (the original behavior).
        private void ApplyCanvasSize(VelvetPreviewStory story)
        {
            if (_canvas == null) return;

            var hasExplicitSize = story != null && (story.Width > 0 || story.Height > 0);
            if (hasExplicitSize)
            {
                _canvas.EnableInClassList(VelvetResponsive.ContainerClass, false);
                _canvas.style.width = story.Width > 0 ? (StyleLength)story.Width : Length.Percent(100f);
                _canvas.style.height = story.Height > 0 ? (StyleLength)story.Height : Length.Percent(100f);
                _canvas.style.flexGrow = 0f;
                return;
            }

            var viewportWidth = ViewportWidth();
            if (viewportWidth > 0f)
            {
                _canvas.style.width = viewportWidth;
                _canvas.style.height = Length.Percent(100f);
                _canvas.style.flexGrow = 0f;
                _canvas.EnableInClassList(VelvetResponsive.ContainerClass, true);
                return;
            }

            _canvas.EnableInClassList(VelvetResponsive.ContainerClass, false);
            _canvas.style.width = Length.Percent(100f);
            _canvas.style.height = Length.Percent(100f);
            _canvas.style.flexGrow = 1f;
        }

        private void UpdateStatus()
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
                ? $"{_selected.Group} / {_selected.Name}"
                : $"{_selected.Name} failed to mount: {error.Message}";
        }
        #endregion
    }
}
