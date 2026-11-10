#nullable enable
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The border colours of Tailwind's preflight, kept in the rule an element's inline style holds below its inline
    // values (the slot a UXML style attribute fills) rather than in an inline value of their own. An inline write of
    // code's then never meets a value of Velvet's: the engine stores nothing for a write equal to the value stored in
    // the slot, so a colour code wrote equal to preflight's last one used to be taken for preflight's own.
    //
    // A colour is applied through the engine's own per-property write for an inline value, which leaves the
    // inline values alone, and is also held in the rule, which a restyle applies again. Taking a side out reverts
    // the property to what the element's rules give it. PreflightInlineRuleTests pins what this relies on.
    internal sealed class PreflightInlineRule
    {
        internal const string SheetName = "velvet-border-preflight";

        private static readonly string[] s_properties =
            { "border-top-color", "border-right-color", "border-bottom-color", "border-left-color" };

        // The engine's id of each side's colour property, in the side order.
        private static readonly object?[] s_ids =
        {
            EngineMember.BorderTopColorPropertyId.ResolveField()?.GetValue(null),
            EngineMember.BorderRightColorPropertyId.ResolveField()?.GetValue(null),
            EngineMember.BorderBottomColorPropertyId.ResolveField()?.GetValue(null),
            EngineMember.BorderLeftColorPropertyId.ResolveField()?.GetValue(null),
        };

        private static readonly Type? s_builderType = EngineMember.StyleSheetBuilderConstruct.ResolveType();
        private static readonly MethodInfo? s_beginRule = EngineMember.StyleSheetBuilderBeginRule.ResolveMethod();
        private static readonly MethodInfo? s_beginProperty = EngineMember.StyleSheetBuilderBeginProperty.ResolveMethod();
        private static readonly MethodInfo? s_addColor = EngineMember.StyleSheetBuilderAddColor.ResolveMethod();
        private static readonly MethodInfo? s_endProperty = EngineMember.StyleSheetBuilderEndProperty.ResolveMethod();
        private static readonly MethodInfo? s_endRule = EngineMember.StyleSheetBuilderEndRule.ResolveMethod();
        private static readonly MethodInfo? s_buildTo = EngineMember.StyleSheetBuilderBuildTo.ResolveMethod();
        private static readonly FieldInfo? s_colors = EngineMember.StyleSheetColors.ResolveField();
        private static readonly PropertyInfo? s_ruleProperties = EngineMember.StyleRuleProperties.ResolveProperty();
        private static readonly MethodInfo? s_setProperties = EngineMember.StyleRuleSetProperties.ResolveMethod();
        private static readonly MethodInfo? s_setInlineRule = EngineMember.ElementSetInlineRule.ResolveMethod();
        private static readonly FieldInfo? s_accessor = EngineMember.ElementInlineStyleAccess.ResolveField();
        private static readonly PropertyInfo? s_inlineRule = EngineMember.InlineStyleAccessInlineRule.ResolveProperty();
        private static readonly FieldInfo? s_inlineRuleSheet = EngineMember.InlineRuleSheet.ResolveField();
        private static readonly FieldInfo? s_inlineRuleRule = EngineMember.InlineRuleRule.ResolveField();
        private static readonly PropertyInfo? s_propertyName = EngineMember.StylePropertyName.ResolveProperty();
        private static readonly MethodInfo? s_applyValue = EngineMember.InlineStyleAccessApplyValue.ResolveMethod();
        private static readonly MethodInfo? s_removeValue = EngineMember.InlineStyleAccessRemoveValue.ResolveMethod();
        private static readonly FieldInfo? s_valueId = EngineMember.StyleValueId.ResolveField();
        private static readonly FieldInfo? s_valueColor = EngineMember.StyleValueColor.ResolveField();
        private static readonly MethodInfo? s_running = EngineMember.HasRunningStyleAnimation.ResolveMethod();

        private static readonly bool s_buildable = s_builderType != null && s_beginRule != null && s_beginProperty != null
            && s_addColor != null && s_endProperty != null && s_endRule != null && s_buildTo != null && s_colors != null;

        private static readonly bool s_installable = s_ruleProperties != null && s_setProperties != null && s_setInlineRule != null
            && s_accessor != null && s_inlineRule != null && s_inlineRuleSheet != null && s_inlineRuleRule != null
            && s_propertyName != null;

        private static readonly bool s_appliable = s_applyValue != null && s_removeValue != null && s_valueId != null
            && s_valueColor != null && s_running != null && Array.TrueForAll(s_ids, id => id != null);

        private static readonly bool s_usable = s_buildable && s_installable && s_appliable;

        // Rules built and let go of, which an element takes again: a sheet is a ScriptableObject, and a pooled element
        // lets go of its own as often as it is reused.
        private static readonly System.Collections.Generic.Stack<PreflightInlineRule> s_spare = new();

        private readonly UnityEngine.Object _sheet;
        private readonly object _rule;
        private readonly Array _all;
        private readonly Color[] _colors;
        private int _shown;
        // Whether the element's inline rule is this one, which a restyle applies again.
        private bool _kept;
        // Whether an element holds this rule, so that letting go of it twice does not put it in the spare stack twice.
        private bool _live;
        // The sides a UXML style attribute's own rule colours, which stand over preflight as any inline colour does.
        private int _blocked;
        // The sides shown where an inline value was read standing over the rule. Clearing that value reverts the property to
        // what the rules give, and the transition then running heads away from the colour shown, which Holds alone takes
        // for one toward it.
        private int _covered;

        private PreflightInlineRule(UnityEngine.Object sheet, object rule, Array all, Color[] colors)
            => (_sheet, _rule, _all, _colors) = (sheet, rule, all, colors);

        // The rule for element, or none where the engine's members are not all there. Where the element's inline rule is
        // one of a UXML style attribute, which this one would replace, the colours are applied without being kept in a
        // rule, and Reassert applies them again after a restyle.
        internal static PreflightInlineRule? Attach(VisualElement element)
        {
            if (!s_usable)
            {
                return null;
            }
            var foreign = false;
            var blocked = 0;
            if (s_accessor!.GetValue(element) is { } accessor
                && s_inlineRuleSheet!.GetValue(s_inlineRule!.GetValue(accessor)) is UnityEngine.Object held
                && held != null && held.name != SheetName)
            {
                foreign = true;
                blocked = BlockedSides(s_inlineRuleRule!.GetValue(s_inlineRule.GetValue(accessor)));
            }
            PreflightInlineRule? rule = null;
            while (rule == null && s_spare.Count > 0)
            {
                var spare = s_spare.Pop();
                rule = spare._sheet != null ? spare : null;
            }
            rule ??= Build();
            rule._kept = !foreign;
            rule._live = true;
            rule._blocked = blocked;
            if (rule._kept)
            {
                s_setInlineRule!.Invoke(element, new[] { rule._sheet, rule._rule });
            }
            return rule;
        }

        private static PreflightInlineRule Build()
        {
            var builder = Activator.CreateInstance(s_builderType!)!;
            var rule = s_beginRule!.Invoke(builder, new object[] { 0 })!;
            foreach (var property in s_properties)
            {
                s_beginProperty!.Invoke(builder, new object[] { property, -1 });
                s_addColor!.Invoke(builder, new object[] { Color.clear });
                s_endProperty!.Invoke(builder, null);
            }
            s_endRule!.Invoke(builder, null);
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            sheet.name = SheetName;
            sheet.hideFlags = HideFlags.HideAndDontSave;
            s_buildTo!.Invoke(builder, new object[] { sheet });
            var all = (Array)((Array)s_ruleProperties!.GetValue(rule)).Clone();
            var made = new PreflightInlineRule(sheet, rule, all, (Color[])s_colors!.GetValue(sheet));
            made.Publish();
            return made;
        }

        private static int BlockedSides(object? rule)
        {
            var blocked = 0;
            foreach (var property in rule == null ? Array.Empty<object>() : (Array)s_ruleProperties!.GetValue(rule))
            {
                blocked |= (string)s_propertyName!.GetValue(property) switch
                {
                    "border-color" => 15,
                    "border-top-color" => 1,
                    "border-right-color" => 2,
                    "border-bottom-color" => 4,
                    "border-left-color" => 8,
                    _ => 0,
                };
            }
            return blocked;
        }

        // Whether the element's UXML style attribute colours side, which preflight leaves to it.
        internal bool Blocked(int side) => (_blocked & (1 << side)) != 0;

        // Whether side (0 to 3, top to left) is in the rule.
        internal bool Shows(int side) => (_shown & (1 << side)) != 0;

        // Puts colour on side, unless the side already shows it. A side the element's computed style has lost since
        // (an inline value written over it and cleared again reverts the property to what the rules give) is put back.
        internal void Show(VisualElement element, int side, Color colour)
        {
            if (Blocked(side))
            {
                return;
            }
            if (Shows(side) && Same(_colors[side], colour) && !Uncovered(element, side) && Holds(element, side, colour))
            {
                return;
            }
            _colors[side] = colour;
            if (!Shows(side))
            {
                _shown |= 1 << side;
                Publish();
            }
            // An inline value stands over the rule, and the computed style shows it already.
            if (HasInlineValue(element, side) || s_accessor!.GetValue(element) is not { } accessor)
            {
                return;
            }
            var value = Activator.CreateInstance(s_valueId!.DeclaringType!)!;
            s_valueId.SetValue(value, s_ids[side]);
            s_valueColor!.SetValue(value, colour);
            s_applyValue!.Invoke(accessor, new[] { value });
            _covered &= ~(1 << side);
            element.MarkDirtyRepaint();
        }

        // The colour shown on side, for a side in the rule.
        internal bool TryColour(int side, out Color colour)
        {
            colour = _colors[side];
            return Shows(side);
        }

        // Applies again every colour the computed style has lost, after a restyle that does not apply this rule.
        internal void Reassert(VisualElement element)
        {
            for (var side = 0; side < s_properties.Length; side++)
            {
                if (Shows(side) && (Uncovered(element, side) || !Holds(element, side, _colors[side])))
                {
                    Show(element, side, _colors[side]);
                }
            }
        }

        // Whether the computed style has lost a colour the rule shows, as it does when an inline value written over a side
        // is cleared again.
        internal bool Lost(VisualElement element)
        {
            var lost = false;
            for (var side = 0; side < s_properties.Length; side++)
            {
                lost |= Shows(side) && (Uncovered(element, side) || !Holds(element, side, _colors[side]));
            }
            return lost;
        }

        // Takes side out of the rule and gives the property back to what the element's rules give it, unless an inline
        // value stands there, which the computed style shows already.
        internal void Hide(VisualElement element, int side)
        {
            if (!Shows(side))
            {
                return;
            }
            _shown &= ~(1 << side);
            _covered &= ~(1 << side);
            Publish();
            if (HasInlineValue(element, side) || s_accessor!.GetValue(element) is not { } accessor)
            {
                return;
            }
            s_removeValue!.Invoke(accessor, new[] { s_ids[side] });
            element.MarkDirtyRepaint();
        }

        // Lets go of the element's inline rule and keeps this one for the next element.
        internal void Release(VisualElement element)
        {
            if (!_live)
            {
                return;
            }
            _live = false;
            for (var side = 0; side < s_properties.Length; side++)
            {
                Hide(element, side);
            }
            if (_kept)
            {
                s_setInlineRule!.Invoke(element, new object?[] { null, null });
            }
            s_spare.Push(this);
        }

        // Whether the element's computed style shows colour on side where nothing overrides the rule: an inline value
        // or a transition still running toward it leaves a different one there legitimately.
        // Whether an inline value read standing over side has been cleared since, recording one that stands now.
        private bool Uncovered(VisualElement element, int side)
        {
            if (HasInlineValue(element, side))
            {
                _covered |= 1 << side;
                return false;
            }
            return (_covered & (1 << side)) != 0;
        }

        private static bool Holds(VisualElement element, int side, Color colour)
            => Same(ResolvedColor(element, side), colour) || HasInlineValue(element, side)
                || (bool)s_running!.Invoke(element, new[] { s_ids[side] });

        private static Color ResolvedColor(VisualElement element, int side) => side switch
        {
            0 => element.resolvedStyle.borderTopColor,
            1 => element.resolvedStyle.borderRightColor,
            2 => element.resolvedStyle.borderBottomColor,
            _ => element.resolvedStyle.borderLeftColor,
        };

        private static bool HasInlineValue(VisualElement element, int side)
        {
            var style = element.style;
            var keyword = side switch
            {
                0 => style.borderTopColor.keyword,
                1 => style.borderRightColor.keyword,
                2 => style.borderBottomColor.keyword,
                _ => style.borderLeftColor.keyword,
            };
            return keyword != StyleKeyword.Null;
        }

        // Colour's operator== is approximate, and a colour one step from another is another colour here.
        private static bool Same(Color a, Color b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        // The properties of the rule, in the side order, for the sides shown.
        private void Publish()
        {
            var shown = Array.CreateInstance(_all.GetType().GetElementType()!, BitCount(_shown));
            var at = 0;
            for (var side = 0; side < s_properties.Length; side++)
            {
                if (Shows(side))
                {
                    shown.SetValue(_all.GetValue(side), at++);
                }
            }
            s_setProperties!.Invoke(_rule, new object[] { shown });
        }

        private static int BitCount(int value)
        {
            var count = 0;
            for (; value != 0; value &= value - 1)
            {
                count++;
            }
            return count;
        }
    }
}
