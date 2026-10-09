#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// An engine member Velvet reaches by name rather than by a compiled reference, with the shape every read of it
    /// depends on. The instances are this type's own static fields and nothing else can construct one, so the set
    /// the build's linker step keeps (<c>EngineMemberLinkerKeep</c>) and the set <c>EngineMemberResolutionTests</c>
    /// resolves against the editor it runs in are the set the runtime reads. <c>EngineMemberRegistryTests</c>
    /// checks the named-reflection source spellings documented in the player-builds guide.
    /// </summary>
    internal sealed class EngineMember
    {
        private static readonly Assembly UIElements = typeof(VisualElement).Assembly;
        private static readonly Assembly IMGUI = typeof(UnityEngine.GUIUtility).Assembly;

        private const string FocusControllerType = "UnityEngine.UIElements.FocusController";
        private const string VisualElementType = "UnityEngine.UIElements.VisualElement";
        private const string FocusableType = "UnityEngine.UIElements.Focusable";
        private const string StyleSheetType = "UnityEngine.UIElements.StyleSheet";
        private const string ComputedStyleType = "UnityEngine.UIElements.ComputedStyle";
        private const string PseudoStatesType = "UnityEngine.UIElements.PseudoStates";
        private const string PseudoStatesShape = "enum:" + PseudoStatesType;

        // PanelFocusMemory's scrub.
        internal static readonly EngineMember LastFocusedElement =
            Field(UIElements, FocusControllerType, "m_LastFocusedElement", FocusableType);

        internal static readonly EngineMember LastPendingFocusedElement =
            Field(UIElements, FocusControllerType, "m_LastPendingFocusedElement", FocusableType);

        internal static readonly EngineMember SelectedTextElement = WrittenProperty(UIElements, FocusControllerType,
            "selectedTextElement", "UnityEngine.UIElements.TextElement");

        internal static readonly EngineMember FocusedElements = Field(UIElements, FocusControllerType,
            "m_FocusedElements", "System.Collections.Generic.List`1[UnityEngine.UIElements.FocusController+FocusedElement]");

        internal static readonly EngineMember FocusedElementEntry =
            Field(UIElements, FocusControllerType + "+FocusedElement", "m_FocusedElement", VisualElementType);

        internal static readonly EngineMember PseudoStates =
            WrittenProperty(UIElements, VisualElementType, "pseudoStates", PseudoStatesShape);

        internal static readonly EngineMember FocusPseudoState = Field(UIElements, PseudoStatesType, "Focus", PseudoStatesShape);

        // FocusManager's composite-field scope.
        internal static readonly EngineMember IsCompositeRoot =
            ReadProperty(UIElements, VisualElementType, "isCompositeRoot", "System.Boolean");

        // V.TextField's onSubmit: — the input's record of an open IME composition.
        internal static readonly EngineMember TextEditingManipulator = ReadProperty(UIElements,
            "UnityEngine.UIElements.TextElement", "editingManipulator", "UnityEngine.UIElements.TextEditingManipulator");

        internal static readonly EngineMember TextEditingUtilities = Field(UIElements,
            "UnityEngine.UIElements.TextEditingManipulator", "editingUtilities", "UnityEngine.TextEditingUtilities");

        internal static readonly EngineMember TextCompositionActive =
            Field(IMGUI, "UnityEngine.TextEditingUtilities", "isCompositionActive", "System.Boolean");

        // EnabledSelfWrites.
        internal static readonly EngineMember PropertyChangedEvent = ConstructedType(UIElements,
            "UnityEngine.UIElements.PropertyChangedEvent",
            "UnityEngine.UIElements.EventBase`1[UnityEngine.UIElements.PropertyChangedEvent]");

        // VelvetStyleUtilities' import walk.
        internal static readonly EngineMember StyleSheetImports =
            Field(UIElements, StyleSheetType, "imports", "UnityEngine.UIElements.StyleSheet+ImportStruct[]");

        internal static readonly EngineMember ImportedStyleSheet =
            Field(UIElements, StyleSheetType + "+ImportStruct", "styleSheet", StyleSheetType);

        internal static readonly EngineMember ElementComputedStyle =
            Field(UIElements, VisualElementType, "m_Style", ComputedStyleType);

        internal static readonly EngineMember ComputedStyleMatchingRulesHash =
            Field(UIElements, ComputedStyleType, "matchingRulesHash", "System.Int64");

        internal static readonly EngineMember TryGetComputedStyle = Method(UIElements,
            "UnityEngine.UIElements.StyleCache", "TryGetValue", "System.Boolean", "System.Int64", ComputedStyleType + "&");

        internal static readonly EngineMember ComputedStyleOpacity =
            ReadProperty(UIElements, ComputedStyleType, "opacity", "System.Single");

        internal static readonly EngineMember ComputedStyleRotate =
            ReadProperty(UIElements, ComputedStyleType, "rotate", "UnityEngine.UIElements.Rotate");

        internal static readonly EngineMember ComputedStyleScale =
            ReadProperty(UIElements, ComputedStyleType, "scale", "UnityEngine.UIElements.Scale");

        internal static readonly EngineMember ComputedStyleTranslate =
            ReadProperty(UIElements, ComputedStyleType, "translate", "UnityEngine.UIElements.Translate");

        internal static readonly EngineMember ComputedStyleTransitionProperty = ReadProperty(UIElements,
            ComputedStyleType, "transitionProperty", "System.Collections.Generic.List`1[UnityEngine.UIElements.StylePropertyName]");

        internal static readonly EngineMember ComputedStyleTransitionDuration = ReadProperty(UIElements,
            ComputedStyleType, "transitionDuration", "System.Collections.Generic.List`1[UnityEngine.UIElements.TimeValue]");

        internal static readonly EngineMember ComputedStyleTransitionDelay = ReadProperty(UIElements,
            ComputedStyleType, "transitionDelay", "System.Collections.Generic.List`1[UnityEngine.UIElements.TimeValue]");

        internal static readonly EngineMember ComputedStyleTransitionTimingFunction = ReadProperty(UIElements,
            ComputedStyleType, "transitionTimingFunction", "System.Collections.Generic.List`1[UnityEngine.UIElements.EasingFunction]");

        internal static readonly EngineMember HasRunningStyleAnimation = Method(UIElements,
            VisualElementType, "HasRunningAnimation", "System.Boolean", "enum:UnityEngine.UIElements.StyleSheets.StylePropertyId");

        internal static readonly EngineMember StylePropertyNameId = ReadProperty(UIElements,
            "UnityEngine.UIElements.StylePropertyName", "id", "enum:UnityEngine.UIElements.StyleSheets.StylePropertyId");

        private const BindingFlags OwnMembers = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        internal readonly Assembly Assembly;

        /// <summary>The declaring type's <see cref="Type.FullName"/>, with <c>+</c> before a nested type.</summary>
        internal readonly string TypeName;

        internal readonly MemberTypes Kind;

        /// <summary>The member's name; for <see cref="MemberTypes.TypeInfo"/>, the same as <see cref="TypeName"/>.</summary>
        internal readonly string Name;

        /// <summary>A field's or a property's type, a type's base type, or a method's return type followed by its
        /// parameter types in parentheses. Names use <see cref="Type.FullName"/>, generic arguments use brackets,
        /// arrays use <c>[]</c>, and by-ref parameters use <c>&amp;</c>. An <c>enum:</c> prefix requires an enum type.</summary>
        internal readonly string Shape;

        /// <summary>A property whose setter is called as well as its getter.</summary>
        internal readonly bool Writable;

        private EngineMember(Assembly assembly, string typeName, MemberTypes kind, string name, string shape,
            bool writable = false)
        {
            Assembly = assembly;
            TypeName = typeName;
            Kind = kind;
            Name = name;
            Shape = shape;
            Writable = writable;
        }

        private static EngineMember Field(Assembly assembly, string typeName, string name, string fieldType)
            => new(assembly, typeName, MemberTypes.Field, name, fieldType);

        private static EngineMember ReadProperty(Assembly assembly, string typeName, string name, string propertyType)
            => new(assembly, typeName, MemberTypes.Property, name, propertyType);

        private static EngineMember WrittenProperty(Assembly assembly, string typeName, string name, string propertyType)
            => new(assembly, typeName, MemberTypes.Property, name, propertyType, writable: true);

        // Return and parameter shapes distinguish overloads, including by-ref parameters.
        private static EngineMember Method(Assembly assembly, string typeName, string name, string returnType,
            params string[] parameterTypes)
            => new(assembly, typeName, MemberTypes.Method, name, Signature(returnType, parameterTypes));

        private static string Signature(string returnType, IEnumerable<string> parameterTypes)
            => $"{returnType}({string.Join(",", parameterTypes)})";

        private static EngineMember ConstructedType(Assembly assembly, string typeName, string baseType)
            => new(assembly, typeName, MemberTypes.TypeInfo, typeName, baseType);

        /// <summary>Every member declared above, read off this type's own static fields so a new declaration needs no
        /// list of its own.</summary>
        internal static IEnumerable<EngineMember> Declared()
            => typeof(EngineMember).GetFields(BindingFlags.Static | BindingFlags.NonPublic)
                .Where(field => field.FieldType == typeof(EngineMember))
                .Select(field => (EngineMember)field.GetValue(null));

        internal FieldInfo? ResolveField() => Resolve() as FieldInfo;

        internal PropertyInfo? ResolveProperty() => Resolve() as PropertyInfo;

        internal Type? ResolveType() => Resolve() as Type;

        internal MethodInfo? ResolveMethod() => Resolve() as MethodInfo;

        public override string ToString() => $"{TypeName}.{Name} ({Kind}: {Shape}{(Writable ? ", writable" : "")})";

        // Null rather than a throw when the member is missing or has changed shape, so a later engine leaves the
        // feature that reads it undone instead of failing the code around it.
        internal MemberInfo? Resolve()
        {
            var type = Assembly.GetType(TypeName);
            if (type == null) return null;

            if (Kind == MemberTypes.TypeInfo)
            {
                return MatchesType(type.BaseType, Shape) && type.GetConstructor(Type.EmptyTypes) != null ? type : null;
            }

            if (Kind == MemberTypes.Method)
            {
                return type.GetMethods(OwnMembers).FirstOrDefault(method => method.Name == Name && MatchesMethod(method, Shape));
            }

            if (Kind == MemberTypes.Field)
            {
                return type.GetField(Name, OwnMembers) is { } field && MatchesType(field.FieldType, Shape) ? field : null;
            }

            return type.GetProperty(Name, OwnMembers) is { GetMethod: not null } property
                   && MatchesType(property.PropertyType, Shape)
                   && (!Writable || property.SetMethod != null)
                ? property
                : null;
        }

        private static bool MatchesMethod(MethodInfo method, string shape)
        {
            var opening = shape.IndexOf('(');
            // MUTANT_SURVIVES(equivalent): < -> <= feeds an empty return shape to MatchesType at opening zero; its element, generic and FullName checks still refuse that shape.
            // The first || -> && leaves both leading checks false: Signature supplies an opening parenthesis and appends the closing one.
            if (opening < 0 || !shape.EndsWith(")", StringComparison.Ordinal)
                || !MatchesType(method.ReturnType, shape.Substring(0, opening))) return false;
            var expected = SplitShapes(shape.Substring(opening + 1, shape.Length - opening - 2));
            var actual = method.GetParameters();
            return actual.Length == expected.Length
                && actual.Select((parameter, index) => MatchesType(parameter.ParameterType, expected[index])).All(matches => matches);
        }

        private static bool MatchesType(Type? type, string shape)
        {
            if (type == null) return false;
            if (shape.StartsWith("enum:", StringComparison.Ordinal))
                return type.IsEnum && MatchesType(type, shape.Substring(5));
            if (shape.EndsWith("&", StringComparison.Ordinal))
                return type.IsByRef && MatchesType(type.GetElementType(), shape.Substring(0, shape.Length - 1));
            if (shape.EndsWith("[]", StringComparison.Ordinal))
                return type.IsSZArray
                    && MatchesType(type.GetElementType(), shape.Substring(0, shape.Length - 2));
            var opening = shape.IndexOf('[');
            // MUTANT_SURVIVES(equivalent, boundary): an opening at zero still compares an empty prefix with the nonempty definition names required by the positive generic-resolution controls.
            if (opening < 0)
                return !type.HasElementType && !type.IsGenericType && type.FullName == shape;
            if (!type.IsGenericType || !shape.EndsWith("]", StringComparison.Ordinal)
                || type.GetGenericTypeDefinition().FullName != shape.Substring(0, opening)) return false;
            var expected = SplitShapes(shape.Substring(opening + 1, shape.Length - opening - 2));
            var actual = type.GetGenericArguments();
            return actual.Length == expected.Length
                && actual.Select((argument, index) => MatchesType(argument, expected[index])).All(matches => matches);
        }

        private static string[] SplitShapes(string shapes)
        {
            if (shapes.Length == 0) return Array.Empty<string>();
            var parts = new List<string>();
            var depth = 0;
            var from = 0;
            for (var i = 0; i < shapes.Length; i++)
            {
                if (shapes[i] == '[') depth++;
                if (shapes[i] == ']') depth--;
                if (shapes[i] != ',' || depth != 0) continue;
                parts.Add(shapes.Substring(from, i - from));
                from = i + 1;
            }
            parts.Add(shapes.Substring(from));
            return parts.ToArray();
        }
    }
}
