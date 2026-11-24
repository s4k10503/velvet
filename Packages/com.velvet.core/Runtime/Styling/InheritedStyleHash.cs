#nullable enable
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet
{
    // A writer that can run inside CustomStyleResolvedEvent calls this after writing an inherited property on the element
    // the event was sent to. Without it, a child the same style pass styles after the element is cached under the hash of
    // the value the write replaced, and an element whose parent still holds that value is given the written one
    // (InEventInheritedWritePanelTests, and FontFamilyFacesPanelTests' two-bold-containers case).
    internal static class InheritedStyleHash
    {
        private static readonly FieldInfo? s_style = EngineMember.ElementComputedStyle.ResolveField();
        private static readonly FieldInfo? s_inheritedData = EngineMember.ComputedStyleInheritedData.ResolveField();
        private static readonly FieldInfo? s_hash = EngineMember.ElementInheritedStylesHash.ResolveField();

        internal static bool Resolves { get; } = s_style != null && s_inheritedData != null && s_hash != null;

        internal static void Refresh(VisualElement element)
        {
            if (Resolves)
            {
                s_hash!.SetValue(element, s_inheritedData!.GetValue(s_style!.GetValue(element)).GetHashCode());
            }
        }
    }
}
