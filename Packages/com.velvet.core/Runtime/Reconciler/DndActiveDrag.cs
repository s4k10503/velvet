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
    //      ancestors. So the PENDING phase observes moves on the panel root (nothing is captured yet;
    //      a child that captures at its own pointer-down blacks the moves out, which makes interactive
    //      capturing children documented non-drag zones in distance mode), while the ACTIVE phase
    //      captures on the source and registers its drag-lifetime callbacks on the source itself.
    //   2. TrickleDown-registered callbacks on the capturing element run before bubble-phase ones on the
    //      same element, so the post-drag PointerUp can be swallowed (StopImmediatePropagation) before
    //      UI Toolkit's own Clickable fires `clicked` — a real drag ending on a draggable Button must
    //      not click it.
    internal sealed class DndActiveDrag
    {
        private readonly ReconcilerContext _ctx;
        private readonly VisualElement _scopeElement;
        private readonly DndScopeBinding _scope;
        private readonly VisualElement _source;
        private readonly DndDraggableBinding _draggable;
        private readonly VisualElement _panelRoot;
        private readonly DragActivation _activation;
        private readonly int _pointerId;

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
        private VisualElement? _overlayPositioner;
        private DndOverlayBinding? _overlay;
        private EventCallback<PointerMoveEvent>? _onDragMove;
        private EventCallback<PointerUpEvent>? _onDragUp;
        private EventCallback<PointerCancelEvent>? _onDragCancel;
        private EventCallback<PointerCaptureOutEvent>? _onCaptureOut;
        private EventCallback<KeyDownEvent>? _onEscape;
        private readonly List<VisualElement> _escapeRoots = new();
        private bool _madeSourceFocusable;
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
            // A lingering PENDING session yields to a fresh press: a press whose release was delivered
            // capture-only to a child (the non-drag-zone case) leaves its pending observers blind to the
            // up, and without this hand-off the dead session would block every future drag. An ACTIVE
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
            var session = new DndActiveDrag(ctx, scopeElement, scope, source, draggable, panelRoot, evt);
            ctx.ActiveDrag = session;
            session.Begin();
        }

        private DndActiveDrag(
            ReconcilerContext ctx, VisualElement scopeElement, DndScopeBinding scope,
            VisualElement source, DndDraggableBinding draggable, VisualElement panelRoot, PointerDownEvent evt)
        {
            _ctx = ctx;
            _scopeElement = scopeElement;
            _scope = scope;
            _source = source;
            _draggable = draggable;
            _panelRoot = panelRoot;
            _pointerId = evt.pointerId;
            _pressPosition = evt.position;
            _lastPointerPosition = _pressPosition;
            _activation = draggable.Settings.Activation ?? scope.Settings.Activation ?? DragActivation.Default;
        }

        private void Begin()
        {
            if (_activation.DelaySec > 0f)
            {
                // Hold-to-drag: activation is time-based; travel only ABORTS (Tolerance), never
                // activates. The clock rides the PANEL ROOT's scheduler, not the source's: a re-attach
                // resets a recurring item's phase in full, so a source inside a keyed list that reorders
                // every frame would postpone a source-scheduled tick forever.
                _delayTick = _panelRoot.schedule.Execute(OnDelayTick).Every(16);
            }
            else if (_activation.Distance <= 0f)
            {
                // Unconstrained (DragActivation.None): dnd-kit's raw PointerSensor — the press IS the drag.
                Activate();
                return;
            }
            RegisterPendingObservers();
        }

        // Pending observers live on BOTH the panel root and the source: uncaptured moves flow through
        // the root (covering fast travel that leaves the source's bounds before activation), while a
        // press whose Clickable SOURCE captured at pointer-down delivers target-only — reaching the
        // source's own callbacks but never the root's (a draggable V.Button must still activate). An
        // uncaptured move over the source hits both registrations; OnPendingMove's state guards make
        // the second invocation a no-op. A CHILD that captured keeps blacking out both — the documented
        // non-drag zone.
        private void RegisterPendingObservers()
        {
            _onPendingMove = OnPendingMove;
            _onPendingUp = OnPendingUp;
            _onPendingCancel = _ => DiscardPending();
            _panelRoot.RegisterCallback(_onPendingMove, TrickleDown.TrickleDown);
            _panelRoot.RegisterCallback(_onPendingUp, TrickleDown.TrickleDown);
            _panelRoot.RegisterCallback(_onPendingCancel, TrickleDown.TrickleDown);
            _source.RegisterCallback(_onPendingMove, TrickleDown.TrickleDown);
            _source.RegisterCallback(_onPendingUp, TrickleDown.TrickleDown);
            _source.RegisterCallback(_onPendingCancel, TrickleDown.TrickleDown);
        }

        private void OnPendingMove(PointerMoveEvent evt)
        {
            // Dual registration (root + source) can deliver one event twice; a move arriving after
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

            // Steals the pointer from a child that captured at its own pointer-down (its
            // PointerCaptureOutEvent aborts its click — "it's a drag now"); from here every pointer event
            // of this id is delivered to the source only, so the drag-lifetime callbacks live there.
            _source.CapturePointer(_pointerId);
            _onDragMove = OnDragMove;
            _onDragUp = OnDragUp;
            _onDragCancel = _ => Cancel();
            _onCaptureOut = OnCaptureOut;
            _onEscape = OnEscapeKey;
            _source.RegisterCallback(_onDragMove, TrickleDown.TrickleDown);
            _source.RegisterCallback(_onDragUp, TrickleDown.TrickleDown);
            _source.RegisterCallback(_onDragCancel, TrickleDown.TrickleDown);
            _source.RegisterCallback(_onCaptureOut, TrickleDown.TrickleDown);
            // Escape must cancel no matter which of this tree's panels holds keyboard focus — key events
            // dispatch through the FOCUSED panel, which need not be the source's.
            RegisterEscapeOnManagedRoots();
            // The runtime input system only routes keyboard events into a panel that HAS a focused
            // element, and a mouse-only drag typically has none — the Escape listener would never see
            // its KeyDownEvent (verified against real editor input). The source anchors keyboard focus
            // for the session; Close restores what it changed.
            var focusController = _source.panel?.focusController;
            if (focusController != null && focusController.focusedElement == null)
            {
                if (!_source.focusable)
                {
                    _source.focusable = true;
                    _madeSourceFocusable = true;
                }
                _source.Focus();
            }

            if (_activeDraggingClasses.Length > 0)
            {
                StyleAnimationClassUtils.AddClasses(_source, _activeDraggingClasses);
            }
            ApplyDragActiveClasses();

            _overlay = DndOverlayDriver.FindOverlay(_ctx, out _overlayPositioner);
            if (_overlay != null && _overlayPositioner != null)
            {
                DndOverlayDriver.BeginSession(_overlayPositioner, _originRect.size);
                DndOverlayDriver.SyncPosition(_overlayPositioner, _overlay, _source.panel, _lastPointerPosition, _grabOffset);
            }

            var args = new DragStartArgs(ActiveInfo(), _origin);
            FireDiscrete(() => _scope.Settings.OnDragStart?.Invoke(args));
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

        // Candidacy pairs a droppable with its NEAREST enclosing DndContext (dnd-kit keeps nested
        // contexts isolated): subtree containment alone would leak an inner scope's droppables into an
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
            // dragging with nothing held. This MOVE-side check is deliberately the only phantom guard:
            // cancelling on a fresh primary DOWN instead misfires on spurious synthesized downs (observed
            // against real editor input killing a legitimate mid-drag session), and a mouse phantom
            // always sees a buttonless hover move before the next press anyway. The residual is
            // touch-only (no hover moves), where a phantom lingers until the next gesture's release.
            if ((evt.pressedButtons & 1) == 0)
            {
                Cancel();
                return;
            }
            _lastPointerPosition = evt.position;
            _delta = _lastPointerPosition - _origin;
            if (_activeMovement == DragMovement.Translate)
            {
                _source.style.translate = new Translate(_baseTranslate.x + _delta.x, _baseTranslate.y + _delta.y);
            }
            if (_overlay != null && _overlayPositioner != null)
            {
                DndOverlayDriver.SyncPosition(_overlayPositioner, _overlay, _source.panel, _lastPointerPosition, _grabOffset);
            }
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
            DndPressVariantSettler.Settle(_source, _ctx);
            // Swallowed before bubble-phase listeners on this same element run, so a Clickable on the
            // source (a draggable V.Button) does not fire `clicked` after a REAL drag. A sub-threshold
            // press never reaches here (it discards in Pending) — clicks stay intact.
            evt.StopImmediatePropagation();
            FireDiscrete(() => _scope.Settings.OnDragEnd?.Invoke(args));
        }

        private void OnCaptureOut(PointerCaptureOutEvent evt)
        {
            // Close() unregisters this callback BEFORE releasing the pointer, so a capture-out observed
            // here is always external (another element stole the capture) — a cancel, not our own release.
            if (evt.pointerId == _pointerId)
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
            DndPressVariantSettler.Settle(_source, _ctx);
            FireDiscrete(() => _scope.Settings.OnDragCancel?.Invoke(args));
        }

        // Teardown-flavored cancel, called by FiberElementCleaner / the drivers when the source, its
        // scope, or the whole tree is going away MID-FLUSH. The scrub runs synchronously (the element
        // must reach the pool clean), but the user OnDragCancel is deferred to the panel's next
        // scheduler tick: a state write from inside the flush is silently lost (the fiber's dirty flag
        // clears when the flush ends) and its lost pending value dedups away the next genuine edge.
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
            DndPressVariantSettler.Settle(_source, _ctx);
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

        internal void OnOverlayInvalidated(VisualElement positioner)
        {
            if (ReferenceEquals(_overlayPositioner, positioner))
            {
                _overlay = null;
                _overlayPositioner = null;
            }
        }

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
                if (_onDragMove != null) _source.UnregisterCallback(_onDragMove, TrickleDown.TrickleDown);
                if (_onDragUp != null) _source.UnregisterCallback(_onDragUp, TrickleDown.TrickleDown);
                if (_onDragCancel != null) _source.UnregisterCallback(_onDragCancel, TrickleDown.TrickleDown);
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
                _onDragCancel = null;
                _onCaptureOut = null;
                _onEscape = null;
                if (_source.HasPointerCapture(_pointerId))
                {
                    _source.ReleasePointer(_pointerId);
                }
                if (_madeSourceFocusable)
                {
                    if (ReferenceEquals(_source.panel?.focusController?.focusedElement, _source))
                    {
                        _source.Blur();
                    }
                    _source.focusable = false;
                    _madeSourceFocusable = false;
                }
                // Restore symmetry runs on the activation-time snapshots (see the field-block note).
                if (_activeMovement == DragMovement.Translate)
                {
                    _source.style.translate = _savedTranslate;
                }
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
                if (_overlayPositioner != null)
                {
                    DndOverlayDriver.EndSession(_overlayPositioner);
                }
                _overlay = null;
                _overlayPositioner = null;
            }
            if (ReferenceEquals(_ctx.ActiveDrag, this))
            {
                _ctx.ActiveDrag = null;
            }
        }

        private void UnregisterPendingObservers()
        {
            if (_onPendingMove != null)
            {
                _panelRoot.UnregisterCallback(_onPendingMove, TrickleDown.TrickleDown);
                _source.UnregisterCallback(_onPendingMove, TrickleDown.TrickleDown);
            }
            if (_onPendingUp != null)
            {
                _panelRoot.UnregisterCallback(_onPendingUp, TrickleDown.TrickleDown);
                _source.UnregisterCallback(_onPendingUp, TrickleDown.TrickleDown);
            }
            if (_onPendingCancel != null)
            {
                _panelRoot.UnregisterCallback(_onPendingCancel, TrickleDown.TrickleDown);
                _source.UnregisterCallback(_onPendingCancel, TrickleDown.TrickleDown);
            }
            _onPendingMove = null;
            _onPendingUp = null;
            _onPendingCancel = null;
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
