using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Velvet.DevTools;

namespace Velvet.Editor.DevTools
{
    /// <summary>
    /// Velvet DevTools — real-time VNode-tree display and time-travel through state-change history.
    /// Inspects a functional component's <see cref="ComponentFiber"/> directly.
    ///
    /// Usage:
    ///   Window > Velvet > DevTools Inspector
    /// </summary>
    public sealed class VelvetDevToolsWindow : EditorWindow
    {
        #region Constants
        private const string WindowTitle = "Velvet DevTools";
        private const string UnreadableCount = "unreadable";
        private const string MenuPath = "Window/Velvet/DevTools Inspector";
        // Fixed-ratio splitter. May be replaced with a draggable splitter in the future.
        private const float SplitterRatio = 0.38f;
        private const double AutoRefreshInterval = 0.5;
        private const BindingFlags NonPublicInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Vector2 WindowMinSize = new(480, 300);
        private const float RefreshButtonWidth = 70f;
        private const float AutoToggleWidth = 50f;
        private const float ClearRegistryButtonWidth = 95f;
        private const float PaneDividerWidth = 1f;
        private const float EntryToggleWidth = 16f;
        private const float RecordStateButtonWidth = 100f;
        private const float ClearHistoryButtonWidth = 55f;
        private const float RenderCountLabelWidth = 70f;
        #endregion

        #region Reflection cache
        // Runtime/AssemblyInfo.cs grants this assembly InternalsVisibleTo, so everything on the way to the
        // memo cache is reached by name and a rename fails the build. Only the entry count is out of
        // reach — FiberMemoCache keeps its entries in a private field and publishes no count — and
        // DevToolsMemoCacheReadoutTests fails if that field stops resolving.
        private const string MemoEntriesFieldName = "_cache";
        private static FieldInfo s_memoEntriesField;
        private static bool s_memoEntriesFieldResolved;
        #endregion

        #region UI State
        private Vector2 _leftScrollPos;
        private Vector2 _rightScrollPos;
        private Vector2 _historyScrollPos;
        private int _selectedEntryIndex = -1;
        private double _lastRefreshTime;
        private bool _autoRefresh = true;
        #endregion

        #region Right Pane Tabs
        private int _rightTab;
        private static readonly string[] s_rightTabLabels = { "VNode Tree", "State History", "Stats" };
        #endregion

        #region State History
        private readonly Dictionary<ComponentFiber, StateHistoryBuffer> _historyMap = new();
        #endregion

        #region Cache
        private string _cachedVNodeText;
        private int _cachedRenderCount;
        private int _cachedHookCount;
        private int _cachedBlockerCount;
        private int? _cachedMemoCacheCount;
        private Vector2 _statsScrollPos;

        // Toolbar string updated only on RegistryChanged.
        private string _cachedRegisteredLabel = "No components registered";

        // Previous value for change detection.
        private int _prevRenderCount = -1;
        #endregion

        [MenuItem(MenuPath)]
        public static void ShowWindow()
        {
            var window = GetWindow<VelvetDevToolsWindow>(false, WindowTitle, true);
            window.minSize = WindowMinSize;
            window.Show();
        }

        private void OnEnable()
        {
            VelvetDevToolsRegistry.RegistryChanged += OnRegistryChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            UpdateRegisteredLabel();
        }

        private void OnDisable()
        {
            VelvetDevToolsRegistry.RegistryChanged -= OnRegistryChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnRegistryChanged()
        {
            if (_selectedEntryIndex >= VelvetDevToolsRegistry.Entries.Count)
            {
                _selectedEntryIndex = -1;
                InvalidateCache();
            }
            UpdateRegisteredLabel();
            Repaint();
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                _historyMap.Clear();
                _selectedEntryIndex = -1;
                InvalidateCache();
                Repaint();
            }
        }

        private void Update()
        {
            if (!_autoRefresh)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup - _lastRefreshTime >= AutoRefreshInterval)
            {
                _lastRefreshTime = EditorApplication.timeSinceStartup;
                RefreshSelectedComponent();

                if (_cachedRenderCount != _prevRenderCount)
                {
                    _prevRenderCount = _cachedRenderCount;
                    Repaint();
                }
            }
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawMainContent();
        }

