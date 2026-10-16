using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Each element's own --tw-shadow-color and --tw-inset-shadow-color, for a descendant's shadow-inherit and
    // inset-shadow-inherit. The two are registered as not inheriting, so `inherit` takes the parent's value, which
    // may itself be an inherit, and a currentcolor among them resolves against the inheriting element's own text
    // colour. Written by the shadow applier (box shadow) and the ring applier (inset shadow) on every element
    // carrying such a class, whether or not a size draws a shadow there.
    internal static class ShadowColorInheritance
    {
        private sealed class Own
        {
            public ShadowColorSource Source;
            public Color Color;
        }

        private static readonly ConditionalWeakTable<VisualElement, Own> s_box = new();
        private static readonly ConditionalWeakTable<VisualElement, Own> s_inset = new();

        private static ConditionalWeakTable<VisualElement, Own> TableOf(bool inset) => inset ? s_inset : s_box;

        // Records element's own colour, or its absence, and repaints the descendants inheriting it when it changed.
        internal static void Record(VisualElement element, bool inset, bool set, ShadowColorSource source, Color color)
        {
            var table = TableOf(inset);
            var had = table.TryGetValue(element, out var own);
            if (!set)
            {
                if (had)
                {
                    table.Remove(element);
                    RepaintInheritors(element, inset);
                }
                return;
            }
            if (had && own!.Source == source && own.Color == color)
            {
                return;
            }
            table.AddOrUpdate(element, new Own { Source = source, Color = color });
            RepaintInheritors(element, inset);
        }

        internal static void Forget(VisualElement element, bool inset) => Record(element, inset, false, default, default);

        // The colour shadow-inherit (inset-shadow-inherit where inset) resolves to on element.
        internal static bool TryInherited(VisualElement element, bool inset, out Color color)
        {
            var table = TableOf(inset);
            for (var ancestor = element.parent; ancestor != null; ancestor = ancestor.parent)
            {
                if (!table.TryGetValue(ancestor, out var own))
                {
                    break;
                }
                switch (own.Source)
                {
                    case ShadowColorSource.Explicit:
                        color = own.Color;
                        return true;
                    case ShadowColorSource.Current:
                        color = element.resolvedStyle.color;
                        color.a *= own.Color.a;
                        return true;
                }
            }
            color = default;
            return false;
        }

        // A changed colour reaches every descendant that inherits it, through any number of inherits between, and
        // none of those is otherwise told: their own classes did not change.
        private static void RepaintInheritors(VisualElement element, bool inset)
        {
            for (var i = 0; i < element.hierarchy.childCount; i++)
            {
                var child = element.hierarchy[i];
                var inherits = inset
                    ? RingOverlay.TryGet(child) is { Spec: { HasInsetShadow: true, InsetShadow: { InheritColor: true } } }
                    : DropShadowSilhouette.TryGet(child) is { Spec: { InheritColor: true } };
                if (inherits)
                {
                    child.MarkDirtyRepaint();
                }
                RepaintInheritors(child, inset);
            }
        }
    }
}
