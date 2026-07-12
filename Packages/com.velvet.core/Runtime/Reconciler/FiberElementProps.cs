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
    /// <paramref name="ResolutionScale"/>), follows geometry changes, and is released on unmount.
    /// </summary>
    public sealed record SceneViewSettings(
        Camera? Camera = null,
        float ResolutionScale = 1f);
}
