using System;
using UnityEngine.UIElements;

namespace Velvet
{
    public static partial class V
    {
        private static readonly MountOptions s_defaultMountOptions = new();

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
            => Mount(target, tree, s_defaultMountOptions);

        /// <summary>
        /// Mounts a VNode tree onto <paramref name="target"/> as <see cref="Mount(VisualElement, VNode)"/> does,
        /// with the root options React's <c>createRoot</c> takes.
        /// </summary>
        /// <param name="target">The VisualElement to mount onto.</param>
        /// <param name="tree">The VNode tree to mount, treated as immutable as in <see cref="Mount(VisualElement, VNode)"/>.</param>
        /// <param name="options">The root options, which <see cref="MountOptions"/> describes.</param>
        /// <returns>A handle for Unmount (dispose to tear down).</returns>
        public static MountedTree Mount(VisualElement target, VNode tree, MountOptions options)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            if (options == null) throw new ArgumentNullException(nameof(options));
            var root = new RootTree(tree);
            var rootFiber = FiberRenderer.CreateRoot(root.Render);
            try
            {
                FiberRenderer.Mount(rootFiber, target, onCaughtError: options.OnCaughtError, motionClock: options.MotionClock);
            }
            catch
            {
                // No handle reaches the caller to dispose, so the clock the mount took is released here.
                rootFiber.Reconciler?.Context.StyleAnimationScheduler.ReleaseClock();
                throw;
            }
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
            var sheetWatch = VelvetStyleUtilities.WatchForMissingSheet(target);
            var focusScrub = PanelFocusMemory.ForgetWhenLeaving(target);
#if UNITY_EDITOR
            DevTools.VelvetDevToolsRegistry.Register(rootFiber, ResolveDevToolsLabel(tree, target));
#endif
            return new MountedTree(rootFiber, root, sheetWatch, focusScrub);
        }

        internal sealed class RootTree
        {
            internal VNode Tree;

            internal RootTree(VNode tree) => Tree = tree;

            internal VNode Render() => Tree;
        }

#if UNITY_EDITOR
        // The root fiber's own Body is the V.Mount lambda (an unnamed closure), so the label is
        // derived from the supplied tree instead.
        private static string ResolveDevToolsLabel(VNode tree, VisualElement target)
        {
            if (tree is ComponentNode component)
            {
                var name = Hooks.ComponentNameOrNull(component);
                if (name != null) return name;
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
        private readonly V.RootTree _tree;
        private readonly IDisposable _sheetWatch;
        private readonly IDisposable _focusScrub;
        private bool _disposed;

        internal MountedTree(ComponentFiber root, V.RootTree tree, IDisposable sheetWatch, IDisposable focusScrub)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            _tree = tree;
            _sheetWatch = sheetWatch;
            _focusScrub = focusScrub;
        }

        // React's root.render(element): the new tree is reconciled against the committed one, so a component
        // keeping its type and position keeps its state.
        internal void Render(VNode tree)
        {
            if (_disposed) throw new InvalidOperationException("Cannot update an unmounted root.");
            _tree.Tree = tree ?? throw new ArgumentNullException(nameof(tree));
            FiberWorkLoop.ScheduleRerender(Root, FiberUpdatePriority.Urgent);
            Root.Reconciler!.Context.BatchScheduler.FlushImmediate();
        }

        /// <summary>
        /// Unmounts the tree: tears down every fiber and runs cleanup (effect teardowns, ref callbacks).
        /// Idempotent — calling it more than once is a no-op.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _sheetWatch.Dispose();
            _focusScrub.Dispose();
            // Unmounting a root is terminal — subsequent hook setters / async
            // continuations must not resurrect the tree. FiberRenderer.Dispose sets IsDisposed
            // before unmounting so IsDisposed-gated closures (UseMutation continuations,
            // optimistic update commits) short-circuit instead of mutating a torn-down fiber.
            FiberRenderer.Dispose(Root);
        }
    }
}
