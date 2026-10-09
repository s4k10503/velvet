using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // React reads layout synchronously in its layout phase: reading a box inside useLayoutEffect or a callback ref
    // forces the reflow the commit's mutations owe. A panel lays itself out later in its frame, so a commit about to
    // run layout effects, build imperative handles or attach callback refs lays out the panels they live on first,
    // through the layout validation IPanel.Pick runs before it picks. UseLayoutEffectLayoutReadTests fails when Pick
    // stops laying the panel out.
    //
    // A commit can also run inside a panel's layout pass, from a GeometryChangedEvent the pass dispatches: a virtual
    // list rendering rows, or application code mounting there. A layout validation started there re-enters the pass
    // the panel is iterating, so the commit's reads wait instead for the pass's next iteration, which lays out what the
    // commit changed and dispatches the GeometryChangedEvent of a probe element this class keeps on the panel.
    internal static class FiberLayoutReflow
    {
        // The pick's result is discarded; a point this far off the panel ends it at the root.
        private static readonly Vector2 s_offPanelPoint = new(-1e9f, -1e9f);

        // Null where this engine has no type by that name, which reads every commit as inside a layout pass.
        private static readonly System.Type? s_layoutUpdaterType =
            typeof(IPanel).Assembly.GetType("UnityEngine.UIElements.VisualTreeLayoutUpdater");

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IPanel, LayoutProbe> s_probes =
            new();

        // Returns null once every panel is laid out for reading, or the panel to hand the reads to with
        // RunAfterLayout when the stack is inside a layout pass.
        internal static IPanel? LayOut(List<IPanel>? panels)
        {
            if (panels == null) return null;
            if (IsInsideLayoutPass()) return panels[0];
            for (var i = 0; i < panels.Count; i++) _ = panels[i].Pick(s_offPanelPoint);
            return null;
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

        // Runs work once the panel's current layout pass has laid out what the commit changed: from the probe's
        // GeometryChangedEvent in the pass's next iteration, or, where the pass stops iterating before that, from the
        // panel's next scheduler tick after laying the panel out.
        internal static void RunAfterLayout(IPanel panel, System.Action work)
        {
            if (!s_probes.TryGetValue(panel, out var probe))
            {
                probe = new LayoutProbe(panel);
                s_probes.Add(panel, probe);
            }
            probe.Enqueue(work);
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

        // Where the stack cannot be read, the commit is taken to be inside a pass: its reads then wait for the probe,
        // which costs synchronous timing, where a validation started inside a pass would throw.
        // A layout validation this class started is read the same way: its layout updater is on the stack too.
        private static bool IsInsideLayoutPass()
        {
            // MUTANT_SURVIVES(unreachable): the engine this package pins declares the type, which
            // UseLayoutEffectLayoutReadTests would read as every commit sitting inside a pass were it missing.
            if (s_layoutUpdaterType == null) return true;
            // MUTANT_SURVIVES(equivalent, literal): capturing file names as well leaves each frame's method as it is.
            var frames = new System.Diagnostics.StackTrace(false).GetFrames();
            // MUTANT_SURVIVES(unreachable): the editor's runtime returns the frames it captured.
            if (frames == null) return true;
            // MUTANT_SURVIVES(equivalent, literal): the stack holds this method's own frame, which resolves, so the
            // loop sets it whatever it starts as.
            var resolved = false;
            for (var i = 0; i < frames.Length; i++)
            {
                var type = frames[i].GetMethod()?.DeclaringType;
                if (type == s_layoutUpdaterType) return true;
                resolved |= type != null;
            }
            return !resolved;
        }

        private sealed class LayoutProbe
        {
            private readonly IPanel _panel;
            private readonly VisualElement _element;
            private readonly List<System.Action> _pending = new();
            private bool _awaitingGeometry;
            private bool _fallbackScheduled;
            private bool _wide;

            internal LayoutProbe(IPanel panel)
            {
                _panel = panel;
                _element = new VisualElement { name = "velvet-layout-probe" };
                // MUTANT_SURVIVES(equivalent, line removed): out of the root's flow, the probe takes no room from
                // the panel's content; a heightless probe in a column root, as the tests' is, takes none either.
                _element.style.position = Position.Absolute;
                // MUTANT_SURVIVES(equivalent, line removed): an absolute element with no content is as wide.
                _element.style.width = 0f;
                _element.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            }

            internal void Enqueue(System.Action work)
            {
                _pending.Add(work);
                if (_element.panel != _panel) _panel.visualTree.Add(_element);
                // A second toggle in the same iteration would hand the probe back the width it was laid out at.
                if (!_awaitingGeometry)
                {
                    _awaitingGeometry = true;
                    _wide = !_wide;
                    _element.style.width = _wide ? 1f : 0f;
                }
                // MUTANT_SURVIVES(equivalent): a second scheduled run finds pending only what the first left, which
                // it lays out and runs as the first would.
                if (_fallbackScheduled) return;
                // MUTANT_SURVIVES(equivalent): as above.
                _fallbackScheduled = true;
                _element.schedule.Execute(RunFromScheduler);
            }

            private void OnGeometryChanged(GeometryChangedEvent evt)
            {
                _awaitingGeometry = false;
                RunPending();
            }

            private void RunFromScheduler()
            {
                _fallbackScheduled = false;
                // MUTANT_SURVIVES(equivalent, guard removed): with nothing pending, the validation below runs nothing.
                if (_pending.Count == 0) return;
                // A scheduler tick runs ahead of the panel's layout pass, so no stack reading is needed here. The
                // validation lays out the width Enqueue toggled, whose GeometryChangedEvent runs what is pending.
                _ = _panel.Pick(s_offPanelPoint);
            }

            // Work that commits again inside this pass enqueues behind what is being run, for the next iteration.
            private void RunPending()
            {
                var batch = _pending.ToArray();
                _pending.Clear();
                for (var i = 0; i < batch.Length; i++)
                {
                    try
                    {
                        batch[i]();
                    }
                    catch (System.Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }
            }
        }
    }
}