        #region Toolbar
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(RefreshButtonWidth)))
            {
                RefreshSelectedComponent();
                Repaint();
            }

            _autoRefresh = GUILayout.Toggle(_autoRefresh, "Auto", EditorStyles.toolbarButton, GUILayout.Width(AutoToggleWidth));

            GUILayout.FlexibleSpace();

            GUILayout.Label(_cachedRegisteredLabel, EditorStyles.toolbarButton);

            if (GUILayout.Button("Clear Registry", EditorStyles.toolbarButton, GUILayout.Width(ClearRegistryButtonWidth)))
            {
                VelvetDevToolsRegistry.Clear();
                _historyMap.Clear();
                _selectedEntryIndex = -1;
                InvalidateCache();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void UpdateRegisteredLabel()
        {
            var count = VelvetDevToolsRegistry.Entries.Count;
            _cachedRegisteredLabel = count == 0
                ? "No components registered"
                : $"{count} component{(count == 1 ? "" : "s")} registered";
        }
        #endregion

        #region Main Content
        private void DrawMainContent()
        {
            var totalWidth = position.width;
            var leftWidth = totalWidth * SplitterRatio;

            var entries = VelvetDevToolsRegistry.Entries;

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical(GUILayout.Width(leftWidth));
            DrawLeftPane(entries);
            EditorGUILayout.EndVertical();

            GUILayout.Box(string.Empty, GUILayout.Width(PaneDividerWidth), GUILayout.ExpandHeight(true));

            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            DrawRightPane(entries);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }
        #endregion

        #region Left Pane
        private void DrawLeftPane(IReadOnlyList<VelvetDevToolsRegistry.ComponentEntry> entries)
        {
            EditorGUILayout.LabelField("Registered Components", EditorStyles.boldLabel);

            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No components registered.\n" +
                    "Mount a tree with V.Mount (in Play Mode, or via a preview) and its root " +
                    "auto-attaches here.",
                    MessageType.Info);
                return;
            }

            _leftScrollPos = EditorGUILayout.BeginScrollView(_leftScrollPos);

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var isSelected = i == _selectedEntryIndex;
                var isDisposed = entry.Fiber.IsDisposed;

                EditorGUI.BeginDisabledGroup(isDisposed);

                var label = isDisposed ? $"[Disposed] {entry.Label}" : entry.Label;

                EditorGUILayout.BeginHorizontal();

                var nowSelected = GUILayout.Toggle(isSelected, GUIContent.none, GUILayout.Width(EntryToggleWidth));
                if (nowSelected != isSelected)
                {
                    _selectedEntryIndex = isSelected ? -1 : i;
                    InvalidateCache();
                    RefreshSelectedComponent();
                }

                GUILayout.Label(label, isSelected ? EditorStyles.selectionRect : EditorStyles.label);
                GUILayout.FlexibleSpace();
                GUILayout.Label(entry.TypeName, EditorStyles.miniLabel);

                EditorGUILayout.EndHorizontal();

                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.EndScrollView();
        }
        #endregion

        #region Right Pane
        private void DrawRightPane(IReadOnlyList<VelvetDevToolsRegistry.ComponentEntry> entries)
        {
            if (_selectedEntryIndex < 0 || _selectedEntryIndex >= entries.Count)
            {
                EditorGUILayout.HelpBox("Select a fiber from the left pane.", MessageType.None);
                return;
            }

            var entry = entries[_selectedEntryIndex];

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(entry.Label, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Record State", GUILayout.Width(RecordStateButtonWidth)))
            {
                RecordCurrentState(entry);
            }
            EditorGUILayout.EndHorizontal();

            DrawComponentMeta(entry);

            _rightTab = GUILayout.Toolbar(_rightTab, s_rightTabLabels);

            switch (_rightTab)
            {
                case 0:
                    DrawVNodeTreeTab();
                    break;
                case 1:
                    DrawStateHistoryTab(entry);
                    break;
                case 2:
                    DrawStatsTab(entry);
                    break;
            }
        }

        private void DrawComponentMeta(VelvetDevToolsRegistry.ComponentEntry entry)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Function", entry.TypeName, EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Render Count", _cachedRenderCount.ToString(), EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Hook Slots (UseCallback)", _cachedHookCount.ToString(), EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Hook Slots (UseBlocker)", _cachedBlockerCount.ToString(), EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Registered At", entry.RegisteredAt.ToString("HH:mm:ss"), EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }
        #endregion

        #region VNode Tree Tab
        private void DrawVNodeTreeTab()
        {
            EditorGUILayout.LabelField("VNode Tree (PreviousTree)", EditorStyles.boldLabel);

            _rightScrollPos = EditorGUILayout.BeginScrollView(_rightScrollPos, GUILayout.ExpandHeight(true));

            if (string.IsNullOrEmpty(_cachedVNodeText))
            {
                EditorGUILayout.HelpBox(
                    "No VNode tree available. The fiber may not be mounted yet.",
                    MessageType.None);
            }
            else
            {
                EditorGUILayout.SelectableLabel(
                    _cachedVNodeText,
                    EditorStyles.wordWrappedLabel,
                    GUILayout.ExpandHeight(true));
            }

            EditorGUILayout.EndScrollView();
        }
        #endregion

        #region State History Tab
        private void DrawStateHistoryTab(VelvetDevToolsRegistry.ComponentEntry entry)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Render History (newest first)", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear", GUILayout.Width(ClearHistoryButtonWidth)))
            {
                if (_historyMap.TryGetValue(entry.Fiber, out var buf))
                {
                    buf.Clear();
                }
            }
            EditorGUILayout.EndHorizontal();

            _historyMap.TryGetValue(entry.Fiber, out var buffer);
            if (buffer == null || buffer.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No history recorded.\nClick \"Record State\" to snapshot the current render count.",
                    MessageType.None);
                return;
            }

            var history = buffer.GetNewestFirst();
            _historyScrollPos = EditorGUILayout.BeginScrollView(_historyScrollPos, GUILayout.ExpandHeight(true));

            for (var i = 0; i < history.Count; i++)
            {
                var h = history[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"[{i}] {h.Timestamp:HH:mm:ss.fff}", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"Render#{h.RenderCount}", EditorStyles.miniLabel, GUILayout.Width(RenderCountLabelWidth));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.SelectableLabel(
                    h.StateString,
                    EditorStyles.wordWrappedLabel,
                    GUILayout.MinHeight(EditorGUIUtility.singleLineHeight));

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }
        #endregion

        #region Stats Tab
        private void DrawStatsTab(VelvetDevToolsRegistry.ComponentEntry entry)
        {
            _statsScrollPos = EditorGUILayout.BeginScrollView(_statsScrollPos, GUILayout.ExpandHeight(true));

            EditorGUILayout.LabelField("Memo / Reconcile Stats", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Memo Cache Entries",
                _cachedMemoCacheCount?.ToString() ?? UnreadableCount, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Render Metrics", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Total Render Count", _cachedRenderCount.ToString(), EditorStyles.miniLabel);

            _historyMap.TryGetValue(entry.Fiber, out var histBuf);
            var snapCount = histBuf?.Count ?? 0;
            EditorGUILayout.LabelField("Recorded Snapshots", snapCount.ToString(), EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Hook Slots", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("UseCallback slots (Indices.HookIndex)", _cachedHookCount.ToString(), EditorStyles.miniLabel);
            EditorGUILayout.LabelField("UseBlocker slots (Indices.BlockerHookIndex)", _cachedBlockerCount.ToString(), EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndScrollView();
        }
        #endregion

        #region Cache Management
        private void RefreshSelectedComponent()
        {
            var entries = VelvetDevToolsRegistry.Entries;
            if (_selectedEntryIndex < 0 || _selectedEntryIndex >= entries.Count)
            {
                return;
            }

            UpdateCache(entries[_selectedEntryIndex].Fiber);
        }

        private void UpdateCache(ComponentFiber fiber)
        {
            // RenderCount is a public field, so access it directly.
            _cachedRenderCount = fiber.RenderCount;

            // Hook indices are public fields exposed via fiber.Indices.
            _cachedHookCount = fiber.Indices.HookIndex;
            _cachedBlockerCount = fiber.Indices.BlockerHookIndex;

            try
            {
                _cachedVNodeText = VNodeTreeRenderer.Render(fiber.PreviousTree);
            }
            catch (Exception ex)
            {
                _cachedVNodeText = $"(error reading VNode tree: {ex.Message})";
            }

            _cachedMemoCacheCount = GetMemoCacheCount(fiber);
        }

        private void InvalidateCache()
        {
            _cachedVNodeText = null;
            _cachedRenderCount = 0;
            _cachedHookCount = 0;
            _cachedBlockerCount = 0;
            _cachedMemoCacheCount = null;
        }
        #endregion

        #region State Recording
        private void RecordCurrentState(VelvetDevToolsRegistry.ComponentEntry entry)
        {
            if (!_historyMap.TryGetValue(entry.Fiber, out var buffer))
            {
                buffer = new StateHistoryBuffer();
                _historyMap[entry.Fiber] = buffer;
            }

            UpdateCache(entry.Fiber);

            var histEntry = new StateHistoryEntry(
                timestamp: DateTime.Now,
                stateString: $"Render#{_cachedRenderCount}",
                renderCount: _cachedRenderCount);

            buffer.Push(histEntry);
        }
        #endregion

        #region Reflection Helpers
        /// <summary>
        /// How many entries the fiber's reconciler holds in its memo cache, or null when there is no
        /// reconciler to ask or the entry field no longer resolves. Null rather than zero: the two
        /// readings mean opposite things to someone reading the window, and while a count this window
        /// could not take was reported as zero, nothing distinguished them.
        /// </summary>
        private static int? GetMemoCacheCount(ComponentFiber fiber)
        {
            var memoCache = fiber.Reconciler?.Context.FiberMemoCache;
            if (memoCache == null)
            {
                return null;
            }

            if (!s_memoEntriesFieldResolved)
            {
                s_memoEntriesField = typeof(FiberMemoCache).GetField(MemoEntriesFieldName, NonPublicInstance);
                s_memoEntriesFieldResolved = true;
            }

            return s_memoEntriesField?.GetValue(memoCache) is ICollection entries ? entries.Count : null;
        }
        #endregion
    }
}
