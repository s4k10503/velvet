using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Each element's own --tw-ring-color, --tw-ring-offset-color and outline-color, for a descendant's ring-inherit,
    // ring-offset-inherit and outline-inherit. `inherit` takes the parent's value, which may itself be an inherit, and
    // a currentcolor among them resolves against the inheriting element's own text colour. Where the parent sets none,
    // its value is the property's initial one: unset for the ring colour, whose var() then falls back to
    // currentcolor; #fff for the ring offset, as Tailwind registers it; currentcolor for outline-color. Written by the
    // ring applier on every element carrying a ring class; the bands re-read it each frame
    // (RingOverlay.SyncElementState), so a change reaches them.
    internal static class BandColourInheritance
    {
        internal enum Family
        {
            Ring,
            Offset,
            Outline,
        }

        // One family's own colour: a literal (Set), currentcolor or inherit (Reference), or none of them.
        internal readonly struct Own
        {
            public readonly bool Set;
            public readonly ColorReference Reference;
            public readonly Color Color;

            public Own(bool set, ColorReference reference, Color color)
            {
                Set = set;
                Reference = set ? ColorReference.None : reference;
                Color = color;
            }

            public bool Names => Set || Reference != ColorReference.None;
        }

        private sealed class Owned
        {
            public Own Ring;
            public Own Offset;
            public Own Outline;

            public Own Of(Family family) => family switch
            {
                Family.Ring => Ring,
                Family.Offset => Offset,
                _ => Outline,
            };
        }

        private static readonly ConditionalWeakTable<VisualElement, Owned> s_owned = new();

        internal static void Record(VisualElement element, Own ring, Own offset, Own outline)
        {
            if (!ring.Names && !offset.Names && !outline.Names)
            {
                s_owned.Remove(element);
                return;
            }
            var owned = s_owned.GetValue(element, static _ => new Owned());
            (owned.Ring, owned.Offset, owned.Outline) = (ring, offset, outline);
        }

        internal static void Forget(VisualElement element) => s_owned.Remove(element);

        // The colour family's -inherit resolves to on element.
        internal static Color Resolve(VisualElement element, Family family)
        {
            for (var ancestor = ClipPathLayoutBox.Of(element).hierarchy.parent; ancestor != null; ancestor = ancestor.hierarchy.parent)
            {
                if (!s_owned.TryGetValue(ancestor, out var owned))
                {
                    break;
                }
                var own = owned.Of(family);
                if (own.Set)
                {
                    return own.Color;
                }
                if (own.Reference != ColorReference.Inherit)
                {
                    return own.Reference == ColorReference.Current || family != Family.Offset
                        ? element.resolvedStyle.color
                        : Color.white;
                }
            }
            return family == Family.Offset ? Color.white : element.resolvedStyle.color;
        }
    }
}
