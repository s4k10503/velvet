#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Type-safe property bag for a VNode (text, tooltip, enabled/visible, field value, and
    /// element-specific settings), passed as the <c>props:</c> argument of the <c>V.*</c> factories.
    /// </summary>
    public sealed class FiberElementProps
    {
        /// <summary>CSS class name toggled when Visible=false.</summary>
        internal const string HiddenClassName = "hidden";

        private bool _isReadOnly;

        /// <summary>Text for Label / Button.</summary>
        public string? Text { get => _text; set { ThrowIfReadOnly(); _text = value; } }
        private string? _text;

        /// <summary>Tooltip text.</summary>
        public string? Tooltip { get => _tooltip; set { ThrowIfReadOnly(); _tooltip = value; } }
        private string? _tooltip;

        /// <summary>Maps to SetEnabled().</summary>
        public bool? Enabled { get => _enabled; set { ThrowIfReadOnly(); _enabled = value; } }
        private bool? _enabled;

        /// <summary>When false, hides the element by toggling its "hidden" USS class.</summary>
        public bool? Visible { get => _visible; set { ThrowIfReadOnly(); _visible = value; } }
        private bool? _visible;

        /// <summary>Generic binding for BaseField&lt;T&gt;.value.</summary>
        public object? FieldValue { get => _fieldValue; set { ThrowIfReadOnly(); _fieldValue = value; } }
        private object? _fieldValue;

        /// <summary>Whether the element is focusable.</summary>
        public bool? Focusable { get => _focusable; set { ThrowIfReadOnly(); _focusable = value; } }
        private bool? _focusable;

        /// <summary>
        /// Maps to the element's tabIndex. Positive values sort ahead of 0 in the sequential (Tab) ring.
        /// Caution: on runtime panels, -1 removes the element from BOTH the Tab ring AND 2D arrow/dpad/stick
        /// navigation — it is not the web's "focusable but not tab-reachable". For a group with one Tab
        /// stop, use a focus scope with SingleTabStop instead.
        /// </summary>
        public int? TabIndex { get => _tabIndex; set { ThrowIfReadOnly(); _tabIndex = value; } }
        private int? _tabIndex;

        /// <summary>
        /// Maps to the element's delegatesFocus: focusing this element forwards to its first focusable
        /// child, and the focus rings skip the element itself.
        /// </summary>
        public bool? DelegatesFocus { get => _delegatesFocus; set { ThrowIfReadOnly(); _delegatesFocus = value; } }
        private bool? _delegatesFocus;

        /// <summary>
        /// Focus-scope behavior for this container element (React Aria's FocusScope parity). Attachable to
        /// ANY container — the modal Div you already have can be the scope; <c>V.FocusScope</c> is sugar for
        /// when no container exists yet.
        /// </summary>
        public FocusScopeSettings? FocusScope { get => _focusScope; set { ThrowIfReadOnly(); _focusScope = value; } }
        private FocusScopeSettings? _focusScope;

        /// <summary>
        /// Drag-and-drop scope configuration for this container element (dnd-kit's DndContext parity).
        /// Draggables and droppables pair with their nearest ancestor scope at event time.
        /// </summary>
        public DndContextSettings? DndContext { get => _dndContext; set { ThrowIfReadOnly(); _dndContext = value; } }
        private DndContextSettings? _dndContext;

        /// <summary>Drag-source configuration (dnd-kit's useDraggable parity) — the element carrying it
        /// is the drag node itself.</summary>
        public DraggableSettings? Draggable { get => _draggable; set { ThrowIfReadOnly(); _draggable = value; } }
        private DraggableSettings? _draggable;

        /// <summary>Drop-target configuration (dnd-kit's useDroppable parity).</summary>
        public DroppableSettings? Droppable { get => _droppable; set { ThrowIfReadOnly(); _droppable = value; } }
        private DroppableSettings? _droppable;

        /// <summary>Marker for the V.DragOverlay positioner (the framework-positioned drag preview).</summary>
        public DragOverlaySettings? DragOverlay { get => _dragOverlay; set { ThrowIfReadOnly(); _dragOverlay = value; } }
        private DragOverlaySettings? _dragOverlay;

        /// <summary>Slider-specific settings.</summary>
        public SliderSettings? Slider { get => _slider; set { ThrowIfReadOnly(); _slider = value; } }
        private SliderSettings? _slider;

        /// <summary>ScrollView-specific settings.</summary>
        public ScrollViewSettings? ScrollView { get => _scrollView; set { ThrowIfReadOnly(); _scrollView = value; } }
        private ScrollViewSettings? _scrollView;

        /// <summary>TextField-specific settings.</summary>
        public TextFieldSettings? TextField { get => _textField; set { ThrowIfReadOnly(); _textField = value; } }
        private TextFieldSettings? _textField;

        /// <summary>Choices for DropdownField / RadioButtonGroup.</summary>
        public ChoicesSettings? Choices { get => _choices; set { ThrowIfReadOnly(); _choices = value; } }
        private ChoicesSettings? _choices;

        /// <summary>SceneView-specific settings (the camera whose output the element displays).</summary>
        public SceneViewSettings? SceneView { get => _sceneView; set { ThrowIfReadOnly(); _sceneView = value; } }
        private SceneViewSettings? _sceneView;

        /// <summary>Particles-specific settings (the effect the element simulates and draws).</summary>
        public ParticlesSettings? Particles { get => _particles; set { ThrowIfReadOnly(); _particles = value; } }
        private ParticlesSettings? _particles;

        /// <summary>Anchored-specific settings (the 3D Transform this element's screen position tracks).</summary>
        public AnchoredSettings? Anchored { get => _anchored; set { ThrowIfReadOnly(); _anchored = value; } }
        private AnchoredSettings? _anchored;

        /// <summary>
        /// Carried <c>data-*</c> attribute values (key → value), the UI-Toolkit stand-in for HTML data
        /// attributes. UI Toolkit has no attributes, so these are stored in the reconciler's per-element
        /// side-table and matched by the <c>data-[key=value]:</c> / <c>data-[key]:</c> variants. Null = none.
        /// </summary>
        public IReadOnlyDictionary<string, string>? Data { get => _data; set { ThrowIfReadOnly(); _data = value; } }
        private IReadOnlyDictionary<string, string>? _data;

        /// <summary>
        /// Carried <c>aria-*</c> attribute values (key → value), matched by the
        /// <c>aria-[key=value]:</c> / <c>aria-[key]:</c> variants. Stored in the reconciler's per-element
        /// side-table like <see cref="Data"/> (UI Toolkit has no HTML attributes). Null = none.
        /// </summary>
        public IReadOnlyDictionary<string, string>? Aria { get => _aria; set { ThrowIfReadOnly(); _aria = value; } }
        private IReadOnlyDictionary<string, string>? _aria;

        /// <summary>Shared read-only instance with no properties set; throws if mutated.</summary>
        public static readonly FiberElementProps Empty = new() { _isReadOnly = true };

        private void ThrowIfReadOnly()
        {
            if (_isReadOnly)
                throw new InvalidOperationException("FiberElementProps.Empty is read-only and cannot be modified.");
        }
    }

    /// <summary>Slider.lowValue / highValue. Record structural equality simplifies DiffProps.</summary>
    public sealed record SliderSettings(
        float? LowValue = null,
        float? HighValue = null);

    /// <summary>Controls ScrollView scroller visibility and touch-scroll behavior.</summary>
    public sealed record ScrollViewSettings(
        ScrollerVisibility? VerticalScrollerVisibility = null,
        ScrollerVisibility? HorizontalScrollerVisibility = null,
        ScrollView.TouchScrollBehavior? TouchScrollBehavior = null);

    /// <summary>TextField.isPasswordField.</summary>
    public sealed record TextFieldSettings(
        bool? IsPassword = null);

    /// <summary>List of choices for DropdownField / RadioButtonGroup.</summary>
    public sealed record ChoicesSettings(
        List<string>? Choices = null);

    /// <summary>
    /// The camera a SceneView element displays, plus its render-resolution policy. The framework owns
    /// the RenderTexture: it is created at the element's laid-out pixel size (times
    /// <see cref="ResolutionScale"/>), follows geometry changes, and is released on unmount.
    /// The constructor fail-fasts on a non-positive or NaN scale so every construction path (the
    /// factory, wrapper hosts, direct construction) shares one guard; a <c>with</c> expression
    /// bypasses it like any record init.
    /// </summary>
    public sealed record SceneViewSettings
    {
        public Camera? Camera { get; init; }
        public float ResolutionScale { get; init; } = 1f;

        /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="resolutionScale"/> is &lt;= 0 or NaN.</exception>
        public SceneViewSettings(Camera? camera = null, float resolutionScale = 1f)
        {
            Camera = camera;
            ResolutionScale = VelvetArgUtil.RequirePositiveFinite(resolutionScale, nameof(resolutionScale),
                "resolutionScale must be greater than 0.");
        }
    }

    /// <summary>
    /// The particle effect a Particles element simulates and draws: the source effect (a prefab's
    /// ParticleSystem — the framework instantiates a hidden simulation host from it and owns that
    /// instance), the play trigger, and the world-unit → element-pixel mapping.
    /// The constructor fail-fasts on a non-positive or NaN mapping so every construction path shares
    /// one guard; a <c>with</c> expression bypasses it like any record init.
    /// </summary>
    public sealed record ParticlesSettings
    {
        public ParticleSystem? Effect { get; init; }
        public PlayTrigger PlayOn { get; init; } = PlayTrigger.Mount;
        public float PixelsPerUnit { get; init; } = 100f;

        /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="pixelsPerUnit"/> is &lt;= 0 or NaN.</exception>
        public ParticlesSettings(ParticleSystem? effect = null, PlayTrigger playOn = PlayTrigger.Mount, float pixelsPerUnit = 100f)
        {
            Effect = effect;
            PlayOn = playOn;
            PixelsPerUnit = VelvetArgUtil.RequirePositiveFinite(pixelsPerUnit, nameof(pixelsPerUnit),
                "pixelsPerUnit must be positive; it maps particle world units to element pixels.");
        }
    }

    /// <summary>
    /// Focus-management behavior for a container subtree — React Aria's FocusScope parity:
    /// <see cref="Contain"/>/<see cref="RestoreFocus"/>/<see cref="AutoFocus"/> map 1:1;
    /// <see cref="SingleTabStop"/> is the WAI-ARIA composite-widget (roving) contract adapted to UI Toolkit,
    /// where arrow/dpad movement inside the group is already engine-native 2D navigation. Record structural
    /// equality simplifies DiffProps.
    /// </summary>
    /// <param name="Contain">Tab/Shift-Tab wrap within the subtree (computed by a focus ring scoped to it);
    /// a 2D/pointer move that exits the subtree is snapped back within the same event flush, wherever it
    /// landed. A press on empty non-focusable space clears focus to nothing first — that path re-focuses
    /// the scope on the panel's next scheduler tick instead.</param>
    /// <param name="RestoreFocus">On unmount while holding focus, refocus the element focus came FROM when
    /// it first entered the scope (skipped if that element is gone, detached, or cannot grab focus).</param>
    /// <param name="AutoFocus">On mount (the scope's FIRST attach-to-panel, never a re-attach such as a
    /// keyed reorder's), focus the scope's first focusable descendant (skipped when focus is already
    /// inside the scope) — matching React's mount-once autoFocus.</param>
    /// <param name="SingleTabStop">The subtree behaves as one Tab stop: Tab from inside exits past the
    /// remaining members (wrapping within the nearest containing scope, if any); Tab entering from outside
    /// — in either direction — lands on the last-focused member, else the scope's first. Members keep
    /// tabIndex 0, so engine 2D arrow/dpad navigation inside is untouched.</param>
    public sealed record FocusScopeSettings(
        bool Contain = false,
        bool RestoreFocus = false,
        bool AutoFocus = false,
        bool SingleTabStop = false);

    /// <summary>
    /// The 3D Transform an Anchored element's screen position tracks, plus the camera whose projection
    /// drives it (null resolves to <see cref="Camera.main"/> on every tick, so a scene's active camera
    /// can change without a settings update) and a pixel offset applied after projection.
    /// </summary>
    public sealed record AnchoredSettings
    {
        public Transform? Target { get; init; }
        public Camera? Camera { get; init; }
        public Vector2 Offset { get; init; }
        public bool HideWhenBehindCamera { get; init; } = true;

        public AnchoredSettings(Transform? target, Camera? camera = null, Vector2 offset = default, bool hideWhenBehindCamera = true)
        {
            Target = target;
            Camera = camera;
            Offset = offset;
            HideWhenBehindCamera = hideWhenBehindCamera;
        }
    }

    // Shared numeric precondition for the settings records above: several element factories carry a
    // positive-finite float knob, and each duplicating its own NaN-safe check invites drift.
    internal static class VelvetArgUtil
    {
        internal static float RequirePositiveFinite(float value, string paramName, string message)
        {
            // The negated > comparison catches NaN too.
            if (!(value > 0f))
            {
                throw new System.ArgumentOutOfRangeException(paramName, message);
            }
            return value;
        }
    }
}
