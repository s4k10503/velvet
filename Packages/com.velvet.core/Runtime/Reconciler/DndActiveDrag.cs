#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The drag-gesture state machine — the single owner of one drag session per mounted tree, referenced solely
    // from ReconcilerContext.ActiveDrag (null = idle). A sensor starts it: the pointer sensor arms it (PENDING)
    // from a draggable's pointer-down and promotes it to ACTIVE when the activation constraint is crossed; any
    // other sensor starts it ACTIVE and drives it through DndSensorSession. It returns to idle on drop, cancel,
    // or teardown. Two engine facts shape the pointer sensor's event wiring (both verified against engine
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

        // How one session is driven, fixed for its life: by the pointer sensor (PointerDriven, with the pointer id
        // and the press target the press chain starts at) or by another sensor through DndSensorSession.
        private readonly struct Drive
        {
            internal bool PointerDriven { get; init; }
            internal int PointerId { get; init; }
            internal VisualElement? PressTarget { get; init; }
            internal Vector2 Start { get; init; }
            internal DragActivation Activation { get; init; }
            internal Vector2? ActivatorCoordinates { get; init; }
            internal bool AutoScroll { get; init; }
            internal System.Action? OnClose { get; init; }
        }

        private readonly ReconcilerContext _ctx;
        private readonly VisualElement _scopeElement;
        private readonly DndScopeBinding _scope;
        private readonly VisualElement _source;
        private readonly DndDraggableBinding _draggable;
        private readonly VisualElement _panelRoot;
        private readonly DragActivation _activation;
        private readonly int _pointerId;
        // True for the pointer sensor's session, whose pointer events the session registers for itself; another
        // sensor drives its session through DndSensorSession.
        private readonly bool _pointerDriven;
        // Where the activating pointer pressed; null for a session no pointer started, which collision
        // detection then reads as having no pointer.
        private readonly Vector2? _activatorCoordinates;
        private readonly bool _autoScrollAllowed;
        private System.Action? _onSensorClose;
        // The press target up to the panel root, deepest first: every element the pointer-down reached.
        // Both phases register their pointer callbacks on all of them, a swallowed release is settled on
        // all of them, and any of them may still hold the pointer when the session closes. Empty for a session
        // no pointer drives.
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
        private Rect? _containerRect;
        private StyleTranslate _savedTranslate;
        private Vector2 _baseTranslate;
        // The sensor's translate, and the one the modifiers made of it: the drag's translate everywhere else.
        private Vector2 _rawDelta;
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
        private static readonly System.Comparison<DndDroppableRect> s_byRegistration =
            (left, right) => left.Order.CompareTo(right.Order);
        // The strategy's ranking at the latest move: what DragOverArgs and DragEndArgs carry.
        private IReadOnlyList<DndCollision> _collisions = System.Array.Empty<DndCollision>();
        // The ScrollViews around the source, nearest first, with their offsets when the drag started: the
        // source's translate makes up what they have scrolled since, so it stays under the pointer.
        private readonly List<(ScrollView View, Vector2 Start)> _sourceScrollers = new();
        private DndAutoScroller? _autoScroller;
        private DndSortableSession? _sortable;

        internal VisualElement Source => _source;
        internal VisualElement ScopeElement => _scopeElement;
        internal bool IsActivePhase => _active;
        internal bool IsClosed => _closed;
        internal ReconcilerContext Context => _ctx;
        internal DndScopeBinding Scope => _scope;
        internal string ActiveId => _draggable.Settings.Id;
        internal DragMovement ActiveMovement => _activeMovement;
        internal Rect OriginRect => _originRect;
        internal Rect CollisionRect => new(_originRect.position + _delta, _originRect.size);

        // See Offer: a stale pending session steps aside for a fresh press. No user callback fires — a
        // pending session never surfaced anything.
        internal void DiscardForRearm()
        {
            if (!_active)
            {
                Close();
            }
        }

        // Offers a draggable's pointer-down or key-down to its scope's sensors, in order. A press or key that can
        // never start a drag (disabled, NoDrag) is refused before any sensor sees it. No capture and no
        // StopPropagation here — a press that never crosses the activation constraint must remain a plain click.
        internal static void Offer(VisualElement source, DndDraggableBinding draggable, ReconcilerContext ctx, EventBase evt)
        {
            if (draggable.Settings.Disabled || ctx.NoDragElements.Contains(source))
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
            var scopeElement = FindEnclosingScope(source, ctx, out var scope);
            if (scopeElement == null || scope == null)
            {
                if (evt is PointerDownEvent { button: 0 } && !draggable.WarnedNoScope)
                {
                    draggable.WarnedNoScope = true;
                    FiberLogger.LogWarning("Dnd",
                        "A draggable has no enclosing DndContext, so its presses can never become drags. "
                        + "Wrap it (at any depth) in V.DndContext, or remove the Draggable setting.");
                }
                return;
            }
            var sensors = scope.Settings.Sensors ?? DndSensors.Default;
            if (sensors.Count == 0)
            {
                return;
            }
            var activation = new DndSensorActivation(source, draggable, ctx, scopeElement, scope, evt);
            for (var i = 0; i < sensors.Count; i++)
            {
                if (sensors[i].Offer(evt, activation))
                {
                    return;
                }
            }
        }

        // The pointer sensor's arm: primary button only; no arming while an ACTIVE session exists. A
        // draggable's own activation wins over the sensor's, and the sensor's over the scope's.
        internal static bool ArmPointer(DndSensorActivation sensorActivation, PointerDownEvent evt, DragActivation? activation)
        {
            // A press that can never arm (secondary button) is validated BEFORE any hand-off: it must not cost a
            // live pending session its observers (a right-button chord mid-press would otherwise silently kill
            // the held left gesture).
            if (evt.button != 0)
            {
                return false;
            }
            var ctx = sensorActivation.Context;
            if (!TryTakeSlot(ctx))
            {
                return false;
            }
            var panelRoot = sensorActivation.Element.panel?.visualTree;
            if (panelRoot == null)
            {
                return false;
            }
            var draggable = sensorActivation.Binding;
            var origin = OriginOf(sensorActivation, panelRoot);
            var resolved = draggable.Settings.Activation
                ?? activation
                ?? sensorActivation.Scope.Settings.Activation
                ?? DragActivation.Default;
            // The session is INSTALLED before it begins: DragActivation.None activates immediately, and
            // its OnDragStart runs a synchronous discrete flush — a teardown that flush causes (the
            // activeId recipe swapping the source out) must find ctx.ActiveDrag set, or every teardown
            // interlock is bypassed and the closed session would be installed afterward, wedging arming
            // for the tree's lifetime.
            var session = new DndActiveDrag(ctx, in origin, new Drive
            {
                PointerDriven = true,
                PointerId = evt.pointerId,
                PressTarget = evt.target as VisualElement,
                Start = evt.position,
                Activation = resolved,
                ActivatorCoordinates = evt.position,
                AutoScroll = true,
            });
            ctx.ActiveDrag = session;
            session.Begin();
            return true;
        }

        // Any other sensor's start: the session is ACTIVE at once, at the coordinates the sensor names.
        internal static DndSensorSession? StartFromSensor(
            DndSensorActivation sensorActivation, Vector2 coordinates, bool autoScroll, System.Action? onClose)
        {
            var ctx = sensorActivation.Context;
            var element = sensorActivation.Element;
            // A sensor may start after its own constraint, by when the draggable may have gone.
            if (!ctx.DraggableBindings.TryGetValue(element, out var binding)
                || !ReferenceEquals(binding, sensorActivation.Binding) || binding.Settings.Disabled
                || !ReferenceEquals(FindEnclosingScope(element, ctx, out _), sensorActivation.ScopeElement))
            {
                return null;
            }
            if (!TryTakeSlot(ctx))
            {
                return null;
            }
            var panelRoot = element.panel?.visualTree;
            if (panelRoot == null)
            {
                return null;
            }
            var origin = OriginOf(sensorActivation, panelRoot);
            var session = new DndActiveDrag(ctx, in origin, new Drive
            {
                PointerId = PointerId.invalidPointerId,
                Start = coordinates,
                Activation = DragActivation.None,
                ActivatorCoordinates = sensorActivation.ActivatorCoordinates,
                AutoScroll = autoScroll,
                OnClose = onClose,
            });
            ctx.ActiveDrag = session;
            var handle = new DndSensorSession(session);
            session.Activate();
            return handle;
        }

        // A lingering PENDING session yields to a fresh start: a release delivered where none of its pending
        // observers sees it leaves the session open, and without this hand-off the dead session would block every
        // future drag. An ACTIVE session never yields — one drag per tree, as dnd-kit's active is singular.
        private static bool TryTakeSlot(ReconcilerContext ctx)
        {
            if (ctx.ActiveDrag != null)
            {
                if (ctx.ActiveDrag.IsActivePhase)
                {
                    return false;
                }
                ctx.ActiveDrag.DiscardForRearm();
            }
            return ctx.ActiveDrag == null;
        }

        private static DragOrigin OriginOf(DndSensorActivation sensorActivation, VisualElement panelRoot) => new()
        {
            ScopeElement = sensorActivation.ScopeElement,
            Scope = sensorActivation.Scope,
            Source = sensorActivation.Element,
            Draggable = sensorActivation.Binding,
            PanelRoot = panelRoot,
        };

        private DndActiveDrag(ReconcilerContext ctx, in DragOrigin origin, in Drive drive)
        {
            _ctx = ctx;
            _scopeElement = origin.ScopeElement;
            _scope = origin.Scope;
            _source = origin.Source;
            _draggable = origin.Draggable;
            _panelRoot = origin.PanelRoot;
            _pointerId = drive.PointerId;
            _pointerDriven = drive.PointerDriven;
            _activatorCoordinates = drive.ActivatorCoordinates;
            _autoScrollAllowed = drive.AutoScroll;
            _onSensorClose = drive.OnClose;
            if (drive.PointerDriven)
            {
                for (var current = drive.PressTarget ?? origin.Source; current != null; current = current.parent)
                {
                    _pressChain.Add(current);
                }
            }
            _pressPosition = drive.Start;
            _lastPointerPosition = drive.Start;
            _activation = drive.Activation;
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
            ClaimMove(evt);
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
            if (_pointerDriven)
            {
                RegisterActiveObservers();
                EstablishFocusAnchor();
            }
            _sortable = DndSortableSession.TryBegin(this);
            ApplyActiveStyling();
            _delta = ApplyModifiers(_scope.Settings.Modifiers, Vector2.zero);
            if (_delta != Vector2.zero)
            {
                WriteSourceTranslate();
            }
            RefreshOverlays();
            StartAutoScroll();

            var args = new DragStartArgs(ActiveInfo(), _origin);
            FireDiscrete(() =>
            {
                PublishState();
                PublishTransform();
                _scope.Settings.OnDragStart?.Invoke(args);
                _scope.Store.Dispatch(args, static listener => listener.OnDragStart);
                Announce(Announcements()?.OnDragStart?.Invoke(args));
            });
        }

        // Published only when it differs, so a hook reading the context re-renders when what it reads changed
        // rather than on every move.
        private void PublishState()
        {
            var store = _scope.Store.State;
            var current = store.Current;
            var active = ActiveInfo();
            var over = CurrentOverInfo();
            if (current.Active == active && current.Over == over && current.ActiveNodeRect == _originRect
                && SameCollisions(current.Collisions, _collisions))
            {
                return;
            }
            store.Publish(new DndContextState(active, over, _collisions, _originRect));
        }

        private void PublishTransform()
            => _scope.Store.Transform.Publish(new DndActiveTransform(_draggable.Settings.Id, _delta));

        private static bool SameCollisions(IReadOnlyList<DndCollision> left, IReadOnlyList<DndCollision> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }
            for (var i = 0; i < left.Count; i++)
            {
                if (left[i].Id != right[i].Id || !left[i].Value.Equals(right[i].Value))
                {
                    return false;
                }
            }
            return true;
        }

        private void CaptureActiveSnapshot()
        {
            _origin = _lastPointerPosition;
            _originRect = _source.worldBound;
            _containerRect = _source.parent?.worldBound;
            _savedTranslate = SeatedTranslate.Read(_source);
            _baseTranslate = ResolveBaseTranslate(_savedTranslate);
            _rawDelta = Vector2.zero;
            _delta = Vector2.zero;
            // Snapshot what the session applies (see the field-block note): restore symmetry must not
            // depend on the live settings surviving the drag unchanged.
            _activeMovement = _draggable.Settings.Movement;
            _activeDraggingClasses = _draggable.DraggingClasses;
            for (var current = _source.parent; current != null; current = current.parent)
            {
                if (current is ScrollView view)
                {
                    _sourceScrollers.Add((view, view.scrollOffset));
                }
            }
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

        // A droppable's class a drag puts on an element ranks as a data-[…]: payload does, after every rule the
        // className declares at that rank, so it outranks the element's own utilities while the drag shows it. The
        // source's whileDraggingClass is Framer's whileDrag, a gesture state, so it ranks in the gesture band
        // (StyleLayerPriority.Gesture).
        private void Toggle(VisualElement element, string[] classes, bool on)
            => StyleVariantPayload.Apply(element, classes, on, ChannelPriority, _ctx, this);

        private void ToggleDragging(bool on)
        {
            StyleVariantPayload.Apply(_source, _activeDraggingClasses, on, DraggingPriority, _ctx, this);
            if (on)
            {
                _ctx.StyleAnimationScheduler.YieldToGesture(_source, _activeDraggingClasses);
            }
            else
            {
                _ctx.StyleAnimationScheduler.ReturnFromGesture(_source, _activeDraggingClasses);
            }
        }

        private static readonly long ChannelPriority = StyleLayerPriority.AfterEveryRule(StyleLayerPriority.Data);
        private static readonly long DraggingPriority = StyleLayerPriority.GestureOf(StyleLayerPriority.Data);

        private void ApplyActiveStyling()
        {
            if (_activeDraggingClasses.Length > 0)
            {
                ToggleDragging(true);
            }
            ApplyDragActiveClasses();
        }

        // Every overlay mounted under this session's scope shows the preview, as every dnd-kit
        // DragOverlay renders its children while its own context's drag is active. Runs at activation,
        // at the end of every drain (JoinOverlaysAfterCommit), where the overlay's placeholder is already
        // parented into its scope, and on every move for a mount that committed outside a drain; a
        // positioner already joined is skipped.
        private void JoinMountedOverlays()
        {
            foreach (var (positioner, binding) in _ctx.DragOverlayBindings)
            {
                if (HasJoinedOverlay(positioner))
                {
                    continue;
                }
                if (!ReferenceEquals(FindEnclosingScope(positioner, _ctx, out _), _scopeElement))
                {
                    continue;
                }
                _overlays.Add((positioner, binding));
                DndOverlayDriver.BeginSession(positioner, binding, _originRect.size);
            }
        }

        // Drain-end hook (Reconciler's drain-end callback): an overlay a render mounted joins on the
        // commit that mounted it rather than on the next move.
        internal void JoinOverlaysAfterCommit()
        {
            if (_active)
            {
                RefreshOverlays();
            }
        }

        // Escape must cancel no matter which of this tree's panels holds keyboard focus — key events
        // dispatch through the FOCUSED panel, which need not be the source's — and an overlay mounting
        // mid-drag can create a layer host whose panel did not exist at activation.
        private void RefreshOverlays()
        {
            JoinMountedOverlays();
            if (_pointerDriven)
            {
                RegisterEscapeOnManagedRoots();
            }
            SyncOverlays();
        }

        private bool HasJoinedOverlay(VisualElement positioner)
        {
            var found = false;
            foreach (var (joined, _) in _overlays)
            {
                found |= ReferenceEquals(joined, positioner);
            }
            return found;
        }

        // An overlay sits on the source's rect at the start, moved by the drag's translate and then by its own
        // modifiers, as dnd-kit's DragOverlay applies its modifiers to the context's modified translate.
        private void SyncOverlays()
        {
            foreach (var (positioner, binding) in _overlays)
            {
                var translate = ApplyModifiers(binding.Settings?.Modifiers, _delta);
                DndOverlayDriver.SyncPosition(positioner, binding, _source.panel, _originRect.position + translate, _ctx);
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
            Toggle(element, binding.ActiveClasses, true);
            _appliedActiveClasses.Add((element, binding.ActiveClasses));
        }

        // Candidacy pairs a droppable with its NEAREST enclosing DndContext, keeping nested contexts
        // isolated: subtree containment alone would leak an inner scope's droppables into an
        // outer scope's drag, handing the outer callbacks drop ids they never registered. A droppable on
        // another panel of the tree is a candidate where DndPanelSpace maps its rect into the source's panel.
        // The source's own droppable is one only for a V.Sortable item, whose drag over its own slot is over
        // itself, as dnd-kit's sortable item is.
        private bool IsCollisionCandidate(VisualElement element, DndDroppableBinding binding)
            => !binding.Settings.Disabled
               && DndPanelSpace.CanMap(element.panel, _source.panel, _ctx)
               && ReferenceEquals(FindEnclosingScope(element, _ctx, out _), _scopeElement)
               && (binding.Settings.Id != _draggable.Settings.Id
                   || (_sortable != null && ReferenceEquals(element, _source)));

        private void OnDragMove(PointerMoveEvent evt)
        {
            if (_closed || evt.pointerId != _pointerId)
            {
                return;
            }
            ClaimMove(evt);
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
            MoveTo(evt.position);
            evt.StopPropagation();
        }

        private void MoveTo(Vector2 coordinates)
        {
            _lastPointerPosition = coordinates;
            _rawDelta = coordinates - _origin;
            _delta = ApplyModifiers(_scope.Settings.Modifiers, _rawDelta);
            WriteSourceTranslate();
            RefreshOverlays();
            UpdateCollision();
            PublishTransform();
            _autoScroller?.Update(PointerCoordinates(), _rawDelta, ScrollSearchStart());
        }

        // A scroll the auto-scroller made moves the droppables under a pointer that has not moved.
        internal void OnAutoScrolled()
        {
            if (_closed || !_active)
            {
                return;
            }
            WriteSourceTranslate();
            UpdateCollision();
        }

        private void WriteSourceTranslate()
        {
            if (_activeMovement != DragMovement.Translate)
            {
                return;
            }
            var applied = _baseTranslate + _delta + ScrollCompensation();
            SeatedTranslate.Write(_source, new Translate(applied.x, applied.y));
        }

        private Vector2 ScrollCompensation()
        {
            var total = Vector2.zero;
            foreach (var (view, start) in _sourceScrollers)
            {
                total += view.scrollOffset - start;
            }
            return total;
        }

        // The pointer the collision strategies read: the pointer sensor's own, a pointer-started sensor's
        // activator moved by its translate, and none for a session no pointer started.
        private Vector2? PointerCoordinates()
        {
            if (_pointerDriven)
            {
                return _lastPointerPosition;
            }
            return _activatorCoordinates is { } pressed ? pressed + _rawDelta : null;
        }

        private VisualElement ScrollSearchStart() => _overElement ?? _source;

        private Vector2 ApplyModifiers(IReadOnlyList<DndModifier>? modifiers, Vector2 transform)
        {
            if (modifiers == null || modifiers.Count == 0)
            {
                return transform;
            }
            var args = new DndModifierArgs
            {
                Transform = transform,
                Active = ActiveInfo(),
                ActiveNodeRect = _originRect,
                DraggingNodeRect = _originRect,
                ContainerNodeRect = _containerRect,
                WindowRect = _panelRoot.worldBound,
                FirstScrollableAncestorRect = NearestScrollViewport(_source),
                ActivatorCoordinates = _activatorCoordinates,
                Over = CurrentOverInfo(),
            };
            for (var i = 0; i < modifiers.Count; i++)
            {
                args = args with { Transform = modifiers[i](in args) };
            }
            return args.Transform;
        }

        private static Rect? NearestScrollViewport(VisualElement element)
        {
            for (var current = element.parent; current != null; current = current.parent)
            {
                if (current is ScrollView view)
                {
                    return view.contentViewport.worldBound;
                }
            }
            return null;
        }

        // A V.Sortable item is measured without the displacement this drag gave it, as dnd-kit measures its
        // droppables without their transforms; otherwise an item the drag pushed aside would leave the pointer.
        private DndDroppableRect CandidateRect(VisualElement element, DndDroppableBinding binding)
        {
            var rect = _sortable != null && _sortable.IsMember(element)
                ? DndSortableSession.TransformAgnosticRect(element)
                : DndPanelSpace.RectIn(element, _source.panel!, _ctx);
            var sortable = _ctx.DraggableBindings.TryGetValue(element, out var draggable)
                ? DndSortableSession.InfoOf(draggable.Settings)
                : null;
            return new DndDroppableRect(binding.Settings.Id, rect, binding.Settings.Data, binding.Order, sortable);
        }

        private void UpdateCollision()
        {
            _queryBuffer.Clear();
            foreach (var (element, binding) in _ctx.DroppableBindings)
            {
                if (IsCollisionCandidate(element, binding))
                {
                    ApplyDragActiveClassTo(element, binding);
                    _queryBuffer.Add(CandidateRect(element, binding));
                }
            }
            // Dictionary enumeration does not follow attach order once an entry has been removed, so the
            // candidates are ordered by when each binding was attached; the churned-droppables case in
            // DndInteractionTests pins it.
            _queryBuffer.Sort(s_byRegistration);
            var strategy = _scope.Settings.CollisionDetection ?? DndCollisions.RectIntersection;
            var collisionRect = CollisionRect;
            var pointer = PointerCoordinates();
            var query = new DndCollisionQuery(collisionRect, pointer ?? collisionRect.center, _queryBuffer,
                hasPointer: pointer.HasValue);
            // A custom strategy may return null for no collision, as dnd-kit's getFirstCollision tolerates.
            DndCollision[]? collisions = strategy(in query);
            _collisions = collisions ?? System.Array.Empty<DndCollision>();
            // dnd-kit runs its move effect ahead of its over effect, and the move event carries the target of
            // the render before, which is the one the session still holds here.
            var move = new DragMoveArgs(ActiveInfo(), CurrentOverInfo(), _delta, _collisions);
            _scope.Settings.OnDragMove?.Invoke(move);
            _scope.Store.Dispatch(move, static listener => listener.OnDragMove);
            Announce(Announcements()?.OnDragMove?.Invoke(move));
            SetOver(collisions is { Length: > 0 } ? collisions[0].Id : null);
            PublishState();
        }

        private void SetOver(string? winnerId)
        {
            if (winnerId == _overId)
            {
                return;
            }
            if (_overElement != null && _appliedOverClasses is { Length: > 0 })
            {
                Toggle(_overElement, _appliedOverClasses, false);
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
                    Toggle(_overElement, _appliedOverClasses, true);
                }
            }
            _sortable?.Update(_overId);
            // Over-change is continuous-lane feedback (it fires mid-move, potentially every frame):
            // a plain invoke, never the discrete synchronous-flush bracket drop/cancel use.
            var overInfo = CurrentOverInfo();
            var args = new DragOverArgs(ActiveInfo(), overInfo, _delta, _collisions);
            _scope.Settings.OnDragOver?.Invoke(args);
            _scope.Store.Dispatch(args, static listener => listener.OnDragOver);
            Announce(Announcements()?.OnDragOver?.Invoke(args));
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
            _rawDelta = _lastPointerPosition - _origin;
            _delta = ApplyModifiers(_scope.Settings.Modifiers, _rawDelta);
            // Swallowed before bubble-phase listeners on this same element run, so a Clickable on the
            // source (a draggable V.Button) does not fire `clicked` after a REAL drag. A sub-threshold
            // press never reaches here (it discards in Pending) — clicks stay intact.
            evt.StopImmediatePropagation();
            Drop();
        }

        private void Drop()
        {
            var args = new DragEndArgs(ActiveInfo(), CurrentOverInfo(), _delta, _lastPointerPosition, _collisions);
            var scope = _scope;
            // Close the session BEFORE the user callback, deviating from "callback then scrub": OnDragEnd
            // runs in the discrete bracket, whose synchronous flush may unmount the source (the sortable
            // commit) — the cleaner would then find a live session mid-teardown and cancel-scrub what the
            // drop already owned. With the session closed first, that path sees idle and does nothing.
            Close(animateOverlays: true);
            DndPressVariantSettler.Settle(_pressChain, _ctx);
            FireDiscrete(() =>
            {
                scope.Settings.OnDragEnd?.Invoke(args);
                scope.Store.Dispatch(args, static listener => listener.OnDragEnd);
                Announce(scope, Announcements(scope)?.OnDragEnd?.Invoke(args));
            });
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

        // DndSensorSession's three verbs. A session the sensor drives is ACTIVE from its start, and a verb that
        // reaches it after it closed does nothing.
        internal void SensorMove(Vector2 coordinates)
        {
            if (!_closed && _active)
            {
                MoveTo(coordinates);
            }
        }

        internal void SensorEnd()
        {
            if (!_closed && _active)
            {
                Drop();
            }
        }

        internal void SensorCancel() => Cancel();

        internal DndKeyboardMove KeyboardMove(KeyCode key)
        {
            _queryBuffer.Clear();
            foreach (var (element, binding) in _ctx.DroppableBindings)
            {
                if (IsCollisionCandidate(element, binding))
                {
                    _queryBuffer.Add(CandidateRect(element, binding));
                }
            }
            _queryBuffer.Sort(s_byRegistration);
            return new DndKeyboardMove(key, CollisionRect, ActiveInfo(), _overId, _queryBuffer.ToArray());
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
            var scope = _scope;
            Close(animateOverlays: true);
            DndPressVariantSettler.Settle(_pressChain, _ctx);
            FireDiscrete(() => InvokeCancel(scope, args));
        }

        private static void InvokeCancel(DndScopeBinding scope, DragCancelArgs args)
        {
            scope.Settings.OnDragCancel?.Invoke(args);
            scope.Store.Dispatch(args, static listener => listener.OnDragCancel);
            Announce(scope, Announcements(scope)?.OnDragCancel?.Invoke(args));
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
                FiberDiscreteEventScope.Run(() => InvokeCancel(scope, args), _ctx.BatchScheduler);
            }
            catch (System.Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
            }
        }

        private DndAnnouncements? Announcements() => Announcements(_scope);

        private static DndAnnouncements? Announcements(DndScopeBinding scope)
            => scope.Settings.Accessibility?.Announcements ?? DndAnnouncements.Default;

        private void Announce(string? text) => Announce(_scope, text);

        private static void Announce(DndScopeBinding scope, string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            var announce = scope.Settings.Accessibility?.Announce;
            if (announce != null)
            {
                announce(text!);
                return;
            }
            if (UnityEngine.Accessibility.AssistiveSupport.isScreenReaderEnabled)
            {
                UnityEngine.Accessibility.AssistiveSupport.notificationDispatcher.SendAnnouncement(text!);
            }
        }

        // Takes this session's classes off element without ending the session, for a teardown that drops the
        // element's class projection before it cancels the session.
        internal void ReleaseClassesOf(VisualElement element)
        {
            if (ReferenceEquals(element, _source) && _activeDraggingClasses.Length > 0)
            {
                ToggleDragging(false);
            }
            OnDroppableInvalidated(element);
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
                    Toggle(element, _appliedActiveClasses[i].Classes, false);
                    _appliedActiveClasses.RemoveAt(i);
                }
            }
            if (ReferenceEquals(_overElement, element))
            {
                if (_appliedOverClasses is { Length: > 0 })
                {
                    Toggle(element, _appliedOverClasses, false);
                }
                _overId = null;
                _overBinding = null;
                _overElement = null;
                _appliedOverClasses = null;
            }
        }

        // A sortable item leaving mid-drag takes its displacement off before the pool can hand it on.
        internal void OnDraggableDetached(VisualElement element) => _sortable?.Forget(element);

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
        // PointerCaptureOutEvent cannot re-enter as a cancel. A drop or a cancel hands its overlays to their
        // drop animation; a teardown hides them.
        private void Close(bool animateOverlays = false)
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
                _autoScroller?.Stop();
                _autoScroller = null;
                _sortable?.Restore();
                _sortable = null;
                RestoreActiveTranslate();
                RemoveActiveStyling();
                EndOverlaySession(animateOverlays);
                _scope.Store.State.Publish(DndContextState.Idle);
                _scope.Store.Transform.Publish(default);
            }
            if (ReferenceEquals(_ctx.ActiveDrag, this))
            {
                _ctx.ActiveDrag = null;
            }
            var onClose = _onSensorClose;
            _onSensorClose = null;
            onClose?.Invoke();
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
                SeatedTranslate.Write(_source, _savedTranslate);
            }
        }

        private void RemoveActiveStyling()
        {
            if (_activeDraggingClasses.Length > 0)
            {
                ToggleDragging(false);
            }
            foreach (var (element, classes) in _appliedActiveClasses)
            {
                Toggle(element, classes, false);
            }
            _appliedActiveClasses.Clear();
            if (_overElement != null && _appliedOverClasses is { Length: > 0 })
            {
                Toggle(_overElement, _appliedOverClasses, false);
            }
            _overId = null;
            _overBinding = null;
            _overElement = null;
            _appliedOverClasses = null;
        }

        private void EndOverlaySession(bool animate)
        {
            foreach (var (positioner, binding) in _overlays)
            {
                if (animate)
                {
                    DndDropAnimator.Begin(positioner, binding, _source, _scopeElement, ActiveId, _ctx);
                }
                else
                {
                    DndOverlayDriver.EndSession(positioner, binding);
                }
            }
        }

        private void StartAutoScroll()
        {
            var settings = _scope.Settings.AutoScroll ?? DndAutoScroll.Default;
            if (_autoScrollAllowed && settings.Enabled)
            {
                _autoScroller = new DndAutoScroller(this, settings, _panelRoot);
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

        // The engine's own mark for a move a draggable owns: a reorderable ListView's dragger and a touch
        // ScrollView skip a move carrying it, so neither starts its own gesture under a press this session holds.
        // The mark is set in the trickle phase, before either one's bubble-phase handler reads it.
        private static readonly System.Reflection.PropertyInfo? s_handledByDraggable =
            EngineMember.PointerMoveHandledByDraggable.ResolveProperty();

        private static void ClaimMove(PointerMoveEvent evt) => s_handledByDraggable?.SetValue(evt, true);

        // An element of the press chain leaving the tree mid-session takes this session's callbacks and its hold
        // on the pointer with it, and drops out of the release settle: a pooled element is free for any role
        // before the session closes, and the settle would otherwise settle whatever it has become.
        internal void ForgetPressChainElement(VisualElement element)
        {
            if (_closed || !_pressChain.Remove(element))
            {
                return;
            }
            UnregisterFrom(element, _onPendingMove);
            UnregisterFrom(element, _onPendingUp);
            UnregisterFrom(element, _onPendingCancel);
            UnregisterFrom(element, _onDragMove);
            UnregisterFrom(element, _onDragUp);
            UnregisterFrom(element, _onDragDown);
            UnregisterFrom(element, _onDragCancel);
            element.ReleasePointer(_pointerId);
        }

        private static void UnregisterFrom<TEvent>(VisualElement element, EventCallback<TEvent>? callback)
            where TEvent : EventBase<TEvent>, new()
        {
            if (callback != null)
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
        // managed panel root that exists when the session refreshes its overlays.
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
            => new(_draggable.Settings.Id, _draggable.Settings.Data, _source)
            {
                Sortable = DndSortableSession.InfoOf(_draggable.Settings),
            };

        private DroppableInfo? CurrentOverInfo()
            => _overBinding != null && _overElement != null
                ? new DroppableInfo(_overBinding.Settings.Id, _overBinding.Settings.Data, _overElement)
                {
                    Sortable = _ctx.DraggableBindings.TryGetValue(_overElement, out var overDraggable)
                        ? DndSortableSession.InfoOf(overDraggable.Settings)
                        : null,
                }
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

        // Over logical parents: what a portal relocated pairs with the scope around the portal's call site, as a
        // React portal's children read the context where the portal is declared.
        internal static VisualElement? FindEnclosingScope(
            VisualElement element, ReconcilerContext ctx, out DndScopeBinding? binding)
        {
            for (var current = element; current != null; current = FiberFocusNavigator.LogicalParentOf(current))
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
