#if UNITY_EDITOR
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// A runtime panel that renders into a texture, built from a copy of a <see cref="PanelSettings"/> with
    /// Velvet's utility stylesheet attached to its root. The preview window shows a story through one, so a
    /// harness that mounts a story without an explicit size on one built from the same settings at the window's
    /// screen size reads the pixels the window renders for it.
    /// </summary>
    /// <remarks>
    /// The copy targets <see cref="Texture"/>, which is cleared to transparent below everything drawn into it,
    /// a portal's layers included, and maps every screen position off the panel; input is sent to it as events to <see cref="Root"/>'s panel. A world-space
    /// settings asset is copied as a screen panel at a scale of one.
    /// </remarks>
    public sealed class VelvetPreviewPanel : IDisposable
    {
        private const int TextureDepth = 24;

        /// <summary>How many update passes <see cref="Render"/> runs over the panels drawing into <see cref="Texture"/>
        /// before it draws them.</summary>
        public const int SettlePasses = 100;

        /// <summary>How far each of <see cref="Render"/>'s update passes moves a panel's clock on, in milliseconds.</summary>
        public const int SettlePassMilliseconds = 16;

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IRuntimePanel, SimulatedClock> s_clocks =
            new();

        // Below any sorting order a layer host takes, which sits a few hundred from its declaring panel's.
        private const float ClearSortingOrder = -1e6f;

        private readonly GameObject _host;
        private readonly PanelSettings _settings;
        private readonly GameObject _clearHost;
        private readonly PanelSettings _clearSettings;
        private readonly PanelScaleMode _ownScaleMode;
        private readonly float _ownScale;
        private bool _disposed;

        /// <summary>
        /// Builds the panel at <paramref name="width"/> by <paramref name="height"/> pixels. With
        /// <paramref name="settings"/> <c>null</c> it is a constant-pixel-size panel at a scale of one with an
        /// empty theme. <paramref name="settings"/> itself is never modified.
        /// </summary>
        public VelvetPreviewPanel(PanelSettings? settings, int width, int height)
        {
            _settings = settings != null
                ? UnityEngine.Object.Instantiate(settings)
                : ScriptableObject.CreateInstance<PanelSettings>();
            _settings.hideFlags = HideFlags.HideAndDontSave;
            if (_settings.themeStyleSheet == null) _settings.themeStyleSheet = PanelHostFactory.SharedEmptyTheme;
            if (settings == null || _settings.renderMode == PanelRenderMode.WorldSpace)
            {
                _settings.renderMode = PanelRenderMode.ScreenSpaceOverlay;
                _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
                _settings.scale = 1f;
            }

            _ownScaleMode = _settings.scaleMode;
            _ownScale = _settings.scale;
            Texture = CreateTexture(width, height);
            // Ahead of the call below, which makes the panel: made with its target, it takes the texture's size and
            // scale from the start rather than on its first update.
            _settings.targetTexture = Texture;
            _settings.SetScreenToPanelSpaceFunction(OffPanel);

            // The clear is a panel of its own, sorted below everything else drawing into the texture: a
            // portal's Background layer host draws there before this panel does, so a clear on this
            // panel would erase it.
            _settings.clearColor = false;
            _clearSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _clearSettings.hideFlags = HideFlags.HideAndDontSave;
            _clearSettings.themeStyleSheet = PanelHostFactory.SharedEmptyTheme;
            _clearSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _clearSettings.sortingOrder = ClearSortingOrder;
            _clearSettings.clearColor = true;
            _clearSettings.colorClearValue = Color.clear;
            _clearSettings.SetScreenToPanelSpaceFunction(OffPanel);
            _clearSettings.targetTexture = Texture;
            _clearHost = CreateDocument(_clearSettings).gameObject;

            var document = CreateDocument(_settings);
            _host = document.gameObject;
            Root = document.rootVisualElement;
            VelvetStyleUtilities.AttachTo(Root);
        }

        private static UIDocument CreateDocument(PanelSettings settings)
        {
            // Hidden in the active scene the way PanelHostFactory's hosts are, so a scene change destroys it;
            // IsAlive reports that.
            var host = new GameObject(nameof(VelvetPreviewPanel));
            VelvetObjectUtil.HideFrameworkSceneObject(host);
            var document = host.AddComponent<UIDocument>();
            document.panelSettings = settings;
            return document;
        }

        /// <summary>The panel's root, to mount a story onto.</summary>
        public VisualElement Root { get; }

        /// <summary>The texture the panel renders into. <see cref="Resize"/> resizes it in place.</summary>
        public RenderTexture Texture { get; }

        /// <summary>
        /// Resizes <see cref="Texture"/> to <paramref name="width"/> by <paramref name="height"/>, keeping the
        /// panel and whatever is mounted on it. Each size is clamped to at least one pixel.
        /// </summary>
        public void Resize(int width, int height)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VelvetPreviewPanel));
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            if (Texture.width == width && Texture.height == height) return;

            // In place rather than replaced: a portal's layer host copied this texture as its target, and
            // keeps drawing into it.
            Texture.Release();
            Texture.width = width;
            Texture.height = height;
            Texture.Create();
        }

        /// <summary>
        /// Draws one frame into <see cref="Texture"/> before it returns, so a capture reads the texture back without
        /// waiting for an editor frame. It first runs <see cref="SettlePasses"/> update passes over the runtime panels
        /// drawing into the texture, each a simulated frame that moves the panel's clock on by
        /// <see cref="SettlePassMilliseconds"/> before the panel updates; the clock does not move back afterwards. Work
        /// those panels run on their own clock advances as over that many frames: the renders, effects and state
        /// updates Velvet schedules on them are drawn, so is what Velvet measures from their layout, and a transition
        /// timed by their clock that ends within the passes is drawn finished. Motion that reads its
        /// mount's <see cref="MotionClock"/> rather than the panel's time moves only as far as that clock has. Work
        /// still scheduled after the passes is drawn without waiting for it, and work resumed on a later editor or
        /// player frame, such as a <see cref="VelvetTask.Yield"/> continuation, is drawn by a call after that frame.
        /// The panels are then repainted and rendered, each pass in the order the panels sort in. An exception the
        /// frame does not catch reaches the caller as it was thrown.
        /// </summary>
        /// <exception cref="ObjectDisposedException">The panel is disposed.</exception>
        /// <exception cref="NotSupportedException">This editor lacks an engine member the frame calls, which the message
        /// names.</exception>
        public void Render()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VelvetPreviewPanel));
            var frame = FrameMembers.Resolved ?? throw new NotSupportedException(
                $"{nameof(VelvetPreviewPanel)}.{nameof(Render)} cannot draw a frame in this editor, which has no "
                + FrameMembers.Missing);

            var active = RenderTexture.active;
            try
            {
                for (var pass = 0; pass != SettlePasses; pass++)
                {
                    foreach (var panel in frame.PanelsDrawingInto(Texture))
                    {
                        frame.Advance(panel);
                        frame.Update.Invoke(panel, null);
                    }
                }
                GL.PushMatrix();
                try
                {
                    foreach (var panel in frame.PanelsDrawingInto(Texture)) frame.Repaint.Invoke(null, new[] { panel });
                    frame.RepaintEnd.Invoke(null, null);
                    foreach (var panel in frame.PanelsDrawingInto(Texture)) frame.Render.Invoke(panel, null);
                }
                finally
                {
                    GL.PopMatrix();
                }
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }
            finally
            {
                RenderTexture.active = active;
            }
        }

        // A scale the preview window resolved for a screen other than this texture, held at that value
        // whatever the texture's size; null restores the copied settings' own scaling.
        internal void PinScale(float? scale)
        {
            var mode = scale.HasValue ? PanelScaleMode.ConstantPixelSize : _ownScaleMode;
            var value = scale ?? _ownScale;
            if (_settings.scaleMode != mode) _settings.scaleMode = mode;
            if (_settings.scale != value) _settings.scale = value;
        }

        // False once a scene change has destroyed the panel's GameObjects.
        internal bool IsAlive => !_disposed && _host != null && _clearHost != null;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            VelvetObjectUtil.Destroy(_host);
            VelvetObjectUtil.Destroy(_settings);
            VelvetObjectUtil.Destroy(_clearHost);
            VelvetObjectUtil.Destroy(_clearSettings);
            Texture.Release();
            VelvetObjectUtil.Destroy(Texture);
        }

        private static RenderTexture CreateTexture(int width, int height) =>
            new(Mathf.Max(1, width), Mathf.Max(1, height), TextureDepth)
            {
                name = nameof(VelvetPreviewPanel),
                hideFlags = HideFlags.HideAndDontSave,
            };

        private static Vector2 OffPanel(Vector2 screen) => new(float.NaN, float.NaN);

        // The frames Render has simulated on a panel, which only grows, so Render never moves the panel's time back.
        private sealed class SimulatedClock
        {
            // A sentinel rather than null: a panel without a function of its own holds null, and is wrapped all the same.
            internal object Installed = new();
            internal long OffsetMilliseconds;
        }

        // One installed wrap: the function the panel read its time through when the wrap was made, plus the clock's
        // offset. The inner function is the wrap's own rather than the clock's, so a function composed over this wrap
        // and wrapped in its turn reaches this one's inner function rather than itself.
        private sealed class ClockWrap
        {
            private readonly Func<double> _inner;
            private readonly SimulatedClock _clock;

            internal ClockWrap(Func<double> inner, SimulatedClock clock)
            {
                _inner = inner;
                _clock = clock;
            }

            internal double Now() => _inner() + _clock.OffsetMilliseconds / 1000d;
        }

        private sealed class FrameMembers
        {
            private static readonly EngineMember[] s_declared =
            {
                EngineMember.SortedScreenOverlayPanels, EngineMember.RuntimePanelUpdate, EngineMember.RepaintRuntimePanel,
                EngineMember.RuntimePanelRender, EngineMember.TextGenerationRepaintEnd, EngineMember.PanelTimeFunction,
                EngineMember.PanelDefaultTime,
            };

            internal static readonly string Missing =
                string.Join(", ", s_declared.Where(member => member.Resolve() == null));

            internal static readonly FrameMembers? Resolved = Missing.Length == 0 ? new FrameMembers() : null;

            private readonly MethodInfo _sortedPanels = EngineMember.SortedScreenOverlayPanels.ResolveMethod()!;
            internal readonly MethodInfo Update = EngineMember.RuntimePanelUpdate.ResolveMethod()!;
            internal readonly MethodInfo Repaint = EngineMember.RepaintRuntimePanel.ResolveMethod()!;
            internal readonly MethodInfo Render = EngineMember.RuntimePanelRender.ResolveMethod()!;
            internal readonly MethodInfo RepaintEnd = EngineMember.TextGenerationRepaintEnd.ResolveMethod()!;
            private readonly PropertyInfo _panelTime = EngineMember.PanelTimeFunction.ResolveProperty()!;
            private readonly Func<double> _defaultTime = (Func<double>)Delegate.CreateDelegate(typeof(Func<double>),
                EngineMember.PanelDefaultTime.ResolveMethod()!);

            // Wraps the function the panel reads its time through the first time, and again once something else has
            // replaced the wrap, keeping the offset either way.
            internal void Advance(IRuntimePanel panel)
            {
                var clock = s_clocks.GetOrCreateValue(panel);
                var current = (Delegate?)_panelTime.GetValue(panel);
                if (!ReferenceEquals(current, clock.Installed))
                {
                    var wrap = new ClockWrap(current == null
                        ? _defaultTime
                        : (Func<double>)Delegate.CreateDelegate(typeof(Func<double>), current.Target, current.Method), clock);
                    Func<double> now = wrap.Now;
                    // Recorded in the same statement that installs it: a wrap the clock has not recorded is taken for
                    // another function next pass and wrapped again, counting the offset once more each time.
                    _panelTime.SetValue(panel,
                        clock.Installed = Delegate.CreateDelegate(_panelTime.PropertyType, wrap, now.Method));
                }
                clock.OffsetMilliseconds += SettlePassMilliseconds;
            }

            internal List<IRuntimePanel> PanelsDrawingInto(RenderTexture texture)
            {
                var drawing = new List<IRuntimePanel>();
                foreach (var panel in (IEnumerable)_sortedPanels.Invoke(null, null))
                {
                    var runtime = panel as IRuntimePanel;
                    if (runtime?.panelSettings.targetTexture == texture) drawing.Add(runtime!);
                }
                return drawing;
            }
        }
    }
}
#endif
