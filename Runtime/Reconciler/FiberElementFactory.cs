using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // Factory that creates VisualElement instances from VNodes.
    // Activator.CreateInstance is 10-100x slower, so this dispatches directly via switch statements.
    internal sealed class FiberElementFactory
    {
        private readonly FiberEventBindingManager _eventManager;

        public FiberElementFactory(FiberEventBindingManager eventManager)
        {
            _eventManager = eventManager;
        }

        public VisualElement Create(ElementNode node)
        {
            var element = CreateByType(node.ElementType);
            ApplyName(element, node.Name);
            ApplyClassNames(element, node.ClassNames);
            ApplyProps(element, node.Props);
            ApplyStyles(element, node.Styles);
            ApplyEvents(element, node.Events);
            return element;
        }

        public Label CreateText(TextNode node) => VNodePool.RentLabel(node.Text);

        // Same as an ElementNode but without StyleOverrides.
        public VisualElement CreateMotion(MotionNode node) => CreateMotion(node, node.ClassNames);

        // Creates a VisualElement from a MotionNode, applying appliedClasses (the node's
        // base classes plus any ancestor-propagated variant classes) in place of the node's raw ClassNames.
        public VisualElement CreateMotion(MotionNode node, string[] appliedClasses)
        {
            var element = CreateByType(node.ElementType);
            ApplyName(element, node.Name);
            ApplyClassNames(element, appliedClasses);
            ApplyProps(element, node.Props);
            ApplyEvents(element, node.Events);
            return element;
        }

        private static VisualElement CreateByType(Type type)
        {
            if (type == null || type == typeof(VisualElement))
            {
                return new VisualElement();
            }

            if (type == typeof(Button))
            {
                return VNodePool.RentButton();
            }

            if (type == typeof(Label))
            {
                return VNodePool.RentLabel(string.Empty);
            }

            if (type == typeof(TextField))
            {
                return VNodePool.RentTextField();
            }

            if (type == typeof(Toggle))
            {
                return VNodePool.RentToggle();
            }

            if (type == typeof(Slider))
            {
                return VNodePool.RentSlider();
            }

            if (type == typeof(ScrollView))
            {
                return new ScrollView();
            }

            if (type == typeof(Image))
            {
                return new Image();
            }

            if (type == typeof(DropdownField))
            {
                return new DropdownField();
            }

            if (type == typeof(ListView))
            {
                return new ListView();
            }

            if (type == typeof(RadioButton))
            {
                return new RadioButton();
            }

            if (type == typeof(RadioButtonGroup))
            {
                return new RadioButtonGroup();
            }

            if (type == typeof(IntegerField))
            {
                return new IntegerField();
            }

            // Fallback for unknown types only (custom elements such as BlurredBackgroundElement are created here).
            return (VisualElement)Activator.CreateInstance(type);
        }

        private static void ApplyName(VisualElement element, string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                element.name = name;
            }
        }

        internal static void ApplyClassNames(VisualElement element, string[] classNames)
        {
            if (classNames == null)
            {
                return;
            }

            foreach (var className in classNames)
            {
                if (string.IsNullOrEmpty(className))
                {
                    continue;
                }

                // State-variant tokens (hover:/focus:/active:) are owned by the variant manipulator
                // (configured by FiberNodeFactory / PatchBaseElement), never added as classes.
                if (StyleVariantClass.IsVariant(className))
                {
                    continue;
                }

                // Structural variants (first:/last:/odd:/[&:nth-child(N)]:) are owned by the reconciler's
                // structural pass (evaluated against sibling position); never added as classes.
                if (StyleStructuralVariantClass.IsStructural(className))
                {
                    continue;
                }

                // has-[...] variants (parent styled by a descendant condition) are owned by the has-variant
                // manipulator / the has-class post-children pass; never added as classes.
                if (StyleHasVariantClass.IsHas(className))
                {
                    continue;
                }

                // data-[...] / aria-[...] variants (element styled by its own carried attribute) are owned
                // by the attribute side-table; never added as classes.
                if (StyleAttributeVariantClass.IsAttribute(className))
                {
                    continue;
                }

                // supports-[...] feature-query variants (element styled when the engine supports a
                // declaration) are owned by the supports side-table (static / always-applied in UITK);
                // never added as classes.
                if (StyleSupportsVariantClass.IsSupports(className))
                {
                    continue;
                }

                // font-[...] arbitrary font classes are owned by StyleFontResolver (resolved from the
                // whole class array and applied as inline style); like other arbitrary values they must
                // not enter the USS class list.
                if (StyleFontClass.IsArbitraryFontClass(className))
                {
                    continue;
                }

                // Important modifier (!utility / utility!): strip the bang and, when present,
                // elevate the inline-resolved utility to the Important layer so it wins over every other
                // layer. A class-only utility has no inline form to elevate, so its bang is inert.
                var cls = StyleArbitraryValueResolver.StripImportant(className, out var important);
                if (string.IsNullOrEmpty(cls))
                {
                    continue;
                }
                var priority = important ? StyleLayerPriority.Important : StyleLayerPriority.Base;

                // Plain classes (the overwhelming majority) go straight to the USS class list and skip both
                // resolvers; inline-value tokens (bracketed, color-opacity, static-scale) resolve to inline
                // style — the same routing AddClass uses on a re-render.
                if (!StyleArbitraryValueResolver.IsInlineResolved(cls))
                {
                    element.AddToClassList(cls);
                }
                else
                {
                    StyleArbitraryValueResolver.ApplyClassToken(element, cls, priority);
                }
            }
        }

        internal static void ApplyProps(VisualElement element, FiberElementProps props)
        {
            if (props == null)
            {
                return;
            }

            if (props.Text != null)
            {
                FiberPropApplier.ApplyText(element, props.Text);
            }

            if (props.Tooltip != null)
            {
                FiberPropApplier.ApplyTooltip(element, props.Tooltip);
            }

            if (props.Enabled.HasValue)
            {
                FiberPropApplier.ApplyEnabled(element, props.Enabled);
            }

            if (props.Visible.HasValue)
            {
                FiberPropApplier.ApplyVisible(element, props.Visible);
            }

            if (props.Focusable.HasValue)
            {
                FiberPropApplier.ApplyFocusable(element, props.Focusable);
            }

            if (props.FieldValue != null)
            {
                FiberPropApplier.ApplyFieldValue(element, props.FieldValue);
            }

            if (props.Slider != null)
            {
                FiberPropApplier.ApplySlider(element, props.Slider);
            }

            if (props.ScrollView != null)
            {
                FiberPropApplier.ApplyScrollView(element, props.ScrollView);
            }

            if (props.TextField != null)
            {
                FiberPropApplier.ApplyTextField(element, props.TextField);
            }

            if (props.Choices != null)
            {
                FiberPropApplier.ApplyChoices(element, props.Choices);
            }
        }

        internal static void ApplyStyles(VisualElement element, StyleOverrides styles)
        {
            if (styles == null)
            {
                return;
            }

            if (styles.BackgroundImage.HasValue)
            {
                element.style.backgroundImage = styles.BackgroundImage.Value;
            }

            if (styles.BackgroundColor.HasValue)
            {
                element.style.backgroundColor = styles.BackgroundColor.Value;
            }

            if (styles.Color.HasValue)
            {
                element.style.color = styles.Color.Value;
            }
        }

        internal void ApplyEvents(VisualElement element, FiberEventBinding[] events)
        {
            if (events == null)
            {
                return;
            }

            foreach (var evt in events)
            {
                _eventManager.Bind(element, evt);
            }
        }

        internal static void ApplyFieldValue(VisualElement element, object value)
        {
            switch (element)
            {
                case INotifyValueChanged<float> floatField when value is float f:
                    floatField.SetValueWithoutNotify(f);
                    break;
                case INotifyValueChanged<bool> boolField when value is bool b:
                    boolField.SetValueWithoutNotify(b);
                    break;
                case INotifyValueChanged<string> stringField when value is string s:
                    stringField.SetValueWithoutNotify(s);
                    break;
                case INotifyValueChanged<int> intField when value is int i:
                    intField.SetValueWithoutNotify(i);
                    break;
            }
        }

        // Resets a controlled field to its type default. Invoked when the FieldValue prop is cleared to null so
        // a re-render does not strand the prior value on screen (or, for a pooled TextField, leak it to the next
        // consumer). Mirrors the type dispatch of ApplyFieldValue.
        internal static void ClearFieldValue(VisualElement element)
        {
            switch (element)
            {
                case INotifyValueChanged<float> floatField:
                    floatField.SetValueWithoutNotify(0f);
                    break;
                case INotifyValueChanged<bool> boolField:
                    boolField.SetValueWithoutNotify(false);
                    break;
                case INotifyValueChanged<string> stringField:
                    stringField.SetValueWithoutNotify(string.Empty);
                    break;
                case INotifyValueChanged<int> intField:
                    intField.SetValueWithoutNotify(0);
                    break;
            }
        }
    }
}
