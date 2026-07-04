using System;
using System.Collections;
using System.Collections.Generic;

namespace Velvet.Experimental
{
    /// <summary>
    /// <b>EXPERIMENTAL — PoC.</b> Object/collection-initializer authoring surface that lets a
    /// <c>V.*</c> tree be written in a declarative nested-brace form while keeping full IDE completion
    /// and requiring no IDE plugin.
    ///
    /// <para>
    /// This is a thin ergonomic layer: a builder never reaches the Reconciler. Each builder is converted to a
    /// plain <see cref="VNode"/> via <see cref="Build"/> (invoked by the implicit <c>VBuilder -&gt; VNode</c>
    /// conversion), delegating to the existing <c>V.*</c> factories so all parsing / pooling semantics are reused.
    /// </para>
    ///
    /// <para>
    /// Why initializers (C# 6/7) and not collection expressions (C# 12): Unity 6.3 defaults to C# 9 and does not
    /// officially support newer language versions; collection expressions <c>[...]</c> would force every consumer
    /// of the redistributed UPM package to swap their Roslyn compiler. Object/collection initializers compile on
    /// stock Unity, so this layer needs no toolchain change.
    /// </para>
    ///
    /// <example>
    /// <code>
    /// VNode Render()
    /// {
    ///     var (count, setCount) = UseState(0);
    ///     return new VDiv("flex flex-col items-center gap-4 p-4")
    ///     {
    ///         new VLabel("text-2xl font-bold") { Text = $"Count: {count}" },
    ///         new VButton("px-4 py-2 rounded bg-primary text-white")
    ///         {
    ///             Text = "Increment",
    ///             OnClick = () => setCount(count + 1),
    ///         },
    ///     };
    /// }
    /// </code>
    /// </example>
    /// </summary>
    public abstract class VBuilder : IEnumerable<VNode>
    {
        /// <summary>Utility class string (space-separated), equivalent to the <c>className</c> factory argument.</summary>
        public string Class { get; set; }

        /// <summary>Reconciler key, equivalent to the <c>key</c> factory argument.</summary>
        public string Key { get; set; }

        /// <summary><see cref="UnityEngine.UIElements.VisualElement.name"/>, equivalent to the <c>name</c> factory argument.</summary>
        public string Name { get; set; }

        private List<VNode> _children;

        /// <param name="className">Initial value for <see cref="Class"/>; pass positionally for the concise form.</param>
        protected VBuilder(string className) => Class = className;

        /// <summary>
        /// Collection-initializer hook. Accepts any <see cref="VNode"/> (e.g. the result of a <c>V.*</c> factory or
        /// <c>V.When(...)</c>) and, through the implicit <c>VBuilder -&gt; VNode</c> conversion, any nested builder.
        /// Null children are skipped, matching the <c>V.When(false, ...)</c> convention. The scratch accumulator
        /// list is rented from a pool so repeated authoring does not churn list allocations.
        /// </summary>
        public void Add(VNode child)
        {
            if (child == null)
            {
                return;
            }

            (_children ??= RentList()).Add(child);
        }

        /// <summary>
        /// Materializes the collected children into the <see cref="VNode"/>[] shape the <c>V.*</c> factories
        /// expect, then returns the scratch accumulator list to the pool. The produced array is rented from
        /// <see cref="VNodePool"/>, so the reconciler reclaims it on the next diff exactly as it does for
        /// <c>V.List</c> output — keeping the builder's only irreducible per-build cost the builder object itself.
        /// </summary>
        protected VNode[] BuildChildren()
        {
            if (_children == null)
            {
                return Array.Empty<VNode>();
            }

            var count = _children.Count;
            if (count == 0)
            {
                ReturnList(_children);
                _children = null;
                return Array.Empty<VNode>();
            }

            var array = VNodePool.RentNodeArray(count);
            for (var i = 0; i < count; i++)
            {
                array[i] = _children[i];
            }

            ReturnList(_children);
            _children = null;
            return array;
        }

        /// <summary>Converts this builder into a concrete <see cref="VNode"/> by delegating to the matching <c>V.*</c> factory.</summary>
        public abstract VNode Build();

        /// <summary>
        /// Implicit conversion that lets a builder be used anywhere a <see cref="VNode"/> is expected — as a child
        /// passed to <see cref="Add"/>, or as the return value of a component <c>Render</c> method.
        /// </summary>
        public static implicit operator VNode(VBuilder builder) => builder?.Build();

