#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
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
        /// <summary>Test-only: drains the cache to isolate cache-bound regression coverage.</summary>
        internal static void ClearClassNameCacheForTesting() => s_classNameCache.Clear();
#endif

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
        /// Creates a VisualElement (generic container). Equivalent to HTML's div.
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
            IReadOnlyDictionary<string, string>? aria = null)
        {
            var events = SingleEvent(onValueChanged != null ? new ChangeEventBinding<string> { Handler = onValueChanged } : null);

            FiberElementProps? props = null;
            if (value != null || label != null || isPasswordField.HasValue || enabled.HasValue)
            {
                props = VNodePool.RentProps();
                props.FieldValue = value;
                props.Text = label;
                props.Enabled = enabled;
                props.TextField = isPasswordField.HasValue
                    ? new TextFieldSettings(isPasswordField)
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
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="pixelsPerUnit"/> is not positive.</exception>
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
        /// but skipped by the Reconciler's <c>FlattenAndFilter</c>.
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
        /// <typeparamref name="TProps"/> is stored as <see cref="object"/> on the fiber and compared
        /// via <see cref="object.Equals(object, object)"/>. Prefer a reference type
        /// (<c>sealed record</c>) to obtain value equality without boxing; a <c>record struct</c>
        /// boxes on every <c>V.Component</c> call.
        /// </remarks>
        /// <typeparam name="TProps">Props type. Use <c>sealed record</c> (reference type) for value equality without boxing.</typeparam>
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
        /// default shallow per-property identity comparison is sufficient, prefer plain
        /// <c>V.Component(body, props)</c> with <c>[Component(Memoize = true)]</c>; this overload is for
        /// the cases where shallow equality is too coarse or too fine (e.g. comparing only a subset of
        /// props, or deep-comparing one field).<br/>
        /// Attributes cannot carry delegates, so the comparator is supplied here at the call site rather
        /// than on <c>[Component]</c>.
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
        /// <see cref="MemoizedWithKey"/> instead. This is distinct from <see cref="Memo{TProps}"/>, which
        /// memoizes a function-style component by props equality.
        /// </summary>
        /// <param name="factory">Factory invoked to produce the cached VNode when <paramref name="deps"/> change.</param>
        /// <param name="deps">Dependency values. When deeply equal to the previous render, the cached VNode is reused.</param>
        /// <returns>The created <see cref="MemoNode"/>.</returns>
        public static MemoNode Memoized(Func<VNode> factory, params object?[]? deps)
        {
            return new MemoNode
            {
                Factory = factory,
                Dependencies = deps,
            };
        }

        // Memoized<T1..T8> / MemoizedWithKey<T1..T8> are auto-generated by Velvet.SourceGenerators
        // (Runtime/Plugins/Generators/Velvet.SourceGenerators.dll, V.Memoized.g.cs).

        /// <summary>
        /// Keyed memoization node. Provides a stable cache keyed by the supplied <paramref name="key"/>.
        /// </summary>
        /// <param name="key">Stable cache key independent of sibling order.</param>
        /// <param name="factory">Factory invoked to produce the cached VNode when <paramref name="deps"/> change.</param>
        /// <param name="deps">Dependency values used to detect changes.</param>
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

        #endregion

        #region Tree structure

        /// <summary>
        /// Provides a context value to the descendant subtree, visible to descendants that read the
        /// same context via <c>Hooks.UseContext</c>.
        /// </summary>
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
        /// Behavior boundary: context inheritance follows the LOGICAL tree (children inherit the
        /// context enclosing the V.Portal call site), but EVENT BUBBLING does NOT. Velvet physically
        /// reparents the children under the registered target element, so UI Toolkit bubbles their
        /// pointer / click / change events up the target's PHYSICAL ancestor chain — not up the logical
        /// ancestors of the V.Portal call site. A handler that must observe a portal child's event has to
        /// be placed on the target element's own ancestor chain (or attached directly to the portal
        /// children). The idealized model — where portal events also bubble through the logical tree — is
        /// not reproducible here: UI Toolkit computes every event's propagation path from the physical
        /// visual tree and exposes no API to redirect it along a logical chain, so faithful logical
        /// bubbling would require re-implementing the dispatcher (which would double-fire handlers on
        /// shared ancestors and could recurse across nested portals).
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
        /// Renders <paramref name="children"/> into a framework-managed screen-space layer panel
        /// sorted around the app's main panel — one host panel per layer per reconciler, created
        /// lazily and destroyed with the reconciler. Like every portal, the children stay part of the
        /// LOGICAL tree (context and state cross the boundary) while attaching physically to the layer
        /// panel — so event bubbling, relational <c>group-</c>/<c>peer-</c> variants and focus-within
        /// do not cross, and responsive breakpoints evaluate against the layer panel's own width.
        /// Screen-space layers always composite over the 3D scene; UI that must sit among scene
        /// geometry is <see cref="WorldSpace"/>'s territory.
        /// </summary>
        /// <param name="layer">The framework-managed layer panel to attach the children to.</param>
        /// <param name="children">Descendant VNodes mounted into the layer panel.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <returns>The created <see cref="PortalNode"/>.</returns>
        public static PortalNode Portal(UILayer layer, VNode?[]? children = null, string? key = null,
            PanelFocusOrder focusOrder = PanelFocusOrder.Isolated)
        {
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
        /// a scene transform — UI that lives among 3D content and is depth-tested against it (the drei
        /// <c>&lt;Html&gt;</c> parity point), unlike the always-on-top screen-space layers. The host
        /// (GameObject + world-space panel) is created on mount, follows <paramref name="position"/> /
        /// <paramref name="rotation"/> updates, and is destroyed on unmount. Children stay part of the
        /// logical tree (context and state cross; events do not — the panel boundary is physical).
        /// Display-only: world-space input routing is not wired.
        /// </summary>
        /// <param name="position">World position of the panel host.</param>
        /// <param name="rotation">World rotation of the panel host (identity when omitted).</param>
        /// <param name="panelSize">Virtual panel resolution in pixels (fixed world-space size mode).</param>
        /// <param name="children">Descendant VNodes rendered inside the world-space panel.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <returns>The created <see cref="WorldSpaceNode"/>.</returns>
        public static WorldSpaceNode WorldSpace(
            Vector3 position,
            Quaternion? rotation = null,
            Vector2? panelSize = null,
            VNode?[]? children = null,
            string? key = null,
            PanelFocusOrder focusOrder = PanelFocusOrder.Isolated)
        {
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
        /// Screen-space element that tracks a 3D scene Transform's projected position every frame — drei's
        /// <c>&lt;Html&gt;</c> parity (default screen-space projection mode). Not depth-tested against scene
        /// geometry (unlike <see cref="WorldSpace"/>, which renders content INTO the 3D scene and can be
        /// occluded by it): this is ordinary 2D UI, positioned dynamically. Forces <c>position: absolute</c>
        /// inline (dynamic left/top positioning has no other way to work; see AnchoredDriver.Attach) — pass
        /// layout classes for everything else.
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
        /// <returns>The created <see cref="ElementNode"/>.</returns>
        public static ElementNode Anchored(
            Transform? target,
            Camera? camera = null,
            Vector2? offset = null,
            bool hideWhenBehindCamera = true,
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
            mergedProps.Anchored = new AnchoredSettings(target, camera, offset ?? Vector2.zero, hideWhenBehindCamera);

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
        /// Container element whose subtree is a focus scope — React Aria's FocusScope. Deviation
        /// (documented): Aria's scope is renderless (sentinel spans); Velvet's is a real VisualElement,
        /// because UI Toolkit containment needs a subtree root for the scoped focus ring and the
        /// membership test. Any existing container can be a scope via props
        /// (<see cref="FiberElementProps.FocusScope"/>) — this factory is convenience for when no
        /// container exists yet. Style it like any Div.
        /// </summary>
        /// <param name="contain">Tab/Shift-Tab wrap within the subtree; a 2D/pointer move that exits is
        /// snapped back within the same event flush.</param>
        /// <param name="restoreFocus">On unmount while holding focus, refocus the element focus came from
        /// when it first entered the scope.</param>
        /// <param name="autoFocus">On attach, focus the scope's first focusable descendant.</param>
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
        /// <returns>The created <see cref="AnimatePresenceNode"/>.</returns>
        /// <remarks>
        /// AnimatePresence emits no element of its own — its keyed children expand directly into the parent.
        /// Put flex / wrap / gap on the <em>parent</em> element.
        /// Equivalent to Framer Motion's <c>AnimatePresence</c> (with <c>mode="wait"</c> and
        /// <c>onExitComplete</c>) for users migrating from Framer Motion.
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
        /// <param name="transition">Transition preset; defaults to <c>StyleTransition.Fade</c> when null.</param>
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
        /// <param name="variants">Named animation states: each label maps to a
        /// utility class string for that state. The label selected by <paramref name="animate"/> has its classes
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
        /// and transitions to <c>variants[animate]</c> (its persistent resting state) using this Motion's
        /// transition timing — whether this Motion is the DIRECT child of an AnimatePresence or mounts standalone
        /// (Framer parity: <c>initial</c>/<c>animate</c> apply to any motion.* component).</param>
        /// <param name="exit">Exit variant label. When this Motion is the DIRECT child of an
        /// AnimatePresence and also sets <paramref name="animate"/> + <paramref name="variants"/>, removal animates
        /// from <c>variants[animate]</c> to <c>variants[exit]</c> (using this Motion's transition timing) before the
        /// element unmounts. Unlike <paramref name="initial"/>, this needs AnimatePresence to defer the unmount —
        /// set outside one, it is inert and logs a warning.</param>
        /// <returns>The created <see cref="MotionNode"/>.</returns>
        /// <remarks>
        /// Equivalent to Framer Motion's <c>motion.&lt;tag&gt;</c> component (with its <c>variants</c>,
        /// <c>initial</c>, <c>animate</c>, and <c>exit</c> props, and parent→child context propagation) for
        /// users migrating from Framer Motion.
        /// </remarks>
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
            IReadOnlyDictionary<string, string>? variants = null,
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
        /// Boundary that displays <paramref name="fallback"/> while any descendant declares a pending async
        /// resource via <c>Use&lt;T&gt;()</c>, until the resource resolves.
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

            var node = new VirtualListNode(
                items: new CastReadOnlyList<T>(items),
                keySelector: obj => keySelector((T)obj),
                itemHeight: itemHeight,
                renderer: obj => renderer((T)obj),
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
        /// <returns>The created <see cref="RouteDefinition"/>.</returns>
        public static RouteDefinition Route(
            string? path,
            ComponentNode? element = null,
            string? scopeId = null,
            Func<RouteLoaderContext, CancellationToken, UniTask<object>>? loader = null,
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
        /// Clickable navigation primitive. Navigates to <paramref name="to"/> via the active
        /// <see cref="Router"/> on click. The target may
        /// be absolute or relative (resolved against the current location).
        /// </summary>
        /// <param name="to">Navigation target path (absolute or relative).</param>
        /// <param name="text">Link label text.</param>
        /// <param name="className">CSS-like utility class string applied to the link element.</param>
        /// <param name="name">Element name for query/debug.</param>
        /// <param name="children">Optional child nodes rendered inside the link.</param>
        /// <param name="replace">When true, replaces the current history entry instead of pushing.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <returns>A <see cref="ComponentNode"/> rendering the link.</returns>
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
        /// Declarative redirect element. Navigates to <paramref name="to"/> via the active
        /// <see cref="Router"/>, then renders nothing. Use it for conditional redirects such as
        /// <c>return loggedIn ? V.Outlet() : V.Navigate("/login")</c>.
        /// </summary>
        /// <param name="to">Navigation target path (absolute or relative).</param>
        /// <param name="replace">When true, replaces the current history entry instead of pushing.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <returns>A <see cref="ComponentNode"/> that performs the redirect and renders nothing.</returns>
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
        /// Clickable navigation primitive that derives an active state from the current location.
        /// Applies <paramref name="activeClass"/> when the link is active.
        /// </summary>
        /// <param name="to">Navigation target path (absolute or relative).</param>
        /// <param name="activeClass">CSS-like class string appended when the link is active.</param>
        /// <param name="text">Link label text.</param>
        /// <param name="className">CSS-like utility class string applied always.</param>
        /// <param name="name">Element name for query/debug.</param>
        /// <param name="children">Optional child nodes rendered inside the link.</param>
        /// <param name="end">When true, the link is active only on an exact path match (otherwise a prefix match counts as active).</param>
        /// <param name="replace">When true, replaces the current history entry instead of pushing.</param>
        /// <param name="caseSensitive">When true, the active-state comparison is Ordinal (case-sensitive). Defaults to false.</param>
        /// <param name="key">Key used to disambiguate siblings at the same position.</param>
        /// <returns>A <see cref="ComponentNode"/> rendering the active-aware link.</returns>
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
                new RouteNavLink.Props(to, text, className, activeClass, name, children, end, replace, caseSensitive),
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
        /// Allocation-free wrapper that adapts <see cref="IReadOnlyList{T}"/> to <see cref="IReadOnlyList{Object}"/>.
        /// Used for type erasure in VirtualListNode; avoids array copies on every Reconcile.
        /// </summary>
        private sealed class CastReadOnlyList<T> : IReadOnlyList<object>
        {
            private readonly IReadOnlyList<T> _inner;

            public CastReadOnlyList(IReadOnlyList<T> inner) => _inner = inner;

            public object? this[int index] => _inner[index];
            public int Count => _inner.Count;

            public IEnumerator<object> GetEnumerator()
            {
                for (var i = 0; i < _inner.Count; i++)
                {
                    yield return _inner[i]!;
                }
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        #endregion
    }
}
