using System;
using System.Collections;
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet
{
    // Drops what a panel's FocusController still holds of a subtree leaving it, so an element Velvet releases
    // from that panel carries no focus into its next mount. PooledFocusMemoryTests pins what each held focus
    // reference does to a Button the pool hands out again, and that the selected text element is dropped.
    //
    // Reflection rather than a public call: Blur() reaches these only while the departing element holds or is
    // about to take focus, and a panel the event system blurred keeps its last focused element with nothing
    // focused. Clearing that one publicly means switching the panel's focus, which would drop a focus change
    // a handler has pending.
    internal static class PanelFocusMemory
    {
        private static readonly FieldInfo? s_lastFocusedElement = EngineMember.LastFocusedElement.ResolveField();

        private static readonly FieldInfo? s_pendingFocusedElement = EngineMember.LastPendingFocusedElement.ResolveField();

        // Written through the property, as the engine's own writers are.
        private static readonly PropertyInfo? s_selectedTextElement = EngineMember.SelectedTextElement.ResolveProperty();

        private static readonly FieldInfo? s_focusedElements = EngineMember.FocusedElements.ResolveField();

        private static readonly FieldInfo? s_entryFocusedElement = EngineMember.FocusedElementEntry.ResolveField();

        private static readonly PropertyInfo? s_pseudoStates = EngineMember.PseudoStates.ResolveProperty();

        private static readonly long s_focusPseudoState =
            EngineMember.FocusPseudoState.ResolveField()?.GetValue(null) is { } focus ? Convert.ToInt64(focus) : 0;

        // A tree disposed after its root left its panel is released with no panel to scrub, so the scrub runs
        // when the root leaves instead.
        public static IDisposable ForgetWhenLeaving(VisualElement root) => new LeaveScrub(root);

        public static void Forget(IPanel? panel, VisualElement departing)
        {
            var controller = panel?.focusController;
            if (controller == null)
            {
                return;
            }
            if (FocusedEntriesWithin(panel!, departing) is { } entries)
            {
                ClearFocusedEntries(entries);
            }
            if (s_lastFocusedElement?.GetValue(controller) is VisualElement last && IsWithin(last, departing))
            {
                s_lastFocusedElement.SetValue(controller, null);
            }
            if (s_selectedTextElement?.GetValue(controller) is VisualElement selected)
            {
                if (IsWithin(selected, departing))
                {
                    s_selectedTextElement.SetValue(controller, null);
                }
            }
            // A focus change still being dispatched lands on its target after this has run: a handler on the
            // element losing focus can release the one gaining it, and the engine then records that target as
            // focused. The scrub is repeated when that target is attached again.
            if (s_pendingFocusedElement?.GetValue(controller) is VisualElement pending && IsWithin(pending, departing))
            {
                new ReturnScrub(panel!, departing, pending).Arm();
            }
        }

        // Boxes an entry only when the public reading cannot rule the subtree out. That reading names the
        // outermost composite root above the focused element, and nothing once that root has left the panel.
        private static IList? FocusedEntriesWithin(IPanel panel, VisualElement departing)
        {
            var controller = panel.focusController;
            if (controller.focusedElement is VisualElement shown
                && !IsWithin(shown, departing) && !shown.Contains(departing))
            {
                return null;
            }
            return s_focusedElements?.GetValue(controller) is IList { Count: > 0 } entries
                   && s_entryFocusedElement?.GetValue(entries[0]) is VisualElement focused
                   && IsWithin(focused, departing)
                ? entries
                : null;
        }

        // Each entry's element took the focus pseudo-state with it, a composite root outside the departing
        // subtree included, and while that element stays in its panel the engine takes it off only by walking
        // these entries on its next focus change.
        private static void ClearFocusedEntries(IList entries)
        {
            if (s_pseudoStates != null)
            {
                foreach (var entry in entries)
                {
                    if (s_entryFocusedElement?.GetValue(entry) is VisualElement element)
                    {
                        var states = Convert.ToInt64(s_pseudoStates.GetValue(element));
                        s_pseudoStates.SetValue(
                            element, Enum.ToObject(s_pseudoStates.PropertyType, states & ~s_focusPseudoState));
                    }
                }
            }
            entries.Clear();
        }

        private static bool IsWithin(VisualElement element, VisualElement root)
            => ReferenceEquals(element, root) || root.Contains(element);

        private sealed class LeaveScrub : IDisposable
        {
            private readonly VisualElement _root;

            public LeaveScrub(VisualElement root)
            {
                _root = root;
                _root.RegisterCallback<DetachFromPanelEvent>(OnDetached);
            }

            public void Dispose() => _root.UnregisterCallback<DetachFromPanelEvent>(OnDetached);

            private void OnDetached(DetachFromPanelEvent evt) => Forget(evt.originPanel, _root);
        }

        private sealed class ReturnScrub
        {
            private readonly IPanel _panel;
            private readonly VisualElement _departing;
            private readonly VisualElement _target;

            public ReturnScrub(IPanel panel, VisualElement departing, VisualElement target)
            {
                _panel = panel;
                _departing = departing;
                _target = target;
            }

            public void Arm() => _target.RegisterCallback<AttachToPanelEvent>(OnAttached);

            private void OnAttached(AttachToPanelEvent evt)
            {
                _target.UnregisterCallback<AttachToPanelEvent>(OnAttached);
                Forget(_panel, _departing);
            }
        }
    }
}
