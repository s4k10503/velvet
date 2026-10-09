using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet
{
    // React reads layout synchronously in its layout phase: reading a box inside useLayoutEffect or a callback ref
    // forces the reflow the commit's mutations owe. A panel lays itself out later in its frame, so a commit about to
    // run layout effects, build imperative handles or attach callback refs runs the panel's style and layout updaters
    // first. UseLayoutEffectLayoutReadTests fails when that stops laying the panel out.
    //
    // The updaters are reached by reflection rather than through IPanel.Pick, whose layout validation is public: a
    // commit can run inside the panel's own layout pass, from a GeometryChangedEvent the pass dispatches, and there
    // Pick validates nothing where the pass came from ValidateLayout, and re-enters the layout updater where it came
    // from UpdateForRepaint. The layout updater dispatches its events from two list fields it clears as a pass
    // runs, so the nested pass is handed lists of its own and the dispatch it interrupted keeps iterating the
    // lists it was given. UseLayoutEffectLayoutReadTests holds both outer passes.
    internal static class FiberLayoutReflow
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IPanel, PanelUpdaters?> s_updaters =
            new();

        // A panel whose updaters are not shaped as this class reads them is left alone, and commits on it read the
        // layout it last computed.
        internal static void LayOut(List<IPanel>? panels)
        {
            if (panels == null) return;
            for (var i = 0; i < panels.Count; i++)
            {
                var panel = panels[i];
                if (!s_updaters.TryGetValue(panel, out var updaters))
                {
                    updaters = PanelUpdaters.Resolve(panel);
                    // MUTANT_SURVIVES(equivalent, line removed): resolving again finds the same updaters.
                    s_updaters.Add(panel, updaters);
                }
                updaters?.Run();
            }
        }

        // The panels of the batch's fibers that have a layout effect to run or a handle to build.
        internal static List<IPanel>? PanelsReadIn(List<(ComponentFiber Fiber, bool IsMount)> batch)
        {
            List<IPanel>? panels = null;
            for (var i = 0; i < batch.Count; i++)
            {
                var fiber = batch[i].Fiber;
                if (ReadsLayout(fiber)) AddPanel(ref panels, fiber.MountPoint?.panel);
            }
            return panels;
        }

        internal static void AddPanel(ref List<IPanel>? panels, IPanel? panel)
        {
            if (panel == null) return;
            panels ??= new List<IPanel>(1);
            if (!panels.Contains(panel)) panels.Add(panel);
        }

        private static bool ReadsLayout(ComponentFiber fiber)
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

        private sealed class PanelUpdaters
        {
            private readonly System.Action _styles;
            private readonly System.Action _layout;
            private readonly object _layoutUpdater;
            private readonly FieldInfo _changeEvents;
            private readonly FieldInfo _missedHierarchyEvents;
            // One pair per nesting depth: a pass this class runs can dispatch an event whose commit runs another.
            private readonly List<(IList ChangeEvents, IList MissedHierarchyEvents)> _spares = new();
            private int _depth;

            private PanelUpdaters(
                System.Action styles, System.Action layout, object layoutUpdater, FieldInfo changeEvents,
                FieldInfo missedHierarchyEvents)
            {
                _styles = styles;
                _layout = layout;
                _layoutUpdater = layoutUpdater;
                _changeEvents = changeEvents;
                _missedHierarchyEvents = missedHierarchyEvents;
            }

            internal static PanelUpdaters? Resolve(IPanel panel)
            {
                var getUpdater = FindMethod(panel.GetType(), "GetUpdater");
                var phases = typeof(IPanel).Assembly.GetType("UnityEngine.UIElements.VisualTreeUpdatePhase");
                // MUTANT_SURVIVES(unreachable): the engine this package pins declares every member read here, and
                // UseLayoutEffectLayoutReadTests fails where one is missing, since nothing is laid out then.
                if (getUpdater == null || phases == null) return null;
                var styles = getUpdater.Invoke(panel, new[] { System.Enum.Parse(phases, "Styles") });
                var layout = getUpdater.Invoke(panel, new[] { System.Enum.Parse(phases, "Layout") });
                // MUTANT_SURVIVES(unreachable): as above.
                if (styles == null || layout == null) return null;
                var changeEvents = layout.GetType().GetField("changeEventsList", Instance);
                var missedHierarchyEvents = layout.GetType().GetField("missedHierarchyChangeEventsList", Instance);
                var stylesUpdate = UpdateOf(styles);
                var layoutUpdate = UpdateOf(layout);
                // MUTANT_SURVIVES(unreachable): as above.
                if (changeEvents == null || missedHierarchyEvents == null || stylesUpdate == null || layoutUpdate == null)
                {
                    return null;
                }
                return new PanelUpdaters(stylesUpdate, layoutUpdate, layout, changeEvents, missedHierarchyEvents);
            }

            internal void Run()
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
                    // MUTANT_SURVIVES(equivalent, line removed): as above.
                    spare.MissedHierarchyEvents.Clear();
                }
            }

            private static IList NewListLike(FieldInfo field) => (IList)System.Activator.CreateInstance(field.FieldType);

            private static System.Action? UpdateOf(object updater)
            {
                var update = updater.GetType().GetMethod("Update", Instance, null, System.Type.EmptyTypes, null);
                return update == null ? null : (System.Action)System.Delegate.CreateDelegate(typeof(System.Action), updater, update);
            }

            // The panel declares GetUpdater on an internal base the concrete panel type inherits.
            private static MethodInfo? FindMethod(System.Type? type, string name)
            {
                for (; type != null; type = type.BaseType)
                {
                    var method = type.GetMethod(name, Instance | BindingFlags.DeclaredOnly);
                    if (method != null) return method;
                }
                return null;
            }
        }
    }
}
