using System;
using UnityEngine.UIElements;

namespace Velvet
{
    public static partial class V
    {
        /// <summary>
        /// Mounts a VNode tree onto <paramref name="target"/>, establishing it as a render root and performing
        /// the initial render. Dispose the returned <see cref="MountedTree"/> to unmount.
        /// </summary>
        /// <param name="target">The VisualElement to mount onto.</param>
        /// <param name="tree">
        /// The VNode tree to mount (V.Provider, V.Component, V.Div, etc.).
        /// Treated as immutable after V.Mount: to change content, Dispose and re-Mount, or express dynamic parts
        /// using hooks (UseState / UseStore / UseContext) inside child components.
        /// </param>
        /// <returns>A handle for Unmount (dispose to tear down).</returns>
        public static MountedTree Mount(VisualElement target, VNode tree)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            var rootFiber = FiberRenderer.CreateRoot(() => tree);
            FiberRenderer.Mount(rootFiber, target);
            var ctx = rootFiber.Reconciler!.Context;
            ctx.MainPanelRoot = target;
            if (!ctx.CrossPanelRouterAttached)
            {
                FiberCrossPanelPointerRouter.AttachToMainPanel(target, ctx);
                ctx.CrossPanelRouterAttached = true;
            }
            // Focus navigation (scopes, chained portals): the navigator resolves the target's TRUE panel
            // root and attaches there once; a target with no panel yet defers to its own AttachToPanelEvent
            // so ring predictions are never computed from a non-root subtree.
            FiberFocusNavigator.EnsureAttached(target, ctx);
#if UNITY_EDITOR
            // Auto-attach to DevTools: opening the inspector shows the live tree
            // with no manual Register call. Editor-only — the registry and this call are compiled out of
            // player builds. Manual Register stays available for labelling interior sub-trees.
            DevTools.VelvetDevToolsRegistry.Register(rootFiber, ResolveDevToolsLabel(tree, target));
#endif
            return new MountedTree(rootFiber);
        }

#if UNITY_EDITOR
        // Picks a human-readable DevTools label for a mounted root. The root fiber's own Body is the
        // V.Mount lambda (an unnamed closure), so the label is derived from the supplied tree instead:
        // the root component's function name, else the root element's name, else the target's name,
        // else a generic fallback.
        private static string ResolveDevToolsLabel(VNode tree, VisualElement target)
        {
            if (tree is ComponentNode component)
            {
                var name = component.Body?.Method?.Name;
                if (!string.IsNullOrEmpty(name)) return name;
            }

            if (tree is BaseElementNode element && !string.IsNullOrEmpty(element.Name))
            {
                return element.Name;
            }

            if (!string.IsNullOrEmpty(target.name))
            {
                return target.name;
            }

            return "Velvet Root";
        }
#endif
    }

    /// <summary>
    /// Handle to a tree created by V.Mount. Unmounts on Dispose.
    /// </summary>
    public sealed class MountedTree : IDisposable
    {
        internal readonly ComponentFiber Root;
        private bool _disposed;

        internal MountedTree(ComponentFiber root)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
        }

        /// <summary>
        /// Unmounts the tree: tears down every fiber and runs cleanup (effect teardowns, ref callbacks).
        /// Idempotent — calling it more than once is a no-op.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
#if UNITY_EDITOR
            // Detach from DevTools so an unmounted tree drops out of the inspector. Paired with the
            // auto-register in V.Mount; editor-only, compiled out of player builds.
            DevTools.VelvetDevToolsRegistry.Unregister(Root);
#endif
            // Unmounting a root is terminal — subsequent hook setters / async
            // continuations must not resurrect the tree. FiberRenderer.Dispose sets IsDisposed
            // before unmounting so IsDisposed-gated closures (UseMutation continuations,
            // optimistic update commits) short-circuit instead of mutating a torn-down fiber.
            FiberRenderer.Dispose(Root);
        }
    }
}
