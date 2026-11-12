using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Applies what the Suspenses of a walk decided about their primaries once the walk has placed its rows: the
    // elements to hide or reveal and the components going offscreen or coming back. A hide follows React's Offscreen
    // mutation case: one pre-order walk of the hidden tree first (disappearLayoutEffects, PreOrderTeardown), in which a
    // component's layout cleanups run where the walk reaches the component and an element's ref detaches where the
    // walk reaches the element, and the display: none after it (hideOrUnhideAllChildren).
    internal sealed class SuspenseOffscreenCommit : PreOrderTeardown
    {
        // An instance per Apply on the stack, so a cleanup that commits another walk takes an instance of its own.
        private static readonly Stack<SuspenseOffscreenCommit> s_pool = new();

        private readonly Dictionary<VisualElement, int> _lastEntry = new();
        private readonly List<VisualElement> _hidden = new();
        private readonly HashSet<VisualElement> _hiddenSet = new();
        private readonly List<ComponentFiber> _hiddenFibers = new();
        private readonly HashSet<ComponentFiber> _hiding = new();
        private readonly List<(ComponentFiber Fiber, bool Hidden)> _afterTheWalk = new();
        private VisualElement? _root;

        internal static void Apply(ReconcilerContext ctx,
            List<(int Row, VisualElement Element, bool Hidden)>? displayChanges,
            List<(ComponentFiber Fiber, bool Hidden)>? offscreenChanges)
        {
            if (displayChanges == null && offscreenChanges == null) return;
            var commit = s_pool.Count > 0 ? s_pool.Pop() : new SuspenseOffscreenCommit();
            commit.Ctx = ctx;
            try
            {
                commit.Run(displayChanges, offscreenChanges);
            }
            finally
            {
                commit.Clear();
                s_pool.Push(commit);
            }
        }

        private void Run(List<(int Row, VisualElement Element, bool Hidden)>? displayChanges,
            List<(ComponentFiber Fiber, bool Hidden)>? offscreenChanges)
        {
            // An element ends as its last entry leaves it: a Suspense of the walk can reveal an element that the
            // Suspense enclosing it, deciding later in the walk, hides.
            if (displayChanges != null)
            {
                for (var i = 0; i < displayChanges.Count; i++) _lastEntry[displayChanges[i].Element] = i;
                for (var i = 0; i < displayChanges.Count; i++)
                {
                    var (_, element, hidden) = displayChanges[i];
                    if (!hidden || _lastEntry[element] != i) continue;
                    _hidden.Add(element);
                    _hiddenSet.Add(element);
                    SuspenseHiddenElements.HoldForHide(element);
                }
            }
            // The walk takes a fiber's first hide out of the list's order, so what the list holds for that fiber
            // before the hide is applied ahead of the walk and what it holds after, behind it.
            if (offscreenChanges != null)
            {
                foreach (var (fiber, hidden) in offscreenChanges)
                {
                    if (_hiding.Contains(fiber))
                    {
                        _afterTheWalk.Add((fiber, hidden));
                    }
                    else if (!hidden)
                    {
                        FiberEffects.ShowLayoutEffects(fiber, Ctx);
                    }
                    else
                    {
                        _hiding.Add(fiber);
                        _hiddenFibers.Add(fiber);
                    }
                }
            }
            Disappear();
            foreach (var element in _hidden) SuspenseHiddenElements.HideHeld(element);
            // After the hides: a reveal attaches the refs below it short of an element a Suspense hides, and the walk
            // has just taken those of the elements hidden here off.
            if (displayChanges != null)
            {
                for (var i = 0; i < displayChanges.Count; i++)
                {
                    var (_, element, hidden) = displayChanges[i];
                    if (hidden || _lastEntry[element] != i) continue;
                    SuspenseHiddenElements.Reveal(element);
                    Ctx.AttachRefsHiddenUnder(element);
                }
            }
            foreach (var element in _hidden)
            {
                // UI Toolkit lets go of the focus of an element no longer displayed only at its next layout
                // pass, which SuspenseHiddenFocusPanelTests pins; the commit that hides it lets go of it here.
                var focused = element.focusController?.focusedElement as VisualElement;
                if (SuspenseHiddenElements.IsAtOrUnderHidden(focused)) focused!.Blur();
            }
            foreach (var (fiber, hidden) in _afterTheWalk)
            {
                if (hidden) FiberEffects.HideLayoutEffects(fiber);
                else FiberEffects.ShowLayoutEffects(fiber, Ctx);
            }
        }

        private void Disappear()
        {
            foreach (var fiber in _hiddenFibers) Place(fiber);
            foreach (var root in _hidden)
            {
                _root = root;
                // Not a root that a commit nested in a cleanup above revealed, hid or returned to the pool, which drops
                // its hold: that commit decided later.
                if (SuspenseHiddenElements.IsHeldForHide(root)) Visit(root);
            }
            // A fiber taken down above returns at once.
            foreach (var fiber in _hiddenFibers) FiberEffects.HideLayoutEffects(fiber);
        }

        protected override void TakeDown(ComponentFiber fiber) => FiberEffects.HideLayoutEffects(fiber);

        protected override void TakeDown(VisualElement element) => Ctx.DetachRefWhileHidden(element);

        protected override bool IsRoot(VisualElement element) => _hiddenSet.Contains(element);

        // Where a commit nested in a cleanup the walk ran took the root's hold, the rest of the root is left to it, as
        // Disappear leaves a root whose hold such a commit took before the walk got there: what the walk has still to
        // reach can be back in the pool, or rented again for an element that commit mounted.
        protected override bool WalksOn() => SuspenseHiddenElements.IsHeldForHide(_root!);

        protected override void Clear()
        {
            base.Clear();
            _lastEntry.Clear();
            _hidden.Clear();
            _hiddenSet.Clear();
            _hiddenFibers.Clear();
            _hiding.Clear();
            _afterTheWalk.Clear();
            // MUTANT_SURVIVES(equivalent): Disappear writes the root ahead of every Visit that reads it.
            _root = null;
        }
    }
}
