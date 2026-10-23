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

        // BundledClassTransform's scan of the rules a stylesheet declares transforms in.
        internal static readonly EngineMember StyleSheetRules =
            ReadProperty(UIElements, StyleSheetType, "rules", "UnityEngine.UIElements.StyleRule[]");

        internal static readonly EngineMember StyleRuleProperties = ReadProperty(UIElements, "UnityEngine.UIElements.StyleRule",
            "properties", "UnityEngine.UIElements.StyleProperty[]");

        internal static readonly EngineMember StyleRuleComplexSelectors = ReadProperty(UIElements,
            "UnityEngine.UIElements.StyleRule", "complexSelectors", "UnityEngine.UIElements.StyleComplexSelector[]");

        internal static readonly EngineMember StylePropertyName =
            ReadProperty(UIElements, "UnityEngine.UIElements.StyleProperty", "name", "System.String");

        internal static readonly EngineMember StyleComplexSelectorSelectors = ReadProperty(UIElements,
            "UnityEngine.UIElements.StyleComplexSelector", "selectors", "UnityEngine.UIElements.StyleSelector[]");

        internal static readonly EngineMember StyleSelectorParts = ReadProperty(UIElements,
            "UnityEngine.UIElements.StyleSelector", "parts", "UnityEngine.UIElements.StyleSelectorPart[]");

        internal static readonly EngineMember StyleSelectorPartValue =
            ReadProperty(UIElements, "UnityEngine.UIElements.StyleSelectorPart", "value", "System.String");

        internal static readonly EngineMember StyleSelectorPartType = ReadProperty(UIElements,
            "UnityEngine.UIElements.StyleSelectorPart", "type", "enum:UnityEngine.UIElements.StyleSelectorType");

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

        internal static readonly EngineMember ComputedStyleBorderLeftColor =
            ReadProperty(UIElements, ComputedStyleType, "borderLeftColor", "UnityEngine.Color");

        internal static readonly EngineMember ComputedStyleBorderRightColor =
            ReadProperty(UIElements, ComputedStyleType, "borderRightColor", "UnityEngine.Color");

        internal static readonly EngineMember ComputedStyleBorderTopColor =
            ReadProperty(UIElements, ComputedStyleType, "borderTopColor", "UnityEngine.Color");

        internal static readonly EngineMember ComputedStyleBorderBottomColor =
            ReadProperty(UIElements, ComputedStyleType, "borderBottomColor", "UnityEngine.Color");

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

        // StyleFilterEngineWrite's filter write past a running transform-origin transition.
        internal static readonly EngineMember CancelStyleAnimation = Method(UIElements,
            VisualElementType, "CancelAnimation", "System.Void", "enum:UnityEngine.UIElements.StyleSheets.StylePropertyId");

        internal static readonly EngineMember StylePropertyNameId = ReadProperty(UIElements,
            "UnityEngine.UIElements.StylePropertyName", "id", StylePropertyIdShape);

        // StyleCascade's cascaded corner radii, and the longhands a shorthand in a transition list stands for.
        internal static readonly EngineMember ComputedStyleBorderTopLeftRadius =
            ReadProperty(UIElements, ComputedStyleType, "borderTopLeftRadius", "UnityEngine.UIElements.Length");

        internal static readonly EngineMember ComputedStyleBorderTopRightRadius =
            ReadProperty(UIElements, ComputedStyleType, "borderTopRightRadius", "UnityEngine.UIElements.Length");

        internal static readonly EngineMember ComputedStyleBorderBottomRightRadius =
            ReadProperty(UIElements, ComputedStyleType, "borderBottomRightRadius", "UnityEngine.UIElements.Length");

        internal static readonly EngineMember ComputedStyleBorderBottomLeftRadius =
            ReadProperty(UIElements, ComputedStyleType, "borderBottomLeftRadius", "UnityEngine.UIElements.Length");

        // ProjectedCornerFace's reading of the face its rules give an element beneath the inline slots it writes, with the
        // border colours above.
        internal static readonly EngineMember ComputedStyleBackgroundColor =
            ReadProperty(UIElements, ComputedStyleType, "backgroundColor", "UnityEngine.Color");

        internal static readonly EngineMember ComputedStyleBackgroundImage =
            ReadProperty(UIElements, ComputedStyleType, "backgroundImage", "UnityEngine.UIElements.Background");

        internal static readonly EngineMember ComputedStyleBackgroundPositionX =
            ReadProperty(UIElements, ComputedStyleType, "backgroundPositionX", "UnityEngine.UIElements.BackgroundPosition");

        internal static readonly EngineMember ComputedStyleBackgroundPositionY =
            ReadProperty(UIElements, ComputedStyleType, "backgroundPositionY", "UnityEngine.UIElements.BackgroundPosition");

        internal static readonly EngineMember ComputedStyleBackgroundRepeat =
            ReadProperty(UIElements, ComputedStyleType, "backgroundRepeat", "UnityEngine.UIElements.BackgroundRepeat");

        internal static readonly EngineMember ComputedStyleBackgroundSize =
            ReadProperty(UIElements, ComputedStyleType, "backgroundSize", "UnityEngine.UIElements.BackgroundSize");

        // ProjectedCornerFace's reading of whether an element clips its content: the element's own computed style, inline
        // style included, since IResolvedStyle carries no overflow.
        internal static readonly EngineMember ComputedStyleOverflow =
            ReadProperty(UIElements, ComputedStyleType, "overflow", "enum:UnityEngine.UIElements.OverflowInternal");

        // BackdropFilterLayer's mapping of an element onto the target UI Toolkit is drawing it into: the viewport and
        // projection the renderer has set while it runs an immediate-mode element, and the rectangle it clips that
        // element to.
        private const string RendererUtilityType = "UnityEngine.UIElements.UIR.Utility";

        internal static readonly EngineMember RendererActiveViewport =
            Method(UIElements, RendererUtilityType, "GetActiveViewport", "UnityEngine.RectInt");

        internal static readonly EngineMember RendererProjection =
            Method(UIElements, RendererUtilityType, "GetUnityProjectionMatrix", "UnityEngine.Matrix4x4");

        internal static readonly EngineMember ElementWorldClip =
            ReadProperty(UIElements, VisualElementType, "worldClip", "UnityEngine.Rect");

        // The stencil test the renderer draws an element's children under, which a masking ancestor (a rounded overflow
        // clip, a clip-path wrapper) changes: an element's render data, the one its filter roots its children in where
        // it has one, and that data's reference and mask depth.
        private const string RenderDataType = "UnityEngine.UIElements.UIR.RenderData";

        internal static readonly EngineMember ElementRenderData = Field(UIElements, VisualElementType, "renderData", RenderDataType);

        internal static readonly EngineMember ElementNestedRenderData =
            Field(UIElements, VisualElementType, "nestedRenderData", RenderDataType);

        internal static readonly EngineMember RenderDataChildrenStencilRef =
            Field(UIElements, RenderDataType, "childrenStencilRef", "System.Int32");

        internal static readonly EngineMember RenderDataChildrenMaskDepth =
            Field(UIElements, RenderDataType, "childrenMaskDepth", "System.Int32");

        internal static readonly EngineMember IsMatchingShorthand = Method(UIElements,
            "UnityEngine.UIElements.StyleSheets.StylePropertyUtil", "IsMatchingShorthand", "System.Boolean",
            StylePropertyIdShape, StylePropertyIdShape);

        // The transitions a panel's style animation system is running, read by NativeTransitionVelocity (a running
        // transition's timing and values) and RunningStyleTransition (the same, and what a reversal of it runs back to).
        // The generic members declare the shape "*", which any type satisfies, because a field typed by a generic
        // parameter has no spelling.
        private const string AnimationSystemType = "UnityEngine.UIElements.StylePropertyAnimationSystem";
        private const string StylePropertyIdType = "UnityEngine.UIElements.StyleSheets.StylePropertyId";
        private const string StylePropertyIdShape = "enum:" + StylePropertyIdType;

        internal static readonly EngineMember PanelStyleAnimationSystem = ReadProperty(UIElements,
            "UnityEngine.UIElements.BaseVisualElementPanel", "styleAnimationSystem",
            "UnityEngine.UIElements.IStylePropertyAnimationSystem");

        internal static readonly EngineMember AnimationSystemValues = Field(UIElements, AnimationSystemType,
            "m_PropertyToValues", "System.Collections.Generic.Dictionary`2[" + StylePropertyIdShape + ","
            + AnimationSystemType + "+Values]");

        internal static readonly EngineMember AnimationSystemFloats =
            Field(UIElements, AnimationSystemType, "m_Floats", AnimationSystemType + "+ValuesFloat");

        internal static readonly EngineMember AnimationSystemRotates =
            Field(UIElements, AnimationSystemType, "m_Rotate", AnimationSystemType + "+ValuesRotate");

        internal static readonly EngineMember AnimationSystemTranslates =
            Field(UIElements, AnimationSystemType, "m_Translate", AnimationSystemType + "+ValuesTranslate");

        internal static readonly EngineMember AnimationSystemScales =
            Field(UIElements, AnimationSystemType, "m_Scale", AnimationSystemType + "+ValuesScale");

        internal static readonly EngineMember AnimationSystemTime =
            Field(UIElements, AnimationSystemType, "m_CurrentTime", "System.Double");

        internal static readonly EngineMember AnimationSystemLengths =
            Field(UIElements, AnimationSystemType, "m_Lengths", AnimationSystemType + "+ValuesLength");

        internal static readonly EngineMember AnimationValuesRunning =
            Field(UIElements, AnimationSystemType + "+Values`1", "running", "*");

        internal static readonly EngineMember AnimationDataSetIndexOf = Method(UIElements,
            AnimationSystemType + "+AnimationDataSet`2", "IndexOf", "System.Boolean",
            "UnityEngine.UIElements.VisualElement", StylePropertyIdShape, "System.Int32&");

        internal static readonly EngineMember AnimationDataSetTiming =
            Field(UIElements, AnimationSystemType + "+AnimationDataSet`2", "timing", "*");

        internal static readonly EngineMember AnimationDataSetStyle =
            Field(UIElements, AnimationSystemType + "+AnimationDataSet`2", "style", "*");

        internal static readonly EngineMember AnimationTimingStart =
            Field(UIElements, AnimationSystemType + "+Values`1+TimingData", "startTime", "System.Double");

        internal static readonly EngineMember AnimationTimingDuration =
            Field(UIElements, AnimationSystemType + "+Values`1+TimingData", "duration", "System.Single");

        internal static readonly EngineMember AnimationTimingEasing = Field(UIElements,
            AnimationSystemType + "+Values`1+TimingData", "easingCurve", "System.Func`2[System.Single,System.Single]");

        internal static readonly EngineMember AnimationTimingEased =
            Field(UIElements, AnimationSystemType + "+Values`1+TimingData", "easedProgress", "System.Single");

        internal static readonly EngineMember AnimationTimingReversingShortening = Field(UIElements,
            AnimationSystemType + "+Values`1+TimingData", "reversingShorteningFactor", "System.Single");

        internal static readonly EngineMember AnimationStyleStart =
            Field(UIElements, AnimationSystemType + "+Values`1+StyleData", "startValue", "*");

        internal static readonly EngineMember AnimationStyleEnd =
            Field(UIElements, AnimationSystemType + "+Values`1+StyleData", "endValue", "*");

        internal static readonly EngineMember AnimationStyleReversingStart = Field(UIElements,
            AnimationSystemType + "+Values`1+StyleData", "reversingAdjustedStartValue", "*");

        internal static readonly EngineMember AnimationCurrentValue =
            Field(UIElements, AnimationSystemType + "+Values`1+StyleData", "currentValue", "*");

        internal static readonly EngineMember OpacityPropertyId =
            Field(UIElements, StylePropertyIdType, "Opacity", StylePropertyIdShape);

        internal static readonly EngineMember RotatePropertyId =
            Field(UIElements, StylePropertyIdType, "Rotate", StylePropertyIdShape);

        internal static readonly EngineMember TranslatePropertyId =
            Field(UIElements, StylePropertyIdType, "Translate", StylePropertyIdShape);

        internal static readonly EngineMember ScalePropertyId =
            Field(UIElements, StylePropertyIdType, "Scale", StylePropertyIdShape);

        internal static readonly EngineMember BorderTopLeftRadiusPropertyId =
            Field(UIElements, StylePropertyIdType, "BorderTopLeftRadius", StylePropertyIdShape);

        internal static readonly EngineMember BorderTopRightRadiusPropertyId =
            Field(UIElements, StylePropertyIdType, "BorderTopRightRadius", StylePropertyIdShape);

        internal static readonly EngineMember BorderBottomRightRadiusPropertyId =
            Field(UIElements, StylePropertyIdType, "BorderBottomRightRadius", StylePropertyIdShape);

        internal static readonly EngineMember BorderBottomLeftRadiusPropertyId =
            Field(UIElements, StylePropertyIdType, "BorderBottomLeftRadius", StylePropertyIdShape);

        // FiberLayoutReflow's layout computation ahead of a commit's layout reads.
        private const string UpdatePhaseType = "UnityEngine.UIElements.VisualTreeUpdatePhase";
        private const string UpdatePhaseShape = "enum:" + UpdatePhaseType;
        private const string LayoutNodeType = "UnityEngine.UIElements.Layout.LayoutNode";
        private const string VersionChangeType = "UnityEngine.UIElements.VersionChangeType";

        internal static readonly EngineMember PanelGetUpdater = Method(UIElements, "UnityEngine.UIElements.Panel",
            "GetUpdater", "UnityEngine.UIElements.IVisualTreeUpdater", UpdatePhaseShape);

        internal static readonly EngineMember PanelVersion =
            ReadProperty(UIElements, "UnityEngine.UIElements.Panel", "version", "System.UInt32");

        internal static readonly EngineMember StyleUpdaterApplying =
            Field(UIElements, "UnityEngine.UIElements.VisualTreeStyleUpdater", "m_IsApplyingStyles", "System.Boolean");

        internal static readonly EngineMember StylesUpdatePhase = Field(UIElements, UpdatePhaseType, "Styles", UpdatePhaseShape);

        internal static readonly EngineMember StyleUpdaterUpdate =
            Method(UIElements, "UnityEngine.UIElements.VisualTreeStyleUpdater", "Update", "System.Void");

        internal static readonly EngineMember ElementLayoutNode =
            Field(UIElements, VisualElementType, "m_LayoutNode", LayoutNodeType);

        internal static readonly EngineMember LayoutNodeCalculate =
            Method(UIElements, LayoutNodeType, "CalculateLayout", "System.Void", "System.Single", "System.Single");

        internal static readonly EngineMember LayoutNodeDirty =
            WrittenProperty(UIElements, LayoutNodeType, "IsDirty", "System.Boolean");

        internal static readonly EngineMember LayoutNodeHasNewLayout =
            ReadProperty(UIElements, LayoutNodeType, "HasNewLayout", "System.Boolean");

        internal static readonly EngineMember ElementLastLayout = Field(UIElements, VisualElementType, "lastLayout", "UnityEngine.Rect");

        internal static readonly EngineMember ElementIncrementVersion = Method(UIElements, VisualElementType,
            "IncrementVersion", "System.Void", "enum:" + VersionChangeType);

        internal static readonly EngineMember TransformVersionChange =
            Field(UIElements, VersionChangeType, "Transform", "enum:" + VersionChangeType);

        internal static readonly EngineMember PanelDuringLayoutPhase = ReadProperty(UIElements,
            "UnityEngine.UIElements.BaseVisualElementPanel", "duringLayoutPhase", "System.Boolean");

        // A shape every type satisfies, for a member typed by a generic parameter.
        private const string AnyShape = "*";
        // StyleCustomProperties' reading of the custom properties a stylesheet declares.
        private const string VariableContextType = "UnityEngine.UIElements.StyleVariableContext";
        private const string StyleVariableType = "UnityEngine.UIElements.StyleVariable";
        private const string StyleValueHandleType = "UnityEngine.UIElements.StyleValueHandle";

        internal static readonly EngineMember ElementVariableContext =
            Field(UIElements, VisualElementType, "variableContext", VariableContextType);

        internal static readonly EngineMember VariableContextVariables = ReadProperty(UIElements, VariableContextType,
            "variables", "System.Collections.Generic.List`1[" + StyleVariableType + "]");

        internal static readonly EngineMember StyleVariableName = Field(UIElements, StyleVariableType, "name", "System.String");

        internal static readonly EngineMember StyleVariableSheet = Field(UIElements, StyleVariableType, "sheet", StyleSheetType);

        internal static readonly EngineMember StyleVariableHandles =
            Field(UIElements, StyleVariableType, "handles", StyleValueHandleType + "[]");

        internal static readonly EngineMember StyleValueHandleKind = ReadProperty(UIElements, StyleValueHandleType,
            "valueType", "enum:UnityEngine.UIElements.StyleValueType");

        internal static readonly EngineMember StyleSheetReadAsString = Method(UIElements,
            "UnityEngine.UIElements.StyleSheets.StyleSheetExtensions", "ReadAsString", "System.String", StyleSheetType,
            StyleValueHandleType);

        internal static readonly EngineMember StyleSheetReadColor =
            Method(UIElements, StyleSheetType, "ReadColor", "UnityEngine.Color", StyleValueHandleType);

        internal static readonly EngineMember StyleSheetReadFloat =
            Method(UIElements, StyleSheetType, "ReadFloat", "System.Single", StyleValueHandleType);

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
        /// arrays use <c>[]</c>, and by-ref parameters use <c>&amp;</c>; <c>*</c> accepts any type. An <c>enum:</c> prefix requires an enum type.</summary>
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
            if (shape == AnyShape) return true;
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
