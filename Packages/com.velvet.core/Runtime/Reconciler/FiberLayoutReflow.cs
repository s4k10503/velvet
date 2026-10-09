using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet
{
    // React reads layout synchronously in its layout phase: reading a box inside useLayoutEffect or a callback ref
    // forces the reflow the commit's mutations owe. A panel lays itself out later in its frame, so a commit about to
    // run a layout effect, build an imperative handle or attach a callback ref runs the style and layout updaters of
    // the panels it renders into first. UseLayoutEffectLayoutReadTests fails when that stops laying the panel out.
    //
    // The updaters are reached by reflection rather than through IPanel.Pick, whose layout validation is public: a
    // commit can run inside the panel's own layout pass, from a GeometryChangedEvent the pass dispatches, and there
    // Pick validates nothing where the pass came from ValidateLayout, and re-enters the layout updater where it came
    // from UpdateForRepaint. The layout updater dispatches its events from two list fields it clears as a pass
    // runs, so the nested pass is handed lists of its own and the dispatch it interrupted keeps iterating the
    // lists it was given. UseLayoutEffectLayoutReadTests holds both outer passes.
    internal static class FiberLayoutReflow
    {
        // The layout updater's own bound on the passes one layout update takes (kMaxValidateLayoutCount).
        internal const int MaxNesting = 10;

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IPanel, PanelUpdaters?> s_updaters =
            new();

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

        // A panel whose updaters are not shaped as this class reads them is left alone, and commits on it read the
        // layout it last computed.
        private static void EnsureLaidOut(IPanel? panel)
        {
            if (panel == null) return;
            if (!s_updaters.TryGetValue(panel, out var updaters))
            {
                updaters = PanelUpdaters.Resolve(panel);
                s_updaters.Add(panel, updaters);
            }
            updaters?.EnsureLaidOut();
        }

        private sealed class PanelUpdaters
        {
            // Set once by Resolve's initializer.
            private System.Func<uint> _version = null!;
            private System.Action _styles = null!;
            private object _styleUpdater = null!;
            private FieldInfo _applyingStyles = null!;
            private System.Action _layout = null!;
            private object _layoutUpdater = null!;
            private FieldInfo _changeEvents = null!;
            private FieldInfo _missedHierarchyEvents = null!;
            // One pair per nesting depth: a pass this class runs can dispatch an event whose commit runs another.
            private readonly List<(IList ChangeEvents, IList MissedHierarchyEvents)> _spares = new();
            private int _depth;
            private bool _laidOut;
            private uint _laidOutVersion;
            private bool _warnedNesting;

            // Every member it reads is an EngineMember, which a player build keeps and EngineMemberResolutionTests
            // resolves against the editor.
            internal static PanelUpdaters? Resolve(IPanel panel)
            {
                var getUpdater = EngineMember.PanelGetUpdater.ResolveMethod();
                var version = EngineMember.PanelVersion.ResolveProperty()?.GetMethod;
                var stylesPhase = EngineMember.StylesUpdatePhase.ResolveField();
                var layoutPhase = EngineMember.LayoutUpdatePhase.ResolveField();
                // MUTANT_SURVIVES(unreachable): EngineMemberResolutionTests holds that each of these resolves.
                if (getUpdater == null || version == null || stylesPhase == null || layoutPhase == null) return null;
                // MUTANT_SURVIVES(unreachable): every panel the suite mounts on is the engine's own Panel type.
                if (!getUpdater.DeclaringType!.IsInstanceOfType(panel)) return null;
                var styles = getUpdater.Invoke(panel, new[] { stylesPhase.GetValue(null) });
                var layout = getUpdater.Invoke(panel, new[] { layoutPhase.GetValue(null) });
                var stylesUpdate = Bind(EngineMember.StyleUpdaterUpdate.ResolveMethod(), styles);
                var layoutUpdate = Bind(EngineMember.LayoutUpdaterUpdate.ResolveMethod(), layout);
                var applyingStyles = EngineMember.StyleUpdaterApplying.ResolveField();
                var changeEvents = EngineMember.LayoutChangeEvents.ResolveField();
                var missedHierarchyEvents = EngineMember.LayoutMissedHierarchyChangeEvents.ResolveField();
                // MUTANT_SURVIVES(unreachable): as above, and the panel's updaters are the engine's own, which hold
                // these members, unless something replaced them.
                if (stylesUpdate == null || layoutUpdate == null || applyingStyles == null || changeEvents == null
                    || missedHierarchyEvents == null)
                {
                    return null;
                }
                var readVersion = (System.Func<uint>)System.Delegate.CreateDelegate(typeof(System.Func<uint>), panel, version);
                return new PanelUpdaters
                {
                    _version = readVersion,
                    _styles = stylesUpdate,
                    _styleUpdater = styles!,
                    _applyingStyles = applyingStyles,
                    _layout = layoutUpdate,
                    _layoutUpdater = layout!,
                    _changeEvents = changeEvents,
                    _missedHierarchyEvents = missedHierarchyEvents,
                };
            }

            // An unchanged panel version is taken as a layout this class has already brought up to date: the panel
            // moves it on every version change of an element on it, and the update case of
            // UseLayoutEffectLayoutReadTests fails when a layout-dirtying one stops moving it.
            internal void EnsureLaidOut()
            {
                // MUTANT_SURVIVES(equivalent): a pass over a panel nothing changed on finds its styles and layout
                // clean and changes nothing, so this only spares the updater calls.
                if (_laidOut && _version() == _laidOutVersion) return;
                // A CustomStyleResolvedEvent is dispatched from inside the style updater's traversal, which a
                // second traversal would restart under it.
                if ((bool)_applyingStyles.GetValue(_styleUpdater)) return;
                if (_depth == MaxNesting)
                {
                    if (_warnedNesting) return;
                    _warnedNesting = true;
                    UnityEngine.Debug.LogWarning(
                        $"Velvet: commits made inside the layout passes of their own layout effects nested {MaxNesting} "
                        + "deep; deeper ones read the layout the panel last computed.");
                    return;
                }
                Run();
                // MUTANT_SURVIVES(equivalent): an unrecorded version only sends the next read through a pass.
                _laidOutVersion = _version();
                // MUTANT_SURVIVES(equivalent): an unrecorded pass only sends the next read through another.
                _laidOut = true;
            }

            private void Run()
            {
                if (_depth == _spares.Count)
                {
                    _spares.Add((NewListLike(_changeEvents), NewListLike(_missedHierarchyEvents)));
                }
                var spare = _spares[_depth];
                var changeEvents = _changeEvents.GetValue(_layoutUpdater);
                var missedHierarchyEvents = _missedHierarchyEvents.GetValue(_layoutUpdater);
                _changeEvents.SetValue(_layoutUpdater, spare.ChangeEvents);
                _missedHierarchyEvents.SetValue(_layoutUpdater, spare.MissedHierarchyEvents);
                _depth++;
                try
                {
                    _styles();
                    _layout();
                }
                finally
                {
                    _depth--;
                    _changeEvents.SetValue(_layoutUpdater, changeEvents);
                    _missedHierarchyEvents.SetValue(_layoutUpdater, missedHierarchyEvents);
                    // MUTANT_SURVIVES(equivalent, line removed): the layout updater clears a list before it fills it;
                    // this only lets go of the elements the pass left in it.
                    spare.ChangeEvents.Clear();
                    // MUTANT_SURVIVES(equivalent, line removed): the other list is cleared before it is filled too.
                    spare.MissedHierarchyEvents.Clear();
                }
            }

            private static IList NewListLike(FieldInfo field) => (IList)System.Activator.CreateInstance(field.FieldType);

            // Null unless updater is of the type that declares update.
            private static System.Action? Bind(MethodInfo? update, object? updater)
                => update == null || !update.DeclaringType!.IsInstanceOfType(updater)
                    ? null
                    : (System.Action)System.Delegate.CreateDelegate(typeof(System.Action), updater, update);
        }
    }
}
