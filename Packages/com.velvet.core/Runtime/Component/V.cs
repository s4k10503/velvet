#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// VNode builder DSL. Declarative UI construction API for composing element trees in C#.
    /// </summary>
    public static partial class V
    {
        #region Internals: fields & event/cache helpers

        private static readonly string[] EmptyClassNames = Array.Empty<string>();
        private static readonly VNode?[] EmptyChildren = Array.Empty<VNode>();
        private static readonly FiberEventBinding[] EmptyEvents = Array.Empty<FiberEventBinding>();

        // Wraps a single event binding in a pooled one-element array (or the shared empty array when the
        // handler was null). Callers pass `onX != null ? new XxxBinding { Handler = onX } : null` so the
        // binding is allocated only when a handler is supplied — preserving the no-handler zero-alloc path.
        private static FiberEventBinding[] SingleEvent(FiberEventBinding? binding)
        {
            if (binding == null)
            {
                return EmptyEvents;
            }
            var events = VNodePool.RentSingleEventArray();
            events[0] = binding;
            return events;
        }

        /// <summary>Not thread-safe. Acceptable because Velvet's Reconciler is main-thread only.</summary>
        private static readonly Dictionary<string, string[]> s_classNameCache = new();
        internal const int MaxClassNameCacheSize = 256;

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticFields()
        {
            s_classNameCache.Clear();
        }
#endif

        #endregion

        #region Host element factories

        /// <summary>
        /// Creates a VisualElement (generic container).
        /// Long form: every prop is a named optional parameter. For the shorthand
        /// <c>V.Div("class", child1, child2)</c> form, see the <c>params</c> overload.
        /// </summary>
        /// <param name="className">CSS-like utility class string. Multiple classes separated by spaces.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <param name="name">Element name assigned to <see cref="VisualElement.name"/> for query/debug.</param>
        /// <param name="props">Optional FiberElementProps (text / tooltip / enabled / etc.) bag.</param>
        /// <param name="styles">Inline style overrides applied on top of USS classes.</param>
        /// <param name="refCallback">Callback invoked on mount with the created VisualElement; returned Action runs on unmount.</param>
        /// <param name="children">Child VNodes rendered inside this element.</param>
        /// <param name="whileHoverClass">USS class toggled while the pointer hovers the element.</param>
        /// <param name="whileTapClass">USS class toggled while the pointer is pressed on the element.</param>
        /// <param name="whileFocusClass">USS class toggled while the element holds keyboard/UI focus.</param>
        /// <param name="data">data-* attribute map matched by <c>data-[...]</c> variants.</param>
        /// <param name="aria">aria-* attribute map matched by <c>aria-[...]</c> variants.</param>
        /// <returns>The created <see cref="ElementNode"/> representing this element.</returns>
        public static ElementNode Div(
            string? className = null,
            string? key = null,
            string? name = null,
            FiberElementProps? props = null,
            StyleOverrides? styles = null,
            Func<VisualElement, Action>? refCallback = null,
            VNode?[]? children = null,
            string? whileHoverClass = null,
            string? whileTapClass = null,
            string? whileFocusClass = null,
            IReadOnlyDictionary<string, string>? data = null,
            IReadOnlyDictionary<string, string>? aria = null)
        {
            return new ElementNode
            {
                Key = key,
                ElementType = typeof(VisualElement),
                Name = name,
                ClassNames = ParseClassNames(className),
                Props = WithAttributes(props, data, aria),
                Styles = styles,
                Children = children ?? EmptyChildren,
                Events = EmptyEvents,
                RefCallback = refCallback,
                WhileHoverClass = whileHoverClass,
                WhileTapClass = whileTapClass,
                WhileFocusClass = whileFocusClass,
            };
        }

        /// <summary>
        /// Shorthand overload: positional <paramref name="className"/> + variadic
        /// <c>children</c>, building a <c>div</c> element with just a class string and children.
        /// For any prop besides <paramref name="className"/>, use the long-form overload
        /// with named arguments.
        /// </summary>
        /// <param name="className">CSS-like utility class string. Multiple classes separated by spaces.</param>
        /// <param name="children">Child VNodes; pass zero or more positionals or expand an existing array.</param>
        /// <returns>The created <see cref="ElementNode"/>.</returns>
        public static ElementNode Div(string className, params VNode?[] children) =>
            new ElementNode
            {
                ElementType = typeof(VisualElement),
                ClassNames = ParseClassNames(className),
                Children = children == null || children.Length == 0 ? EmptyChildren : children,
                Events = EmptyEvents,
            };

        /// <summary>
        /// Creates an element backed by a custom <see cref="VisualElement"/> subclass <typeparamref name="T"/>,
        /// for control types the built-in factories (<see cref="Div"/>, <see cref="Label"/>, …) do not expose.
        /// Long form: every prop is a named optional parameter. For the shorthand
        /// <c>V.Custom&lt;T&gt;("class", child1, child2)</c> form, see the <c>params</c> overload.
        /// </summary>
        /// <typeparam name="T">Concrete VisualElement subclass to instantiate.</typeparam>
        /// <param name="className">CSS-like utility class string. Multiple classes separated by spaces.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <param name="name">Element name assigned to <see cref="VisualElement.name"/> for query/debug.</param>
        /// <param name="props">Optional FiberElementProps (text / tooltip / enabled / etc.) bag.</param>
        /// <param name="refCallback">Callback invoked on mount with the created VisualElement; returned Action runs on unmount.</param>
        /// <param name="children">Child VNodes rendered inside this element.</param>
        /// <param name="whileHoverClass">USS class toggled while the pointer hovers the element.</param>
        /// <param name="whileTapClass">USS class toggled while the pointer is pressed on the element.</param>
        /// <param name="whileFocusClass">USS class toggled while the element holds keyboard/UI focus.</param>
        /// <param name="data">data-* attribute map matched by <c>data-[...]</c> variants.</param>
        /// <param name="aria">aria-* attribute map matched by <c>aria-[...]</c> variants.</param>
        /// <returns>The created <see cref="ElementNode"/> representing this element.</returns>
        public static ElementNode Custom<T>(
            string? className = null,
            string? key = null,
            string? name = null,
            FiberElementProps? props = null,
            Func<VisualElement, Action>? refCallback = null,
            VNode?[]? children = null,
            string? whileHoverClass = null,
            string? whileTapClass = null,
            string? whileFocusClass = null,
            IReadOnlyDictionary<string, string>? data = null,
            IReadOnlyDictionary<string, string>? aria = null) where T : VisualElement
        {
            return new ElementNode
            {
                Key = key,
                ElementType = typeof(T),
                Name = name,
                ClassNames = ParseClassNames(className),
                Props = WithAttributes(props, data, aria),
                Children = children ?? EmptyChildren,
                Events = EmptyEvents,
                RefCallback = refCallback,
                WhileHoverClass = whileHoverClass,
                WhileTapClass = whileTapClass,
                WhileFocusClass = whileFocusClass,
            };
        }

        /// <summary>
        /// Shorthand overload: positional <paramref name="className"/> + variadic
        /// <c>children</c>, building a <typeparamref name="T"/> element with just a class string and children.
        /// For any prop besides <paramref name="className"/>, use the long-form overload
        /// with named arguments.
        /// </summary>
        /// <typeparam name="T">Concrete VisualElement subclass to instantiate.</typeparam>
        /// <param name="className">CSS-like utility class string. Multiple classes separated by spaces.</param>
        /// <param name="children">Child VNodes; pass zero or more positionals or expand an existing array.</param>
        /// <returns>The created <see cref="ElementNode"/>.</returns>
        public static ElementNode Custom<T>(string className, params VNode?[] children)
            where T : VisualElement =>
            new ElementNode
            {
                ElementType = typeof(T),
                ClassNames = ParseClassNames(className),
                Children = children == null || children.Length == 0 ? EmptyChildren : children,
                Events = EmptyEvents,
            };

        /// <summary>
        /// Creates a ScrollView.
        /// Long form: every prop is a named optional parameter. For the shorthand
        /// <c>V.ScrollView("class", child1, child2)</c> form, see the <c>params</c> overload.
        /// </summary>
        /// <param name="className">CSS-like utility class string. Multiple classes separated by spaces.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <param name="name">Element name assigned to <see cref="VisualElement.name"/> for query/debug.</param>
        /// <param name="verticalScrollerVisibility">Visibility policy for the vertical scroller.</param>
        /// <param name="horizontalScrollerVisibility">Visibility policy for the horizontal scroller.</param>
        /// <param name="touchScrollBehavior">Touch scroll behavior (Clamped / Elastic / Unrestricted).</param>
        /// <param name="onCreated">Callback invoked once when the ScrollView VisualElement is first created.</param>
        /// <param name="refCallback">Callback invoked on mount with the created VisualElement; returned Action runs on unmount.</param>
        /// <param name="children">Child VNodes placed inside the scroll content container.</param>
        /// <param name="whileHoverClass">USS class toggled while the pointer hovers the element.</param>
        /// <param name="whileTapClass">USS class toggled while the pointer is pressed on the element.</param>
        /// <param name="whileFocusClass">USS class toggled while the element holds keyboard/UI focus.</param>
        /// <param name="data">data-* attribute map matched by <c>data-[...]</c> variants.</param>
        /// <param name="aria">aria-* attribute map matched by <c>aria-[...]</c> variants.</param>
        /// <returns>The created <see cref="ElementNode"/> representing the ScrollView.</returns>
        public static ElementNode ScrollView(
            string? className = null,
            string? key = null,
            string? name = null,
            ScrollerVisibility? verticalScrollerVisibility = null,
            ScrollerVisibility? horizontalScrollerVisibility = null,
            ScrollView.TouchScrollBehavior? touchScrollBehavior = null,
            Action<VisualElement>? onCreated = null,
            Func<VisualElement, Action>? refCallback = null,
            VNode?[]? children = null,
            string? whileHoverClass = null,
            string? whileTapClass = null,
            string? whileFocusClass = null,
            IReadOnlyDictionary<string, string>? data = null,
            IReadOnlyDictionary<string, string>? aria = null)
        {
            FiberElementProps? props = null;
            if (verticalScrollerVisibility.HasValue || horizontalScrollerVisibility.HasValue || touchScrollBehavior.HasValue)
            {
                props = VNodePool.RentProps();
                props.ScrollView = new ScrollViewSettings(
                    verticalScrollerVisibility, horizontalScrollerVisibility, touchScrollBehavior);
            }
            props = WithAttributes(props, data, aria);

            return new ElementNode
            {
                Key = key,
                ElementType = typeof(ScrollView),
                Name = name,
                ClassNames = ParseClassNames(className),
                Props = props,
                Children = children ?? EmptyChildren,
                Events = EmptyEvents,
                OnCreated = onCreated,
                RefCallback = refCallback,
                WhileHoverClass = whileHoverClass,
                WhileTapClass = whileTapClass,
                WhileFocusClass = whileFocusClass,
            };
        }

        /// <summary>
        /// Shorthand overload: positional <paramref name="className"/> + variadic
        /// <c>children</c>, building a <c>ScrollView</c> element with just a class string and children.
        /// For any prop besides <paramref name="className"/>, use the long-form overload
        /// with named arguments.
        /// </summary>
        /// <param name="className">CSS-like utility class string. Multiple classes separated by spaces.</param>
        /// <param name="children">Child VNodes; pass zero or more positionals or expand an existing array.</param>
        /// <returns>The created <see cref="ElementNode"/>.</returns>
        public static ElementNode ScrollView(string className, params VNode?[] children) =>
            new ElementNode
            {
                ElementType = typeof(ScrollView),
                ClassNames = ParseClassNames(className),
                Children = children == null || children.Length == 0 ? EmptyChildren : children,
                Events = EmptyEvents,
            };

        /// <summary>
        /// Creates a Button.
        /// <c>text</c> and <c>children</c> can be combined. UI Toolkit's Button inherits from TextElement and
        /// keeps the <c>text</c> property and child VisualElements independently, so use <c>children</c> when
        /// declaring multiple children (e.g. icon + label) and <c>text</c> for text-only buttons.
        /// </summary>
        /// <param name="className">CSS-like utility class string. Multiple classes separated by spaces.</param>
        /// <param name="text">Button label text. Coexists with <paramref name="children"/>.</param>
        /// <param name="onClick">Click handler. When null, no click event is bound.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <param name="name">Element name assigned to <see cref="VisualElement.name"/> for query/debug.</param>
        /// <param name="tooltip">Tooltip string shown on hover.</param>
        /// <param name="enabled">When false, disables the button (greyed out, no click).</param>
        /// <param name="styles">Inline style overrides applied on top of USS classes.</param>
        /// <param name="refCallback">Callback invoked on mount with the created VisualElement; returned Action runs on unmount.</param>
        /// <param name="wrapElement">Optional wrapper that returns a parent VisualElement enclosing the button (e.g. for shadow effects).</param>
        /// <param name="whileHoverClass">USS class toggled while the pointer hovers the element.</param>
        /// <param name="whileTapClass">USS class toggled while the pointer is pressed on the element.</param>
        /// <param name="whileFocusClass">USS class toggled while the element holds keyboard/UI focus.</param>
        /// <param name="children">Child VNodes (e.g. icon + label) rendered inside the button.</param>
        /// <param name="data">data-* attribute map matched by <c>data-[...]</c> variants.</param>
        /// <param name="aria">aria-* attribute map matched by <c>aria-[...]</c> variants.</param>
        /// <returns>The created <see cref="ElementNode"/> representing this button.</returns>
        public static ElementNode Button(
            string? className = null,
            string? text = null,
            Action? onClick = null,
            string? key = null,
            string? name = null,
            string? tooltip = null,
            bool? enabled = null,
            StyleOverrides? styles = null,
            Func<VisualElement, Action>? refCallback = null,
            Func<VisualElement, VisualElement>? wrapElement = null,
            string? whileHoverClass = null,
            string? whileTapClass = null,
            string? whileFocusClass = null,
            VNode?[]? children = null,
            IReadOnlyDictionary<string, string>? data = null,
            IReadOnlyDictionary<string, string>? aria = null)
        {
            var events = SingleEvent(onClick != null ? new ClickedBinding { Handler = onClick } : null);

            FiberElementProps? props = null;
            if (text != null || tooltip != null || enabled.HasValue)
            {
                props = VNodePool.RentProps();
                props.Text = text;
                props.Tooltip = tooltip;
                props.Enabled = enabled;
            }
            props = WithAttributes(props, data, aria);

            return new ElementNode
            {
                Key = key,
                ElementType = typeof(Button),
                Name = name,
                ClassNames = ParseClassNames(className),
                Props = props,
                Styles = styles,
                Children = children ?? EmptyChildren,
                Events = events,
                RefCallback = refCallback,
                WrapElement = wrapElement,
                WhileHoverClass = whileHoverClass,
                WhileTapClass = whileTapClass,
                WhileFocusClass = whileFocusClass,
            };
        }

        /// <summary>
        /// Shorthand overload: positional <paramref name="className"/> + variadic
        /// <c>children</c>, building a <c>Button</c> with just a class string and children (e.g. an
        /// icon + label). For any other prop — notably <c>onClick</c> — use the long-form overload
        /// with named arguments.
        /// </summary>
        /// <param name="className">CSS-like utility class string. Multiple classes separated by spaces.</param>
        /// <param name="children">Child VNodes; pass zero or more positionals or expand an existing array.</param>
        /// <returns>The created <see cref="ElementNode"/>.</returns>
        public static ElementNode Button(string className, params VNode?[] children) =>
            new ElementNode
            {
                ElementType = typeof(Button),
                ClassNames = ParseClassNames(className),
                Children = children == null || children.Length == 0 ? EmptyChildren : children,
                Events = EmptyEvents,
            };

        /// <summary>
        /// Creates a Label.
        /// </summary>
        /// <param name="className">CSS-like utility class string. Multiple classes separated by spaces.</param>
        /// <param name="text">Label text content.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <param name="name">Element name assigned to <see cref="VisualElement.name"/> for query/debug.</param>
        /// <param name="refCallback">Callback invoked on mount with the created VisualElement; returned Action runs on unmount.</param>
        /// <param name="whileHoverClass">USS class toggled while the pointer hovers the element.</param>
        /// <param name="whileTapClass">USS class toggled while the pointer is pressed on the element.</param>
        /// <param name="whileFocusClass">USS class toggled while the element holds keyboard/UI focus.</param>
        /// <param name="data">data-* attribute map matched by <c>data-[...]</c> variants.</param>
        /// <param name="aria">aria-* attribute map matched by <c>aria-[...]</c> variants.</param>
        /// <returns>The created <see cref="ElementNode"/> representing this label.</returns>
        public static ElementNode Label(
            string? className = null,
            string? text = null,
            string? key = null,
            string? name = null,
            Func<VisualElement, Action>? refCallback = null,
            string? whileHoverClass = null,
            string? whileTapClass = null,
            string? whileFocusClass = null,
            IReadOnlyDictionary<string, string>? data = null,
            IReadOnlyDictionary<string, string>? aria = null)
        {
            FiberElementProps? props = null;
            if (text != null)
            {
                props = VNodePool.RentProps();
                props.Text = text;
            }
            props = WithAttributes(props, data, aria);

            return new ElementNode
            {
                Key = key,
                ElementType = typeof(Label),
                Name = name,
                ClassNames = ParseClassNames(className),
                Props = props,
                Children = EmptyChildren,
                Events = EmptyEvents,
                RefCallback = refCallback,
                WhileHoverClass = whileHoverClass,
                WhileTapClass = whileTapClass,
                WhileFocusClass = whileFocusClass,
            };
        }

        /// <summary>
        /// Creates a Slider.
        /// </summary>
        /// <param name="className">CSS-like utility class string. Multiple classes separated by spaces.</param>
        /// <param name="value">Current slider value (controlled).</param>
        /// <param name="lowValue">Minimum value of the slider range.</param>
        /// <param name="highValue">Maximum value of the slider range.</param>
        /// <param name="onValueChanged">Handler invoked when the slider value changes.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <param name="name">Element name assigned to <see cref="VisualElement.name"/> for query/debug.</param>
        /// <param name="enabled">When false, disables the slider input.</param>
        /// <param name="refCallback">Callback invoked on mount with the created VisualElement; returned Action runs on unmount.</param>
        /// <param name="onCreated">Callback invoked once when the Slider VisualElement is first created.</param>
        /// <param name="whileHoverClass">USS class toggled while the pointer hovers the element.</param>
        /// <param name="whileTapClass">USS class toggled while the pointer is pressed on the element.</param>
        /// <param name="whileFocusClass">USS class toggled while the element holds keyboard/UI focus.</param>
        /// <param name="data">data-* attribute map matched by <c>data-[...]</c> variants.</param>
        /// <param name="aria">aria-* attribute map matched by <c>aria-[...]</c> variants.</param>
        /// <returns>The created <see cref="ElementNode"/> representing this slider.</returns>
        public static ElementNode Slider(
            string? className = null,
            float? value = null,
            float? lowValue = null,
            float? highValue = null,
            Action<float>? onValueChanged = null,
            string? key = null,
            string? name = null,
            bool? enabled = null,
            Func<VisualElement, Action>? refCallback = null,
            Action<VisualElement>? onCreated = null,
            string? whileHoverClass = null,
            string? whileTapClass = null,
            string? whileFocusClass = null,
            IReadOnlyDictionary<string, string>? data = null,
            IReadOnlyDictionary<string, string>? aria = null)
        {
            var events = SingleEvent(onValueChanged != null ? new ChangeEventBinding<float> { Handler = onValueChanged } : null);

            FiberElementProps? props = null;
            if (value.HasValue || lowValue.HasValue || highValue.HasValue || enabled.HasValue)
            {
                props = VNodePool.RentProps();
                props.FieldValue = value;
                props.Enabled = enabled;
                props.Slider = (lowValue.HasValue || highValue.HasValue)
                    ? new SliderSettings(lowValue, highValue)
                    : null;
            }
            props = WithAttributes(props, data, aria);

            return new ElementNode
            {
                Key = key,
                ElementType = typeof(Slider),
                Name = name,
                ClassNames = ParseClassNames(className),
                Props = props,
                Children = EmptyChildren,
                Events = events,
                RefCallback = refCallback,
                OnCreated = onCreated,
                WhileHoverClass = whileHoverClass,
                WhileTapClass = whileTapClass,
                WhileFocusClass = whileFocusClass,
            };
        }

        /// <summary>
        /// Creates a Toggle.
        /// </summary>
        /// <param name="className">CSS-like utility class string. Multiple classes separated by spaces.</param>
        /// <param name="value">Current toggle state (controlled).</param>
        /// <param name="onValueChanged">Handler invoked when the toggle state changes.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <param name="name">Element name assigned to <see cref="VisualElement.name"/> for query/debug.</param>
        /// <param name="label">Label text shown next to the toggle.</param>
        /// <param name="enabled">When false, disables user interaction with the toggle.</param>
        /// <param name="refCallback">Callback invoked on mount with the created VisualElement; returned Action runs on unmount.</param>
        /// <param name="whileHoverClass">USS class toggled while the pointer hovers the element.</param>
        /// <param name="whileTapClass">USS class toggled while the pointer is pressed on the element.</param>
        /// <param name="whileFocusClass">USS class toggled while the element holds keyboard/UI focus.</param>
        /// <param name="data">data-* attribute map matched by <c>data-[...]</c> variants.</param>
        /// <param name="aria">aria-* attribute map matched by <c>aria-[...]</c> variants.</param>
        /// <returns>The created <see cref="ElementNode"/> representing this toggle.</returns>
        public static ElementNode Toggle(
            string? className = null,
            bool? value = null,
            Action<bool>? onValueChanged = null,
            string? key = null,
            string? name = null,
            string? label = null,
            bool? enabled = null,
            Func<VisualElement, Action>? refCallback = null,
            string? whileHoverClass = null,
            string? whileTapClass = null,
            string? whileFocusClass = null,
            IReadOnlyDictionary<string, string>? data = null,
            IReadOnlyDictionary<string, string>? aria = null)
        {
            var events = SingleEvent(onValueChanged != null ? new ChangeEventBinding<bool> { Handler = onValueChanged } : null);

            FiberElementProps? props = null;
            if (value.HasValue || label != null || enabled.HasValue)
            {
                props = VNodePool.RentProps();
                props.FieldValue = value;
                props.Text = label;
                props.Enabled = enabled;
            }
            props = WithAttributes(props, data, aria);

            return new ElementNode
            {
                Key = key,
                ElementType = typeof(Toggle),
                Name = name,
                ClassNames = ParseClassNames(className),
                Props = props,
                Children = EmptyChildren,
                Events = events,
                RefCallback = refCallback,
                WhileHoverClass = whileHoverClass,
                WhileTapClass = whileTapClass,
                WhileFocusClass = whileFocusClass,
            };
        }

        /// <summary>
        /// Creates a TextField.
        /// </summary>
        /// <remarks>
        /// <paramref name="placeholder"/>, <paramref name="maxLength"/>, <paramref name="isReadOnly"/> and
        /// <paramref name="isDelayed"/> are undeclared when null rather than reset to a default;
        /// <c>Documentation~/react-migration.md</c> owns what a null and a dropped one each do.
        /// </remarks>
        /// <param name="className">CSS-like utility class string. Multiple classes separated by spaces.</param>
        /// <param name="value">Current text value (controlled).</param>
        /// <param name="onValueChanged">Handler invoked when the input text changes.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <param name="name">Element name assigned to <see cref="VisualElement.name"/> for query/debug.</param>
        /// <param name="label">Label text shown next to the field.</param>
        /// <param name="isPasswordField">When true, masks the input as a password field.</param>
        /// <param name="enabled">When false, disables user input.</param>
        /// <param name="refCallback">Callback invoked on mount with the created VisualElement; returned Action runs on unmount.</param>
        /// <param name="whileHoverClass">USS class toggled while the pointer hovers the element.</param>
        /// <param name="whileTapClass">USS class toggled while the pointer is pressed on the element.</param>
        /// <param name="whileFocusClass">USS class toggled while the element holds keyboard/UI focus.</param>
        /// <param name="data">data-* attribute map matched by <c>data-[...]</c> variants.</param>
        /// <param name="aria">aria-* attribute map matched by <c>aria-[...]</c> variants.</param>
        /// <param name="placeholder">A short hint shown in the empty field (HTML <c>placeholder</c>). An empty string declares an empty hint.</param>
        /// <param name="maxLength">Maximum number of characters the field accepts, -1 for no limit (HTML <c>maxlength</c>).</param>
        /// <param name="isReadOnly">When true, the field cannot be edited (HTML <c>readonly</c>).</param>
        /// <param name="isDelayed">When true, the value is not updated per keystroke but on Enter, on the field losing focus, and on a later render taking the flag off.</param>
        /// <returns>The created <see cref="ElementNode"/> representing this text field.</returns>
        public static ElementNode TextField(
            string? className = null,
            string? value = null,
            Action<string>? onValueChanged = null,
            string? key = null,
            string? name = null,
            string? label = null,
            bool? isPasswordField = null,
            bool? enabled = null,
            Func<VisualElement, Action>? refCallback = null,
            string? whileHoverClass = null,
            string? whileTapClass = null,
            string? whileFocusClass = null,
            IReadOnlyDictionary<string, string>? data = null,
            IReadOnlyDictionary<string, string>? aria = null,
            // These sit after `aria` rather than beside `isPasswordField`, where they belong by subject,
            // so a positional call written against 2.1.0 still binds as it did.
            // TextFieldPositionalOrderTests holds the prefix that placement protects.
            string? placeholder = null,
            int? maxLength = null,
            bool? isReadOnly = null,
            bool? isDelayed = null)
        {
            var events = SingleEvent(onValueChanged != null ? new ChangeEventBinding<string> { Handler = onValueChanged } : null);

            var declaresTextField = isPasswordField.HasValue || placeholder != null || maxLength.HasValue
                                    || isReadOnly.HasValue || isDelayed.HasValue;

            FiberElementProps? props = null;
            if (value != null || label != null || declaresTextField || enabled.HasValue)
            {
                props = VNodePool.RentProps();
                props.FieldValue = value;
                props.Text = label;
                props.Enabled = enabled;
                props.TextField = declaresTextField
                    ? new TextFieldSettings(isPasswordField, placeholder, maxLength, isReadOnly, isDelayed)
                    : null;
            }
            props = WithAttributes(props, data, aria);

            return new ElementNode
            {
                Key = key,
                ElementType = typeof(TextField),
                Name = name,
                ClassNames = ParseClassNames(className),
                Props = props,
                Children = EmptyChildren,
                Events = events,
                RefCallback = refCallback,
                WhileHoverClass = whileHoverClass,
                WhileTapClass = whileTapClass,
                WhileFocusClass = whileFocusClass,
            };
        }

        /// <summary>
        /// Creates an Image element for displaying sprites or textures.
        /// </summary>
        /// <param name="className">CSS-like utility class string. Multiple classes separated by spaces.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <param name="name">Element name assigned to <see cref="VisualElement.name"/> for query/debug.</param>
        /// <param name="styles">Inline style overrides applied on top of USS classes (sprite / image typically set here).</param>
        /// <param name="refCallback">Callback invoked on mount with the created VisualElement; returned Action runs on unmount.</param>
        /// <param name="whileHoverClass">USS class toggled while the pointer hovers the element (gesture-driven).</param>
        /// <param name="whileTapClass">USS class toggled while the pointer is pressed on the element (gesture-driven).</param>
        /// <param name="whileFocusClass">USS class toggled while the element holds keyboard/UI focus (gesture-driven).</param>
        /// <param name="data">data-* attribute map matched by <c>data-[...]</c> variants.</param>
        /// <param name="aria">aria-* attribute map matched by <c>aria-[...]</c> variants.</param>
        /// <returns>The created <see cref="ElementNode"/> representing this image.</returns>
        public static ElementNode Image(
            string? className = null,
            string? key = null,
            string? name = null,
            StyleOverrides? styles = null,
            Func<VisualElement, Action>? refCallback = null,
            string? whileHoverClass = null,
            string? whileTapClass = null,
            string? whileFocusClass = null,
            IReadOnlyDictionary<string, string>? data = null,
            IReadOnlyDictionary<string, string>? aria = null)
        {
            return new ElementNode
            {
                Key = key,
                ElementType = typeof(Image),
                Name = name,
                ClassNames = ParseClassNames(className),
                Props = WithAttributes(null, data, aria),
                Styles = styles,
                Children = EmptyChildren,
                Events = EmptyEvents,
                RefCallback = refCallback,
                WhileHoverClass = whileHoverClass,
                WhileTapClass = whileTapClass,
                WhileFocusClass = whileFocusClass,
            };
        }

        /// <summary>
        /// Creates a SceneView element displaying <paramref name="camera"/>'s output — the canvas-parity
        /// element. The framework owns the RenderTexture: it is sized to the element's laid-out pixel
        /// size (times <paramref name="resolutionScale"/>), rounded up to reuse the existing texture
        /// across minor resizes (preserving the element's aspect ratio), resized when the element's
        /// geometry changes,
        /// assigned to <c>camera.targetTexture</c> while mounted, and released on unmount (restoring the
        /// camera's target only if it is still the framework's own texture). The output arrives through
        /// the element's background image, so background utilities and rounded corners apply to it.
        /// </summary>
        /// <param name="camera">Camera whose output the element displays. Null renders nothing (the
        /// element mounts as an empty box until a camera is supplied).</param>
        /// <param name="className">CSS-like utility class string. Multiple classes separated by spaces.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <param name="name">Element name assigned to <see cref="VisualElement.name"/> for query/debug.</param>
        /// <param name="resolutionScale">Render-resolution multiplier over the element's laid-out pixel
        /// size (0.5 renders at half resolution).</param>
        /// <param name="styles">Inline style overrides applied on top of USS classes.</param>
        /// <param name="refCallback">Callback invoked on mount with the created VisualElement; returned Action runs on unmount.</param>
        /// <param name="whileHoverClass">USS class toggled while the pointer hovers the element (gesture-driven).</param>
        /// <param name="whileTapClass">USS class toggled while the pointer is pressed on the element (gesture-driven).</param>
        /// <param name="whileFocusClass">USS class toggled while the element holds keyboard/UI focus (gesture-driven).</param>
        /// <param name="data">data-* attribute map matched by <c>data-[...]</c> variants.</param>
        /// <param name="aria">aria-* attribute map matched by <c>aria-[...]</c> variants.</param>
        /// <returns>The created <see cref="ElementNode"/> representing this scene view.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="resolutionScale"/> is &lt;= 0 or NaN.</exception>
        public static ElementNode SceneView(
            Camera? camera,
            string? className = null,
            string? key = null,
            string? name = null,
            float resolutionScale = 1f,
            StyleOverrides? styles = null,
            Func<VisualElement, Action>? refCallback = null,
            string? whileHoverClass = null,
            string? whileTapClass = null,
            string? whileFocusClass = null,
            IReadOnlyDictionary<string, string>? data = null,
            IReadOnlyDictionary<string, string>? aria = null)
        {
            // Validated BEFORE renting pooled props so a throwing call leaks nothing (the settings
            // constructor fail-fasts on an invalid scale for every construction path, this factory
            // included). Always carried (even with a null camera): the patcher needs the settings on
            // BOTH sides of a diff to see a camera arriving or leaving as a settings change.
            var sceneView = new SceneViewSettings(camera, resolutionScale);
            var props = VNodePool.RentProps();
            props.SceneView = sceneView;

            return new ElementNode
            {
                Key = key,
                ElementType = typeof(SceneViewElement),
                Name = name,
                ClassNames = ParseClassNames(className),
                Props = WithAttributes(props, data, aria),
                Styles = styles,
                Children = EmptyChildren,
                Events = EmptyEvents,
                RefCallback = refCallback,
                WhileHoverClass = whileHoverClass,
                WhileTapClass = whileTapClass,
                WhileFocusClass = whileFocusClass,
            };
        }

        /// <summary>
        /// Creates a Particles element that simulates <paramref name="effect"/> in a hidden,
        /// framework-owned host and draws the live particles as textured quads inside the element —
        /// no camera, no world-space canvas, no render-pipeline coupling. The simulation host is
        /// instantiated on mount (its renderer disabled; only the simulation is consumed), destroyed
        /// on unmount, and recreated when <paramref name="effect"/> changes.
        /// </summary>
        /// <param name="effect">The source ParticleSystem (typically a prefab reference) to simulate.
        /// Null mounts an inert element until an effect is supplied. Local simulation space only.</param>
        /// <param name="className">CSS-like utility class string. Multiple classes separated by spaces.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <param name="name">Element name assigned to <see cref="VisualElement.name"/> for query/debug.</param>
        /// <param name="playOn">When the effect starts: on mount (default) or never (manual control).</param>
        /// <param name="pixelsPerUnit">World-unit → element-pixel mapping for particle positions and
        /// sizes, centered on the element. Must be positive.</param>
        /// <param name="styles">Inline style overrides applied on top of USS classes.</param>
        /// <param name="refCallback">Callback invoked on mount with the created VisualElement; returned Action runs on unmount.</param>
        /// <param name="whileHoverClass">USS class toggled while the pointer hovers the element (gesture-driven).</param>
        /// <param name="whileTapClass">USS class toggled while the pointer is pressed on the element (gesture-driven).</param>
        /// <param name="whileFocusClass">USS class toggled while the element holds keyboard/UI focus (gesture-driven).</param>
        /// <param name="data">data-* attribute map matched by <c>data-[...]</c> variants.</param>
        /// <param name="aria">aria-* attribute map matched by <c>aria-[...]</c> variants.</param>
        /// <returns>The created <see cref="ElementNode"/> representing this particles element.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="pixelsPerUnit"/> is not positive,
        /// or when <paramref name="playOn"/> names no member of <see cref="PlayTrigger"/>.</exception>
        public static ElementNode Particles(
            ParticleSystem? effect,
            string? className = null,
            string? key = null,
            string? name = null,
            PlayTrigger playOn = PlayTrigger.Mount,
            float pixelsPerUnit = 100f,
            StyleOverrides? styles = null,
            Func<VisualElement, Action>? refCallback = null,
            string? whileHoverClass = null,
            string? whileTapClass = null,
            string? whileFocusClass = null,
            IReadOnlyDictionary<string, string>? data = null,
            IReadOnlyDictionary<string, string>? aria = null)
        {
            if (playOn is not (PlayTrigger.Mount or PlayTrigger.Manual))
            {
                throw new ArgumentOutOfRangeException(nameof(playOn), playOn,
                    "V.Particles takes a member of PlayTrigger as its playOn.");
            }
            // Validated BEFORE renting pooled props so a throwing call leaks nothing (the settings
            // constructor fail-fasts on an invalid mapping for every construction path, this factory
            // included). Always carried (even with a null effect): the patcher needs the settings on
            // BOTH sides of a diff to see an effect arriving or leaving as a settings change.
            var particles = new ParticlesSettings(effect, playOn, pixelsPerUnit);
            var props = VNodePool.RentProps();
            props.Particles = particles;

            return new ElementNode
            {
                Key = key,
                ElementType = typeof(ParticlesElement),
                Name = name,
                ClassNames = ParseClassNames(className),
                Props = WithAttributes(props, data, aria),
                Styles = styles,
                Children = EmptyChildren,
                Events = EmptyEvents,
                RefCallback = refCallback,
                WhileHoverClass = whileHoverClass,
                WhileTapClass = whileTapClass,
                WhileFocusClass = whileFocusClass,
            };
        }

        /// <summary>
        /// Creates a DropdownField.
        /// </summary>
        /// <param name="className">CSS-like utility class string. Multiple classes separated by spaces.</param>
        /// <param name="value">Currently selected value (controlled).</param>
        /// <param name="choices">List of selectable values shown in the dropdown.</param>
        /// <param name="onValueChanged">Handler invoked when the selection changes.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <param name="name">Element name assigned to <see cref="VisualElement.name"/> for query/debug.</param>
        /// <param name="label">Label text shown next to the dropdown.</param>
        /// <param name="enabled">When false, disables user interaction with the dropdown.</param>
        /// <param name="refCallback">Callback invoked on mount with the created VisualElement; returned Action runs on unmount.</param>
        /// <param name="whileHoverClass">USS class toggled while the pointer hovers the element.</param>
        /// <param name="whileTapClass">USS class toggled while the pointer is pressed on the element.</param>
        /// <param name="whileFocusClass">USS class toggled while the element holds keyboard/UI focus.</param>
        /// <param name="data">data-* attribute map matched by <c>data-[...]</c> variants.</param>
        /// <param name="aria">aria-* attribute map matched by <c>aria-[...]</c> variants.</param>
        /// <returns>The created <see cref="ElementNode"/> representing this dropdown.</returns>
        public static ElementNode DropdownField(
            string? className = null,
            string? value = null,
            List<string>? choices = null,
            Action<string>? onValueChanged = null,
            string? key = null,
            string? name = null,
            string? label = null,
            bool? enabled = null,
            Func<VisualElement, Action>? refCallback = null,
            string? whileHoverClass = null,
            string? whileTapClass = null,
            string? whileFocusClass = null,
            IReadOnlyDictionary<string, string>? data = null,
            IReadOnlyDictionary<string, string>? aria = null)
        {
            var events = SingleEvent(onValueChanged != null ? new ChangeEventBinding<string> { Handler = onValueChanged } : null);

            FiberElementProps? props = null;
            if (value != null || choices != null || label != null || enabled.HasValue)
            {
                props = VNodePool.RentProps();
                props.FieldValue = value;
                props.Text = label;
                props.Enabled = enabled;
                props.Choices = choices != null ? new ChoicesSettings(choices) : null;
            }
            props = WithAttributes(props, data, aria);

            return new ElementNode
            {
                Key = key,
                ElementType = typeof(DropdownField),
                Name = name,
                ClassNames = ParseClassNames(className),
                Props = props,
                Children = EmptyChildren,
                Events = events,
                RefCallback = refCallback,
                WhileHoverClass = whileHoverClass,
                WhileTapClass = whileTapClass,
                WhileFocusClass = whileFocusClass,
            };
        }

        /// <summary>
        /// Creates a ListView (virtualized scrollable list).
        /// </summary>
        /// <param name="className">CSS-like utility class string. Multiple classes separated by spaces.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <param name="name">Element name assigned to <see cref="VisualElement.name"/> for query/debug.</param>
        /// <param name="enabled">When false, disables user interaction with the list.</param>
        /// <param name="styles">Inline style overrides applied on top of USS classes.</param>
        /// <param name="refCallback">Callback invoked on mount with the created VisualElement; returned Action runs on unmount.</param>
        /// <param name="whileHoverClass">USS class toggled while the pointer hovers the element.</param>
        /// <param name="whileTapClass">USS class toggled while the pointer is pressed on the element.</param>
        /// <param name="whileFocusClass">USS class toggled while the element holds keyboard/UI focus.</param>
        /// <param name="data">data-* attribute map matched by <c>data-[...]</c> variants.</param>
        /// <param name="aria">aria-* attribute map matched by <c>aria-[...]</c> variants.</param>
        /// <returns>The created <see cref="ElementNode"/> representing this list view.</returns>
        public static ElementNode ListView(
            string? className = null,
            string? key = null,
            string? name = null,
            bool? enabled = null,
            StyleOverrides? styles = null,
            Func<VisualElement, Action>? refCallback = null,
            string? whileHoverClass = null,
            string? whileTapClass = null,
            string? whileFocusClass = null,
            IReadOnlyDictionary<string, string>? data = null,
            IReadOnlyDictionary<string, string>? aria = null)
        {
            FiberElementProps? props = null;
            if (enabled.HasValue)
            {
                props = VNodePool.RentProps();
                props.Enabled = enabled;
            }
            props = WithAttributes(props, data, aria);

            return new ElementNode
            {
                Key = key,
                ElementType = typeof(ListView),
                Name = name,
                ClassNames = ParseClassNames(className),
                Props = props,
                Styles = styles,
                Children = EmptyChildren,
                Events = EmptyEvents,
                RefCallback = refCallback,
                WhileHoverClass = whileHoverClass,
                WhileTapClass = whileTapClass,
                WhileFocusClass = whileFocusClass,
            };
        }

        /// <summary>
        /// Creates a RadioButton. An individual radio button used inside a RadioButtonGroup.
        /// </summary>
        /// <param name="className">CSS-like utility class string. Multiple classes separated by spaces.</param>
        /// <param name="value">Current selected state (controlled).</param>
        /// <param name="onValueChanged">Handler invoked when the radio button selection state changes.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <param name="name">Element name assigned to <see cref="VisualElement.name"/> for query/debug.</param>
        /// <param name="label">Label text shown next to the radio button.</param>
        /// <param name="enabled">When false, disables user interaction with the radio button.</param>
        /// <param name="refCallback">Callback invoked on mount with the created VisualElement; returned Action runs on unmount.</param>
        /// <param name="whileHoverClass">USS class toggled while the pointer hovers the element.</param>
        /// <param name="whileTapClass">USS class toggled while the pointer is pressed on the element.</param>
        /// <param name="whileFocusClass">USS class toggled while the element holds keyboard/UI focus.</param>
        /// <param name="data">data-* attribute map matched by <c>data-[...]</c> variants.</param>
        /// <param name="aria">aria-* attribute map matched by <c>aria-[...]</c> variants.</param>
        /// <returns>The created <see cref="ElementNode"/> representing this radio button.</returns>
        public static ElementNode RadioButton(
            string? className = null,
            bool? value = null,
            Action<bool>? onValueChanged = null,
            string? key = null,
            string? name = null,
            string? label = null,
            bool? enabled = null,
            Func<VisualElement, Action>? refCallback = null,
            string? whileHoverClass = null,
            string? whileTapClass = null,
            string? whileFocusClass = null,
            IReadOnlyDictionary<string, string>? data = null,
            IReadOnlyDictionary<string, string>? aria = null)
        {
            var events = SingleEvent(onValueChanged != null ? new ChangeEventBinding<bool> { Handler = onValueChanged } : null);

            FiberElementProps? props = null;
            if (value.HasValue || label != null || enabled.HasValue)
            {
                props = VNodePool.RentProps();
                props.FieldValue = value;
                props.Text = label;
                props.Enabled = enabled;
            }
            props = WithAttributes(props, data, aria);

            return new ElementNode
            {
                Key = key,
                ElementType = typeof(RadioButton),
                Name = name,
                ClassNames = ParseClassNames(className),
                Props = props,
                Children = EmptyChildren,
                Events = events,
                RefCallback = refCallback,
                WhileHoverClass = whileHoverClass,
                WhileTapClass = whileTapClass,
                WhileFocusClass = whileFocusClass,
            };
        }

        /// <summary>
        /// Creates a RadioButtonGroup that materializes a set of radio buttons from a list of choices.
        /// </summary>
        /// <param name="className">CSS-like utility class string. Multiple classes separated by spaces.</param>
        /// <param name="value">Selected index within <paramref name="choices"/> (controlled).</param>
        /// <param name="choices">List of label strings, one per radio button.</param>
        /// <param name="onValueChanged">Handler invoked with the new selected index when the choice changes.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <param name="name">Element name assigned to <see cref="VisualElement.name"/> for query/debug.</param>
        /// <param name="label">Group-level label text.</param>
        /// <param name="enabled">When false, disables user interaction with the entire group.</param>
        /// <param name="refCallback">Callback invoked on mount with the created VisualElement; returned Action runs on unmount.</param>
        /// <param name="whileHoverClass">USS class toggled while the pointer hovers the element.</param>
        /// <param name="whileTapClass">USS class toggled while the pointer is pressed on the element.</param>
        /// <param name="whileFocusClass">USS class toggled while the element holds keyboard/UI focus.</param>
        /// <param name="data">data-* attribute map matched by <c>data-[...]</c> variants.</param>
        /// <param name="aria">aria-* attribute map matched by <c>aria-[...]</c> variants.</param>
        /// <returns>The created <see cref="ElementNode"/> representing this radio button group.</returns>
        public static ElementNode RadioButtonGroup(
            string? className = null,
            int? value = null,
            List<string>? choices = null,
            Action<int>? onValueChanged = null,
            string? key = null,
            string? name = null,
            string? label = null,
            bool? enabled = null,
            Func<VisualElement, Action>? refCallback = null,
            string? whileHoverClass = null,
            string? whileTapClass = null,
            string? whileFocusClass = null,
            IReadOnlyDictionary<string, string>? data = null,
            IReadOnlyDictionary<string, string>? aria = null)
        {
            var events = SingleEvent(onValueChanged != null ? new ChangeEventBinding<int> { Handler = onValueChanged } : null);

            FiberElementProps? props = null;
            if (value.HasValue || choices != null || label != null || enabled.HasValue)
            {
                props = VNodePool.RentProps();
                props.FieldValue = value;
                props.Text = label;
                props.Enabled = enabled;
                props.Choices = choices != null ? new ChoicesSettings(choices) : null;
            }
            props = WithAttributes(props, data, aria);

            return new ElementNode
            {
                Key = key,
                ElementType = typeof(RadioButtonGroup),
                Name = name,
                ClassNames = ParseClassNames(className),
                Props = props,
                Children = EmptyChildren,
                Events = events,
                RefCallback = refCallback,
                WhileHoverClass = whileHoverClass,
                WhileTapClass = whileTapClass,
                WhileFocusClass = whileFocusClass,
            };
        }

        /// <summary>
        /// Creates an IntegerField for entering integer values.
        /// </summary>
        /// <param name="className">CSS-like utility class string. Multiple classes separated by spaces.</param>
        /// <param name="value">Current integer value (controlled).</param>
        /// <param name="onValueChanged">Handler invoked when the integer value changes.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <param name="name">Element name assigned to <see cref="VisualElement.name"/> for query/debug.</param>
        /// <param name="label">Label text shown next to the field.</param>
        /// <param name="enabled">When false, disables user input.</param>
        /// <param name="refCallback">Callback invoked on mount with the created VisualElement; returned Action runs on unmount.</param>
        /// <param name="whileHoverClass">USS class toggled while the pointer hovers the element.</param>
        /// <param name="whileTapClass">USS class toggled while the pointer is pressed on the element.</param>
        /// <param name="whileFocusClass">USS class toggled while the element holds keyboard/UI focus.</param>
        /// <param name="data">data-* attribute map matched by <c>data-[...]</c> variants.</param>
        /// <param name="aria">aria-* attribute map matched by <c>aria-[...]</c> variants.</param>
        /// <returns>The created <see cref="ElementNode"/> representing this integer field.</returns>
        public static ElementNode IntegerField(
            string? className = null,
            int? value = null,
            Action<int>? onValueChanged = null,
            string? key = null,
            string? name = null,
            string? label = null,
            bool? enabled = null,
            Func<VisualElement, Action>? refCallback = null,
            string? whileHoverClass = null,
            string? whileTapClass = null,
            string? whileFocusClass = null,
            IReadOnlyDictionary<string, string>? data = null,
            IReadOnlyDictionary<string, string>? aria = null)
        {
            var events = SingleEvent(onValueChanged != null ? new ChangeEventBinding<int> { Handler = onValueChanged } : null);

            FiberElementProps? props = null;
            if (value.HasValue || label != null || enabled.HasValue)
            {
                props = VNodePool.RentProps();
                props.FieldValue = value;
                props.Text = label;
                props.Enabled = enabled;
            }
            props = WithAttributes(props, data, aria);

            return new ElementNode
            {
                Key = key,
                ElementType = typeof(IntegerField),
                Name = name,
                ClassNames = ParseClassNames(className),
                Props = props,
                Children = EmptyChildren,
                Events = events,
                RefCallback = refCallback,
                WhileHoverClass = whileHoverClass,
                WhileTapClass = whileTapClass,
                WhileFocusClass = whileFocusClass,
            };
        }

        /// <summary>
        /// Creates a text-only node. Materialized as a Label.
        /// </summary>
        /// <param name="text">Text content to display. Treated as empty when null.</param>
        /// <returns>The created <see cref="TextNode"/>.</returns>
        public static TextNode Text(string? text) => new() { Text = text ?? string.Empty };

        #endregion

        #region Lists

        /// <summary>
        /// Builds a keyed VNode list from a collection by mapping each item to a VNode and
        /// attaching a stable per-item key.
        /// If <paramref name="renderer"/> returns null for an item, the slot is included in the array
        /// but renders nothing: the reconciler's inline expansion drops it.
        /// </summary>
        /// <typeparam name="T">Element type of the source collection.</typeparam>
        /// <param name="items">Source collection. When null or empty, returns an empty VNode array.</param>
        /// <param name="keySelector">Selector that derives a stable per-item key.</param>
        /// <param name="renderer">Function that produces a VNode for each item.</param>
        /// <returns>Array of rendered VNodes (each carrying the selected key).</returns>
        public static VNode?[] List<T>(
            IReadOnlyList<T> items,
            Func<T, string> keySelector,
            Func<T, VNode> renderer)
        {
            if (items == null || items.Count == 0)
            {
                return EmptyChildren;
            }

            var result = VNodePool.RentNodeArray(items.Count);
            for (var i = 0; i < items.Count; i++)
            {
                var node = renderer(items[i]);
                if (node != null)
                {
                    // The selector key is authoritative: it overrides any key the renderer set on the
                    // node, so the list-mapping site owns the identity used for reconciliation.
                    node.Key = keySelector(items[i]);
                }
                result[i] = node;
            }

            return result;
        }

        /// <summary>
        /// Builds a keyed VNode list from an indexed collection, mapping each item together with
        /// its index to a VNode.
        /// </summary>
        /// <typeparam name="T">Element type of the source collection.</typeparam>
        /// <param name="items">Source collection. When null or empty, returns an empty VNode array.</param>
        /// <param name="keySelector">Selector that derives a stable per-item key from the item and its index.</param>
        /// <param name="renderer">Function that produces a VNode from the item and its index.</param>
        /// <returns>Array of rendered VNodes (each carrying the selected key).</returns>
        public static VNode?[] List<T>(
            IReadOnlyList<T> items,
            Func<T, int, string> keySelector,
            Func<T, int, VNode> renderer)
        {
            if (items == null || items.Count == 0)
            {
                return EmptyChildren;
            }

            var result = VNodePool.RentNodeArray(items.Count);
            for (var i = 0; i < items.Count; i++)
            {
                var node = renderer(items[i], i);
                if (node != null)
                {
                    // The selector key is authoritative: it overrides any key the renderer set on the
                    // node, so the list-mapping site owns the identity used for reconciliation.
                    node.Key = keySelector(items[i], i);
                }
                result[i] = node;
            }

            return result;
        }

        /// <summary>
        /// Sibling-friendly variant of <see cref="List{T}(IReadOnlyList{T}, Func{T, string}, Func{T, VNode})"/>
        /// that wraps the mapped nodes in a single <see cref="FragmentNode"/>. Because a Fragment is a VNode
        /// that the reconciler expands inline, the result can sit among sibling nodes in one children list
        /// (e.g. <c>V.Div("c", header, V.ListFragment(...), footer)</c>) without an extra wrapper element.
        /// </summary>
        /// <typeparam name="T">Element type of the source collection.</typeparam>
        /// <param name="items">Source collection. When null or empty, yields an empty Fragment.</param>
        /// <param name="keySelector">Selector that derives a stable per-item key.</param>
        /// <param name="renderer">Function that produces a VNode for each item.</param>
        /// <param name="key">Optional key disambiguating this Fragment from siblings at the same position.</param>
        /// <returns>A <see cref="FragmentNode"/> wrapping the rendered VNodes.</returns>
        public static FragmentNode ListFragment<T>(
            IReadOnlyList<T> items,
            Func<T, string> keySelector,
            Func<T, VNode> renderer,
            string? key = null) =>
            Fragment(List(items, keySelector, renderer), key);

        /// <summary>
        /// Sibling-friendly variant of <see cref="List{T}(IReadOnlyList{T}, Func{T, int, string}, Func{T, int, VNode})"/>
        /// that wraps the mapped nodes in a single <see cref="FragmentNode"/> so the result can sit inline
        /// among sibling nodes in one children list.
        /// </summary>
        /// <typeparam name="T">Element type of the source collection.</typeparam>
        /// <param name="items">Source collection. When null or empty, yields an empty Fragment.</param>
        /// <param name="keySelector">Selector that derives a stable per-item key from the item and its index.</param>
        /// <param name="renderer">Function that produces a VNode from the item and its index.</param>
        /// <param name="key">Optional key disambiguating this Fragment from siblings at the same position.</param>
        /// <returns>A <see cref="FragmentNode"/> wrapping the rendered VNodes.</returns>
        public static FragmentNode ListFragment<T>(
            IReadOnlyList<T> items,
            Func<T, int, string> keySelector,
            Func<T, int, VNode> renderer,
            string? key = null) =>
            Fragment(List(items, keySelector, renderer), key);

        #endregion

        #region Control flow

        /// <summary>
        /// Conditional rendering. Returns null when <paramref name="condition"/> is false
        /// (handled by the Reconciler's null filter).
        /// </summary>
        /// <param name="condition">When true, <paramref name="factory"/> is invoked and its result is returned.</param>
        /// <param name="factory">Factory invoked only when <paramref name="condition"/> is true.</param>
        /// <returns>The VNode produced by <paramref name="factory"/>, or null when <paramref name="condition"/> is false.</returns>
        public static VNode? When(bool condition, Func<VNode>? factory)
        {
            if (!condition) return null;
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            return factory();
        }

        /// <summary>
        /// Embeds a function-style component (`[Component] static VNode Foo()`) into the VNode tree
        /// as a child node. Props are read from Stores / Context via hooks,
        /// so passing state/props through method arguments is not the supported pattern.
        /// </summary>
        /// <param name="body">Delegate of a static method annotated with <c>[Component]</c> (e.g. <c>FooComp.Render</c>).</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <returns>The created <see cref="ComponentNode"/> embedding the function-style component.</returns>
        public static ComponentNode Component(Func<VNode>? body, string? key = null)
            => CreateComponent(body, externalRef: null, key);

        /// <summary>
        /// Embeds a function-style component with parent-to-child ref forwarding.
        /// The child retrieves the ref via <c>Hooks.ForwardedRef&lt;THandle&gt;()</c>
        /// and exposes it through <c>Hooks.UseImperativeHandle</c>.
        /// </summary>
        /// <typeparam name="TRef">Handle type that the parent receives via <see cref="Ref{TRef}"/>.</typeparam>
        /// <param name="body">Delegate of a static method annotated with <c>[Component]</c>.</param>
        /// <param name="componentRef">The <see cref="Ref{TRef}"/> used for forwarding. Must not be null (use the refless overload when no ref is needed).</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <returns>The created <see cref="ComponentNode"/> with the parent-to-child ref wired through <paramref name="componentRef"/>.</returns>
        public static ComponentNode Component<TRef>(
            Func<VNode>? body,
            Ref<TRef> componentRef,
            string? key = null) where TRef : class
        {
            if (componentRef == null) throw new ArgumentNullException(nameof(componentRef));
            return CreateComponent(body, componentRef, key);
        }

        /// <summary>
        /// Embeds a function-style component that receives a single <typeparamref name="TProps"/>
        /// argument carrying the per-instance values (e.g. an item id plus a click handler).
        /// Use this overload for V.List iteration / per-item callbacks where Context-based prop
        /// distribution would require allocating a Provider node per item.
        /// </summary>
        /// <remarks>
        /// Each parent render allocates a closure (DisplayClass + delegate) capturing
        /// <paramref name="props"/>. The fiber is reused across renders via <c>body.Method</c>
        /// identity, and child hooks (<c>UseCallback</c> / <c>UseMemo</c>) can declare
        /// <paramref name="props"/> fields as <c>deps</c> to stabilize callbacks across renders.
        /// <br/>
        /// <typeparamref name="TProps"/> is stored as <see cref="object"/> on the fiber. Prefer a
        /// reference type (<c>sealed record</c>): a <c>record struct</c> boxes on every
        /// <c>V.Component</c> call. Whether that stored value is compared at all, and under which
        /// rule, is what <see cref="ComponentAttribute.Memoize"/> states.
        /// </remarks>
        /// <typeparam name="TProps">Props type. Use <c>sealed record</c> (reference type) to avoid boxing.</typeparam>
        /// <param name="body">Delegate of a static method annotated with <c>[Component]</c> taking a single <typeparamref name="TProps"/> parameter.</param>
        /// <param name="props">The props value to pass to <paramref name="body"/>.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <returns>The created <see cref="ComponentNode"/>.</returns>
        public static ComponentNode Component<TProps>(
            Func<TProps, VNode> body,
            TProps props,
            string? key = null)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            Func<VNode> wrapped = () => body(props);
            return CreateComponentNode(wrapped, body.Method, props, areEqual: null, externalRef: null, key);
        }

        /// <summary>
        /// Memoizes a component with a custom <c>areEqual</c> comparator. Embeds a props-receiving function
        /// component (the same shape as <see cref="Component{TProps}(Func{TProps, VNode}, TProps, string)"/>)
        /// but supplies an explicit <paramref name="areEqual"/> predicate that decides whether a parent
        /// re-render bails this component.
        /// </summary>
        /// <remarks>
        /// <paramref name="areEqual"/> receives the previous and next props and returns <c>true</c> to
        /// <b>bail</b> (skip re-render). When the
        /// default shallow comparison <see cref="ComponentAttribute.Memoize"/> states is sufficient, prefer plain
        /// <c>V.Component(body, props)</c> with <c>[Component(Memoize = true)]</c>; this overload is for
        /// the cases where shallow equality is too coarse or too fine (e.g. comparing only a subset of
        /// props, or deep-comparing one field).<br/>
        /// Attributes cannot carry delegates, so the comparator is supplied here at the call site rather
        /// than on <c>[Component]</c>.
        /// <para>
        /// Argument order note: <paramref name="areEqual"/> takes <c>(previous, next)</c>. This is the
        /// reverse of <see cref="Store{TState}.Subscribe(Action{TState, TState}, bool)"/> and
        /// <see cref="Store{TState}.Select{T}"/>, whose listener/observer callbacks take
        /// <c>(current, previous)</c> — the two areas settled on opposite conventions, so check the
        /// parameter names at the call site rather than assuming one order.
        /// </para>
        /// </remarks>
        /// <typeparam name="TProps">Props type. Use <c>sealed record</c> (reference type) to avoid boxing.</typeparam>
        /// <param name="body">Delegate of a static method annotated with <c>[Component]</c> taking a single <typeparamref name="TProps"/> parameter.</param>
        /// <param name="props">The props value to pass to <paramref name="body"/>.</param>
        /// <param name="areEqual">Predicate comparing previous and next props; returns <c>true</c> to bail the re-render. Must not be null (use the propless <c>V.Component</c> overload when no props comparison is needed).</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <returns>The created <see cref="ComponentNode"/> carrying the custom comparator.</returns>
        public static ComponentNode Memo<TProps>(
            Func<TProps, VNode> body,
            TProps props,
            Func<TProps, TProps, bool> areEqual,
            string? key = null)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (areEqual == null) throw new ArgumentNullException(nameof(areEqual));
            Func<VNode> wrapped = () => body(props);
            // Adapt the typed predicate to the object-based comparison the registry uses. Same-reference
            // and null cases short-circuit before the cast so areEqual only sees real TProps instances.
            Func<object?, object?, bool> adapted = (prev, next) =>
            {
                if (ReferenceEquals(prev, next)) return true;
                if (prev is null || next is null) return false;
                return areEqual((TProps)prev, (TProps)next);
            };
            // V.Memo carries an explicit comparator, so it is memoized by construction (forceMemoize); the
            // bail gate also treats AreEqual != null as memoized.
            return CreateComponentNode(wrapped, body.Method, props, adapted, externalRef: null, key, forceMemoize: true);
        }

        /// <summary>
        /// Helper that wraps <paramref name="children"/> in an inline Error Boundary with the given fallback.
        /// Catches exceptions thrown during render of the child tree (including rethrows from pending Suspense
        /// resources via <c>Hooks.Use</c>) and renders the VNode produced by <paramref name="fallback"/> instead.
        /// Useful for reducing boilerplate where introducing a dedicated <c>[Component(IsErrorBoundary = true)]</c>
        /// wrapper class would be overkill (e.g. a root boundary directly under Mount).
        /// </summary>
        /// <remarks>
        /// When placing multiple <c>V.ErrorBoundary</c> instances at the same position, always supply
        /// <paramref name="key"/> to avoid identity collisions in the reconciler. The helper's lambda body
        /// has the same MethodInfo on every call, so siblings cannot be distinguished without a key.<br/>
        /// <paramref name="fallback"/> and <paramref name="children"/> are **captured into the closure on the
        /// initial mount**; the same fiber is reused on parent re-renders so subsequent value changes are
        /// not reflected. For dynamic cases, read values via Context / Hooks, or use the function-style
        /// pattern with <c>[Component(IsErrorBoundary = true)]</c> + <c>Hooks.UseFallback</c>.<br/>
        /// Each invocation allocates a closure (DisplayClass + delegate). The helper is intended for
        /// root boundaries directly under Mount; calling it repeatedly inside a render function will
        /// allocate on every render.
        /// </remarks>
        /// <param name="fallback">Factory that receives the caught exception and returns the fallback VNode.</param>
        /// <param name="children">Child nodes rendered in the normal (non-error) path.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <returns>The created <see cref="ComponentNode"/> wrapping <paramref name="children"/> in an Error Boundary.</returns>
        public static ComponentNode ErrorBoundary(
            Func<Exception, VNode> fallback,
            VNode?[] children,
            string? key = null)
        {
            if (fallback == null) throw new ArgumentNullException(nameof(fallback));
            if (children == null) throw new ArgumentNullException(nameof(children));

            Func<VNode> body = () =>
            {
                Hooks.UseFallback(fallback);
                return Fragment(children);
            };

            return CreateComponent(body, externalRef: null, key, forceErrorBoundary: true);
        }

        private static ComponentNode CreateComponent(
            Func<VNode>? body,
            IHookRefSetter? externalRef,
            string? key,
            bool forceErrorBoundary = false)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            return CreateComponentNode(body, body.Method, props: null, areEqual: null, externalRef, key, forceErrorBoundary);
        }

        // Single construction site for every ComponentNode the V.* component factories build. body is the
        // (possibly wrapped) render delegate; identity is the ORIGINAL [Component] method that keys the fiber
        // across renders (for the props overloads body is a fresh per-call closure, so identity must be the
        // underlying method, not body.Method). forceMemoize is set by V.Memo (an explicit comparator implies
        // memoization); otherwise Memoize / IsErrorBoundary derive from the method's [Component] attributes.
        private static ComponentNode CreateComponentNode(
            Func<VNode>? body,
            MethodInfo? identity,
            object? props,
            Func<object?, object?, bool>? areEqual,
            IHookRefSetter? externalRef,
            string? key,
            bool forceErrorBoundary = false,
            bool forceMemoize = false)
            => new ComponentNode
            {
                Body = body,
                Identity = identity,
                Props = props,
                AreEqual = areEqual,
                Memoize = forceMemoize || ComponentMethodRegistry.IsMemoized(identity),
                ExternalRef = externalRef,
                IsErrorBoundary = forceErrorBoundary || ComponentMethodRegistry.IsErrorBoundary(identity),
                Key = key,
            };

        /// <summary>
        /// Memoization node. Skips rebuilding the child subtree while the dependency array is unchanged.
        /// When <c>key</c> is omitted, the order of MemoNodes within the same component must remain stable,
        /// since identity is resolved by call order. If the order can change dynamically, use
        /// <see cref="MemoizedWithKey(string, Func{VNode}, object[])"/> instead. This is distinct from
        /// <see cref="Memo{TProps}"/>, which
        /// memoizes a function-style component by props equality.
        /// </summary>
        /// <param name="factory">Factory invoked to produce the cached VNode when <paramref name="deps"/> change.</param>
        /// <param name="deps">Dependency values. When equal to the previous render, the cached VNode is reused; <see cref="MemoNode.Dependencies"/> states the branch each element type takes. Pass an empty array to declare no dependencies and cache the subtree for the node's whole life; null declares no dependency array, which no newly built node's comparison can satisfy.</param>
        /// <returns>The created <see cref="MemoNode"/>.</returns>
        public static MemoNode Memoized(Func<VNode> factory, params object?[]? deps)
        {
            return new MemoNode
            {
                Factory = factory,
                Dependencies = deps,
            };
        }

        /// <summary>
        /// Declares no dependency array, for a factory whose inputs are not expressible as one: a newly built
        /// node never reuses the cached subtree, so a call site inside a render body rebuilds every render.
        /// </summary>
        /// <remarks>
        /// The single-argument overload exists so that omitting deps is unambiguous — see
        /// <see cref="Hooks.UseCallback{T}(T)"/> for the hazard it avoids. To cache the subtree for the node's
        /// whole life instead, pass an empty array.
        /// </remarks>
        /// <param name="factory">Factory invoked on every reconcile to produce the subtree.</param>
        /// <returns>The created <see cref="MemoNode"/>.</returns>
        public static MemoNode Memoized(Func<VNode> factory)
        {
            return new MemoNode
            {
                Factory = factory,
                Dependencies = null,
            };
        }

        // Memoized<T1..T8> / MemoizedWithKey<T1..T8> are auto-generated by Velvet.SourceGenerators
        // (Runtime/Plugins/Generators/Velvet.SourceGenerators.dll, V.Memoized.g.cs).

        /// <summary>
        /// Keyed memoization node. Provides a stable cache keyed by the supplied <paramref name="key"/>.
        /// </summary>
        /// <param name="key">Stable cache key independent of sibling order.</param>
        /// <param name="factory">Factory invoked to produce the cached VNode when <paramref name="deps"/> change.</param>
        /// <param name="deps">Dependency values used to detect changes, with the same empty-array and null meanings as <see cref="Memoized(Func{VNode}, object[])"/>.</param>
        /// <returns>The created <see cref="MemoNode"/>.</returns>
        public static MemoNode MemoizedWithKey(string? key, Func<VNode> factory, params object?[]? deps)
        {
            return new MemoNode
            {
                Key = key,
                Factory = factory,
                Dependencies = deps,
            };
        }

        /// <summary>
        /// Keyed variant of <see cref="Memoized(Func{VNode})"/>: declares no dependency array, under an
        /// explicit cache key.
        /// </summary>
        /// <param name="key">Stable cache key independent of sibling order.</param>
        /// <param name="factory">Factory invoked on every reconcile to produce the subtree.</param>
        /// <returns>The created <see cref="MemoNode"/>.</returns>
        public static MemoNode MemoizedWithKey(string? key, Func<VNode> factory)
        {
            return new MemoNode
            {
                Key = key,
                Factory = factory,
                Dependencies = null,
            };
        }

        #endregion

        #region Tree structure

        /// <summary>
        /// Provides a context value to the descendant subtree, visible to descendants that read the
        /// same context via <c>Hooks.UseContext</c>.
        /// </summary>
        /// <remarks>
        /// A value change reaches consumers by comparing this provider against the one that held the same
        /// position last render, and an unkeyed provider is identified by its own sibling index. That covers
        /// the ordinary cases, including a conditional sibling rendered as <c>null</c>, which keeps its slot.
        /// Give the provider an explicit <paramref name="key"/> when the index itself can move — it follows a
        /// variable number of siblings, or is appended after a mapped list — so it stays identified across
        /// the shift. The key pins the provider's own place among its siblings, not the path above it: if an
        /// unkeyed fragment or component enclosing it is what moves, key the moving ancestor too.
        /// </remarks>
        /// <typeparam name="T">Context value type.</typeparam>
        /// <param name="context">Context object whose value is being provided.</param>
        /// <param name="value">Value visible to descendants via <c>Hooks.UseContext(context)</c>.</param>
        /// <param name="children">Descendant VNodes that observe this provider.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <returns>The created <see cref="ContextProviderNode{T}"/>.</returns>
        public static ContextProviderNode<T> Provider<T>(
            ComponentContext<T> context,
            T value,
            VNode?[]? children = null,
            string? key = null)
        {
            return new ContextProviderNode<T>
            {
                Key = key,
                Context = context,
                Value = value,
                Children = children ?? EmptyChildren,
            };
        }

        /// <summary>
        /// Renders <paramref name="children"/> into the Portal target identified by <paramref name="targetId"/>,
        /// detaching them from the surrounding DOM position so they mount under a different host element.
        /// <paramref name="targetId"/> must reference an ID previously registered via <c>FiberPortalRegistry.Register</c>.
        /// </summary>
        /// <remarks>
        /// Context inheritance and event bubbling both follow the LOGICAL tree. Children inherit the
        /// context enclosing the V.Portal call site, and an <c>events:</c> handler on a logical
        /// ancestor of the call site also fires for a pointer / key / focus event raised on a portal
        /// child: Velvet physically reparents the children under the registered target element (so UI
        /// Toolkit's own native dispatch bubbles them up the target's PHYSICAL ancestor chain too),
        /// then separately bridges the event synthetically to the logical ancestor chain outside the
        /// call site. An element that happens to sit on BOTH chains — a physical ancestor of the
        /// target AND a logical ancestor of the call site — still fires exactly once: the synthetic
        /// walk detects that native bubbling already covers it and stops there rather than
        /// double-firing. <c>Button</c>'s native click
        /// (<c>ClickedBinding</c>) and field value-change (<c>ChangeEventBinding&lt;T&gt;</c>) stay
        /// physical-tree-only in every portal form — neither has an underlying bubbling event object to
        /// carry across a logical boundary. See the portals documentation for the full contract.
        /// </remarks>
        /// <param name="targetId">Portal target ID registered via <c>FiberPortalRegistry.Register</c>.</param>
        /// <param name="children">Descendant VNodes mounted into the resolved portal target.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <returns>The created <see cref="PortalNode"/>.</returns>
        public static PortalNode Portal(string targetId, VNode?[]? children = null, string? key = null)
        {
            return new PortalNode
            {
                Key = key,
                TargetId = targetId,
                Children = children ?? EmptyChildren,
            };
        }

        /// <summary>
        /// Renders <paramref name="children"/> into <paramref name="target"/> — a container the caller
        /// already holds, rather than one published under a name. The React form:
        /// <c>createPortal(children, container)</c> takes the node itself, so two trees in one process
        /// cannot collide the way two registrations of one id do, and an element reached through a
        /// <c>refCallback</c> is a valid container without being named first.
        /// </summary>
        /// <remarks>
        /// Passing a different container on a later render moves the children: the reconciler cannot
        /// patch one container's portal into another's, so the old unmounts and the new mounts. A
        /// registry target behaves differently — its id resolves once at mount and is then held, so
        /// re-registering the id points only future portals elsewhere. The portals documentation
        /// states that difference; the rest of the contract (context inheritance, event bubbling) is
        /// the same as the <see cref="Portal(string, VNode?[], string)"/> form.
        /// </remarks>
        /// <param name="target">Container the children attach to. Null renders nothing and warns.</param>
        /// <param name="children">Nodes to render at the target.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <returns>The created <see cref="PortalNode"/>.</returns>
        public static PortalNode Portal(VisualElement target, VNode?[]? children = null, string? key = null)
        {
            return new PortalNode
            {
                Key = key,
                TargetElement = target,
                Children = children ?? EmptyChildren,
            };
        }

        /// <summary>
        /// Renders <paramref name="children"/> into a framework-managed screen-space layer panel
        /// sorted around the app's main panel — one host panel per layer per reconciler, created
        /// lazily and destroyed with the reconciler. Like every portal, the children stay part of the
        /// LOGICAL tree: context and state cross the boundary, and an <c>events:</c> handler on a
        /// logical ancestor of the call site also fires for a pointer / key / focus event raised on a
        /// child, bridged synthetically across the separate host Panel. Relational
        /// <c>group-</c>/<c>peer-</c> variants and focus-within do NOT cross (they register their own
        /// native callbacks directly, bypassing the bridge), and responsive breakpoints evaluate
        /// against the layer panel's own width. Screen-space layers always composite over the 3D
        /// scene; UI that must sit among scene geometry is <see cref="WorldSpace"/>'s territory.
        /// </summary>
        /// <param name="layer">The framework-managed layer panel to attach the children to.</param>
        /// <param name="children">Descendant VNodes mounted into the layer panel.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="layer"/> names no member of <see cref="UILayer"/>,
        /// or <paramref name="focusOrder"/> names no member of <see cref="PanelFocusOrder"/>.</exception>
        /// <returns>The created <see cref="PortalNode"/>.</returns>
        public static PortalNode Portal(UILayer layer, VNode?[]? children = null, string? key = null,
            PanelFocusOrder focusOrder = PanelFocusOrder.Isolated)
        {
            if (layer is not (UILayer.Background or UILayer.Overlay or UILayer.Topmost))
            {
                throw new ArgumentOutOfRangeException(nameof(layer), layer,
                    "V.Portal takes a member of UILayer as its layer.");
            }
            if (focusOrder is not (PanelFocusOrder.Isolated or PanelFocusOrder.Chained))
            {
                throw new ArgumentOutOfRangeException(nameof(focusOrder), focusOrder,
                    "V.Portal takes a member of PanelFocusOrder as its focusOrder.");
            }
            return new PortalNode
            {
                Key = key,
                Layer = layer,
                FocusOrder = focusOrder,
                Children = children ?? EmptyChildren,
            };
        }

        /// <summary>
        /// Renders <paramref name="children"/> into a framework-owned world-space panel positioned by
        /// a scene transform — UI that lives among 3D content and is depth-tested against it, unlike
        /// the always-on-top screen-space layers. The host
        /// (GameObject + world-space panel) is created on mount, follows <paramref name="position"/> /
        /// <paramref name="rotation"/> updates, and is destroyed on unmount. Children stay part of the
        /// logical tree: context and state cross, and an <c>events:</c> handler on a logical ancestor
        /// of the call site also fires for a pointer / key / focus event raised on a child, bridged
        /// synthetically across the separate host Panel. Velvet does not arbitrate world-space pointer
        /// DELIVERY itself the way it does screen-space layer picking order: a <c>BoxCollider</c> sized
        /// to the panel is attached to the host, and Unity's own runtime input system drives picking
        /// and delivery through it once a Play session is running (see the package's cross-panel input
        /// routing docs). The event bridging above applies to whatever that system delivers, as well as
        /// to a programmatically dispatched event (e.g. in tests).
        /// </summary>
        /// <param name="position">World position of the panel host.</param>
        /// <param name="rotation">World rotation of the panel host (identity when omitted).</param>
        /// <param name="panelSize">Virtual panel resolution in pixels (fixed world-space size mode).</param>
        /// <param name="children">Descendant VNodes rendered inside the world-space panel.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="focusOrder"/> names no member of
        /// <see cref="PanelFocusOrder"/>.</exception>
        /// <returns>The created <see cref="WorldSpaceNode"/>.</returns>
        public static WorldSpaceNode WorldSpace(
            Vector3 position,
            Quaternion? rotation = null,
            Vector2? panelSize = null,
            VNode?[]? children = null,
            string? key = null,
            PanelFocusOrder focusOrder = PanelFocusOrder.Isolated)
        {
            if (focusOrder is not (PanelFocusOrder.Isolated or PanelFocusOrder.Chained))
            {
                throw new ArgumentOutOfRangeException(nameof(focusOrder), focusOrder,
                    "V.WorldSpace takes a member of PanelFocusOrder as its focusOrder.");
            }
            return new WorldSpaceNode
            {
                Key = key,
                Position = position,
                Rotation = rotation ?? Quaternion.identity,
                PanelSize = panelSize ?? new Vector2(1920f, 1080f),
                FocusOrder = focusOrder,
                Children = children ?? EmptyChildren,
            };
        }

        /// <summary>
        /// Screen-space element that tracks a 3D scene Transform's projected position every frame (the
        /// default screen-space projection mode). This is ordinary 2D UI with no
        /// inherent scene depth (unlike <see cref="WorldSpace"/>, which renders content INTO the 3D scene and
        /// is occluded by it for free) — <paramref name="occlude"/> opts into an explicit physics stand-in for
        /// that test. Forces <c>position: absolute</c> inline (dynamic left/top positioning has no other way
        /// to work; see AnchoredDriver.Attach) — pass layout classes for everything else.
        /// </summary>
        /// <param name="target">The Transform this element's screen position tracks. Null (or a Transform
        /// destroyed later) mounts an inert, hidden (display: none) element until a live target is supplied —
        /// matching <see cref="SceneView"/>/<see cref="Particles"/>'s own null-tolerant convention, since a
        /// component holding a Transform in state can have it destroyed by unrelated game logic between
        /// renders.</param>
        /// <param name="camera">The camera to project through. Null resolves to <see cref="Camera.main"/> on
        /// every tick, so a scene's active camera can change without re-supplying this.</param>
        /// <param name="offset">Pixel offset applied after projection (e.g. to center a label on the point).</param>
        /// <param name="hideWhenBehindCamera">When true (default), the element is hidden (display: none)
        /// while <paramref name="target"/> is behind the camera rather than jumping to a wrong on-screen spot.</param>
        /// <param name="occlude">When true, a solid (non-trigger) collider between the camera and
        /// <paramref name="target"/> hides the element — an extra physics query every tick, so it is off by
        /// default rather than a standing cost every consumer pays. A target whose own collider sits on
        /// <paramref name="occludeLayerMask"/> will typically occlude itself; scope the mask to scene geometry
        /// that excludes it.</param>
        /// <param name="occludeLayerMask">Which colliders count as occluders when <paramref name="occlude"/>
        /// is true. Null (default) resolves to <see cref="Physics.DefaultRaycastLayers"/>.</param>
        /// <param name="distanceFactor">When set, scales the element by this value divided by its current
        /// distance to the camera, faking perspective size
        /// falloff for otherwise-flat screen-space content. Null (default) leaves the element unscaled and
        /// never touches <c>style.scale</c>, so it composes with a <c>scale-*</c> class or a Motion scale
        /// variant on the same element; a non-null value OWNS that style slot every tick instead and will
        /// fight either of those for it. Must be positive when supplied.</param>
        /// <returns>The created <see cref="ElementNode"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="distanceFactor"/> is &lt;= 0, NaN, or positive infinity.</exception>
        public static ElementNode Anchored(
            Transform? target,
            Camera? camera = null,
            Vector2? offset = null,
            bool hideWhenBehindCamera = true,
            bool occlude = false,
            LayerMask? occludeLayerMask = null,
            float? distanceFactor = null,
            string? className = null,
            string? key = null,
            string? name = null,
            FiberElementProps? props = null,
            StyleOverrides? styles = null,
            Func<VisualElement, Action>? refCallback = null,
            VNode?[]? children = null,
            string? whileHoverClass = null,
            string? whileTapClass = null,
            string? whileFocusClass = null,
            IReadOnlyDictionary<string, string>? data = null,
            IReadOnlyDictionary<string, string>? aria = null)
        {
            // Validated BEFORE renting pooled props so a throwing call leaks nothing (mirrors V.SceneView):
            // the settings constructor fail-fasts on an invalid distanceFactor for every construction path,
            // this factory included.
            var anchored = new AnchoredSettings(target, camera, offset ?? Vector2.zero, hideWhenBehindCamera,
                occlude, occludeLayerMask, distanceFactor);
            var mergedProps = WithAttributes(props, data, aria) ?? VNodePool.RentProps();
            mergedProps.Anchored = anchored;

            return new ElementNode
            {
                Key = key,
                ElementType = typeof(VisualElement),
                Name = name,
                ClassNames = ParseClassNames(className),
                Props = mergedProps,
                Styles = styles,
                Children = children ?? EmptyChildren,
                Events = EmptyEvents,
                RefCallback = refCallback,
                WhileHoverClass = whileHoverClass,
                WhileTapClass = whileTapClass,
                WhileFocusClass = whileFocusClass,
            };
        }

        /// <summary>
        /// Container element whose subtree is a focus scope. Deviation
        /// (documented): a renderless, sentinel-span scope is common elsewhere; Velvet's is a real VisualElement,
        /// because UI Toolkit containment needs a subtree root for the scoped focus ring and the
        /// membership test. Any existing container can be a scope via props
        /// (<see cref="FiberElementProps.FocusScope"/>) — this factory is convenience for when no
        /// container exists yet. Style it like any Div.
        /// </summary>
        /// <param name="contain">Tab/Shift-Tab wrap within the subtree; a 2D/pointer move that exits is
        /// snapped back within the same event flush (a press on empty space that clears focus to nothing
        /// re-focuses on the panel's next tick).</param>
        /// <param name="restoreFocus">On unmount while holding focus, refocus the element focus came from
        /// when it first entered the scope.</param>
        /// <param name="autoFocus">On mount (first attach only — never a keyed reorder's re-attach), focus
        /// the scope's first focusable descendant.</param>
        /// <param name="singleTabStop">The subtree behaves as one Tab stop (roving); engine 2D
        /// arrow/dpad navigation inside is untouched.</param>
        /// <returns>The created <see cref="ElementNode"/>.</returns>
        public static ElementNode FocusScope(
            string? className = null,
            string? key = null,
            string? name = null,
            bool contain = false,
            bool restoreFocus = false,
            bool autoFocus = false,
            bool singleTabStop = false,
            FiberElementProps? props = null,
            StyleOverrides? styles = null,
            Func<VisualElement, Action>? refCallback = null,
            VNode?[]? children = null,
            string? whileHoverClass = null,
            string? whileTapClass = null,
            string? whileFocusClass = null,
            IReadOnlyDictionary<string, string>? data = null,
            IReadOnlyDictionary<string, string>? aria = null)
        {
            var mergedProps = WithAttributes(props, data, aria) ?? VNodePool.RentProps();
            mergedProps.FocusScope = new FocusScopeSettings(contain, restoreFocus, autoFocus, singleTabStop);

            return new ElementNode
            {
                Key = key,
                ElementType = typeof(VisualElement),
                Name = name,
                ClassNames = ParseClassNames(className),
                Props = mergedProps,
                Styles = styles,
                Children = children ?? EmptyChildren,
                Events = EmptyEvents,
                RefCallback = refCallback,
                WhileHoverClass = whileHoverClass,
                WhileTapClass = whileTapClass,
                WhileFocusClass = whileFocusClass,
            };
        }

        /// <summary>
        /// Drag-and-drop scope. A real container element (the FocusScope
        /// precedent: the element is the stable scope identity and cleanup anchor). Draggables and
        /// droppables pair with their nearest ancestor scope at event time; one drag may be active per
        /// mounted tree at a time. Any existing container can be a scope via props
        /// (<see cref="FiberElementProps.DndContext"/>) — this factory is convenience.
        /// </summary>
        /// <param name="onDragStart">Fired once when a press crosses its activation constraint.</param>
        /// <param name="onDragOver">Fired when the winning drop target CHANGES (including to null).</param>
        /// <param name="onDragEnd">Fired on release; state written here flushes synchronously like any
        /// discrete input handler. Over is null when dropped on nothing.</param>
        /// <param name="onDragCancel">Fired when a drag aborts (Escape, pointer cancel, lost capture,
        /// or the source/scope unmounting mid-drag).</param>
        /// <param name="collisionDetection">Collision strategy; null means
        /// <see cref="DndCollisions.RectIntersection"/>.</param>
        /// <param name="activation">Scope-wide activation default; a per-draggable override wins.</param>
        /// <returns>The created <see cref="ElementNode"/>.</returns>
        public static ElementNode DndContext(
            Action<DragStartArgs>? onDragStart = null,
            Action<DragOverArgs>? onDragOver = null,
            Action<DragEndArgs>? onDragEnd = null,
            Action<DragCancelArgs>? onDragCancel = null,
            DndCollisionDetection? collisionDetection = null,
            DragActivation? activation = null,
            string? className = null,
            string? key = null,
            string? name = null,
            FiberElementProps? props = null,
            StyleOverrides? styles = null,
            Func<VisualElement, Action>? refCallback = null,
            VNode?[]? children = null,
            string? whileHoverClass = null,
            string? whileTapClass = null,
            string? whileFocusClass = null,
            IReadOnlyDictionary<string, string>? data = null,
            IReadOnlyDictionary<string, string>? aria = null)
        {
            var mergedProps = WithAttributes(props, data, aria) ?? VNodePool.RentProps();
            mergedProps.DndContext = new DndContextSettings(
                onDragStart, onDragOver, onDragEnd, onDragCancel, collisionDetection, activation);

            return new ElementNode
            {
                Key = key,
                ElementType = typeof(VisualElement),
                Name = name,
                ClassNames = ParseClassNames(className),
                Props = mergedProps,
                Styles = styles,
                Children = children ?? EmptyChildren,
                Events = EmptyEvents,
                RefCallback = refCallback,
                WhileHoverClass = whileHoverClass,
                WhileTapClass = whileTapClass,
                WhileFocusClass = whileFocusClass,
            };
        }

        /// <summary>
        /// Drag source. The element itself is the drag node; it must sit (at
        /// any depth) under a <see cref="DndContext"/> scope.
        /// </summary>
        /// <param name="id">Identity reported to the scope callbacks. An element that is both draggable
        /// and droppable under one id never collides with itself.</param>
        /// <param name="dragData">Arbitrary payload carried into the callbacks.</param>
        /// <param name="disabled">A disabled draggable never arms.</param>
        /// <param name="movement"><see cref="DragMovement.Translate"/> (default) writes the pointer delta
        /// as an inline translate during the drag; <see cref="DragMovement.None"/> leaves the source in
        /// place (the V.DragOverlay ghost pattern).</param>
        /// <param name="activation">Per-draggable activation constraint; wins over the scope's.</param>
        /// <param name="whileDraggingClass">Classes applied while this element's drag is active — the
        /// zero-re-render isDragging channel.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="movement"/> names no member of
        /// <see cref="DragMovement"/>.</exception>
        /// <returns>The created <see cref="ElementNode"/>.</returns>
        public static ElementNode Draggable(
            string id,
            object? dragData = null,
            bool disabled = false,
            DragMovement movement = DragMovement.Translate,
            DragActivation? activation = null,
            string? whileDraggingClass = null,
            string? className = null,
            string? key = null,
            string? name = null,
            FiberElementProps? props = null,
            StyleOverrides? styles = null,
            Func<VisualElement, Action>? refCallback = null,
            VNode?[]? children = null,
            string? whileHoverClass = null,
            string? whileTapClass = null,
            string? whileFocusClass = null,
            IReadOnlyDictionary<string, string>? data = null,
            IReadOnlyDictionary<string, string>? aria = null)
        {
            // Above the rent below, so a refusal here strands no bag this factory rented.
            if (movement is not (DragMovement.Translate or DragMovement.None))
            {
                throw new ArgumentOutOfRangeException(nameof(movement), movement,
                    "V.Draggable takes a member of DragMovement as its movement.");
            }
            var mergedProps = WithAttributes(props, data, aria) ?? VNodePool.RentProps();
            mergedProps.Draggable = new DraggableSettings(
                id, dragData, disabled, movement, activation, whileDraggingClass);

            return new ElementNode
            {
                Key = key,
                ElementType = typeof(VisualElement),
                Name = name,
                ClassNames = ParseClassNames(className),
                Props = mergedProps,
                Styles = styles,
                Children = children ?? EmptyChildren,
                Events = EmptyEvents,
                RefCallback = refCallback,
                WhileHoverClass = whileHoverClass,
                WhileTapClass = whileTapClass,
                WhileFocusClass = whileFocusClass,
            };
        }

        /// <summary>
        /// Drop target. Collides with the active drag's rect under the scope's
        /// collision strategy; accept-filtering stays app logic in the scope callbacks.
        /// </summary>
        /// <param name="id">Identity reported to the scope callbacks.</param>
        /// <param name="dropData">Arbitrary payload carried into the callbacks.</param>
        /// <param name="disabled">A disabled droppable never collides.</param>
        /// <param name="whileOverClass">Classes applied while this target is the winning collision.</param>
        /// <param name="whileDragActiveClass">Classes applied to every enabled candidate while any drag
        /// is live in scope.</param>
        /// <returns>The created <see cref="ElementNode"/>.</returns>
        public static ElementNode Droppable(
            string id,
            object? dropData = null,
            bool disabled = false,
            string? whileOverClass = null,
            string? whileDragActiveClass = null,
            string? className = null,
            string? key = null,
            string? name = null,
            FiberElementProps? props = null,
            StyleOverrides? styles = null,
            Func<VisualElement, Action>? refCallback = null,
            VNode?[]? children = null,
            string? whileHoverClass = null,
            string? whileTapClass = null,
            string? whileFocusClass = null,
            IReadOnlyDictionary<string, string>? data = null,
            IReadOnlyDictionary<string, string>? aria = null)
        {
            var mergedProps = WithAttributes(props, data, aria) ?? VNodePool.RentProps();
            mergedProps.Droppable = new DroppableSettings(
                id, dropData, disabled, whileOverClass, whileDragActiveClass);

            return new ElementNode
            {
                Key = key,
                ElementType = typeof(VisualElement),
                Name = name,
                ClassNames = ParseClassNames(className),
                Props = mergedProps,
                Styles = styles,
                Children = children ?? EmptyChildren,
                Events = EmptyEvents,
                RefCallback = refCallback,
                WhileHoverClass = whileHoverClass,
                WhileTapClass = whileTapClass,
                WhileFocusClass = whileFocusClass,
            };
        }

        /// <summary>
        /// Portal-rendered drag preview. Expands to
        /// <c>V.Portal(UILayer.Overlay)</c> hosting a framework-positioned, picking-ignored positioner
        /// that is sized to the drag source at activation and tracks the pointer while a drag is active
        /// (hidden otherwise). Render preview content conditionally from state set in
        /// <c>onDragStart</c>/<c>onDragEnd</c>. Inherits
        /// <c>V.Portal(layer:)</c>'s editor-context degradation.
        /// </summary>
        /// <param name="children">The preview content.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <returns>The created <see cref="PortalNode"/>.</returns>
        public static PortalNode DragOverlay(VNode?[]? children = null, string? key = null)
        {
            var positionerProps = VNodePool.RentProps();
            positionerProps.DragOverlay = new DragOverlaySettings();
            return Portal(UILayer.Overlay, key: key, children: new VNode?[]
            {
                new ElementNode
                {
                    Key = "drag-overlay-positioner",
                    ElementType = typeof(VisualElement),
                    Props = positionerProps,
                    Children = children ?? EmptyChildren,
                    Events = EmptyEvents,
                },
            });
        }

        /// <summary>
        /// Placeholder that renders the matched child route component of a nested route at this position.
        /// </summary>
        /// <param name="context">
        /// Optional value supplied to the rendered child route, consumed by <c>Hooks.UseOutletContext</c>.
        /// </param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <returns>The created <see cref="OutletNode"/>.</returns>
        public static OutletNode Outlet(object? context = null, string? key = null) =>
            new() { Key = key, OutletContextValue = context };

        /// <summary>
        /// Fragment node. Returns multiple nodes without an enclosing wrapper element.
        /// When <paramref name="key"/> is supplied, the Fragment's children participate in the
        /// parent's keyed sibling list as a single keyed unit: their identity is scoped by
        /// <paramref name="key"/> so siblings under a Fragment with a different key do not collide,
        /// and per-child fiber state (Hooks, refs) is preserved across reorders of the keyed
        /// Fragments.
        /// </summary>
        /// <param name="children">Child VNodes returned as a flat sibling list.</param>
        /// <param name="key">
        /// Optional key used to disambiguate the Fragment from siblings at the same position. Must
        /// not contain a NUL (U+0000) character; NUL is reserved as the internal scope delimiter
        /// used by the reconciler to compose Fragment scope chains.
        /// </param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> contains a NUL character.</exception>
        /// <returns>The created <see cref="FragmentNode"/>.</returns>
        public static FragmentNode Fragment(VNode?[] children, string? key = null)
        {
            if (key != null && key.IndexOf('\0') >= 0)
            {
                throw new ArgumentException(
                    "Fragment key must not contain a NUL (U+0000) character; NUL is reserved as the internal scope delimiter.",
                    nameof(key));
            }
            return new FragmentNode
            {
                Key = key,
                Children = children ?? EmptyChildren,
            };
        }

        #endregion

        #region Motion

        /// <summary>
        /// Container that supports mount / unmount animations.
        /// When a keyed child becomes null, removal is deferred until the exit animation completes.
        /// </summary>
        /// <param name="children">Child VNodes whose enter / exit transitions are tracked.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <param name="initial">When false, suppresses enter animations on the very first mount.</param>
        /// <param name="staggerSec">Delay (seconds) staggered between sequential children.</param>
        /// <param name="mode">Exit / enter sequencing. <see cref="AnimatePresenceMode.Sync"/> (default) overlaps
        /// exit and enter; <see cref="AnimatePresenceMode.Wait"/> holds a brand-new child back until in-flight
        /// exits finish (suited to single-child route / screen swaps); <see cref="AnimatePresenceMode.PopLayout"/>
        /// pulls an exiting child out of layout flow so still-present siblings reflow immediately.</param>
        /// <param name="onExitComplete">Invoked once when every in-flight exit animation has finished;
        /// not fired for cancelled exits or animation-less removals.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> names no member of
        /// <see cref="AnimatePresenceMode"/>.</exception>
        /// <returns>The created <see cref="AnimatePresenceNode"/>.</returns>
        /// <remarks>
        /// AnimatePresence emits no element of its own — its keyed children expand directly into the parent.
        /// Put flex / wrap / gap on the <em>parent</em> element.
        /// </remarks>
        public static AnimatePresenceNode AnimatePresence(
            VNode?[]? children = null,
            string? key = null,
            bool initial = true,
            float staggerSec = 0f,
            float delayChildrenSec = 0f,
            int staggerDirection = 1,
            AnimatePresenceMode mode = AnimatePresenceMode.Sync,
            Action? onExitComplete = null)
        {
            if (mode is not (AnimatePresenceMode.Sync or AnimatePresenceMode.Wait or AnimatePresenceMode.PopLayout))
            {
                throw new ArgumentOutOfRangeException(nameof(mode), mode,
                    "V.AnimatePresence takes a member of AnimatePresenceMode as its mode.");
            }
            return new AnimatePresenceNode
            {
                Key = key,
                Children = children ?? EmptyChildren,
                Initial = initial,
                StaggerSec = staggerSec,
                DelayChildrenSec = delayChildrenSec,
                StaggerDirection = staggerDirection,
                Mode = mode,
                OnExitComplete = onExitComplete,
            };
        }

        /// <summary>
        /// Element targeted by an animation.
        /// Used inside AnimatePresence; toggles CSS classes on mount / unmount according to <paramref name="transition"/>.
        /// The one exception is a variant <paramref name="initial"/>/<paramref name="animate"/> pair (see
        /// <paramref name="initial"/>), which plays its mount enter on any Motion, standalone or not.
        /// When <paramref name="transition"/> is null, <c>StyleTransition.Fade</c> is applied as the default.
        /// <paramref name="duration"/> / <paramref name="easing"/> can override individual fields of the
        /// transition preset (e.g. setting only the duration while keeping the preset's other fields).
        /// To disable animation entirely (immediate mount / unmount), pass <c>transition: StyleTransitionConfig.None</c>.
        /// Note: when <c>DurationSec</c> is 0 (including <c>StyleTransitionConfig.None</c>), <c>delay</c> is ignored
        /// and completion is immediate.
        /// </summary>
        /// <param name="className">CSS-like utility class string. Multiple classes separated by spaces.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <param name="name">Element name assigned to <see cref="VisualElement.name"/> for query/debug.</param>
        /// <param name="transition">Transition preset; defaults to <c>StyleTransition.Fade</c> when null. A
        /// <paramref name="variants"/> entry naming its own replaces it for swaps into that pose.</param>
        /// <param name="duration">Override the transition duration (seconds).</param>
        /// <param name="easing">Override the transition easing mode.</param>
        /// <param name="delay">Override the transition delay (seconds). Ignored when <c>DurationSec</c> is 0.</param>
        /// <param name="onEnterComplete">Callback invoked when the enter transition finishes.</param>
        /// <param name="children">Child VNodes rendered inside the motion element.</param>
        /// <param name="props">Optional FiberElementProps (text / tooltip / enabled / etc.) bag.</param>
        /// <param name="events">Array of pre-built <see cref="FiberEventBinding"/> objects applied to the element.</param>
        /// <param name="refCallback">Callback invoked on mount with the created VisualElement; returned Action runs on unmount.</param>
        /// <param name="whileHoverClass">USS class toggled while the pointer hovers the element.</param>
        /// <param name="whileTapClass">USS class toggled while the pointer is pressed on the element.</param>
        /// <param name="whileFocusClass">USS class toggled while the element holds keyboard/UI focus.</param>
        /// <param name="elementType">The VisualElement type the animated cell IS,
        /// e.g. <c>typeof(Button)</c> so a clickable cell is one element instead of a Motion wrapping a
        /// Button. Defaults to <see cref="VisualElement"/>. Supply interactions via <paramref name="events"/>.</param>
        /// <param name="variants">Named animation states: each label maps to a <see cref="MotionVariant"/> —
        /// a utility class string for that state, plus an optional transition for swaps INTO it (a bare
        /// <c>string</c> converts implicitly, taking <paramref name="transition"/>). The label selected by
        /// <paramref name="animate"/> has its classes
        /// merged on top of <paramref name="className"/>. Because switching <paramref name="animate"/> changes the
        /// element's class list, a USS <c>transition-*</c> utility in the classes tweens between states.
        /// Parent→child propagation: a descendant Motion that supplies
        /// <paramref name="variants"/> but leaves <paramref name="animate"/> null inherits the nearest ANCESTOR
        /// Motion's active label and resolves it against its OWN variants — so setting <paramref name="animate"/>
        /// on a parent drives the whole subtree.</param>
        /// <param name="animate">The active variant label (a key of <paramref name="variants"/>). When null, the
        /// nearest ancestor Motion's active label is inherited; when set, it overrides any inherited label.</param>
        /// <param name="initial">Mount-time starting variant label. When this Motion also sets
        /// <paramref name="animate"/> + <paramref name="variants"/>, the enter starts at <c>variants[initial]</c>
        /// and transitions to <c>variants[animate]</c> (its persistent resting state) — whether this Motion is
        /// the DIRECT child of an AnimatePresence or mounts standalone. <see cref="MotionVariant.Transition"/>
        /// resolves the timing, and <see cref="MotionVariant.ClassName"/> the class <c>variants[initial]</c>
        /// must apply for the enter to play at all.</param>
        /// <param name="exit">Exit variant label. When this Motion is the DIRECT child of an
        /// AnimatePresence and also sets <paramref name="animate"/> + <paramref name="variants"/>, removal animates
        /// from <c>variants[animate]</c> to <c>variants[exit]</c> before the element unmounts.
        /// <see cref="MotionVariant.Transition"/> resolves the timing, and
        /// <see cref="MotionVariant.ClassName"/> the class <c>variants[exit]</c> must apply for the variant
        /// exit to run at all. Unlike <paramref name="initial"/>, this needs AnimatePresence to defer the
        /// unmount — set outside one, it is inert and logs a warning.</param>
        /// <returns>The created <see cref="MotionNode"/>.</returns>
        public static MotionNode Motion(
            string? className = null,
            string? key = null,
            string? name = null,
            StyleTransitionConfig? transition = null,
            float? duration = null,
            EasingMode? easing = null,
            float? delay = null,
            Action? onEnterComplete = null,
            VNode?[]? children = null,
            FiberElementProps? props = null,
            FiberEventBinding[]? events = null,
            Func<VisualElement, Action>? refCallback = null,
            string? whileHoverClass = null,
            string? whileTapClass = null,
            string? whileFocusClass = null,
            Type? elementType = null,
            IReadOnlyDictionary<string, MotionVariant>? variants = null,
            string? animate = null,
            string? initial = null,
            string? exit = null,
            string? layoutId = null)
        {
            var resolvedTransition = transition ?? StyleTransition.Fade;
            if (duration != null || easing != null || delay != null)
            {
                resolvedTransition = resolvedTransition.With(durationSec: duration, easing: easing, delaySec: delay);
            }

            // The variant inputs are carried RAW (not merged into ClassNames here): the reconciler resolves the
            // effective label (Animate ?? inherited-from-ancestor) against these variants at reconcile time — the
            // ancestor-context model — so both the self-case and parent→child propagation go through one path.
            return new MotionNode
            {
                Key = key,
                ElementType = elementType ?? typeof(VisualElement),
                Name = name,
                ClassNames = ParseClassNames(className),
                Transition = resolvedTransition,
                Children = children ?? EmptyChildren,
                Props = props,
                Events = events ?? EmptyEvents,
                RefCallback = refCallback,
                OnEnterComplete = onEnterComplete,
                WhileHoverClass = whileHoverClass,
                WhileTapClass = whileTapClass,
                WhileFocusClass = whileFocusClass,
                Variants = variants,
                Animate = animate,
                Initial = initial,
                Exit = exit,
                LayoutId = layoutId,
            };
        }

        #endregion

        #region Suspense

        /// <summary>
        /// Boundary that displays <paramref name="fallback"/> while a descendant declares a pending async
        /// resource via <c>Use&lt;T&gt;()</c>, until the resource resolves. A descendant behind a nested
        /// <c>V.Suspense()</c> is caught by that boundary instead, so it does not raise this one's fallback.
        /// On error, the failure is propagated to the nearest Error Boundary
        /// (a component that overrides <c>RenderFallback</c>).
        /// </summary>
        /// <remarks>
        /// Sibling <c>V.Suspense()</c> boundaries placed in the same component's <c>Render()</c> are tracked
        /// independently: each is keyed by its own scoped position, so one boundary showing its fallback while
        /// a descendant is pending does not force its siblings into fallback. When the parent re-renders, every
        /// boundary re-evaluates only its own pending state. Splitting children into separate components is not
        /// required to get per-boundary fallbacks.
        /// </remarks>
        /// <param name="fallback">VNode displayed while any descendant is suspended. Must not be null.</param>
        /// <param name="children">Child VNodes whose pending async resources trigger the boundary.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <returns>The created <see cref="SuspenseNode"/>.</returns>
        public static SuspenseNode Suspense(
            VNode? fallback,
            VNode?[] children,
            string? key = null)
        {
            if (fallback == null)
            {
                throw new ArgumentNullException(nameof(fallback));
            }

            return new SuspenseNode
            {
                Key = key,
                Fallback = fallback,
                Children = children ?? EmptyChildren,
            };
        }

        #endregion

        #region Virtualized list

        /// <summary>
        /// Virtualized list component for rendering large item collections.
        /// Renders a fixed-height-item ScrollView and only places the visible range in the DOM
        /// (a virtualized list).
        /// </summary>
        /// <typeparam name="T">Element type of the source collection.</typeparam>
        /// <param name="items">Source collection. Must not be null.</param>
        /// <param name="keySelector">Selector that derives a stable per-item key. Must not be null.</param>
        /// <param name="itemHeight">Fixed height (pixels) used for layout and visible-range calculation.</param>
        /// <param name="renderer">Function that produces a VNode for each visible item. Must not be null.</param>
        /// <param name="overscan">Extra items rendered above/below the visible window to smooth scroll-in.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <param name="className">CSS-like utility class string. Multiple classes separated by spaces.</param>
        /// <param name="name">Element name assigned to <see cref="VisualElement.name"/> for query/debug.</param>
        /// <returns>The created <see cref="VirtualListNode"/>.</returns>
        public static VirtualListNode VirtualList<T>(
            IReadOnlyList<T> items,
            Func<T, string> keySelector,
            float itemHeight,
            Func<T, VNode> renderer,
            int overscan = 3,
            string? key = null,
            string? className = null,
            string? name = null)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (keySelector == null)
            {
                throw new ArgumentNullException(nameof(keySelector));
            }

            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }

            // The erased item is the element the caller's own list held at that index, so the cast hands
            // their delegate back the T they put in — a null included, where their T admits one.
            var node = new VirtualListNode(
                items: new CastReadOnlyList<T>(items),
                keySelector: obj => keySelector((T)obj!),
                itemHeight: itemHeight,
                renderer: obj => renderer((T)obj!),
                overscan: overscan)
            {
                ClassNames = ParseClassNames(className),
                Name = name,
                Key = key
            };
            return node;
        }

        #endregion

        #region Routing DSL

        /// <summary>
        /// Path-based route definition. Declaratively expresses Velvet Router's nested routes and Loaders.
        /// </summary>
        /// <param name="path">URL path pattern for matching. Must not be null.</param>
        /// <param name="element">Component rendered when the route matches. Mutually exclusive with <paramref name="redirectTo"/>.</param>
        /// <param name="scopeId">Optional VContainer scope ID associated with this route.</param>
        /// <param name="loader">Async loader invoked on entry; result is exposed via Loader hook.</param>
        /// <param name="loaderMode">Whether the navigator awaits the loader (Await) or commits immediately and streams the result (Suspend).</param>
        /// <param name="errorElement">Component rendered when the loader throws.</param>
        /// <param name="children">Nested child route definitions.</param>
        /// <param name="redirectTo">Path to redirect to. Mutually exclusive with <paramref name="element"/> and <paramref name="guard"/>.</param>
        /// <param name="guard">Pass-through guard returning a redirect path or null. Cannot be combined with <paramref name="redirectTo"/>.</param>
        /// <param name="caseSensitive">When true, literal path segments match case-sensitively. Defaults to false (case-insensitive).</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="loaderMode"/> names no member of
        /// <see cref="LoaderMode"/>.</exception>
        /// <returns>The created <see cref="RouteDefinition"/>.</returns>
        public static RouteDefinition Route(
            string? path,
            ComponentNode? element = null,
            string? scopeId = null,
            Func<RouteLoaderContext, CancellationToken, VelvetTask<object>>? loader = null,
            LoaderMode loaderMode = LoaderMode.Await,
            ComponentNode? errorElement = null,
            RouteDefinition[]? children = null,
            string? redirectTo = null,
            Func<RouteLoaderContext, string>? guard = null,
            bool caseSensitive = false)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (element == null && redirectTo == null)
            {
                throw new ArgumentException(
                    "Either element or redirectTo must be specified.");
            }

            if (element != null && redirectTo != null)
            {
                throw new ArgumentException(
                    "element and redirectTo cannot be specified at the same time. Omit element for redirect-only routes.");
            }

            if (redirectTo != null && guard != null)
            {
                throw new ArgumentException(
                    "redirectTo and guard cannot be specified together. Use redirectTo for redirect-only routes and guard for pass-through routes.");
            }

            if (loaderMode is not (LoaderMode.Await or LoaderMode.Suspend))
            {
                throw new ArgumentOutOfRangeException(nameof(loaderMode), loaderMode,
                    "V.Route takes a member of LoaderMode as its loaderMode.");
            }

            return new RouteDefinition
            {
                Path = path,
                Element = element,
                ScopeId = scopeId,
                Loader = loader,
                LoaderMode = loaderMode,
                ErrorElement = errorElement,
                Children = children,
                RedirectTo = redirectTo,
                Guard = guard,
                CaseSensitive = caseSensitive,
            };
        }

        /// <summary>
        /// Container for an array of <see cref="RouteDefinition"/> values. Aggregates routes declared via <c>V.Route()</c>
        /// into the route table consumed when configuring the Router.
        /// </summary>
        /// <param name="routes">Route definitions to aggregate.</param>
        /// <returns>The same <paramref name="routes"/> array (passes through).</returns>
        public static RouteDefinition[] Routes(params RouteDefinition[] routes)
            => routes;

        /// <summary>
        /// Renders a button whose activation requests navigation.
        /// </summary>
        /// <param name="to">Absolute or route-relative navigation target.</param>
        /// <param name="replace">Selects replacement instead of push navigation.</param>
        public static ComponentNode Link(
            string to,
            string? text = null,
            string? className = null,
            string? name = null,
            VNode?[]? children = null,
            bool replace = false,
            string? key = null)
        {
            if (to == null) throw new ArgumentNullException(nameof(to));
            return Component(
                RouteLink.Render,
                new RouteLink.Props(to, text, className, name, children, replace),
                key);
        }

        /// <summary>
        /// No element participates in layout while the active <see cref="Router"/> handles the target.
        /// </summary>
        /// <remarks>Redirects on mount and again when <paramref name="to"/> or <paramref name="replace"/> changes.</remarks>
        /// <param name="to">Absolute and route-relative targets follow <see cref="Hooks.UseNavigate(bool)"/>.</param>
        /// <param name="replace">When true, replaces the current history entry instead of pushing.</param>
        public static ComponentNode Navigate(
            string to,
            bool replace = false,
            string? key = null)
        {
            if (to == null) throw new ArgumentNullException(nameof(to));
            return Component(
                global::Velvet.Navigate.Render,
                new global::Velvet.Navigate.Props(to, replace),
                key);
        }

        /// <summary>
        /// Adds current-location active styling to <see cref="Link"/>.
        /// </summary>
        /// <param name="to">Absolute or route-relative navigation target. A relative one takes the
        /// same resolution for the active comparison that it takes to navigate.</param>
        /// <param name="activeClass">Appended to <paramref name="className"/> while non-empty and active.</param>
        /// <param name="end">Restricts active state to an exact path match instead of a segment-prefix match.</param>
        /// <param name="replace">Selects replacement instead of push navigation.</param>
        /// <param name="caseSensitive">Uses ordinal matching instead of the case-insensitive default.</param>
        public static ComponentNode NavLink(
            string to,
            string activeClass,
            string? text = null,
            string? className = null,
            string? name = null,
            VNode?[]? children = null,
            bool end = false,
            bool replace = false,
            bool caseSensitive = false,
            string? key = null)
        {
            if (to == null) throw new ArgumentNullException(nameof(to));
            return Component(
                RouteNavLink.Render,
                new RouteNavLink.Props
                {
                    To = to,
                    Text = text,
                    ClassName = className,
                    ActiveClass = activeClass,
                    Name = name,
                    Children = children,
                    End = end,
                    Replace = replace,
                    CaseSensitive = caseSensitive,
                },
                key);
        }

        #endregion

        #region Internals: attribute & class-name helpers

        // Threads data-[...] / aria-[...] attribute maps onto a typed widget's props bag so the matching
        // data-/aria- variants reach the typed factories (Toggle, Button, …), not just the props-bag elements
        // (Div / Span). Rents a bag only when the factory did not already build one AND an attribute map is
        // actually supplied; otherwise it returns the bag unchanged (often null). Keeps the VNode immutable:
        // the bag is finalized here, before the ElementNode is constructed.
        private static FiberElementProps? WithAttributes(
            FiberElementProps? props, IReadOnlyDictionary<string, string>? data, IReadOnlyDictionary<string, string>? aria)
        {
            if (data == null && aria == null)
            {
                return props;
            }
            props ??= VNodePool.RentProps();
            if (data != null) props.Data = data;
            if (aria != null) props.Aria = aria;
            return props;
        }

        /// <summary>
        /// Splits a space-separated class name string into an array.
        /// "btn btn--active" → ["btn", "btn--active"]
        /// Note: results are cached, so passing dynamically-built strings will grow the cache without bound.
        /// Pass only literal or constant strings.
        /// </summary>
        internal static string[] ParseClassNames(string? classNames)
        {
            if (string.IsNullOrEmpty(classNames))
            {
                return EmptyClassNames;
            }

            if (s_classNameCache.TryGetValue(classNames, out var cached))
            {
                return cached;
            }

            if (s_classNameCache.Count >= MaxClassNameCacheSize)
            {
                Debug.LogWarning(
                    "[Velvet] ParseClassNames cache exceeded limit. Ensure only constant class name strings are passed.");
                s_classNameCache.Clear();
                // After clearing, the new entry below is added immediately, so the triggering key is cached right away.
            }

            var result = classNames.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            s_classNameCache[classNames] = result;
            return result;
        }

        /// <summary>
        /// Allocation-free wrapper that adapts an <see cref="IReadOnlyList{T}"/> to a list of nullable
        /// <see cref="object"/>. Used for type erasure in VirtualListNode; avoids array copies on every
        /// Reconcile.
        /// </summary>
        /// <remarks>
        /// The erased element type is nullable because <typeparamref name="T"/> may itself be, and nothing
        /// here narrows that: Velvet never dereferences an item — every one goes straight back to the
        /// caller's own selector and renderer.
        /// </remarks>
        private sealed class CastReadOnlyList<T> : IReadOnlyList<object?>
        {
            private readonly IReadOnlyList<T> _inner;

            public CastReadOnlyList(IReadOnlyList<T> inner) => _inner = inner;

            public object? this[int index] => _inner[index];
            public int Count => _inner.Count;

            public IEnumerator<object?> GetEnumerator()
            {
                for (var i = 0; i < _inner.Count; i++)
                {
                    yield return _inner[i];
                }
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        #endregion
    }
}
