using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    internal static partial class StyleCustomProperties
    {
        #region Stylesheet declarations
        private static readonly FieldInfo? s_variableContext = EngineMember.ElementVariableContext.ResolveField();
        private static readonly PropertyInfo? s_variables = EngineMember.VariableContextVariables.ResolveProperty();
        private static readonly FieldInfo? s_variableName = EngineMember.StyleVariableName.ResolveField();
        private static readonly FieldInfo? s_variableSheet = EngineMember.StyleVariableSheet.ResolveField();
        private static readonly FieldInfo? s_variableHandles = EngineMember.StyleVariableHandles.ResolveField();
        private static readonly PropertyInfo? s_handleKind = EngineMember.StyleValueHandleKind.ResolveProperty();
        private static readonly MethodInfo? s_readAsString = EngineMember.StyleSheetReadAsString.ResolveMethod();
        private static readonly MethodInfo? s_readColor = EngineMember.StyleSheetReadColor.ResolveMethod();
        private static readonly MethodInfo? s_readFloat = EngineMember.StyleSheetReadFloat.ResolveMethod();

        // One reading per variable context: the engine shares a context between every element whose matched rules
        // declare the same variables, and builds a new one when they change.
        private static readonly ConditionalWeakTable<object, Dictionary<string, (string? Text, object Identity)>> s_contexts = new();
        private static readonly ConditionalWeakTable<object, Dictionary<string, (string? Text, object Identity)>>.CreateValueCallback
            s_readContext = ReadContext;

        // A custom property a stylesheet declares on element itself rather than one it inherits: element's
        // variable context holds it and its parent's does not hold the same declaration. A value Velvet cannot
        // spell (an asset reference) is read as null.
        private static bool TryStylesheet(VisualElement element, string name, out string? value)
        {
            value = null;
            if (!TryReadContext(element, out var context) || !context.TryGetValue(name, out var own))
            {
                return false;
            }
            var parent = element.hierarchy.parent;
            var inherited = parent == null ? null
                : ReferenceEquals(parent, s_traversed) ? Read(s_traversedPrevious)
                : TryReadContext(parent, out var read) ? read
                : null;
            if (inherited != null && inherited.TryGetValue(name, out var outer) && ReferenceEquals(outer.Identity, own.Identity))
            {
                return false;
            }
            value = own.Text;
            return true;
        }

        // The element OnCustomStyle is handling and the context it held before the traversal rebuilt it.
        private static VisualElement? s_traversed;
        private static object? s_traversedPrevious;

        // The context last read on each element, which OnCustomStyle hands TryStylesheet as the declaring element's
        // previous one. An element is listened to from its first reading.
        private static readonly ConditionalWeakTable<VisualElement, object> s_seen = new();
        private static readonly EventCallback<CustomStyleResolvedEvent> s_onCustomStyle = OnCustomStyle;

        private static Dictionary<string, (string? Text, object Identity)>? Read(object? handle)
            => handle == null ? null : s_contexts.GetValue(handle, s_readContext);

        private static bool TryReadContext(VisualElement element, out Dictionary<string, (string? Text, object Identity)> context)
        {
            context = null!;
            if (s_variableContext?.GetValue(element) is not { } handle)
            {
                return false;
            }
            if (!s_seen.TryGetValue(element, out var seen))
            {
                element.RegisterCallback(s_onCustomStyle);
                s_seen.Add(element, handle);
            }
            else if (!ReferenceEquals(seen, handle))
            {
                s_seen.Remove(element);
                s_seen.Add(element, handle);
            }
            context = s_contexts.GetValue(handle, s_readContext);
            return true;
        }

        private static Dictionary<string, (string? Text, object Identity)> ReadContext(object handle)
        {
            var read = new Dictionary<string, (string? Text, object Identity)>(StringComparer.Ordinal);
            if (s_variables?.GetValue(handle) is not IList variables || s_variableName == null
                || s_variableSheet == null || s_variableHandles == null)
            {
                return read;
            }
            // Later entries override earlier ones, as the engine's own lookup reads them from the end.
            foreach (var variable in variables)
            {
                if (s_variableName.GetValue(variable) is string name
                    && s_variableHandles.GetValue(variable) is Array handles)
                {
                    read[name] = (Spell(s_variableSheet.GetValue(variable), handles), handles);
                }
            }
            return read;
        }

        // A declaration's value handles as CSS text: numbers, dimensions, colours, keywords, names and strings,
        // commas, resource() paths and functions with their arguments. A var() with a fallback is stored with the
        // fallback as the rest of the declaration, so its parenthesis closes at the end.
        private static string? Spell(object? sheet, Array handles)
        {
            if (sheet == null || s_handleKind == null || s_readAsString == null || s_readColor == null || s_readFloat == null)
            {
                return null;
            }
            var text = new StringBuilder();
            var opened = 0;
            var index = 0;
            if (!SpellRange(sheet, handles, ref index, handles.Length, text, ref opened))
            {
                return null;
            }
            text.Append(')', opened);
            return text.ToString();
        }

        private static bool SpellRange(object sheet, Array handles, ref int index, int end, StringBuilder text, ref int opened)
        {
            for (; index < end; index++)
            {
                var handle = handles.GetValue(index);
                var kind = s_handleKind!.GetValue(handle)?.ToString();
                if (kind == "CommaSeparator")
                {
                    text.Append(',');
                    continue;
                }
                Separate(text);
                if (kind != "Function")
                {
                    var token = SpellValue(sheet, handle, kind);
                    if (token == null)
                    {
                        return false;
                    }
                    text.Append(token);
                    continue;
                }
                if (index + 1 >= handles.Length)
                {
                    return false;
                }
                var name = s_readAsString!.Invoke(null, new[] { sheet, handle }) as string;
                var arguments = (int)(float)s_readFloat!.Invoke(sheet, new[] { handles.GetValue(index + 1) })!;
                text.Append(name).Append('(');
                index += 2;
                if (name == "var")
                {
                    var spelled = SpellValue(sheet, handles.GetValue(index), "Variable");
                    text.Append(spelled);
                    if (arguments > 1)
                    {
                        opened++;
                    }
                    else
                    {
                        text.Append(')');
                    }
                    continue;
                }
                var argumentsEnd = Math.Min(handles.Length, index + arguments);
                if (!SpellRange(sheet, handles, ref index, argumentsEnd, text, ref opened))
                {
                    return false;
                }
                text.Append(')');
                index--;
            }
            return true;
        }

        private static void Separate(StringBuilder text)
        {
            if (text.Length > 0 && text[text.Length - 1] != ',' && text[text.Length - 1] != '(')
            {
                text.Append(' ');
            }
        }

        private static string? SpellValue(object sheet, object? handle, string? kind)
            => kind switch
            {
                "Float" or "Dimension" or "Keyword" or "Enum" or "String" or "Variable"
                    => s_readAsString!.Invoke(null, new[] { sheet, handle }) as string,
                "ResourcePath" => "resource(\"" + s_readAsString!.Invoke(null, new[] { sheet, handle }) + "\")",
                "Color" => Hex((Color)s_readColor!.Invoke(sheet, new[] { handle })!),
                _ => null,
            };

        private static string Hex(Color color)
        {
            var c = (Color32)color;
            return string.Format(CultureInfo.InvariantCulture, "#{0:x2}{1:x2}{2:x2}{3:x2}", c.r, c.g, c.b, c.a);
        }
        #endregion
    }
}
