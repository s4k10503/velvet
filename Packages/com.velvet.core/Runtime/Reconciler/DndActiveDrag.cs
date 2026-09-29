#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The drag-gesture state machine — the single owner of one pointer-drag session per mounted tree,
    // referenced solely from ReconcilerContext.ActiveDrag (null = idle). Armed (PENDING) by a draggable's
    // pointer-down, promoted to ACTIVE when the activation constraint is crossed, and returned to idle on
    // drop, cancel, or teardown. Two engine facts shape the event wiring (both verified against engine
    // source and pinned by the DnD PlayMode tests):
    //
    //   1. CAPTURED pointer events are delivered to the capturing element ONLY — no trickle through
    //      ancestors. So both phases observe every element from the press target up to the panel root,
    //      which uncaptured events trickle through: any of them that captures at its own pointer-down
    //      (a button inside a draggable card, or a button around one) receives the events alone. The
    //      ACTIVE phase captures on the source.
    //   2. TrickleDown-registered callbacks on the capturing element run before bubble-phase ones on the
    //      same element, so the post-drag PointerUp can be swallowed (StopImmediatePropagation) before
    //      UI Toolkit's own Clickable fires `clicked` — a real drag ending on a draggable Button must
    //      not click it.
    internal sealed class DndActiveDrag
    {
        // Hold-to-drag clock cadence, matching the sibling per-frame element drivers' TickIntervalMs
        // convention (AnchoredDriver, SceneViewDriver, ParticlesDriver).
        private const long DelayTickIntervalMs = 16;

        // Where one session starts: the pressed draggable, the scope enclosing it, and the panel root the
        // PENDING phase listens on. Resolved together at arm time — a session exists only once all three
        // resolve — and none of them changes for the session's life.
        internal readonly struct DragOrigin
        {
            internal VisualElement ScopeElement { get; init; }
            internal DndScopeBinding Scope { get; init; }
            internal VisualElement Source { get; init; }
            internal DndDraggableBinding Draggable { get; init; }
            internal VisualElement PanelRoot { get; init; }
        }

        private readonly ReconcilerContext _ctx;
        private readonly VisualElement _scopeElement;
        private readonly DndScopeBinding _scope;
        private readonly VisualElement _source;
        private readonly DndDraggableBinding _draggable;
        private readonly VisualElement _panelRoot;
        private readonly DragActivation _activation;
        private readonly int _pointerId;
        // The press target up to the panel root, deepest first: every element the pointer-down reached.
        // Both phases register their pointer callbacks on all of them, a swallowed release is settled on
        // all of them, and any of them may still hold the pointer when the session closes.
        private readonly List<VisualElement> _pressChain = new();

        private bool _active;
        private bool _closed;
        private Vector2 _lastPointerPosition;
        private float _pendingMaxTravel;

        // Pending-phase observation (panel root; see the class note on captured delivery).
        private EventCallback<PointerMoveEvent>? _onPendingMove;
        private EventCallback<PointerUpEvent>? _onPendingUp;
        private EventCallback<PointerCancelEvent>? _onPendingCancel;
        // Hold-to-drag clock. Recurring (survives a mid-hold re-attach; the baseline is taken from the
        // scheduler's own TimerState, so a restart is harmless) rather than a one-shot ExecuteLater,
        // which restarts IN FULL on re-attach — the pool-ghosting time-bomb shape.
        private IVisualElementScheduledItem? _delayTick;
        private long _delayBaselineMs = -1;

        // Active-session state. Movement mode and the applied class arrays are SNAPSHOTS taken when they
        // are applied, never re-read from the live bindings: a mid-drag re-render may swap the settings
        // records (DndDraggableDriver.Update / DndDroppableDriver.Reparse), and restoring by the new
        // values would strand the actually-applied classes and inline translate on the element — the
        // state-ghosting class this codebase's pool discipline exists to prevent.
        private Vector2 _origin;
        private Rect _originRect;
        private Vector2 _grabOffset;
        private StyleTranslate _savedTranslate;
        private Vector2 _baseTranslate;
        private Vector2 _delta;
        private DragMovement _activeMovement;
        private string[] _activeDraggingClasses = System.Array.Empty<string>();
        private string? _overId;
        private DndDroppableBinding? _overBinding;
        private VisualElement? _overElement;
        private string[]? _appliedOverClasses;
        private readonly List<(VisualElement Element, string[] Classes)> _appliedActiveClasses = new();
        private readonly List<(VisualElement Positioner, DndOverlayBinding Binding)> _overlays = new();
        private EventCallback<PointerMoveEvent>? _onDragMove;
        private EventCallback<PointerUpEvent>? _onDragUp;
        private EventCallback<PointerDownEvent>? _onDragDown;
        private EventCallback<PointerCancelEvent>? _onDragCancel;
        private EventCallback<PointerCaptureOutEvent>? _onCaptureOut;
        private EventCallback<KeyDownEvent>? _onEscape;
        private readonly List<VisualElement> _escapeRoots = new();
        private bool _madeSourceFocusable;
        private bool _anchoredFocus;
        private readonly List<DndDroppableRect> _queryBuffer = new();

        internal VisualElement Source => _source;
        internal VisualElement ScopeElement => _scopeElement;
        internal bool IsActivePhase => _active;

        // See Arm: a stale pending session steps aside for a fresh press. No user callback fires — a
        // pending session never surfaced anything.
        internal void DiscardForRearm()
        {
            if (!_active)
            {
                Close();
            }
        }

        // Arms a session from a draggable's pointer-down. Primary button only; no arming while another
        // session (pending or active) exists; disabled draggables never arm; a draggable outside any
        // DndContext warns once per binding and stays inert. No capture and no StopPropagation here —
        // a press that never crosses the activation constraint must remain a plain click.
        internal static void Arm(VisualElement source, DndDraggableBinding draggable, ReconcilerContext ctx, PointerDownEvent evt)
        {
            // A press that can never arm (secondary button, disabled) is validated BEFORE any hand-off:
            // it must not cost a live pending session its observers (a right-button chord mid-press
            // would otherwise silently kill the held left gesture).
            if (evt.button != 0 || draggable.Settings.Disabled)
            {
                return;
            }
            // The trickle-phase armers run outermost first, the reverse of dnd-kit's bubbling activators,
            // where the innermost draggable claims the press and the outer ones see it claimed. So an
            // outer draggable yields to one on the press path below it. It yields to a NoDrag element
            // there too: that is this layer's spelling of a dnd-kit child stopping or preventing its
            // pointer-down, since a child's own pointer-down callbacks run after every armer above it.
            if (IsPressClaimedBelow(source, evt.target as VisualElement, ctx))
            {
                return;
            }
            // A lingering PENDING session yields to a fresh press: a release delivered where none of its
            // pending observers sees it leaves the session open, and without this hand-off the dead
            // session would block every future drag. An ACTIVE
            // session never yields — extra pointer-downs during a drag do not arm.
            if (ctx.ActiveDrag != null)
            {
                if (ctx.ActiveDrag.IsActivePhase)
                {
                    return;
                }
                ctx.ActiveDrag.DiscardForRearm();
            }
            if (ctx.ActiveDrag != null)
            {
                return;
            }
            var scopeElement = FindEnclosingScope(source, ctx, out var scope);
            if (scopeElement == null || scope == null)
            {
                if (!draggable.WarnedNoScope)
                {
                    draggable.WarnedNoScope = true;
                    FiberLogger.LogWarning("Dnd",
                        "A draggable has no enclosing DndContext, so its presses can never become drags. "
                        + "Wrap it (at any depth) in V.DndContext, or remove the Draggable setting.");
                }
                return;
            }
            var panelRoot = source.panel?.visualTree;
            if (panelRoot == null)
            {
                return;
            }
            // The session is INSTALLED before it begins: DragActivation.None activates immediately, and
            // its OnDragStart runs a synchronous discrete flush — a teardown that flush causes (the
            // activeId recipe swapping the source out) must find ctx.ActiveDrag set, or every teardown
            // interlock is bypassed and the closed session would be installed afterward, wedging arming
            // for the tree's lifetime.
            var origin = new DragOrigin
            {
                ScopeElement = scopeElement,
                Scope = scope,
                Source = source,
                Draggable = draggable,
                PanelRoot = panelRoot,
            };
            var session = new DndActiveDrag(ctx, in origin, evt);
            ctx.ActiveDrag = session;
            session.Begin();
        }

        private DndActiveDrag(ReconcilerContext ctx, in DragOrigin origin, PointerDownEvent evt)
        {
            _ctx = ctx;
            _scopeElement = origin.ScopeElement;
            _scope = origin.Scope;
            _source = origin.Source;
            _draggable = origin.Draggable;
            _panelRoot = origin.PanelRoot;
            _pointerId = evt.pointerId;
            for (var current = evt.target as VisualElement ?? origin.Source; current != null; current = current.parent)
            {
                _pressChain.Add(current);
            }
            _pressPosition = evt.position;
            _lastPointerPosition = _pressPosition;
            _activation = origin.Draggable.Settings.Activation
                ?? origin.Scope.Settings.Activation
                ?? DragActivation.Default;
        }

        private void Begin()
        {
            if (_activation.DelaySec > 0f)
            {
                // Hold-to-drag: activation is time-based; travel only ABORTS (Tolerance), never
                // activates. The clock rides the PANEL ROOT's scheduler, not the source's: a re-attach
                // resets a recurring item's phase in full, so a source inside a keyed list that reorders
                // every frame would postpone a source-scheduled tick forever.
                _delayTick = _panelRoot.schedule.Execute(OnDelayTick).Every(DelayTickIntervalMs);
            }
            else if (_activation.Distance <= 0f)
            {
                // Unconstrained (DragActivation.None): the press IS the drag.
                Activate();
                return;
            }
            RegisterPendingObservers();
        }

        // Uncaptured moves flow through the root (covering fast travel that leaves the source's bounds
        // before activation), while a press whose Clickable captured at pointer-down — the source's own
        // (a draggable V.Button) or a child's — delivers target-only, reaching the capturer's callbacks
        // but never the root's. An uncaptured move hits several registrations; OnPendingMove's state
        // guards make the repeats a no-op.
        private void RegisterPendingObservers()
        {
            _onPendingMove = OnPendingMove;
            _onPendingUp = OnPendingUp;
            _onPendingCancel = _ => DiscardPending();
            RegisterOnObserved(_onPendingMove);
            RegisterOnObserved(_onPendingUp);
            RegisterOnObserved(_onPendingCancel);
        }

        private void OnPendingMove(PointerMoveEvent evt)
        {
            // Several registrations can deliver one event more than once; a move arriving after
            // activation or discard is stale either way.
            if (_closed || _active || evt.pointerId != _pointerId)
            {
                return;
            }
            // A stale Pending (the release happened where this panel could not observe it) must never
            // spuriously activate: any move arriving without the PRIMARY button held discards the
            // session lazily. Specifically bit 0, not the whole bitfield — a secondary-button drag after
            // an unobserved left release must not keep a dead left-press session alive.
            if ((evt.pressedButtons & 1) == 0)
            {
                DiscardPending();
                return;
            }
            _lastPointerPosition = evt.position;
            var travel = (_lastPointerPosition - _pressPosition).magnitude;
            if (travel > _pendingMaxTravel)
            {
                _pendingMaxTravel = travel;
            }
            if (_activation.DelaySec > 0f)
            {
                if (_pendingMaxTravel > _activation.Tolerance)
                {
                    DiscardPending();
                }
                return;
            }
            if (travel >= _activation.Distance)
            {
                Activate();
            }
        }

        private Vector2 _pressPosition;

        private void OnPendingUp(PointerUpEvent evt)
        {
            // A secondary-button release is not the end of the primary-press gesture (UI Toolkit
            // dispatches one PointerUpEvent per button under the same pointer id).
            if (_closed || _active || evt.pointerId != _pointerId || evt.button != 0)
            {
                return;
            }
            // Sub-threshold release: a plain click, deliberately untouched (no suppression, no callback).
            DiscardPending();
        }

        private void OnDelayTick(TimerState timer)
        {
            if (_delayBaselineMs < 0)
            {
                _delayBaselineMs = timer.now;
                return;
            }
            if (timer.now - _delayBaselineMs >= (long)(_activation.DelaySec * 1000f))
            {
                // A source mid-reorder (transiently detached) cannot take pointer capture; hold the
                // elapsed clock and activate on a later tick once it is back in the panel.
                if (_source.panel == null)
                {
                    return;
                }
                if (_pendingMaxTravel <= _activation.Tolerance)
                {
                    Activate();
                }
                else
                {
                    DiscardPending();
                }
            }
        }

        private void DiscardPending()
        {
            if (_closed || _active)
            {
                return;
            }
            Close();
        }

        private void Activate()
        {
            _active = true;
            UnregisterPendingObservers();
            _delayTick?.Pause();
            _delayTick = null;

            CaptureActiveSnapshot();
            RegisterActiveObservers();
            EstablishFocusAnchor();
            ApplyActiveStyling();
            BeginOverlaySession();

            var args = new DragStartArgs(ActiveInfo(), _origin);
            FireDiscrete(() => _scope.Settings.OnDragStart?.Invoke(args));
        }

        private void CaptureActiveSnapshot()
        {
            _origin = _lastPointerPosition;
            _originRect = _source.worldBound;
            _grabOffset = _origin - _originRect.position;
            _savedTranslate = _source.style.translate;
            _baseTranslate = ResolveBaseTranslate(_savedTranslate);
            _delta = Vector2.zero;
            // Snapshot what the session applies (see the field-block note): restore symmetry must not
            // depend on the live settings surviving the drag unchanged.
            _activeMovement = _draggable.Settings.Movement;
            _activeDraggingClasses = _draggable.DraggingClasses;
        }

        // Steals the pointer from a child that captured at its own pointer-down (its
        // PointerCaptureOutEvent aborts its click — "it's a drag now"). The drag-lifetime callbacks sit
        // on the whole press chain, not the source alone: an activation inside the pointer-down itself
        // (DragActivation.None) captures before a child's or an ancestor's own pointer-down handler
        // does, so one that captures there holds the pointer until OnDragMove takes it back, and a
        // release with no move between reaches it alone. After a child unmounts while holding the
        // pointer, a move off the source reaches the root alone, and OnDragMove takes it back there.
        private void RegisterActiveObservers()
        {
            _source.CapturePointer(_pointerId);
            _onDragMove = OnDragMove;
            _onDragUp = OnDragUp;
            _onDragDown = OnDragDown;
            _onDragCancel = _ => Cancel();
            _onCaptureOut = OnCaptureOut;
            _onEscape = OnEscapeKey;
            RegisterOnObserved(_onDragMove);
            RegisterOnObserved(_onDragUp);
            RegisterOnObserved(_onDragDown);
            RegisterOnObserved(_onDragCancel);
            _source.RegisterCallback(_onCaptureOut, TrickleDown.TrickleDown);
            // Escape must cancel no matter which of this tree's panels holds keyboard focus — key events
            // dispatch through the FOCUSED panel, which need not be the source's.
            RegisterEscapeOnManagedRoots();
        }

        // The runtime input system only routes keyboard events into a panel that HAS a focused
        // element, and a mouse-only drag typically has none — the Escape listener would never see
        // its KeyDownEvent (verified against real editor input). The source anchors keyboard focus
        // for the session; Close restores what it changed.
        // Panel-local null is not "focus went nowhere": keyboard routes through whichever managed
        // panel holds focus, so the anchor must not steal routing from e.g. a main-panel TextField
        // while the drag runs in a layer panel.
        private void EstablishFocusAnchor()
        {
            var focusController = _source.panel?.focusController;
            if (focusController != null && focusController.focusedElement == null
                && !FiberFocusNavigator.AnyManagedPanelHoldsFocus(_ctx))
            {
                if (!_source.focusable)
                {
                    // Ordering: seed before the write, per FiberPropApplier.RecordFocusableDefault. A Focusable
                    // prop first declared while this anchor stands would otherwise take the anchor's value as
                    // the source's own, and dropping that prop later would hand it back.
                    FiberPropApplier.RecordFocusableDefault(_source);
                    _source.focusable = true;
                    _madeSourceFocusable = true;
                }
                _source.Focus();
                _anchoredFocus = true;
            }
        }

        private void ApplyActiveStyling()
        {
            if (_activeDraggingClasses.Length > 0)
            {
                StyleAnimationClassUtils.AddClasses(_source, _activeDraggingClasses);
            }
            ApplyDragActiveClasses();
        }

        // Every overlay mounted at activation under this session's scope shows the preview, as every
        // dnd-kit DragOverlay renders its children while its own context's drag is active.
        private void BeginOverlaySession()
        {
            foreach (var (positioner, binding) in _ctx.DragOverlayBindings)
            {
                if (!ReferenceEquals(FindEnclosingScope(binding.Anchor ?? positioner, _ctx, out _), _scopeElement))
                {
                    continue;
                }
                _overlays.Add((positioner, binding));
                DndOverlayDriver.BeginSession(positioner, _originRect.size);
            }
            SyncOverlays();
        }

        private void SyncOverlays()
        {
            foreach (var (positioner, binding) in _overlays)
            {
                DndOverlayDriver.SyncPosition(positioner, binding, _source.panel, _lastPointerPosition, _grabOffset);
            }
        }

        private void ApplyDragActiveClasses()
        {
            foreach (var (element, binding) in _ctx.DroppableBindings)
            {
                if (IsCollisionCandidate(element, binding))
                {
                    ApplyDragActiveClassTo(element, binding);
                }
            }
        }

        // Applies the droppable "drag is live" cue exactly once per element per session. Also invoked
        // from the per-move collision sweep, so a droppable that mounts or is re-enabled MID-drag (the
        // activeId recipe conditionally rendering drop zones) gets the cue the moment it becomes a
        // candidate — candidacy and the visual affordance must not diverge.
        private void ApplyDragActiveClassTo(VisualElement element, DndDroppableBinding binding)
        {
            if (binding.ActiveClasses.Length == 0)
            {
                return;
            }
            for (var i = 0; i < _appliedActiveClasses.Count; i++)
            {
                if (ReferenceEquals(_appliedActiveClasses[i].Element, element))
                {
                    return;
                }
            }
            StyleAnimationClassUtils.AddClasses(element, binding.ActiveClasses);
            _appliedActiveClasses.Add((element, binding.ActiveClasses));
        }

        // Candidacy pairs a droppable with its NEAREST enclosing DndContext, keeping nested contexts
        // isolated: subtree containment alone would leak an inner scope's droppables into an
        // outer scope's drag, handing the outer callbacks drop ids they never registered.
        private bool IsCollisionCandidate(VisualElement element, DndDroppableBinding binding)
            => !binding.Settings.Disabled
               && element.panel != null && element.panel == _source.panel
               && ReferenceEquals(FindEnclosingScope(element, _ctx, out _), _scopeElement)
               && binding.Settings.Id != _draggable.Settings.Id;

        private void OnDragMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != _pointerId)
            {
                return;
            }
            // A phantom session (the primary release happened where no observer could see it — a
            // capture-only delivery, or off-window) dies on the first buttonless motion instead of
            // dragging with nothing held. This MOVE-side check is the mouse phantom guard (a mouse
            // phantom always sees a buttonless hover move before the next press); non-mouse pointers,
            // which have no hover moves, are covered by the same-pointer down-cancel in OnDragDown.
            // Cancelling on a fresh MOUSE down instead would misfire on spurious synthesized downs
            // (observed against real editor input killing a legitimate mid-drag session).
            if ((evt.pressedButtons & 1) == 0)
            {
                Cancel();
                return;
            }
            // A child still holding the pointer from its own pointer-down (see RegisterActiveObservers)
            // gives it up to the source here; the child's PointerCaptureOutEvent aborts a Clickable's click.
            if (!_source.HasPointerCapture(_pointerId))
            {
                _source.CapturePointer(_pointerId);
            }
            _lastPointerPosition = evt.position;
            _delta = _lastPointerPosition - _origin;
            if (_activeMovement == DragMovement.Translate)
            {
                _source.style.translate = new Translate(_baseTranslate.x + _delta.x, _baseTranslate.y + _delta.y);
            }
            SyncOverlays();
            UpdateCollision();
            evt.StopPropagation();
        }

        private void UpdateCollision()
        {
            _queryBuffer.Clear();
            foreach (var (element, binding) in _ctx.DroppableBindings)
            {
                if (IsCollisionCandidate(element, binding))
                {
                    ApplyDragActiveClassTo(element, binding);
                    _queryBuffer.Add(new DndDroppableRect(binding.Settings.Id, element.worldBound, binding.Settings.Data));
                }
            }
            var strategy = _scope.Settings.CollisionDetection ?? DndCollisions.RectIntersection;
            var query = new DndCollisionQuery(
                new Rect(_originRect.position + _delta, _originRect.size), _lastPointerPosition, _queryBuffer);
            SetOver(strategy(in query));
        }

        private void SetOver(string? winnerId)
        {
            if (winnerId == _overId)
            {
                return;
            }
            if (_overElement != null && _appliedOverClasses is { Length: > 0 })
            {
                StyleAnimationClassUtils.RemoveClasses(_overElement, _appliedOverClasses);
            }
            _overId = winnerId;
            _overBinding = null;
            _overElement = null;
            _appliedOverClasses = null;
            if (winnerId != null)
            {
                foreach (var (element, binding) in _ctx.DroppableBindings)
                {
                    if (binding.Settings.Id == winnerId && IsCollisionCandidate(element, binding))
                    {
                        _overBinding = binding;
                        _overElement = element;
                        break;
                    }
                }
                if (_overElement != null && _overBinding is { OverClasses.Length: > 0 })
                {
                    // Snapshot the applied array (see the field-block note): removal must target what
                    // was actually applied, not a mid-drag re-parse.
                    _appliedOverClasses = _overBinding.OverClasses;
                    StyleAnimationClassUtils.AddClasses(_overElement, _appliedOverClasses);
                }
            }
            // Over-change is continuous-lane feedback (it fires mid-move, potentially every frame):
            // a plain invoke, never the discrete synchronous-flush bracket drop/cancel use.
            var overInfo = CurrentOverInfo();
            var args = new DragOverArgs(ActiveInfo(), overInfo, _delta);
            _scope.Settings.OnDragOver?.Invoke(args);
        }

        private void OnDragUp(PointerUpEvent evt)
        {
            // Only the PRIMARY release ends the drag — UI Toolkit dispatches one PointerUpEvent per
            // button under the same pointer id, and a right-button tap mid-drag must not commit a drop.
            if (evt.pointerId != _pointerId || evt.button != 0)
            {
                return;
            }
            _lastPointerPosition = evt.position;
            _delta = _lastPointerPosition - _origin;
            var args = new DragEndArgs(ActiveInfo(), CurrentOverInfo(), _delta, _lastPointerPosition);
            // Close the session BEFORE the user callback, deviating from "callback then scrub": OnDragEnd
            // runs in the discrete bracket, whose synchronous flush may unmount the source (the sortable
            // commit) — the cleaner would then find a live session mid-teardown and cancel-scrub what the
            // drop already owned. With the session closed first, that path sees idle and does nothing.
            Close();
            DndPressVariantSettler.Settle(_pressChain, _ctx);
            // Swallowed before bubble-phase listeners on this same element run, so a Clickable on the
            // source (a draggable V.Button) does not fire `clicked` after a REAL drag. A sub-threshold
            // press never reaches here (it discards in Pending) — clicks stay intact.
            evt.StopImmediatePropagation();
            FireDiscrete(() => _scope.Settings.OnDragEnd?.Invoke(args));
        }

        // A fresh primary press under the SAME touch pointer id while this session is active means the
        // previous release was never observed (capture-only or off-window delivery): a finger cannot go
        // down twice. Without this, the stale session hijacks the new tap — its moves drive the ghost
        // drag and its release commits a drop the user never made. Deliberately TOUCH only: mouse AND
        // pen hover, so their phantoms die on the first buttonless hover move (the move-side guard), and
        // real mouse input was observed delivering a spurious synthesized down mid-drag that must not
        // cancel anything. Residual (documented, not structural): touch surfaced through mouse
        // emulation carries the mouse id and has no hover moves — that phantom lingers until Escape.
        private void OnDragDown(PointerDownEvent evt)
        {
            var isTouch = _pointerId >= PointerId.touchPointerIdBase
                && _pointerId < PointerId.touchPointerIdBase + PointerId.touchPointerCount;
            if (isTouch && evt.pointerId == _pointerId && evt.button == 0)
            {
                Cancel();
            }
        }

        private void OnCaptureOut(PointerCaptureOutEvent evt)
        {
            // Close() unregisters this callback BEFORE releasing the pointer, so a capture-out the source
            // itself loses here is always external (another element stole the capture) — a cancel, not
            // our own release. The event trickles and bubbles, so the source also sees the one a press-path
            // child receives when OnDragMove takes the pointer from it, which is not a loss.
            if (evt.pointerId == _pointerId && evt.target == _source)
            {
                Cancel();
            }
        }

        private void OnEscapeKey(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape)
            {
                return;
            }
            evt.StopPropagation();
            Cancel();
        }

        private void Cancel()
        {
            if (_closed)
            {
                return;
            }
            if (!_active)
            {
                Close();
                return;
            }
            var args = new DragCancelArgs(ActiveInfo());
            Close();
            DndPressVariantSettler.Settle(_pressChain, _ctx);
            FireDiscrete(() => _scope.Settings.OnDragCancel?.Invoke(args));
        }

        // Teardown-flavored cancel, called by FiberElementCleaner / the drivers when the source, its
        // scope, or the whole tree is going away MID-FLUSH. The scrub runs synchronously (the element
        // must reach the pool clean), but the user OnDragCancel is deferred to the panel's next
        // scheduler tick: while a commit-phase STATE WRITE is safe mid-flush (it schedules a
        // follow-up render), an arbitrary user callback is not — it can read a half-mutated tree or
        // re-enter the reconciler through anything beyond a setter, so it runs once the flush is
        // done, mirroring how effect callbacks run after the commit.
        // Reconciler disposal passes deferUserCallback: false — a deferred item would fire against the
        // disposed tree (or never, if the panel dies with it), so the callback runs inline as a best
        // effort (its state writes no-op on disposed fibers; external side effects still run).
        internal void CancelForTeardown(bool deferUserCallback = true)
        {
            if (_closed)
            {
                return;
            }
            var wasActive = _active;
            var deferRoot = _source.panel?.visualTree ?? _panelRoot;
            var scope = _scope;
            var args = wasActive ? new DragCancelArgs(ActiveInfo()) : null;
            Close();
            if (!wasActive || args == null)
            {
                return;
            }
            DndPressVariantSettler.Settle(_pressChain, _ctx);
            if (!deferUserCallback)
            {
                InvokeCancelGuarded(scope, args);
                return;
            }
            deferRoot.schedule.Execute(() => InvokeCancelGuarded(scope, args));
        }

        // A throwing scheduled item is never unscheduled — it would re-fire (re-invoking the user
        // callback) and abort the panel's remaining scheduled items every frame — so the deferred
        // cancel contains user exceptions itself, like the frame-tick hooks do.
        private void InvokeCancelGuarded(DndScopeBinding scope, DragCancelArgs args)
        {
            try
            {
                FiberDiscreteEventScope.Run(() => scope.Settings.OnDragCancel?.Invoke(args), _ctx.BatchScheduler);
            }
            catch (System.Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
            }
        }

        // A droppable leaving mid-drag (unmount, or a settings flip to disabled) must drop out of this
        // session's bookkeeping: its applied classes die with it, the over slot clears silently (the next
        // move recomputes and fires OnDragOver as usual — no user callback from mid-flush here).
        internal void OnDroppableInvalidated(VisualElement element)
        {
            for (var i = _appliedActiveClasses.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_appliedActiveClasses[i].Element, element))
                {
                    StyleAnimationClassUtils.RemoveClasses(element, _appliedActiveClasses[i].Classes);
                    _appliedActiveClasses.RemoveAt(i);
                }
            }
            if (ReferenceEquals(_overElement, element))
            {
                if (_appliedOverClasses is { Length: > 0 })
                {
                    StyleAnimationClassUtils.RemoveClasses(element, _appliedOverClasses);
                }
                _overId = null;
                _overBinding = null;
                _overElement = null;
                _appliedOverClasses = null;
            }
        }

        // A render that DECLARES Focusable on the source takes ownership of the flag mid-session: the
        // anchor's transient flip must not be "restored" over a value the props now own (the prop diff
        // would never re-apply it). Dropping the declaration hands ownership back — the applier has just
        // put the element's own default over the anchor's flip, so the anchor re-takes the focusability the
        // session still needs for Escape delivery, and owes the restore again exactly when it wrote for it.
        internal void OnSourceFocusableDeclarationChanged(VisualElement element, bool declared)
        {
            if (!ReferenceEquals(element, _source))
            {
                return;
            }

            if (declared)
            {
                _madeSourceFocusable = false;
                return;
            }

            if (!_anchoredFocus)
            {
                return;
            }

            _madeSourceFocusable = !_source.focusable;
            if (_madeSourceFocusable)
            {
                _source.focusable = true;
            }
        }

        // True while the given element's focus is this session's own keyboard anchor — plumbing, not
        // user intent, so the focus layer must not record it (roving-stop memory, restore capture).
        internal bool IsAnchorFocus(VisualElement element)
            => _anchoredFocus && (element == _source || _source.Contains(element));

        internal void OnOverlayInvalidated(VisualElement positioner)
            => _overlays.RemoveAll(overlay => ReferenceEquals(overlay.Positioner, positioner));

        // Restores everything this session ever wrote and returns the context to idle. Ordering note:
        // the drag-lifetime callbacks unregister BEFORE ReleasePointer, so our own release's
        // PointerCaptureOutEvent cannot re-enter as a cancel.
        private void Close()
        {
            if (_closed)
            {
                return;
            }
            _closed = true;
            UnregisterPendingObservers();
            _delayTick?.Pause();
            _delayTick = null;
            if (_active)
            {
                UnregisterActiveObservers();
                ReleaseFocusAnchor();
                RestoreActiveTranslate();
                RemoveActiveStyling();
                EndOverlaySession();
            }
            if (ReferenceEquals(_ctx.ActiveDrag, this))
            {
                _ctx.ActiveDrag = null;
            }
        }

        private void UnregisterActiveObservers()
        {
            UnregisterFromObserved(_onDragMove);
            UnregisterFromObserved(_onDragUp);
            // MUTANT_SURVIVES(equivalent): OnDragDown only reaches Cancel, which returns at once on the
            // closed session; the unregister releases the session and keeps the chain's callback lists
            // from growing with every drag.
            UnregisterFromObserved(_onDragDown);
            // MUTANT_SURVIVES(equivalent): the pointer-cancel handler also only reaches Cancel, for the
            // same reason as the line above.
            UnregisterFromObserved(_onDragCancel);
            if (_onCaptureOut != null) _source.UnregisterCallback(_onCaptureOut, TrickleDown.TrickleDown);
            if (_onEscape != null)
            {
                foreach (var root in _escapeRoots)
                {
                    root.UnregisterCallback(_onEscape, TrickleDown.TrickleDown);
                }
            }
            _escapeRoots.Clear();
            _onDragMove = null;
            _onDragUp = null;
            _onDragDown = null;
            _onDragCancel = null;
            _onCaptureOut = null;
            _onEscape = null;
            // Not the source alone: an element that took the pointer at its own pointer-down and never
            // gave it back (a release with no move between, see RegisterActiveObservers) would otherwise
            // keep it, and its Clickable its pressed state, after the drag ends.
            foreach (var element in _pressChain)
            {
                element.ReleasePointer(_pointerId);
            }
        }

        // Undo the whole anchor, not just the focusable flag: the session created this focus
        // from nothing, so an already-focusable source must not silently keep it (a lit
        // focus-visible ring and keyboard routing the user never asked for). Focus the user
        // moved elsewhere during the drag is left alone. The check is subtree-aware because a
        // composite source delegates focus to an inner child; the focusable-restore nests inside
        // the anchor branch because made-focusable implies anchored — the flags are never
        // independent.
        private void ReleaseFocusAnchor()
        {
            if (_anchoredFocus)
            {
                if (FiberFocusNavigator.IsFocusedElementWithin(_source, out _))
                {
                    _source.Blur();
                }
                _anchoredFocus = false;
                if (_madeSourceFocusable)
                {
                    _source.focusable = false;
                    _madeSourceFocusable = false;
                }
            }
        }

        private void RestoreActiveTranslate()
        {
            // Restore symmetry runs on the activation-time snapshots (see the field-block note).
            if (_activeMovement == DragMovement.Translate)
            {
                _source.style.translate = _savedTranslate;
            }
        }

        private void RemoveActiveStyling()
        {
            if (_activeDraggingClasses.Length > 0)
            {
                StyleAnimationClassUtils.RemoveClasses(_source, _activeDraggingClasses);
            }
            foreach (var (element, classes) in _appliedActiveClasses)
            {
                StyleAnimationClassUtils.RemoveClasses(element, classes);
            }
            _appliedActiveClasses.Clear();
            if (_overElement != null && _appliedOverClasses is { Length: > 0 })
            {
                StyleAnimationClassUtils.RemoveClasses(_overElement, _appliedOverClasses);
            }
            _overId = null;
            _overBinding = null;
            _overElement = null;
            _appliedOverClasses = null;
        }

        private void EndOverlaySession()
        {
            foreach (var (positioner, _) in _overlays)
            {
                DndOverlayDriver.EndSession(positioner);
            }
        }

        private void UnregisterPendingObservers()
        {
            // MUTANT_SURVIVES(equivalent): OnPendingMove returns at once once the session is active or
            // closed, the only two states this is called in; the unregister releases the session and
            // keeps the chain's callback lists from growing with every drag.
            UnregisterFromObserved(_onPendingMove);
            // MUTANT_SURVIVES(equivalent): OnPendingUp returns at once once the session is active or
            // closed, for the same reason as the line above.
            UnregisterFromObserved(_onPendingUp);
            // MUTANT_SURVIVES(equivalent): the pending pointer-cancel handler reaches DiscardPending, which
            // returns at once once the session is active or closed, for the same reason as the lines above.
            UnregisterFromObserved(_onPendingCancel);
            _onPendingMove = null;
            _onPendingUp = null;
            _onPendingCancel = null;
        }

        private void RegisterOnObserved<TEvent>(EventCallback<TEvent> callback)
            where TEvent : EventBase<TEvent>, new()
        {
            foreach (var element in _pressChain)
            {
                element.RegisterCallback(callback, TrickleDown.TrickleDown);
            }
        }

        private void UnregisterFromObserved<TEvent>(EventCallback<TEvent>? callback)
            where TEvent : EventBase<TEvent>, new()
        {
            if (callback == null)
            {
                return;
            }
            foreach (var element in _pressChain)
            {
                element.UnregisterCallback(callback, TrickleDown.TrickleDown);
            }
        }

        private static bool IsPressClaimedBelow(VisualElement source, VisualElement? target, ReconcilerContext ctx)
        {
            var claimed = false;
            for (var current = target; current != null && current != source && !claimed; current = current.parent)
            {
                ctx.DraggableBindings.TryGetValue(current, out var inner);
                // A disabled draggable never arms, so it claims nothing.
                claimed = ctx.NoDragElements.Contains(current) || inner is { Settings.Disabled: false };
            }
            return claimed;
        }

        // Key events dispatch through whichever panel holds keyboard focus, which need not be the
        // source's panel in a tree spanning layer/world-space hosts — the Escape cancel listens on every
        // managed panel root that exists at activation time.
        private void RegisterEscapeOnManagedRoots()
        {
            void AddRoot(VisualElement? root)
            {
                if (root == null || _escapeRoots.Contains(root))
                {
                    return;
                }
                root.RegisterCallback(_onEscape, TrickleDown.TrickleDown);
                _escapeRoots.Add(root);
            }

            AddRoot(_panelRoot);
            AddRoot(_ctx.MainPanelRoot?.panel?.visualTree);
            foreach (var host in _ctx.LayerHosts.Values)
            {
                if (host.Document != null)
                {
                    AddRoot(host.Document.rootVisualElement?.panel?.visualTree);
                }
            }
            foreach (var record in _ctx.WorldSpaceBindings.Values)
            {
                if (record.Document != null)
                {
                    AddRoot(record.Document.rootVisualElement?.panel?.visualTree);
                }
            }
        }

        private DraggableInfo ActiveInfo()
            => new(_draggable.Settings.Id, _draggable.Settings.Data, _source);

        private DroppableInfo? CurrentOverInfo()
            => _overBinding != null && _overElement != null
                ? new DroppableInfo(_overBinding.Settings.Id, _overBinding.Settings.Data, _overElement)
                : null;

        private void FireDiscrete(System.Action callback)
            => FiberDiscreteEventScope.Run(callback, _ctx.BatchScheduler);

        private static Vector2 ResolveBaseTranslate(StyleTranslate saved)
        {
            // Only a concrete inline pixel translate composes additively with the drag delta; a keyword
            // (unset/null) means zero base, and percent lengths have no panel-space meaning to add — the
            // drag then drives from zero and the original value is restored verbatim at close.
            if (saved.keyword != StyleKeyword.Undefined)
            {
                return Vector2.zero;
            }
            var value = saved.value;
            var x = value.x.unit == LengthUnit.Pixel ? value.x.value : 0f;
            var y = value.y.unit == LengthUnit.Pixel ? value.y.value : 0f;
            return new Vector2(x, y);
        }

        private static VisualElement? FindEnclosingScope(
            VisualElement element, ReconcilerContext ctx, out DndScopeBinding? binding)
        {
            for (var current = element; current != null; current = current.parent)
            {
                if (ctx.DndScopeBindings.TryGetValue(current, out var found))
                {
                    binding = found;
                    return current;
                }
            }
            binding = null;
            return null;
        }
    }
}
