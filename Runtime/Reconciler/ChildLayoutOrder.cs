#nullable enable
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet
{
    // UI Toolkit keeps a parent's layout children in a list of their own, written at the same index as its paint
    // children by every insert, removal and sort, so the two orders never differ unless something rewrites the
    // layout list. This rewrites it, for FiberZLayerCoordinator: a relocated element paints from a band but lays out
    // at its declared slot. Every later insert, removal or sort the engine makes writes both lists at the same
    // index again, so the coordinator puts the layout list back in paint order (Rebuild with the paint order) before
    // any removal of an element it exchanged.
    internal static class ChildLayoutOrder
    {
        private static readonly FieldInfo? s_node = EngineMember.ElementLayoutNode.ResolveField();
        private static readonly MethodInfo? s_clear = EngineMember.LayoutNodeClear.ResolveMethod();
        private static readonly MethodInfo? s_insert = EngineMember.LayoutNodeInsert.ResolveMethod();

        // MUTANT_SURVIVES(unreachable): EngineMemberResolutionTests holds that each of these resolves.
        internal static bool Available => s_node != null && s_clear != null && s_insert != null;

        // Lays parent's children out in order, a permutation of parent's own children.
        internal static void Rebuild(VisualElement parent, IReadOnlyList<VisualElement> order)
        {
            if (!Available)
            {
                return;
            }
            var node = s_node!.GetValue(parent);
            s_clear!.Invoke(node, null);
            var args = new object[2];
            for (var i = 0; i < order.Count; i++)
            {
                args[0] = i;
                args[1] = s_node.GetValue(order[i]);
                s_insert!.Invoke(node, args);
            }
        }
    }
}
