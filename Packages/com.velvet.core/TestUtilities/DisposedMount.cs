using System;
using UnityEngine.UIElements;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Mounts and disposes a tree holding nodes of one kind, so a factory that took parts from a disposed tree would
    /// have that tree's parts to take.
    /// </summary>
    public static class DisposedMount
    {
        /// <summary>
        /// Mounts a <c>div</c> holding <paramref name="count"/> nodes from <paramref name="build"/> on a panel-less
        /// element, renders it and disposes it.
        /// </summary>
        public static void Holding(int count, Func<VNode> build)
        {
            var children = new VNode?[count];
            for (var i = 0; i < children.Length; i++)
            {
                children[i] = build();
            }
            V.Mount(new VisualElement(), V.Div(children: children)).Dispose();
        }
    }
}
