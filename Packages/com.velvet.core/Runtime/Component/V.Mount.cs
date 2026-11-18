using System;
using UnityEngine.UIElements;

namespace Velvet
{
    public static partial class V
    {
        private static readonly MountOptions s_defaultMountOptions = new();

        private static readonly Func<Exception, ErrorInfo, VNode> s_renderNothing = static (_, _) => Fragment();

        /// <summary>
        /// Mounts a VNode tree onto <paramref name="target"/>, establishing it as a render root and performing
        /// the initial render. Dispose the returned <see cref="MountedTree"/> to unmount.
        /// </summary>
        /// <remarks>
        /// An error no error boundary in the tree catches unmounts the whole tree, as React's root does: its effect
        /// cleanups run as on any unmount, <paramref name="target"/> is left without the tree's elements, and the
        /// error is reported to <see cref="MountOptions.OnUncaughtError"/>. The handle stays valid, and
        /// disposing it is still how the root is released.
        /// <para>
        /// Called while a root renders, commits or runs its passive effects (from an <c>onCreated</c> callback, a
        /// layout effect or a <c>UseEffect</c>, say), it returns the handle at once and renders the tree once that work
        /// has ended, before control returns to whatever started it, as React's <c>root.render()</c> only schedules
        /// there. Disposing a handle there waits in the same way, as <c>root.unmount()</c> does.
        /// </para>
        /// </remarks>
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
            rootFiber.IsMountRoot = true;
            rootFiber.FallbackFactory = s_renderNothing;
            FiberRenderer.SetupMount(rootFiber, target, onCaughtError: options.OnCaughtError,
                onUncaughtError: options.OnUncaughtError, motionClock: options.MotionClock);
            if (FiberExecutionContext.IsSet)
            {
                FiberRenderer.HoldFirstRender(rootFiber);
                return Wire(target, tree, rootFiber, root);
            }
            // The handle is made inside the entry, so what is held while the root renders runs once it exists.
            try
            {
                return FiberExecutionContext.Enter(rootFiber.Reconciler!.Context.BatchScheduler,
                    (Target: target, Tree: tree, Fiber: rootFiber, Root: root), static mount =>
                    {
                        FiberRenderer.RenderMountedWork(mount.Fiber, opensWave: true);
                        return Wire(mount.Target, mount.Tree, mount.Fiber, mount.Root);
                    });
            }
            catch
            {
                // No handle reaches the caller to dispose, so the clock the mount took is released here, whether the
                // render threw or the flush the entry starts with did.
                rootFiber.Reconciler?.Context.StyleAnimationScheduler.ReleaseClock();
                throw;
            }
        }

        private static MountedTree Wire(VisualElement target, VNode tree, ComponentFiber rootFiber, RootTree root)
        {
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
            var defaultFamily = DefaultFamilyRoots.Hold(target);
#if UNITY_EDITOR
            DevTools.VelvetDevToolsRegistry.Register(rootFiber, ResolveDevToolsLabel(tree, target));
#endif
            return new MountedTree(rootFiber, root, sheetWatch, focusScrub, defaultFamily);
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
        private readonly IDisposable _defaultFamily;
        private bool _disposed;

        internal MountedTree(ComponentFiber root, V.RootTree tree, IDisposable sheetWatch, IDisposable focusScrub,
            IDisposable defaultFamily)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            _tree = tree;
            _sheetWatch = sheetWatch;
            _focusScrub = focusScrub;
            _defaultFamily = defaultFamily;
        }

        // React's root.render(element): the new tree is reconciled against the committed one, so a component
        // keeping its type and position keeps its state.
        internal void Render(VNode tree)
        {
            if (_disposed) throw new InvalidOperationException("Cannot update an unmounted root.");
            _tree.Tree = tree ?? throw new ArgumentNullException(nameof(tree));
            // A root that unmounted its tree for an uncaught error renders the new one, as React's root.render does.
            Root.CaughtError = null;
            FiberWorkLoop.ScheduleRerender(Root, FiberUpdatePriority.Urgent);
            var scheduler = Root.Reconciler!.Context.BatchScheduler;
            if (FiberExecutionContext.IsSet) scheduler.HoldFlush();
            else FiberExecutionContext.Enter(scheduler, scheduler, static flushed => flushed.FlushImmediateWork());
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
            _defaultFamily.Dispose();
            // Unmounting a root is terminal — subsequent hook setters / async
            // continuations must not resurrect the tree. FiberRenderer.Dispose sets IsDisposed
            // before unmounting so IsDisposed-gated closures (UseMutation continuations,
            // optimistic update commits) short-circuit instead of mutating a torn-down fiber.
            FiberExecutionContext.Drop(FiberExecutionContext.Request.Mount, Root);
            var context = Root.Reconciler?.Context;
            if (FiberExecutionContext.IsSet)
            {
                // Stood down as the unmount is held, so the work of the root held ahead of it in the same release, a
                // flush a click on the root asked for among it, renders nothing either.
                if (context != null) FiberExecutionContext.StandDown(context.BatchScheduler);
                FiberExecutionContext.Hold(FiberExecutionContext.Request.Unmount, Root, context?.BatchScheduler,
                    (Tree: this, Context: context), static held => held.Tree.Unmount(held.Context));
                return;
            }
            Unmount(context);
        }

        // The root stands down before its unmount's entry, so the passive effects that entry starts by running still run
        // but render nothing of the root, as React's root.unmount() folds the unmount and the root's pending updates into
        // one render.
        private void Unmount(ReconcilerContext? context)
        {
            if (context != null) FiberExecutionContext.StandDown(context.BatchScheduler);
            FiberExecutionContext.Enter(context?.BatchScheduler, (Tree: this, Context: context), static state =>
            {
                FiberRenderer.DisposeWork(state.Tree.Root);
                StyleThemeTokens.Release(state.Context);
                if (state.Context != null) FiberErrorBoundary.ReportErrorsDuringDisposal(state.Context);
            });
        }
    }
}
