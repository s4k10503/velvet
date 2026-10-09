using System.Reflection;
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
    // UseLayoutEffectLayoutReadTests holds that a callback which mounts a tree runs once.
    internal static class FiberLayoutReflow
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IPanel, PanelLayout?> s_panels =
            new();

        private static readonly object[] s_unbounded = { float.NaN, float.NaN };

        // Lays out the panels a read by the fiber's layout effects or handles can reach.
        internal static void LayOutFor(ReconcilerContext ctx, ComponentFiber fiber)
        {
            EnsureLaidOut(fiber.MountPoint?.panel);
            LayOutContextPanels(ctx);
        }

        // Lays out the panels a callback ref attached to element can read.
        internal static void LayOutFor(ReconcilerContext ctx, VisualElement element)
        {
            EnsureLaidOut(element.panel);
            LayOutContextPanels(ctx);
        }

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
                s_panels.Add(panel, layout);
            }
            layout?.EnsureLaidOut();
        }

        private sealed class PanelLayout
        {
            // Set once by Resolve's initializer.
            private System.Func<uint> _version = null!;
            private System.Action _styles = null!;
            private object _styleUpdater = null!;
            private FieldInfo _applyingStyles = null!;
            private VisualElement _root = null!;
            private FieldInfo _layoutNode = null!;
            private MethodInfo _calculate = null!;
            private PropertyInfo _dirty = null!;
            private bool _laidOut;
            private uint _laidOutVersion;

            // Every member it reads is an EngineMember, which a player build keeps and EngineMemberResolutionTests
            // resolves against the editor.
            internal static PanelLayout? Resolve(IPanel panel)
            {
                var getUpdater = EngineMember.PanelGetUpdater.ResolveMethod();
                var version = EngineMember.PanelVersion.ResolveProperty()?.GetMethod;
                var stylesPhase = EngineMember.StylesUpdatePhase.ResolveField();
                var stylesUpdate = EngineMember.StyleUpdaterUpdate.ResolveMethod();
                var applyingStyles = EngineMember.StyleUpdaterApplying.ResolveField();
                var layoutNode = EngineMember.ElementLayoutNode.ResolveField();
                var calculate = EngineMember.LayoutNodeCalculate.ResolveMethod();
                var dirty = EngineMember.LayoutNodeDirty.ResolveProperty();
                // MUTANT_SURVIVES(unreachable): EngineMemberResolutionTests holds that each of these resolves.
                if (getUpdater == null || version == null || stylesPhase == null || stylesUpdate == null
                    || applyingStyles == null || layoutNode == null || calculate == null || dirty == null)
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
                    _version = (System.Func<uint>)System.Delegate.CreateDelegate(typeof(System.Func<uint>), panel, version),
                    _styles = (System.Action)System.Delegate.CreateDelegate(typeof(System.Action), styles, stylesUpdate),
                    _styleUpdater = styles!,
                    _applyingStyles = applyingStyles,
                    _root = panel.visualTree,
                    _layoutNode = layoutNode,
                    _calculate = calculate,
                    _dirty = dirty,
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
                // A CustomStyleResolvedEvent is dispatched from inside the style updater's traversal, which a
                // second traversal would restart under it.
                if ((bool)_applyingStyles.GetValue(_styleUpdater)) return;
                _styles();
                var node = _layoutNode.GetValue(_root);
                if ((bool)_dirty.GetValue(node))
                {
                    _calculate.Invoke(node, s_unbounded);
                    // Left dirty for the layout updater, whose visit of the tree sends the GeometryChangedEvents for
                    // the boxes computed above; UseLayoutEffectLayoutReadTests fails when they stop arriving.
                    _dirty.SetValue(node, true);
                }
                // MUTANT_SURVIVES(equivalent): an unrecorded version only sends the next read through the computation.
                _laidOutVersion = _version();
                // MUTANT_SURVIVES(equivalent): an unrecorded computation only sends the next read through another.
                _laidOut = true;
            }
        }
    }
}
