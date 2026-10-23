using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Reads where FiberZLayerCoordinator painted a parent's relocated children: the band in front of its declared
    /// slots, after them, or neither.
    /// </summary>
    public static class ZBandProbe
    {
        /// <summary>
        /// The relocated elements physically after (front) or before (back) every declared slot of parent, in
        /// paint order, each followed by the ring bands and face border overlays hosted after it. Empty when none.
        /// </summary>
        public static List<VisualElement> Band(VisualElement parent, bool front)
        {
            var band = new List<VisualElement>();
            if (parent == null)
            {
                return band;
            }
            var first = -1;
            var last = -1;
            for (var i = 0; i < parent.hierarchy.childCount; i++)
            {
                if (!LogicalChildSlots.IsInvisible(parent.hierarchy[i]))
                {
                    first = first < 0 ? i : first;
                    last = i;
                }
            }
            var from = front ? last + 1 : 0;
            var to = front ? parent.hierarchy.childCount : (first < 0 ? parent.hierarchy.childCount : first);
            var following = false;
            for (var i = from; i < to; i++)
            {
                var child = parent.hierarchy[i];
                if (FiberZLayerCoordinator.IsRelocatedReal(child))
                {
                    band.Add(child);
                    following = true;
                }
                else if (following && IsOverlay(child))
                {
                    band.Add(child);
                }
                else
                {
                    following = false;
                }
            }
            return band;
        }

        /// <summary>Whether element paints in its parent's front (or back) band.</summary>
        public static bool InBand(VisualElement element, bool front)
            => element?.parent != null && Band(element.parent, front).Contains(element);

        /// <summary>The band's elements' names joined by commas, a ring band read as "band".</summary>
        public static string Names(List<VisualElement> band)
        {
            var names = new List<string>();
            foreach (var element in band)
            {
                names.Add(element.ClassListContains(RingOverlay.MarkerClass) ? "band" : element.name);
            }
            return string.Join(",", names);
        }

        /// <summary>
        /// The names of parent's children that paint, in paint order: placeholders, anchors and retired children
        /// left out, a relocated element at the place it paints, a ring band read as "band".
        /// </summary>
        public static string PaintOrder(VisualElement parent)
        {
            var names = new List<string>();
            for (var i = 0; i < parent.hierarchy.childCount; i++)
            {
                var child = parent.hierarchy[i];
                if (FiberZLayerCoordinator.IsInternal(child) || IsPlaceholder(child)
                    || child.ClassListContains(SilhouetteBoundsSpacer.MarkerClass))
                {
                    continue;
                }
                names.Add(child.ClassListContains(RingOverlay.MarkerClass) ? "band" : child.name);
            }
            return string.Join(",", names);
        }

        /// <summary>
        /// A bare element held as FiberZLayerCoordinator holds an element it relocated into a back band, standing
        /// in for one where a case needs only the kind.
        /// </summary>
        /// <exception cref="MissingMemberException">The coordinator's hold or its role type was renamed.</exception>
        public static VisualElement BackBandMember(string name)
        {
            var element = new VisualElement { name = name };
            var coordinator = typeof(FiberZLayerCoordinator);
            var role = coordinator.GetNestedType("Role", BindingFlags.NonPublic)
                ?? throw new MissingMemberException(coordinator.FullName, "Role");
            var hold = coordinator.GetMethod("Hold", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException(coordinator.FullName, "Hold");
            hold.Invoke(null, new[] { element, Enum.Parse(role, "BackReal"), null, null });
            return element;
        }

        private static bool IsOverlay(VisualElement child)
            => child.ClassListContains(RingOverlay.MarkerClass) || child.ClassListContains(FaceBorderOverlay.MarkerClass);

        // A declared slot standing for an element painted elsewhere, as FiberZLayerCoordinator holds it.
        private static bool IsPlaceholder(VisualElement child)
        {
            var coordinator = typeof(FiberZLayerCoordinator);
            var table = coordinator.GetField("s_held", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
                ?? throw new MissingFieldException(coordinator.FullName, "s_held");
            var args = new object?[] { child, null };
            if (!(bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, args))
            {
                return false;
            }
            var role = args[1]!.GetType().GetField("Role")?.GetValue(args[1])
                ?? throw new MissingFieldException(coordinator.FullName, "Held.Role");
            return role.ToString() == "Slot";
        }
    }
}