        // IEnumerable is a compiler requirement for collection-initializer syntax; it is not otherwise meaningful.
        IEnumerator<VNode> IEnumerable<VNode>.GetEnumerator() =>
            (_children ?? (IEnumerable<VNode>)Array.Empty<VNode>()).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<VNode>)this).GetEnumerator();

        #region Pooled scratch lists (main thread only)

        private const int MaxListPoolSize = 8;
        private const int MaxPooledListCapacity = 64;
        private static readonly Stack<List<VNode>> s_listPool = new();

        private static List<VNode> RentList() =>
            s_listPool.Count > 0 ? s_listPool.Pop() : new List<VNode>();

        private static void ReturnList(List<VNode> list)
        {
            if (list == null || s_listPool.Count >= MaxListPoolSize || list.Capacity > MaxPooledListCapacity)
            {
                return;
            }

            list.Clear();
            s_listPool.Push(list);
        }

#if UNITY_EDITOR
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetListPool() => s_listPool.Clear();
#endif

        #endregion
    }

    /// <summary>
    /// <b>EXPERIMENTAL.</b> Initializer-style builder for a generic container. Maps to <see cref="V.Div"/>.
    /// </summary>
    public sealed class VDiv : VBuilder
    {
        /// <param name="className">Utility class string applied to the div.</param>
        public VDiv(string className = null) : base(className) { }

        /// <inheritdoc/>
        public override VNode Build() =>
            V.Div(className: Class, key: Key, name: Name, children: BuildChildren());
    }

    /// <summary>
    /// <b>EXPERIMENTAL.</b> Initializer-style builder for a text label. Maps to <see cref="V.Label"/>.
    /// Labels are leaves; children added via the collection initializer are ignored.
    /// </summary>
    public sealed class VLabel : VBuilder
    {
        /// <summary>Label text content.</summary>
        public string Text { get; set; }

        /// <param name="className">Utility class string applied to the label.</param>
        public VLabel(string className = null) : base(className) { }

        /// <inheritdoc/>
        public override VNode Build() =>
            V.Label(className: Class, text: Text, key: Key, name: Name);
    }

    /// <summary>
    /// <b>EXPERIMENTAL.</b> Initializer-style builder for a button. Maps to <see cref="V.Button"/>.
    /// </summary>
    public sealed class VButton : VBuilder
    {
        /// <summary>Button label text. Coexists with children.</summary>
        public string Text { get; set; }

        /// <summary>Click handler. When null, no click event is bound.</summary>
        public Action OnClick { get; set; }

        /// <summary>When false, disables the button.</summary>
        public bool? Enabled { get; set; }

        /// <param name="className">Utility class string applied to the button.</param>
        public VButton(string className = null) : base(className) { }

        /// <inheritdoc/>
        public override VNode Build() =>
            V.Button(className: Class, text: Text, onClick: OnClick, key: Key, name: Name,
                enabled: Enabled, children: BuildChildren());
    }

    /// <summary>
    /// <b>EXPERIMENTAL.</b> Initializer-style builder for a scrolling container.
    /// Maps to <see cref="V.ScrollView"/>; children added via the collection initializer become the scroll content.
    /// </summary>
    public sealed class VScrollView : VBuilder
    {
        /// <param name="className">Utility class string applied to the scroll view.</param>
        public VScrollView(string className = null) : base(className) { }

        /// <inheritdoc/>
        public override VNode Build() =>
            V.ScrollView(className: Class, key: Key, name: Name, children: BuildChildren());
    }

    /// <summary>
    /// <b>EXPERIMENTAL.</b> Initializer-style builder for a custom <see cref="UnityEngine.UIElements.VisualElement"/>
    /// subclass. Maps to <see cref="V.Custom{T}(string, string, string, System.Func{UnityEngine.UIElements.VisualElement, System.Action}, VNode[])"/>;
    /// children added via the collection initializer are kept.
    /// </summary>
    /// <typeparam name="T">Concrete VisualElement subclass to instantiate.</typeparam>
    public sealed class VCustom<T> : VBuilder where T : UnityEngine.UIElements.VisualElement
    {
        /// <param name="className">Utility class string applied to the element.</param>
        public VCustom(string className = null) : base(className) { }

        /// <inheritdoc/>
        public override VNode Build() =>
            V.Custom<T>(className: Class, key: Key, name: Name, children: BuildChildren());
    }

    /// <summary>
    /// <b>EXPERIMENTAL.</b> Initializer-style builder for a text field. Maps to <see cref="V.TextField"/>.
    /// Text fields are leaves; children added via the collection initializer are ignored.
    /// </summary>
    public sealed class VTextField : VBuilder
    {
        /// <summary>Current text value (controlled).</summary>
        public string Value { get; set; }

        /// <summary>Handler invoked when the input text changes.</summary>
        public Action<string> OnChange { get; set; }

        /// <summary>Label text shown next to the field.</summary>
        public string Label { get; set; }

        /// <summary>When true, masks the input as a password field.</summary>
        public bool? IsPasswordField { get; set; }

        /// <summary>When false, disables user input.</summary>
        public bool? Enabled { get; set; }

        /// <param name="className">Utility class string applied to the field.</param>
        public VTextField(string className = null) : base(className) { }

        /// <inheritdoc/>
        public override VNode Build() =>
            V.TextField(className: Class, value: Value, onValueChanged: OnChange, key: Key, name: Name,
                label: Label, isPasswordField: IsPasswordField, enabled: Enabled);
    }

    /// <summary>
    /// <b>EXPERIMENTAL.</b> Initializer-style builder for a slider. Maps to <see cref="V.Slider"/>.
    /// Sliders are leaves; children added via the collection initializer are ignored.
    /// </summary>
    public sealed class VSlider : VBuilder
    {
        /// <summary>Current value (controlled).</summary>
        public float? Value { get; set; }

        /// <summary>Lower bound of the slider range.</summary>
        public float? LowValue { get; set; }

        /// <summary>Upper bound of the slider range.</summary>
        public float? HighValue { get; set; }

        /// <summary>Handler invoked when the value changes.</summary>
        public Action<float> OnChange { get; set; }

        /// <summary>When false, disables user interaction.</summary>
        public bool? Enabled { get; set; }

        /// <param name="className">Utility class string applied to the slider.</param>
        public VSlider(string className = null) : base(className) { }

        /// <inheritdoc/>
        public override VNode Build() =>
            V.Slider(className: Class, value: Value, lowValue: LowValue, highValue: HighValue,
                onValueChanged: OnChange, key: Key, name: Name, enabled: Enabled);
    }

    /// <summary>
    /// <b>EXPERIMENTAL.</b> Initializer-style builder for a toggle. Maps to <see cref="V.Toggle"/>.
    /// Toggles are leaves; children added via the collection initializer are ignored.
    /// </summary>
    public sealed class VToggle : VBuilder
    {
        /// <summary>Current toggle state (controlled).</summary>
        public bool? Value { get; set; }

        /// <summary>Handler invoked when the toggle state changes.</summary>
        public Action<bool> OnChange { get; set; }

        /// <summary>Label text shown next to the toggle.</summary>
        public string Label { get; set; }

        /// <summary>When false, disables user interaction.</summary>
        public bool? Enabled { get; set; }

        /// <param name="className">Utility class string applied to the toggle.</param>
        public VToggle(string className = null) : base(className) { }

        /// <inheritdoc/>
        public override VNode Build() =>
            V.Toggle(className: Class, value: Value, onValueChanged: OnChange, key: Key, name: Name,
                label: Label, enabled: Enabled);
    }

    /// <summary>
    /// <b>EXPERIMENTAL.</b> Initializer-style builder for an image. Maps to <see cref="V.Image"/>.
    /// Images are leaves; children added via the collection initializer are ignored. The sprite/texture is
    /// typically set through <see cref="Styles"/>.
    /// </summary>
    public sealed class VImage : VBuilder
    {
        /// <summary>Inline style overrides applied on top of USS classes (sprite / image typically set here).</summary>
        public StyleOverrides Styles { get; set; }

        /// <summary>USS class toggled while the pointer hovers the element (gesture-driven).</summary>
        public string WhileHoverClass { get; set; }

        /// <summary>USS class toggled while the pointer is pressed on the element (gesture-driven).</summary>
        public string WhileTapClass { get; set; }

        /// <summary>USS class toggled while the element holds keyboard/UI focus (gesture-driven).</summary>
        public string WhileFocusClass { get; set; }

        /// <param name="className">Utility class string applied to the image.</param>
        public VImage(string className = null) : base(className) { }

        /// <inheritdoc/>
        public override VNode Build() =>
            V.Image(className: Class, key: Key, name: Name, styles: Styles,
                whileHoverClass: WhileHoverClass, whileTapClass: WhileTapClass, whileFocusClass: WhileFocusClass);
    }
}
