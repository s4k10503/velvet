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
    /// resolves against the editor it runs in are the set the runtime reads. <c>EngineMemberRegistryTests</c> fails
    /// on a lookup by name made anywhere else in the runtime.
    /// </summary>
    internal sealed class EngineMember
    {
        private static readonly Assembly UIElements = typeof(VisualElement).Assembly;

        private const string FocusControllerType = "UnityEngine.UIElements.FocusController";
        private const string VisualElementType = "UnityEngine.UIElements.VisualElement";
        private const string FocusableType = "UnityEngine.UIElements.Focusable";
        private const string StyleSheetType = "UnityEngine.UIElements.StyleSheet";
        private const string PseudoStatesType = "UnityEngine.UIElements.PseudoStates";

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
            WrittenProperty(UIElements, VisualElementType, "pseudoStates", PseudoStatesType);

        internal static readonly EngineMember FocusPseudoState = Field(UIElements, PseudoStatesType, "Focus", PseudoStatesType);

        // FocusManager's composite-field scope.
        internal static readonly EngineMember IsCompositeRoot =
            ReadProperty(UIElements, VisualElementType, "isCompositeRoot", "System.Boolean");

        // EnabledSelfWrites.
        internal static readonly EngineMember PropertyChangedEvent = ConstructedType(UIElements,
            "UnityEngine.UIElements.PropertyChangedEvent",
            "UnityEngine.UIElements.EventBase`1[UnityEngine.UIElements.PropertyChangedEvent]");

        // VelvetStyleUtilities' import walk.
        internal static readonly EngineMember StyleSheetImports =
            Field(UIElements, StyleSheetType, "imports", "UnityEngine.UIElements.StyleSheet+ImportStruct[]");

        internal static readonly EngineMember ImportedStyleSheet =
            Field(UIElements, StyleSheetType + "+ImportStruct", "styleSheet", StyleSheetType);

        private const BindingFlags OwnMembers = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        internal readonly Assembly Assembly;

        /// <summary>The declaring type's <see cref="Type.FullName"/>, with <c>+</c> before a nested type.</summary>
        internal readonly string TypeName;

        internal readonly MemberTypes Kind;

        /// <summary>The member's name; for <see cref="MemberTypes.TypeInfo"/>, the same as <see cref="TypeName"/>.</summary>
        internal readonly string Name;

        /// <summary>A field's or a property's type, or a type's base type, as <see cref="Type.ToString"/> renders
        /// it.</summary>
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

        // Constructed through its parameterless constructor, which is kept with it.
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

        public override string ToString() => $"{TypeName}.{Name} ({Kind}: {Shape}{(Writable ? ", writable" : "")})";

        // Null rather than a throw when the member is missing or has changed shape, so a later engine leaves the
        // feature that reads it undone instead of failing the code around it.
        internal MemberInfo? Resolve()
        {
            var type = Assembly.GetType(TypeName);
            if (type == null) return null;

            if (Kind == MemberTypes.TypeInfo)
            {
                return type.BaseType?.ToString() == Shape && type.GetConstructor(Type.EmptyTypes) != null ? type : null;
            }

            if (Kind == MemberTypes.Field)
            {
                return type.GetField(Name, OwnMembers) is { } field && field.FieldType.ToString() == Shape ? field : null;
            }

            return type.GetProperty(Name, OwnMembers) is { GetMethod: not null } property
                   && property.PropertyType.ToString() == Shape
                   && (!Writable || property.SetMethod != null)
                ? property
                : null;
        }
    }
}
