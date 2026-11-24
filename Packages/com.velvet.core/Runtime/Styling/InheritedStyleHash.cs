#nullable enable
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet
{
    // A writer that can run inside CustomStyleResolvedEvent calls this where it may have written an inherited property on
    // the element the event was sent to. Without it, a child the same style pass styles after the element is cached under
    // the hash of the value the write replaced, and a later element of that child's type, matched rules and variables
    // whose parent still holds that value is given the written one (InEventInheritedWritePanelTests, and
    // FontFamilyFacesPanelTests' two-bold-containers case). Each path stores the hash the style pass stores
    // (InheritedStyleHashTests).
    internal static class InheritedStyleHash
    {
        private static readonly FieldInfo? s_style = EngineMember.ElementComputedStyle.ResolveField();
        private static readonly FieldInfo? s_inheritedData = EngineMember.ComputedStyleInheritedData.ResolveField();
        private static readonly FieldInfo? s_hash = EngineMember.ElementInheritedStylesHash.ResolveField();

        internal static bool Resolves { get; } = s_style != null && s_inheritedData != null && s_hash != null;

        internal static void Refresh(VisualElement element)
        {
#if !ENABLE_IL2CPP
            if (s_compiled != null)
            {
                s_compiled(element);
                return;
            }
#endif
            RefreshByReflection(element);
        }

        // What IL2CPP, which compiles nothing at run time, and an editor where a member does not resolve refresh through.
        private static void RefreshByReflection(VisualElement element)
        {
            if (Resolves)
            {
                s_hash!.SetValue(element, s_inheritedData!.GetValue(s_style!.GetValue(element)).GetHashCode());
            }
        }

#if !ENABLE_IL2CPP
        // Compiled once, as StyleCascade's readers are, so that a refresh allocates nothing (InheritedStyleHashTests).
        // Null where a member does not resolve. Not readonly: InheritedStyleHashTests nulls it, as an IL2CPP player has
        // none, to drive Refresh down the reflection path.
        private static System.Action<VisualElement>? s_compiled = Compile();

        private static System.Action<VisualElement>? Compile()
        {
            System.Action<VisualElement>? compiled = null;
            try
            {
                var element = System.Linq.Expressions.Expression.Parameter(typeof(VisualElement), "refreshed");
                var data = System.Linq.Expressions.Expression.Field(
                    System.Linq.Expressions.Expression.Field(element, s_style!), s_inheritedData!);
                var content = System.Linq.Expressions.Expression.Call(data,
                    s_inheritedData!.FieldType.GetMethod(nameof(object.GetHashCode), System.Type.EmptyTypes)!);
                var stored = System.Linq.Expressions.Expression.Field(element, s_hash!);
                var hash = System.Linq.Expressions.Expression.Variable(typeof(int), "hash");
                var body = System.Linq.Expressions.Expression.Block(new[] { hash },
                    System.Linq.Expressions.Expression.Assign(hash, content),
                    System.Linq.Expressions.Expression.IfThen(System.Linq.Expressions.Expression.NotEqual(stored, hash),
                        System.Linq.Expressions.Expression.Assign(stored, hash)));
                compiled = System.Linq.Expressions.Expression.Lambda<System.Action<VisualElement>>(body, element).Compile();
            }
            catch (System.Exception)
            {
                // Left null, so that Refresh reads by reflection.
            }
            return compiled;
        }
#endif
    }
}
