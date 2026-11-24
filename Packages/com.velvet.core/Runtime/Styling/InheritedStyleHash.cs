#nullable enable
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet
{
    // A writer that can run inside CustomStyleResolvedEvent calls this after writing an inherited property on the element
    // the event was sent to. Without it, a child the same style pass styles after the element is cached under the hash of
    // the value the write replaced, and a later element of that child's type, matched rules and variables whose parent
    // still holds that value is given the written one (InEventInheritedWritePanelTests, and FontFamilyFacesPanelTests'
    // two-bold-containers case).
    internal static class InheritedStyleHash
    {
        private static readonly FieldInfo? s_style = EngineMember.ElementComputedStyle.ResolveField();
        private static readonly FieldInfo? s_inheritedData = EngineMember.ComputedStyleInheritedData.ResolveField();
        private static readonly FieldInfo? s_hash = EngineMember.ElementInheritedStylesHash.ResolveField();

        internal static bool Resolves { get; } = s_style != null && s_inheritedData != null && s_hash != null;

        internal static void Refresh(VisualElement element)
        {
#if !ENABLE_IL2CPP
            // MUTANT_SURVIVES(equivalent, equality): the reflection below stores the hash the compiled accessor stores; the two differ in what they allocate alone.
            if (s_compiled != null)
            {
                s_compiled(element);
                return;
            }
#endif
            if (Resolves)
            {
                s_hash!.SetValue(element, s_inheritedData!.GetValue(s_style!.GetValue(element)).GetHashCode());
            }
        }

#if !ENABLE_IL2CPP
        // Compiled once, as StyleCascade's readers are, so that a refresh boxes neither the computed style nor the hash,
        // and writes the hash only where it moved. Null where a member does not resolve; under IL2CPP the reflection
        // above runs instead (Given_TheEditor_When_InheritedStyleHashLoads_Then_ItRefreshesThroughACompiledAccessor).
        private static readonly System.Action<VisualElement>? s_compiled = Compile();

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
