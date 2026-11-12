using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // React reads layout synchronously in its layout phase: reading a box inside useLayoutEffect or a callback ref
    // forces the reflow the commit's mutations owe. A panel lays itself out later in its frame, so a commit about to
    // run a layout effect, build an imperative handle or attach a callback ref first applies the styles of the
    // panels it renders into and computes their layout. UseLayoutEffectLayoutReadTests fails when that stops laying
    // the panel out.
    //
    // Only the computation runs here, never the layout updater: the updater sends GeometryChangedEvents as it goes,
    // and a commit can run inside one of them, where a second dispatch would reach a callback the first is still
    // running, one that has unregistered itself included. The tree is left dirty instead, so the updater's own pass
    // sends the events for what the computation moved, as it does for any change a callback makes.
    // UseLayoutEffectLayoutReadTests holds that a callback which mounts a tree runs once. What a control settles from
    // those events, a ScrollView's scrollers among them, therefore settles with that pass, which the same fixture
    // pins.
    internal static class FiberLayoutReflow
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IPanel, PanelLayout?> s_panels =
            new();

        private static int s_readDepth;
        private static readonly System.Collections.Generic.List<PanelLayout> s_computedDuringReads = new();

        // Held open across a batch of layout reads — a commit's layout effects, handles and callback refs, or the
        // ref setups a pass drains — so the panels they compute are left dirty once, when the outermost read ends,
        // rather than after each computation.
        internal static void BeginReads() => s_readDepth++;

        internal static void EndReads()
        {
            if (--s_readDepth > 0) return;
            for (var i = 0; i < s_computedDuringReads.Count; i++) s_computedDuringReads[i].LeaveDirty();
            s_computedDuringReads.Clear();
        }

        // Lays out the panels a read by the fiber's layout effects or handles can reach.
        internal static void LayOutFor(ReconcilerContext ctx, ComponentFiber fiber)
            // MUTANT_SURVIVES(unreachable): the flag is read only in LayOut's catch, which no panel the suite mounts reaches.
            => LayOut(ctx, fiber.MountPoint?.panel, fiber, ownerIsLifecycle: true);

        // Lays out the panels a callback ref attached to element, rendered by owner, can read.
        internal static void LayOutFor(ReconcilerContext ctx, VisualElement element, ComponentFiber? owner)
            // MUTANT_SURVIVES(unreachable): the flag is read only in LayOut's catch, which no panel the suite mounts reaches.
            => LayOut(ctx, element.panel, owner, ownerIsLifecycle: false);

        internal static bool ReadsLayout(ComponentFiber fiber)
        {
            if (fiber.PendingLayoutEffects is { Count: > 0 }) return true;
            var slots = fiber.ImperativeHandleSlots;
            if (slots == null) return false;
            for (var i = 0; i < slots.Count; i++)
            {
                if (slots[i].NextFactory != null && slots[i].NextNeedsRecompute) return true;
            }
            return false;
        }

        // A computation that throws, from a measure callback inside it, fails the commit's owner. The owner of a
        // layout effect or handle is the fiber itself, whose error starts above it as an effect's does
        // (ComponentBoundarySearch.PropagateException); a ref's owner is the component that rendered the element,
        // which ReconcilerContext.ContainUserCallbackFailure gives a failing callback.
        private static void LayOut(ReconcilerContext ctx, IPanel? panel, ComponentFiber? owner, bool ownerIsLifecycle)
        {
            BeginReads();
            try
            {
                EnsureLaidOut(panel);
                LayOutContextPanels(ctx);
            }
            catch (TargetInvocationException exception)
            {
                if (ownerIsLifecycle)
                {
                    // MUTANT_SURVIVES(unreachable): no panel the suite mounts on holds a measure callback that throws.
                    ComponentBoundarySearch.PropagateException(owner, exception.InnerException!);
                    return;
                }
                // MUTANT_SURVIVES(unreachable): no panel the suite mounts on holds a measure callback that throws.
                ReconcilerContext.ContainUserCallbackFailure(owner, exception.InnerException!);
            }
            finally
            {
                EndReads();
            }
        }

        // Beside the reading fiber's or element's own panel, the panels the context holds: the one the tree is
        // mounted on and the layer and world-space panels its portals host. A portal into an element of some other
        // panel is laid out where a fiber or a ref inside it reads, through that fiber's or element's own panel.
        private static void LayOutContextPanels(ReconcilerContext ctx)
        {
            EnsureLaidOut(ctx.BatchScheduler.Anchor?.panel);
            foreach (var host in ctx.LayerHosts.Values) EnsureLaidOut(PanelOf(host));
            foreach (var host in ctx.WorldSpaceBindings.Values) EnsureLaidOut(PanelOf(host));
        }

        private static IPanel? PanelOf(PanelHostRecord host)
            => host.Document != null ? host.Document.rootVisualElement?.panel : null;

        // A panel whose members are not shaped as this class reads them is left alone, and commits on it read the
        // layout it last computed.
        private static void EnsureLaidOut(IPanel? panel)
        {
            if (panel == null) return;
            if (!s_panels.TryGetValue(panel, out var layout))
            {
                layout = PanelLayout.Resolve(panel);
                // MUTANT_SURVIVES(equivalent): an uncached panel is resolved again on its next read, to the same members.
                // A fresh resolution has no version recorded, so that read only runs the computation again.
                s_panels.Add(panel, layout);
            }
            layout?.EnsureLaidOut();
        }

        // The engine members a computation reads, the same for every panel. Each is an EngineMember, which a player
        // build keeps and EngineMemberResolutionTests resolves against the editor.
        private sealed class LayoutMembers
        {
            internal static readonly LayoutMembers? Resolved = Resolve();

            internal FieldInfo LayoutNode = null!;
            internal MethodInfo Calculate = null!;
            internal PropertyInfo Dirty = null!;
            internal PropertyInfo HasNewLayout = null!;
            internal FieldInfo LastLayout = null!;
            internal MethodInfo IncrementVersion = null!;
            internal object[] Transform = null!;

            private static LayoutMembers? Resolve()
            {
                var layoutNode = EngineMember.ElementLayoutNode.ResolveField();
                var calculate = EngineMember.LayoutNodeCalculate.ResolveMethod();
                var dirty = EngineMember.LayoutNodeDirty.ResolveProperty();
                var hasNewLayout = EngineMember.LayoutNodeHasNewLayout.ResolveProperty();
                var lastLayout = EngineMember.ElementLastLayout.ResolveField();
                var incrementVersion = EngineMember.ElementIncrementVersion.ResolveMethod();
                var transform = EngineMember.TransformVersionChange.ResolveField();
                // MUTANT_SURVIVES(unreachable): EngineMemberResolutionTests holds that each of these resolves.
                if (layoutNode == null || calculate == null || dirty == null || hasNewLayout == null
                    || lastLayout == null || incrementVersion == null || transform == null)
                {
                    return null;
                }
                return new LayoutMembers
                {
                    LayoutNode = layoutNode,
                    Calculate = calculate,
                    Dirty = dirty,
                    HasNewLayout = hasNewLayout,
                    LastLayout = lastLayout,
                    IncrementVersion = incrementVersion,
                    Transform = new[] { transform.GetValue(null) },
                };
            }
        }

        private sealed class PanelLayout
        {
            private static readonly object[] s_unbounded = { float.NaN, float.NaN };

            // Set once by Resolve's initializer.
            private LayoutMembers _members = null!;
            private System.Func<uint> _version = null!;
            private System.Func<bool> _duringLayout = null!;
            private System.Action _styles = null!;
            private object _styleUpdater = null!;
            private FieldInfo _applyingStyles = null!;
            private VisualElement _root = null!;
            private bool _laidOut;
            private uint _laidOutVersion;

            internal static PanelLayout? Resolve(IPanel panel)
            {
                var getUpdater = EngineMember.PanelGetUpdater.ResolveMethod();
                var version = EngineMember.PanelVersion.ResolveProperty()?.GetMethod;
                var duringLayout = EngineMember.PanelDuringLayoutPhase.ResolveProperty()?.GetMethod;
                var stylesPhase = EngineMember.StylesUpdatePhase.ResolveField();
                var stylesUpdate = EngineMember.StyleUpdaterUpdate.ResolveMethod();
                var applyingStyles = EngineMember.StyleUpdaterApplying.ResolveField();
                // MUTANT_SURVIVES(unreachable): EngineMemberResolutionTests holds that each of these resolves.
                if (LayoutMembers.Resolved == null || getUpdater == null || version == null || duringLayout == null
                    || stylesPhase == null || stylesUpdate == null || applyingStyles == null)
                {
                    return null;
                }
                // MUTANT_SURVIVES(unreachable): every panel the suite mounts on is the engine's own Panel type.
                if (!getUpdater.DeclaringType!.IsInstanceOfType(panel)) return null;
                var styles = getUpdater.Invoke(panel, new[] { stylesPhase.GetValue(null) });
                // MUTANT_SURVIVES(unreachable): every panel the suite mounts on keeps the engine's styles updater.
                if (!stylesUpdate.DeclaringType!.IsInstanceOfType(styles)) return null;
                return new PanelLayout
                {
                    _members = LayoutMembers.Resolved,
                    _version = (System.Func<uint>)System.Delegate.CreateDelegate(typeof(System.Func<uint>), panel, version),
                    _duringLayout =
                        (System.Func<bool>)System.Delegate.CreateDelegate(typeof(System.Func<bool>), panel, duringLayout),
                    _styles = (System.Action)System.Delegate.CreateDelegate(typeof(System.Action), styles, stylesUpdate),
                    _styleUpdater = styles!,
                    _applyingStyles = applyingStyles,
                    _root = panel.visualTree,
                };
            }

            // An unchanged panel version is taken as a layout this class has already brought up to date: the panel
            // moves it on every version change of an element on it, and the update case of
            // UseLayoutEffectLayoutReadTests fails when a layout-dirtying one stops moving it.
            internal void EnsureLaidOut()
            {
                // MUTANT_SURVIVES(equivalent): a computation over a panel nothing changed on changes nothing.
                // This only spares the reflection calls.
                if (_laidOut && _version() == _laidOutVersion) return;
                // A commit made from a measure callback runs inside the panel's own computation, which a second one
                // would restart under it.
                // MUTANT_SURVIVES(unreachable): no suite case commits from inside a measure callback.
                if (_duringLayout()) return;
                // A CustomStyleResolvedEvent is dispatched from inside the style updater's traversal, which a
                // second traversal would restart under it.
                if ((bool)_applyingStyles.GetValue(_styleUpdater)) return;
                _styles();
                var node = _members.LayoutNode.GetValue(_root);
                // The tree is left dirty for the layout updater only when the reads end (EndReads), so after this
                // panel's first computation in them it reads as dirty here only where something has dirtied its layout
                // since. Left dirty after each computation instead, it would read as dirty to the next read in them
                // too, and a callback ref behind a write that moves the panel's version and no box would run the
                // computation and the walk below again; RefAttachLayoutCostTests fails when refs that each write a
                // colour compute the layout more than once.
                if ((bool)_members.Dirty.GetValue(node))
                {
                    _members.Calculate.Invoke(node, s_unbounded);
                    MoveTransforms(_root);
                    if (!s_computedDuringReads.Contains(this)) s_computedDuringReads.Add(this);
                }
                // MUTANT_SURVIVES(equivalent): an unrecorded version only sends the next read through the computation.
                _laidOutVersion = _version();
                // MUTANT_SURVIVES(equivalent): an unrecorded computation only sends the next read through another.
                _laidOut = true;
            }

            // For the layout updater, whose visit of the tree sends the GeometryChangedEvents for the boxes the reads
            // computed; UseLayoutEffectLayoutReadTests fails when they stop arriving.
            internal void LeaveDirty() => _members.Dirty.SetValue(_members.LayoutNode.GetValue(_root), true);

            // Bumps now, over the elements the computation laid out, the transform version the layout updater's visit
            // bumps later for an element whose position changed. The visit still compares against the same last
            // layout and sends the events. UseLayoutEffectLayoutReadTests fails when a moved element's bump is missing.
            private void MoveTransforms(VisualElement element)
            {
                // MUTANT_SURVIVES(equivalent): a subtree the computation left alone holds no element that moved.
                // Its elements' last layout is the layout they still have, so walking it moves no transform.
                if (!(bool)_members.HasNewLayout.GetValue(_members.LayoutNode.GetValue(element))) return;
                var last = (Rect)_members.LastLayout.GetValue(element);
                if (element.layout.position != last.position) _members.IncrementVersion.Invoke(element, _members.Transform);
                var children = element.hierarchy;
                for (var i = 0; i < children.childCount; i++) MoveTransforms(children[i]);
            }
        }
    }
}
