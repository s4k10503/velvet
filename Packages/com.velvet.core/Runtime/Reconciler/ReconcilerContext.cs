#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Slot range that a single Portal placeholder owns within its target's children list.
    // Multiple Portals targeting the same DOM node each carry one entry of this record so
    // PatchPortal / CleanupPortal can address only that range without disturbing siblings.
    // Keyed by the RESOLVED target element (not a registry id): registry portals, layer portals
    // and world-space panels all share this bookkeeping, and only the element identity groups the
    // portals whose ranges shift together — two layer portals on different layers must never
    // shift each other even though neither carries a registry id. Null only for the mount-time
    // path that found no registry target (nothing was mounted; SlotLength is 0).
    internal readonly record struct PortalSlotInfo(VisualElement? Target, int SlotStart, int SlotLength);

    // Captured on the top-level child fiber of a DETACHED mount — one whose children reconcile outside the
    // normal parent-walked reconcile, so FiberContextSpine's parent-walk cannot reach the host that carries
    // their enclosing Providers. Two cases:
    //   Portal — children mount in the deferred drain (DrainPendingPortalMounts) under the reconcile root.
    //   VirtualList — items mount via the controller (FiberVirtualListController) on scroll, outside any pass.
    // EnclosingSnapshot is the top value of every context active outside the detached mount (the base the
    // consumer reads). DescendantNodes is the committed VNode children to walk to recover any Provider placed
    // directly inside the detached subtree above this fiber (Portal children; null for VirtualList, whose
    // items add no in-list Provider layer above the item). Anchor is the fiber the detached mount parented
    // the children under (the registry-lookup parent the DescendantNodes walk matches against; null when no
    // walk is needed). The snapshot reflects mount time; a host re-render that changes the enclosing context
    // re-runs the detached mount with a correct cursor, so this stale copy only matters for the narrow case
    // of an isolated re-render after such a change.
    internal sealed class DetachedMountContext
    {
        internal readonly List<KeyValuePair<object, object>>? EnclosingSnapshot;
        internal readonly VNode?[]? DescendantNodes;
        internal readonly ComponentFiber Anchor;
        // The ComponentFiber that logically called V.Portal/V.WorldSpace (captured at enqueue time, when
        // FiberStack.Current is genuinely correct — see PendingPortalMounts). Null for VirtualList's
        // detached items (no portal call site to resolve) and for a bare Reconciler.Reconcile() drain
        // with nothing on FiberStack. Distinct from Anchor: Anchor is a REGISTRY-lookup key (where the
        // drain happened to leave the child fiber parented), not the authoring component.
        internal readonly ComponentFiber? LogicalParent;

        internal DetachedMountContext(
            List<KeyValuePair<object, object>>? enclosingSnapshot, VNode?[]? descendantNodes, ComponentFiber anchor,
            ComponentFiber? logicalParent = null)
        {
            EnclosingSnapshot = enclosingSnapshot;
            DescendantNodes = descendantNodes;
            Anchor = anchor;
            LogicalParent = logicalParent;
        }
    }

    // Shared helpers for maintaining the multi-Portal slot range invariant.
    internal static class PortalSlotTracker
    {
        // Shifts PortalSlotInfo.SlotStart by delta for every Portal
        // sharing the same resolved target element whose range starts at or after
        // boundary. excludePlaceholder skips one entry (typically
        // the Portal that just patched its own range and updated its own state). When the cleanup
        // path invokes this after removing its own entry, excludePlaceholder may
        // be left null. Mutate-while-iterate is avoided via a deferred 2-pass scan: first collect the
        // placeholders to shift, then rewrite their entries.
        internal static void ShiftSlotStartsAfter(
            Dictionary<VisualElement, PortalSlotInfo> portalState,
            VisualElement? target,
            int boundary,
            int delta,
            VisualElement? excludePlaceholder = null)
        {
            if (delta == 0 || target == null) return;
            List<VisualElement>? placeholders = null;
            foreach (var entry in portalState)
            {
                if (ReferenceEquals(entry.Key, excludePlaceholder)) continue;
                if (!ReferenceEquals(entry.Value.Target, target)) continue;
                if (entry.Value.SlotStart < boundary) continue;
                placeholders ??= new List<VisualElement>();
                placeholders.Add(entry.Key);
            }
            if (placeholders == null) return;
            foreach (var ph in placeholders)
            {
                var state = portalState[ph];
                portalState[ph] = state with { SlotStart = state.SlotStart + delta };
            }
        }
    }


    // Shared infrastructure between Reconciler subsystems.
    // Holds the host-platform element operations and shared reconcile state injected into each subsystem.
    internal sealed class ReconcilerContext
    {
        public FiberEventBindingManager EventManager { get; }
        public FiberElementFactory FiberElementFactory { get; }
        public ComponentRegistry ComponentRegistry { get; }
        public FiberMemoCache FiberMemoCache { get; }
        public StyleAnimationScheduler StyleAnimationScheduler { get; }
        public ComponentContextStack ComponentContextStack { get; }
        public FiberStack FiberStack { get; }
        public ReconcilerBufferPool BufferPool { get; }

        public Dictionary<VisualElement, VisualElement> WrapperToInnerMap { get; } = new();
        public Dictionary<VisualElement, StyleGestureClassManipulator> GestureManipulators { get; } = new();
        public Dictionary<VisualElement, StyleVariantManipulator> VariantManipulators { get; } = new();
        public Dictionary<VisualElement, StyleConditionalVariantManipulator> ConditionalVariantManipulators { get; } = new();
        public Dictionary<VisualElement, StyleRelationalVariantManipulator> RelationalVariantManipulators { get; } = new();

        // Structural variants (first:/last:/odd:/even:/only:/[&:nth-child(N)]:) declared on a CHILD but
        // evaluated against its position among siblings. Each such child registers its parsed rules here at
        // config time; the container's post-children pass (ApplyStructuralVariants) re-derives every rule's
        // match from the live sibling order. Cleared on element cleanup / reconciler dispose.
        public Dictionary<VisualElement, List<(StyleStructuralKind Kind, int N, string[] Payloads)>> StructuralVariants { get; } = new();

        // has-[:checked]: / has-[:focus]: — an element styled by an event-driven descendant condition. The
        // manipulator lives on the element and listens to bubbling descendant events (ChangeEvent<bool> for
        // checked, FocusIn/Out for focus-within). Mirrors VariantManipulators. The has-[.class]: form is NOT
        // here — it is a side-table (HasClassVariants) re-evaluated by the container post-children pass.
        public Dictionary<VisualElement, StyleHasVariantManipulator> HasVariantManipulators { get; } = new();

        // has-[.class]: — an element styled when one of its DESCENDANTS carries the named class. Unlike the
        // event-driven has- forms there is no signal to listen to, so each such element registers its parsed
        // (className, payload) rules here at config time; the element's own post-children pass
        // (ApplyHasClassVariants, the same hook ApplyStructuralVariants uses but with the element as subject)
        // re-derives every rule from a fresh descendant query, so a child added / removed re-derives it.
        // Cleared on element cleanup / reconciler dispose.
        public Dictionary<VisualElement, List<(string? ClassName, string?[] Payloads)>> HasClassVariants { get; } = new();

        // data-[key=value]: / data-[key]: / aria-[...]: — an element styled by its OWN carried attribute
        // values (UI Toolkit has no HTML attributes, so the element supplies them via the Data / Aria props).
        // Two side-tables, both keyed by the element:
        //   DataAttributes — the live attribute store, namespaced by an "data:"/"aria:" key prefix so one map
        //     holds both families. Updated on mount and on the props (DiffProps) path; the single source the
        //     variant matches against.
        //   AttributeVariants — the parsed (namespace, key, expectedValue, payload) rules, registered at
        //     config time from the class list, re-evaluated against DataAttributes whenever the class list OR
        //     the attribute store changes. There is no UI-Toolkit signal to listen to (no attribute-changed
        //     event), so the props path drives the re-evaluation, mirroring the has-[.class]: side-table.
        // Both cleared on element cleanup / reconciler dispose.
        public Dictionary<VisualElement, Dictionary<string, string>> DataAttributes { get; } = new();
        public Dictionary<VisualElement, List<(StyleAttributeNamespace Ns, string Key, string? ExpectedValue, string[] Payloads)>> AttributeVariants { get; } = new();

        // supports-[prop:value]: — an element styled by a CSS feature query. UI Toolkit targets one fixed
        // engine, so a feature query is STATIC: a well-formed token is always-applied (see
        // StyleSupportsVariantClass). There is no reactive signal and nothing to re-derive, so this table
        // exists only to remember the applied payloads: it is written once at config time (payload applied
        // immediately, always-on) and read solely so a later class-list change can clear the prior payload
        // before re-deriving. Cleared on element cleanup / reconciler dispose.
        public Dictionary<VisualElement, List<string[]>> SupportsVariants { get; } = new();

        // text-transform / text-decoration (uppercase / underline / …). UI Toolkit has no property for either,
        // so they are realised by mutating the displayed text (StyleTextEffectResolver). TextEffects holds each
        // element's OWN parsed effect (the cascade source); TextRawText holds the untransformed text captured at
        // the text-set seams so the effect re-applies idempotently. Both are pure (teardown = Remove(element)).
        public Dictionary<VisualElement, TextEffect> TextEffects { get; } = new();
        public Dictionary<VisualElement, string> TextRawText { get; } = new();

        // The per-element "pure" side-tables (structural / has-[.class]: / data-/aria- rules + their attribute
        // store / supports- / Motion applied-classes) — those whose teardown is a plain Remove(element): no
        // manipulator to detach and no resource (a shader Material, a baked VectorImage, an event subscription,
        // a cleanup callback) to dispose, because the applied style payloads simply die with the element. They
        // are collected here so element cleanup and reconciler dispose clear them through one mechanism
        // (ClearElementSideTables / ClearAllSideTables) rather than one hand-wired line per table: a NEW such
        // side-table is enrolled by adding it to this list, which removes the recurring risk that a new
        // variant's table is introduced but its cleanup / dispose wiring is forgotten (a ghost-leak / pool-reuse
        // bug). Tables backed by a manipulator, a disposable resource, or a cleanup callback (e.g. the variant /
        // gesture / gap / divide / stacked manipulators, the ref cleanups, the shadow / clip / gradient / skew
        // bindings, VirtualList controllers, Outlet scopes) are deliberately NOT here — each needs bespoke
        // teardown and stays wired explicitly. Populated in the constructor (an instance field initializer may
        // not reference these instance properties).
        private readonly System.Collections.IDictionary[] _pureElementSideTables;

        // Removes the element's entry from every pure side-table. The non-generic IDictionary.Remove ignores a
        // key the table does not hold, so this is a no-op for any table the element was never registered in.
        internal void ClearElementSideTables(VisualElement element)
        {
            foreach (var table in _pureElementSideTables)
            {
                table.Remove(element);
            }
        }

        // Empties every pure side-table. Used by Reconciler.Dispose to drop all element references at once.
        internal void ClearAllSideTables()
        {
            foreach (var table in _pureElementSideTables)
            {
                table.Clear();
            }
        }

        // Stacked-variant manipulators (dark:hover:..., group-hover:focus:...), keyed by (target, outer owner,
        // inner kind, inner name, leaf) so each distinct stacked leaf gets its own gated manipulator and is torn
        // down with the element. The owner is the top-level manipulator instance (or a parent stacked
        // manipulator for a 3-deep stack), so two different outer variants stacking the same inner+leaf stay
        // distinct. innerName is the relational name of a NAMED inner (dark:group-hover/sidebar:bg-on) — "" for
        // every other inner — so two stacked named relationals (dark:group-hover/a / dark:group-hover/b) get
        // separate manipulators resolving their own source.
        public Dictionary<(VisualElement target, object owner, int outerPriority, StyleVariantKind inner, string innerName, string? leaf), StyleStackedVariantManipulator> StackedVariantManipulators { get; } = new();

        // Looks up/creates the stacked manipulator for this outer owner + inner variant + leaf and toggles its
        // outer gate. Called by StyleVariantPayload.Apply when a payload is itself a variant. A composed
        // arbitrary leaf layers at max(outer, inner) priority so it sits above either variant alone. A named
        // inner relational (group-hover/sidebar:) threads its name through so the nested manipulator resolves
        // the named source, not the unnamed group/peer.
        internal void GateStackedVariant(VisualElement target, object owner, string variantPayload, bool outerOn, int outerPriority)
        {
            if (!StyleVariantClass.TryParse(variantPayload, out var innerKind, out var innerName, out var leafPayload))
            {
                return;
            }
            // outerPriority is part of the key because two sub-states of one owning manipulator (dark: + sm:
            // share the conditional manipulator; the six group/peer states share the relational one) have the
            // same owner instance and must NOT share a stacked manipulator or its single outer-gate bit.
            var key = (target, owner, outerPriority, innerKind, innerName ?? string.Empty, leafPayload);
            if (outerOn)
            {
                if (!StackedVariantManipulators.TryGetValue(key, out var m))
                {
                    var innerPriority = StyleLayerPriority.ForVariant(innerKind);
                    var priority = outerPriority > innerPriority ? outerPriority : innerPriority;
                    m = new StyleStackedVariantManipulator(this, innerKind, innerName, new string?[] { leafPayload }, priority);
                    StackedVariantManipulators[key] = m;
                    target.AddManipulator(m);
                }
                m.SetOuterGate(true);
            }
            else if (StackedVariantManipulators.TryGetValue(key, out var m))
            {
                // Outer gate closed: clear the leaf. Level-based inners (dark, responsive) are then
                // detached + dropped so their subscription (a stacked dark:'s process-wide
                // DarkModeChanged) is released immediately, not left until unmount — safe because a
                // re-created manipulator re-derives that ambient truth on attach. An EDGE-based
                // inner (hover/focus/active, element-local or relational) must stay attached with
                // just the gate closed: its pointer/focus signals fire only on state edges, so a
                // fresh instance cannot re-seed "pointer already over" and a continuously-held
                // hover would be lost across an outer close/reopen until a physical re-hover. Its
                // hooks are per-element (no process-wide leak) and unmount still sweeps it.
                m.SetOuterGate(false);
                if (!m.RetainsAcrossOuterClose)
                {
                    target.RemoveManipulator(m);
                    StackedVariantManipulators.Remove(key);
                }
            }
        }

        public Dictionary<VisualElement, StyleGapManipulator> GapManipulators { get; } = new();
        public Dictionary<VisualElement, StyleDivideManipulator> DivideManipulators { get; } = new();
        public Dictionary<VisualElement, StyleGridManipulator> GridManipulators { get; } = new();

        // Per-divided-child dashed / dotted divider paint (divide-dashed / divide-dotted), keyed by the CHILD
        // element (not the container) — the divider is painted on the child's own generateVisualContent, since
        // a container paints BEHIND its children. Attached / updated by StyleDivideManipulator; the callback is
        // not a style property, so the pool reset cannot scrub it: FiberElementCleaner detaches it per child
        // (so a keyed-list reorder recycling one child is still caught) and Reconciler.Dispose sweeps the rest.
        public Dictionary<VisualElement, DivideDashChildBinding> DivideDashBindings { get; } = new();

        // Per-Motion-element bookkeeping of the class set actually APPLIED (base ClassNames plus any
        // variant classes propagated from an ancestor Motion's active label, PLUS the variant-only classes
        // alone — see MotionAppliedClassSet). The patch path diffs the new applied set against this stored one
        // so a label change swaps the propagated classes even though the node's base ClassNames are unchanged,
        // and replays that swap as a runtime variant transition when the variant-only classes actually
        // changed and the node declares a Transition (see FiberNodePatcher.PatchMotion). A pure side-table
        // (bare Remove on teardown), so it is enrolled in _pureElementSideTables and cleared on element
        // cleanup / reconciler dispose through that mechanism.
        public Dictionary<VisualElement, MotionAppliedClassSet> MotionAppliedClasses { get; } = new();

        // Per-Motion-element bookkeeping of the label last propagated to CHILDREN (Animate ?? ambient) —
        // independent of MotionAppliedClasses above, because a "coordinator" Motion may propagate a label to
        // descendants (for staggerChildren/delayChildren orchestration) while carrying no Variants of its own,
        // and so never gets a MotionAppliedClasses entry at all. FiberNodePatcher.PatchMotion compares the
        // freshly-resolved child label against this stored one to detect an ACTUAL change (not merely an
        // unrelated re-render that happens to re-propagate the same label) before establishing a fresh
        // MotionOrchestrationFrame — a re-render that keeps the same label must not re-trigger the stagger.
        // A pure side-table (bare Remove on teardown), so it is enrolled in _pureElementSideTables and cleared
        // on element cleanup / reconciler dispose through that mechanism.
        public Dictionary<VisualElement, string> MotionChildLabel { get; } = new();

        // The last VisualElement a V.Motion(layoutId:) id settled at, and the resolved layout rect
        // (parent-relative, from element.layout) it settled at — used by MotionLayoutIdDriver to
        // detect a rect change (including across a DIFFERENT physical element entirely, e.g. after a
        // same-key type flip or a move to a different parent) and FLIP-tween from the old rect to the
        // new one. Keyed by the id string, not an element, so it cannot ride the _pureElementSideTables
        // auto-clear mechanism below (that clears entries keyed BY a departing element, not entries
        // that happen to reference one as a value) — ElementToLayoutId is the reverse index that makes
        // manual cleanup possible: when an element is torn down, look up its id here, and only then
        // remove the LayoutIdRegistry entry IF it still points at this exact element (a same-key type
        // flip may already have overwritten it with the replacement before the old element's own
        // teardown runs).
        public Dictionary<string, (VisualElement Element, Rect Rect)> LayoutIdRegistry { get; } = new();
        public Dictionary<VisualElement, string> ElementToLayoutId { get; } = new();

        // The recurring physics tick for an in-flight layoutId FLIP tween, keyed by the animating
        // element. Owns a real scheduled resource (unlike ElementToLayoutId above), so it is deliberately
        // NOT enrolled in _pureElementSideTables — MotionLayoutIdDriver.CancelForTeardown pauses and
        // removes it explicitly, mirroring StyleAnimationScheduler's own spring-tick teardown for the
        // variant enter/exit case.
        public Dictionary<VisualElement, IVisualElementScheduledItem> LayoutIdTicks { get; } = new();

        // Per-element drop-shadow bookkeeping for the shadow-* className layer, keyed by the element
        // itself — the shadow needs NO structural wrapper. Like skew and gradient, the shadow is painted
        // by the element's own generateVisualContent (DropShadowSilhouette draws the baked shadow texture
        // BEHIND the content, bleeding outside the box), so it composes as a paint, not a wrapper —
        // matching CSS box-shadow (non-structural: it neither alters layout nor escapes a transform on the
        // element). Maintained by FiberNodePatcher's shadow methods and torn down by FiberElementCleaner.
        public Dictionary<VisualElement, DropShadowBinding> ShadowBindings { get; } = new();

        // Per-element clip-path bookkeeping for the clip-path-* className layer, keyed by the INNER
        // (real) element. An entry means the element is wrapped in a stencil-masking clip wrapper
        // (also present in WrapperToInnerMap) whose baked VectorImage must be destroyed when the
        // element is cleaned up or the clip class is removed. Maintained by FiberNodePatcher's
        // clip-path methods and torn down by FiberElementCleaner. The shadow paint is suppressed
        // while a clip is active (CSS clip-path clips the box-shadow too), but that is a paint gate,
        // not a wrapper conflict — the clip wrapper still hosts a now-paint-shadow inner fine.
        public Dictionary<VisualElement, ClipPathBinding> ClipPathBindings { get; } = new();

        // Hook to re-resolve a clipped element's mask from its live class list, set by FiberNodePatcher.
        // StyleVariantPayload.Apply invokes it after toggling a clip-path payload on a state change
        // (hover:clip-path-[…] etc.) — a clip class toggle alone does nothing (UITK has no clip-path
        // property), so the clip wrapper's mask must be re-derived. Null until the patcher wires it.
        public System.Action<VisualElement> ClipPathReResolve { get; set; } = null!;

        // Per-element ring-* / outline-* bookkeeping, keyed by the INNER (real) element. An entry means the
        // element is wrapped in a passthrough wrapper (also in WrapperToInnerMap) holding a native-border
        // overlay that paints the outset (or inset) ring band. No GPU resource to dispose (unlike clip): the
        // overlay is a plain bordered VisualElement, so cleanup just unwraps + drops the entry. Mutually
        // exclusive with ClipPathBindings on the same element (one structural wrapper per element — ring is
        // the lowest-precedence wrapper layer). The shadow is now a paint (no wrapper), so it composes with a
        // ring rather than competing for the wrapper.
        public Dictionary<VisualElement, RingBinding> RingBindings { get; } = new();

        // Sheared-silhouette bookkeeping for skew-x-*/skew-y-* elements (SkewSilhouette). Keyed by
        // the element itself — skew paints via the element's own generateVisualContent, no wrapper.
        public Dictionary<VisualElement, SkewBinding> SkewBindings { get; } = new();

        // Dashed / dotted border outline per element (border-dashed / border-dotted), keyed by the element
        // itself — like skew, the outline is painted by the element's own generateVisualContent with no
        // wrapper (only the border color is suppressed; the fill and width are untouched). Defers to skew /
        // shadow when either owns the face. Maintained by FiberWrapperElementAppliers' border-style methods
        // and torn down by FiberElementCleaner / Reconciler.Dispose.
        public Dictionary<VisualElement, BorderStyleBinding> BorderStyleBindings { get; } = new();

        // Active gradient background per element (bg-gradient-to-* + from/via/to). Keyed by the element
        // itself — the gradient is baked to a texture set as the element's own background-image, no
        // wrapper. The stored spec lets the patch path skip a redundant re-bake, and cleanup clears the
        // background-image so a pooled element cannot ghost a prior gradient.
        public Dictionary<VisualElement, GradientSpec> GradientBackgrounds { get; } = new();

        // Per-element animate-* motion (animate-gradient / -shimmer / -hue). Keyed by the element itself — the
        // motion drives the element's own inline style (a background-position pan or a hue-rotate filter) with
        // no wrapper. The binding holds a recurring scheduled tick, so cleanup must PAUSE it (unlike the pure
        // side-tables); FiberElementCleaner / Reconciler.Dispose call StyleAnimateDriver.Detach.
        public Dictionary<VisualElement, StyleAnimateBinding> AnimationBindings { get; } = new();

        // Per-element filter-* transition (transition-filter opt-in), keyed by the element itself — the tween
        // drives the element's own inline filter with no wrapper. The binding holds a one-shot scheduled tick,
        // so cleanup must PAUSE it (unlike the pure side-tables); FiberElementCleaner / Reconciler.Dispose call
        // StyleFilterTransitionDriver.Detach. The driver's own ConditionalWeakTable is the lookup the resolver
        // uses during event callbacks (no context there); this dictionary only mirrors the refs so the dispose
        // sweep can enumerate them (a CWT is not enumerable).
        public Dictionary<VisualElement, StyleFilterTransitionBinding> FilterTransitionBindings { get; } = new();

        // Per-SceneView-element bookkeeping (V.SceneView), keyed by the element itself. The binding
        // owns a live resource pair — the framework-created RenderTexture and the camera targeting it —
        // so it is NOT a pure side-table: FiberElementCleaner releases both ends on element teardown
        // (geometry callback unregistered, camera politely untargeted, texture destroyed) and
        // Reconciler.Dispose sweeps any binding still live at root disposal.
        public Dictionary<VisualElement, SceneViewBinding> SceneViewBindings { get; } = new();

        // Per-Particles-element bookkeeping (V.Particles), keyed by the element itself. The binding
        // owns a live GameObject — the hidden simulation host cloned from the source effect — plus a
        // painter callback and a repaint tick, so it is NOT a pure side-table: FiberElementCleaner
        // destroys the host on element teardown and Reconciler.Dispose sweeps any binding still live
        // at root disposal.
        public Dictionary<VisualElement, ParticlesBinding> ParticlesBindings { get; } = new();

        // Per-Anchored-element bookkeeping (V.Anchored), keyed by the element itself. The binding owns a
        // live resource — the recurring per-frame projection tick (AnchoredDriver) — so it is NOT a pure
        // side-table: FiberElementCleaner pauses the tick on element teardown and Reconciler.Dispose sweeps
        // any binding still live at root disposal.
        public Dictionary<VisualElement, AnchoredBinding> AnchoredBindings { get; } = new();

        // Per-focus-scope bookkeeping (FiberElementProps.FocusScope / V.FocusScope), keyed by the scope
        // root element. The binding owns a registered AttachToPanelEvent callback (AutoFocus / lazy
        // navigator attach), so it is NOT a pure side-table: FiberElementCleaner fires RestoreFocus and
        // detaches it on teardown, and Reconciler.Dispose sweeps any binding still live at root disposal.
        // FiberFocusNavigator consults this registry at event time (innermost-scope resolution walks an
        // element's parent chain against these keys).
        public Dictionary<VisualElement, FocusScopeBinding> FocusScopeBindings { get; } = new();

        // Chained-portal focus bookkeeping (PanelFocusOrder.Chained): the DECLARING-panel placeholder that
        // acts as the portal's proxy tab stop → the host record its focus forwards into, plus the reverse
        // index (host panel root → placeholder) the host-edge escape consults. Entries are added in
        // DrainPendingPortalMounts and removed by FiberElementCleaner when the portal unmounts;
        // Reconciler.Dispose sweeps the rest.
        public Dictionary<VisualElement, PanelHostRecord> ChainedPlaceholders { get; } = new();
        public Dictionary<VisualElement, VisualElement> ChainedHostRoots { get; } = new();

        // The panel roots FiberFocusNavigator has attached its listener trio to, with the registered
        // callbacks retained so Reconciler.Dispose can unregister them (panel roots belong to the user /
        // the host panels and can outlive this reconciler). Keyed by the panel's TRUE root
        // (panel.visualTree), so two attach requests from different elements of one panel dedup.
        public Dictionary<VisualElement, (EventCallback<NavigationMoveEvent> OnMove, EventCallback<FocusInEvent> OnFocusIn, EventCallback<FocusOutEvent> OnFocusOut)> NavigatorAttachments { get; } = new();

        // Deferred navigator attachments: elements whose panel was unresolved when EnsureAttached ran (a
        // detached mount root, a placeholder configured before its declaring tree attaches), each holding
        // a self-removing AttachToPanelEvent hook. Tracked so DetachAll can unregister hooks that never
        // fired.
        public List<(VisualElement Element, EventCallback<AttachToPanelEvent> Hook)> NavigatorPendingAttachHooks { get; } = new();

        // Drag-and-drop registries (V.DndContext / V.Draggable / V.Droppable / V.DragOverlay), keyed by
        // the carrying element. None are pure side-tables: the draggable binding owns a registered
        // pointer-down armer, the overlay owns forced inline picking/position state, and all four are
        // torn down explicitly by FiberElementCleaner and swept by Reconciler.Dispose.
        public Dictionary<VisualElement, DndScopeBinding> DndScopeBindings { get; } = new();
        public Dictionary<VisualElement, DndDraggableBinding> DraggableBindings { get; } = new();
        public Dictionary<VisualElement, DndDroppableBinding> DroppableBindings { get; } = new();
        public Dictionary<VisualElement, DndOverlayBinding> DragOverlayBindings { get; } = new();

        // The one live drag session (pending or active) for this tree, owned by DndActiveDrag itself:
        // null = idle. See DndActiveDrag for the state machine.
        internal DndActiveDrag? ActiveDrag { get; set; }

        // Per-Portal placeholder bookkeeping. SlotStart + SlotLength identify the
        // range of FiberPortalRegistry.Get(TargetId).Children owned by this Portal — the
        // invariant that lets multiple Portals coexist on the same target without overwriting each
        // other on patch (each Portal reconciles only its own slot range) and on cleanup (only the
        // range is removed, not the entire target). When a Portal's range grows or shrinks, the
        // SlotStart of Portals later in target.children is shifted by the delta.
        public Dictionary<VisualElement, PortalSlotInfo> PortalState { get; } = new();

        // Portal mounts deferred until the enclosing reconcile pass completes. When a PortalNode
        // (or a WorldSpaceNode — the same deferred-mount flow) is encountered during a reconcile,
        // its target-side reconcile is queued here instead of
        // running synchronously — synchronous mount would interleave the inner Portal's children
        // into the outer Portal's slot range and stale every nested slot index by the placeholder
        // insertion that follows. The queue is drained by Reconciler.Reconcile at
        // top-level (the SharedReconcileDepth == 0 finally branch) after the outer
        // reconcile finishes producing target.children for outer's own contribution, so each
        // queued Portal mounts with a fresh slotStart = target.childCount that sits after
        // all previously-mounted Portals' ranges. PortalState is recorded for each
        // placeholder only at drain time — between enqueue and drain the placeholder has no entry,
        // matching FiberNodePatcher.PatchPortal's missing-entry path which logs and skips.
        // Target is resolved at ENQUEUE only for registry portals (their not-registered warning
        // stays a mount-time signal); layer portals and world-space nodes carry null and resolve —
        // creating their framework host on first use — at drain time, when the placeholder is
        // attached and the declaring panel is therefore known.
        // LogicalParent is the ComponentFiber whose Body was mid-render when V.Portal/V.WorldSpace was
        // called (_ctx.FiberStack.Current at enqueue time — the only point this is available, since the
        // drain runs after the whole top-level reconcile pass unwinds). Captured for cross-panel
        // synthetic event bubbling: the drained children's own fiber.Parent ends up pointing at
        // whatever fiber happens to be on top of FiberStack at DRAIN time (see DrainPendingPortalMounts'
        // drainAnchor), which is NOT the logically-enclosing component — LogicalParent is the correct
        // one, stamped onto DetachedMountContext separately from Anchor.
        public Queue<(VisualElement Placeholder, VNode Node, VisualElement? Target,
            List<KeyValuePair<object, object>> ContextSnapshot, ComponentFiber? LogicalParent)> PendingPortalMounts { get; } = new();

        // Guards FiberCrossPanelPointerRouter.AttachToMainPanel against attaching twice on the same
        // main panel — V.Mount is idempotent-safe to call from a component's own render (uncommon but
        // not disallowed) and the router's TrickleDown listeners must not stack.
        internal bool CrossPanelRouterAttached { get; set; }

        // The VisualElement V.Mount attached this context's root fiber to. Recorded so a framework
        // host panel (layer or world-space) that currently holds focus can hand it back here before
        // being torn down — the host's own FocusController is destroyed along with its GameObject
        // (world-space) or reused across children (layer), so without this, focus would either
        // dangle on a defunct panel or simply vanish, leaving keyboard input going nowhere until the
        // app author notices and refocuses something manually.
        internal VisualElement? MainPanelRoot { get; set; }

        // Framework-owned layer host panels (V.Portal(layer:)), one per UILayer, created lazily at
        // the first drain that needs the layer and shared by every portal on it. NOT a pure
        // side-table: each record owns a live GameObject plus runtime-created panel assets, torn
        // down by the reconciler dispose sweep. Hosts persist across child removals — a layer whose
        // last portal left keeps its (empty, cheap) host so toggling portals does not churn panels.
        public Dictionary<UILayer, PanelHostRecord> LayerHosts { get; } = new();

        // Framework-owned world-space host panels (V.WorldSpace), one per placeholder. NOT a pure
        // side-table: each record owns a live GameObject plus runtime-created panel assets,
        // destroyed by FiberElementCleaner when the placeholder leaves the tree and swept at
        // reconciler disposal.
        public Dictionary<VisualElement, PanelHostRecord> WorldSpaceBindings { get; } = new();

        // The declaring panel's driving UIDocument per panel, filled by
        // PanelHostFactory.ResolveDeclaring so each distinct declaring panel costs one
        // FindObjectsOfTypeAll scan per reconciler instead of one per host mount. Only the document
        // lookup is cached — the settings and the sorting base are re-read live so runtime changes
        // propagate — and only successful resolutions land here (a miss must stay retryable for the
        // late-declaring upgrade).
        public Dictionary<IPanel, UIDocument> DeclaringSettingsCache { get; } = new();

        // Declaring panels whose resolution MISSED during the current top-level pass. A miss means a
        // full FindObjectsOfTypeAll scan found nothing, and every further host mount in the same
        // pass would repeat that scan for the same answer; the set clears at the top-level boundary
        // so a panel that gains a driving document later still resolves on the next pass (the
        // late-declaring upgrade path).
        public HashSet<IPanel> DeclaringResolveMisses { get; } = new();

        // Inline-mounted ComponentFiber whose insertion / layout effect commit is deferred to the
        // post-commit drain. The DOM mutation + ref attachment must complete before layout effect
        // setup runs. Velvet's inline-mount path defers child reconcile via
        // RenderAndReconcile(deferReconcile: true) so the parent expansion calls
        // CreateElement + InvokeRefCallback for child elements AFTER MountInline
        // returns; running LayoutEffects inside MountInline would observe stale (null) refs.
        // The top-level reconcile entry drains this stack before its own layout-effect commit so
        // every layout effect runs once all child refs are attached. A Stack (LIFO) is used so
        // the drain runs deepest-first — layout effects commit children
        // before their parent (bottom-up), so a parent layout effect that reads a child's
        // imperative handle / measured size observes the child's already-applied effect.
        // Both Mount and update commits push onto this stack: MountInline pushes with
        // IsMount: true, RenderInlineForExpansion (parent re-render reaching an
        // existing inline child) pushes with IsMount: false. The drain forwards the flag
        // to the insertion- and layout-effect passes as mountDoubleInvoke so
        // the Editor-only mount double-invoke fires only on initial Mount, not on the deps-changed
        // re-expansion path: a layout effect on an update fires its deps
        // cleanup + setup once, not twice.
        public Stack<(ComponentFiber Fiber, bool IsMount)> DeferredInlineLayoutEffectFibers { get; } = new();

        // Fibers with pending passive (UseEffect) effects awaiting the next post-paint drain. Unlike
        // layout effects (committed synchronously, bottom-up, before paint) passive effects fire
        // asynchronously after paint — but they must still observe the tree-ordered 2-phase
        // contract: every pending fiber's effect CLEANUPS run before ANY fiber's effect SETUP, and
        // within each phase fibers commit child-before-parent (post-order). A single drain
        // (FiberEffects.DrainPassiveEffects) walks this set instead of the old per-fiber
        // schedule.Execute(RunEffects), which fired each fiber's own cleanup+setup pair in
        // scheduler-callback order with no cross-fiber phase separation. Insertion-ordered (List)
        // with a membership set so a fiber re-staged in the same pass is not enqueued twice.
        public List<ComponentFiber> PendingPassiveEffectFibers { get; } = new();
        public HashSet<ComponentFiber> PendingPassiveEffectFiberSet { get; } = new();

        // True once a DrainPassiveEffects callback has been registered on the host scheduler for
        // the current batch of PendingPassiveEffectFibers. Prevents scheduling more than
        // one drain per paint-tick even when many fibers stage passive effects in one reconcile pass.
        // Reset when the drain runs.
        public bool PassiveEffectDrainScheduled { get; set; }
        public Dictionary<VisualElement, FiberVirtualListController> VirtualListControllers { get; } = new();
        public Dictionary<VisualElement, IRouteScope> OutletScopes { get; } = new();

        // Set of every Outlet container VE created by FiberNodeFactory. Populated
        // unconditionally at Outlet mount time (independent of whether a Router is registered, so
        // scope-less tests are covered). The spine reconstruction reads this to verify a
        // wrapper-mounted spineChild's MountPoint actually belongs to an Outlet — using the USS
        // class as an identity discriminator instead would be vulnerable to user code or external
        // styling toggling the class via className props.
        public HashSet<VisualElement> OutletContainers { get; } = new();

        // Effective key override published by the expansion pass for VNodes whose identity is gated
        // by an enclosing keyed FragmentNode. The keyed reconciler reads this map (via
        // ChildReconciler.EffectiveKey) instead of VNode.Key when looking up
        // identity. The override composes Fragment scope chain with the child's own key (or its
        // positional index when unkeyed) so children of the same keyed Fragment pair as a unit
        // across reorders, while sibling Fragments with the same inner child keys do not collide.
        // Keyed by VNode reference — each reconcile pass produces fresh VNode instances, so entries
        // do not collide across passes. Cleared at the end of every top-level Reconcile.
        public Dictionary<VNode, string> EffectiveKeys { get; } = new();

        // Old VNode trees of inline children re-rendered via RenderInlineForExpansion during the
        // current reconcile pass, queued for pooled-object return at the top-level boundary rather than
        // immediately. A parent re-render reaches an existing inline child, captures that child's old tree
        // as the patch baseline during its old-side expansion, then re-renders the child (overwriting its
        // PreviousTree) on the new side. Returning the old tree to the VNode pool inside that nested
        // render would let the same pass rent and mutate those very nodes while rendering later siblings —
        // a use-after-return that empties the baseline's children so PatchNode re-inserts the child's
        // subtree instead of patching it (the subtree visibly duplicates). Deferring the return to the
        // top-level finally keeps the nodes alive for the whole pass (so the baseline stays intact and no
        // renter can alias them) while still pooling them for the next pass — no added GC pressure.
        // Each entry carries the owning fiber so the drain-time sweep can mark that fiber's committed
        // tree (and memoized roots) live. Drained and cleared at the end of every top-level Reconcile;
        // a fiber unmounting mid-pass takes its own entries with it (FiberRenderer.Unmount) so they are
        // swept while its committed state and parent chain are still intact rather than after teardown
        // emptied the mark roots.
        public List<(VNode?[] Tree, ComponentFiber Owner)> DeferredInlineOldTreeReturns { get; } = new();

        // Fibers whose time-sliced reconcile is parked with a PendingOldTree baseline. A paused pass
        // keeps reading that baseline across frames, so every retirement sweep marks these baselines
        // live — another fiber retiring in between must not recycle nodes a parked diff still reads
        // (memo hits legitimately share instances across fibers' trees). Maintained by the park /
        // resume / unmount paths; a stale leftover entry only over-spares (leak-safe direction).
        internal HashSet<ComponentFiber> ParkedBaselineFibers { get; } = new();

        // Callback-ref bookkeeping per element (BaseElementNode.RefCallback): the callback identity
        // that installed the current ref plus its returned cleanup. The identity is stored so a
        // patch can tell a STABLE ref apart from a swapped one — matching React, a ref cycles
        // (cleanup, then setup) only when its identity changes or the element remounts. The cleanup
        // fires when the element detaches from the DOM, releasing resources such as resetting
        // Ref<T>.Current to null.
        public Dictionary<VisualElement, (System.Func<VisualElement, System.Action> Callback, System.Action? Cleanup)>
            RefCallbacks { get; } = new();

        // Invokes a callback ref and records (identity, cleanup) in RefCallbacks. A patch carrying
        // the SAME callback delegate is a no-op: unconditionally re-invoking made any state write in
        // a ref cleanup a per-patch mid-flush write, forcing consumers into deferred-correction
        // workarounds. An entry is stored even when the setup returns no cleanup — the identity is
        // what makes the stable-ref skip possible.
        internal void InvokeRefCallback(VisualElement element, System.Func<VisualElement, System.Action>? refCallback)
        {
            if (RefCallbacks.TryGetValue(element, out var installed))
            {
                if (refCallback != null && ReferenceEquals(installed.Callback, refCallback)) return;
                // Remove first: if a user-defined cleanup throws, leaving a stale entry would cause
                // a double-fire on the next reconcile. Remove → Invoke order preserves exception safety.
                RefCallbacks.Remove(element);
                installed.Cleanup?.Invoke();
            }
            if (refCallback == null) return;
            var cleanup = refCallback(element);
            RefCallbacks[element] = (refCallback, cleanup);
        }

        // DOM-less AnimatePresence keeps its per-boundary state in PresenceStates (below); the old
        // wrapper-container-keyed maps were removed with PresenceReconciler.

        // Wrapper-less Suspense fallback state, keyed by (boundary fiber, the Suspense's scoped position
        // key). True while that Suspense is showing its fallback subtree instead of its children. The
        // new-side walk sets it after attempting the children expansion (suspended ⇒ true); the old-side
        // structural walk reads it to reproduce whichever subtree (children vs fallback) was committed
        // last render, so the diff's old leaves match the DOM. Entries are pruned when the boundary fiber
        // is disposed (registry teardown).
        internal Dictionary<(ComponentFiber? boundary, string positionKey), bool> SuspenseFallbackShown { get; } = new();

        // Boundary fibers that currently have a wrapper-less Suspense showing its fallback. A dirty fiber
        // whose nearest Suspense boundary is in this set is "offscreen": its own lane flush is deferred to
        // the boundary's re-render, which re-attempts the primary subtree and commits the reveal in one pass
        // (a resolved resource schedules the boundary itself, not the suspended child — committing
        // the child independently would write into the slot range the fallback occupies). Maintained by
        // ChildReconciler.ExpandSuspenseInline; read by FiberWorkLoop.FlushState.
        internal HashSet<ComponentFiber> SuspendedBoundaries { get; } = new();

        // Removes all wrapper-less Suspense boundary state keyed by boundary.
        // Invoked from ComponentRegistry when the boundary fiber is unregistered, so a
        // boundary that unmounts while suspended leaves no dangling fiber reference in
        // SuspendedBoundaries / SuspenseFallbackShown.
        internal void PruneSuspenseBoundaryState(ComponentFiber boundary)
        {
            SuspendedBoundaries.Remove(boundary);
            if (SuspenseFallbackShown.Count == 0) return;
            List<(ComponentFiber? boundary, string positionKey)>? stale = null;
            foreach (var key in SuspenseFallbackShown.Keys)
            {
                if (key.boundary == boundary) (stale ??= new()).Add(key);
            }
            if (stale != null)
            {
                foreach (var key in stale) SuspenseFallbackShown.Remove(key);
            }
        }

        // Per-AnimatePresence state for the DOM-less (wrapper-less) expansion, keyed by
        // (boundary fiber, the AnimatePresence's scoped position key) — mirroring
        // SuspenseFallbackShown's keying. The keyed children are expanded directly into the
        // parent's slot range (no wrapper element); this state records, per AnimatePresence, the leaf
        // composition currently committed to the DOM so the old-side structural walk can reproduce it for
        // the diff, plus which keys are mid-exit (kept mounted as ghosts) and which have finished exiting
        // (dropped on the next render). Pruned when the boundary fiber is disposed.
        internal sealed class PresenceBoundaryState
        {
            // Keyed children currently in the DOM (in DOM order), including exiting ghosts.
            public readonly List<(string key, VNode node)> Committed = new();

            // Keys whose exit animation is running; the leaf is kept mounted as a ghost.
            public readonly HashSet<string> Exiting = new();

            // Keys whose exit animation has completed; the next render stops emitting them so the
            // diff removes their leaves.
            public readonly HashSet<string> ExitComplete = new();

            // Anchor element of each exiting ghost (its first emitted leaf). Once a Motion's exit detaches
            // its element, the old-side reproduction can no longer recurse into the ghost's subtree, so its
            // inline fibers escape the orphan sweep. Keeping the anchor lets the drop path dispose them
            // explicitly via DisposeFibersUnder — otherwise a same-key re-entry restores a zombie fiber whose
            // local state updates never re-render.
            public readonly Dictionary<string, VisualElement> ExitAnchors = new();
        }

        // Keyed by (boundary fiber, parent element, scoped position key). The parent element is part of the
        // key — not just (boundary, key) like Suspense — because an AnimatePresence nested inside a real
        // element (e.g. a Motion) reconciles its children through a fresh ReconcileChildren call that
        // resets the fragment scope to null; without the parent, that inner AnimatePresence would collide
        // with an outer one at the same (null fiber, null scope, index 0). The parent is stable across the
        // AnimatePresence's renders (the host element is reused), and disambiguates inner from outer.
        internal Dictionary<(ComponentFiber? boundary, VisualElement? parent, string presenceKey), PresenceBoundaryState> PresenceStates { get; } = new();

        // Non-zero while an AnimatePresence expansion is on the stack. Motion nodes created inside
        // it are presence-managed (initial/exit tweens are scheduled by the expansion); a Motion
        // created at depth 0 mounts standalone, where those props are inert and warn.
        internal int PresenceExpansionDepth;

        // The MotionNode a presence expansion is CURRENTLY dispatching enter/exit for — the one
        // FindFirstMotionDescendant resolved for whichever keyed child GeneralPathReconciler is expanding right
        // now (set/restored around each EmitPresenceChild call, not just cleared, so a nested AnimatePresence
        // inside that child's own subtree does not lose the OUTER anchor once its own expansion returns). Null
        // outside any presence expansion, and also null for a keyed child whose FindFirstMotionDescendant walk
        // found no Motion (e.g. a plain Div wrapper). FiberNodeFactory's standalone-enter gate compares a
        // freshly created MotionNode against this BY REFERENCE — not PresenceExpansionDepth — so only the ONE
        // node the presence itself already plays an enter for (via PlayVariantEnter/PlayEnter) skips its
        // redundant standalone enter; every OTHER Motion created while the expansion is on the stack (nested
        // deeper, sitting under a non-anchor wrapper, or a sibling keyed child) is not presence-managed at all
        // and must keep its own mount enter.
        internal MotionNode? PresenceAnchorMotion;

        // Removes all DOM-less AnimatePresence state keyed by boundary. Invoked from
        // ComponentRegistry when the boundary fiber is unregistered, so a boundary that
        // unmounts while a child is exiting leaves no dangling fiber reference in PresenceStates.
        internal void PrunePresenceBoundaryState(ComponentFiber boundary)
        {
            if (PresenceStates.Count == 0) return;
            List<(ComponentFiber? boundary, VisualElement? parent, string presenceKey)>? stale = null;
            foreach (var key in PresenceStates.Keys)
            {
                if (key.boundary == boundary) (stale ??= new()).Add(key);
            }
            if (stale != null)
            {
                foreach (var key in stale) PresenceStates.Remove(key);
            }
        }

        // Tree-wide auto-batching scheduler. Coalesces setState across every fiber that shares this
        // context into a single frame-boundary flush so an event handler touching N fibers commits in
        // one reconcile pass rather than N. Inline-mounted descendants share the root fiber's context
        // (see Reconciler), so this set spans the canonical
        // cross-fiber case. Automatic batching is always on; there is no opt-out.
        internal FiberBatchScheduler BatchScheduler { get; } = new();

        // Cross-tier tearing guard for Hooks.UseStore<TStore,TSel>. Holds the store snapshot
        // pinned for the current batch-scheduler drain wave, keyed by the Store reference (the value
        // is the store's TState snapshot, boxed). Every UseStore read of the same store within
        // one wave returns the selector applied to this pinned snapshot rather than the live
        // store.Current, so an ancestor on the immediate tier and a descendant on the delayed tier
        // (separated by up to DeferredDelayMs) observe the SAME value even if the store mutates
        // between their tier drains — giving every external-store read in one wave a consistent snapshot.
        // Pinning is active only inside a batch-scheduler drain (_storeSnapshotWaveActive). A
        // "wave" spans the immediate drain and the delayed drain that follows it in the same frame: the
        // immediate drain opens the wave dropping the prior wave's pins (BeginStoreSnapshotWave
        // with reset = true), so its first UseStore read pins the now-current snapshot; the delayed
        // drain opens with reset = false so it REUSES that pin. A store mutation mid-wave re-schedules every
        // reader (via the subscription's RequestRender), and that follow-up render lands on the next immediate
        // drain, which opens a fresh wave and re-pins to the now-current snapshot so readers converge. Outside
        // a drain — on mount or a synchronous whole-tree flush — there is no tier separation, so reads return
        // the live store.Current and nothing is pinned. Pinning is reference-keyed, so distinct stores
        // never collide and the map is empty in the steady state.
        private readonly Dictionary<object, object?> _pinnedStoreSnapshots = new();
        private bool _storeSnapshotWaveActive;

        // While a batch drain is in progress, fibers defer their effect commit (insertion / layout effects)
        // here instead of running it immediately after their own render, so EVERY fiber's render (mutation)
        // completes before ANY layout effect runs — the commit-phase order. Flushed in collection order at
        // the end of the outer drain. Outside a drain (mount / synchronous flush) this stays false and effects
        // run inline. See FiberEffects.CommitSubtreeEffects / FlushDeferredDrainLayoutEffects.
        internal bool DeferDrainLayoutEffects;
        internal readonly List<(ComponentFiber fiber, bool mountDoubleInvoke)> PendingDrainLayoutEffects = new();

        // Returns the snapshot pinned for store within the current drain wave, capturing
        // liveSnapshot on the first read that finds no pin. Returns liveSnapshot
        // unpinned when no wave is active. See _pinnedStoreSnapshots for the consistency contract.
        internal TStore PinStoreSnapshot<TStore>(object store, TStore liveSnapshot)
        {
            if (!_storeSnapshotWaveActive) return liveSnapshot;
            if (_pinnedStoreSnapshots.TryGetValue(store, out var pinned) && pinned is TStore typed)
            {
                return typed;
            }
            _pinnedStoreSnapshots[store] = liveSnapshot;
            return liveSnapshot;
        }

        // Activates UseStore snapshot pinning for the span of a batch drain. reset drops the
        // previous wave's pins (the immediate drain that opens a fresh wave) versus reusing them (the delayed
        // drain continuing the same wave). Paired with EndStoreSnapshotWave.
        internal void BeginStoreSnapshotWave(bool reset)
        {
            if (reset) _pinnedStoreSnapshots.Clear();
            _storeSnapshotWaveActive = true;
        }

        // Deactivates UseStore snapshot pinning at the end of a batch drain. The pinned snapshots are retained
        // (not cleared) so the delayed drain that continues the wave can reuse them; the next immediate drain
        // clears them via BeginStoreSnapshotWave with reset = true.
        internal void EndStoreSnapshotWave() => _storeSnapshotWaveActive = false;

        // Gates the one-time ContextPropagationGeneration bump within a reconcile pass: the first Provider
        // whose value changed flips this and bumps the generation; later changed Providers in the same pass
        // see it already set and reuse that generation (dedup). Saved and restored around each inline
        // Provider-expansion scope in ChildReconciler so a nested expansion cannot leak its flag state out.
        public bool ContextValueChanged { get; internal set; }

        // Generation counter bumped by the outermost PatchContextProvider (one whose value changed).
        // When multiple PatchContextProvider calls within the same reconcile pass invoke
        // FiberTreeTraversal.NotifyContextChanged in succession, this is matched against
        // the consumer fiber's ComponentFiber.LastForceRenderGeneration to dedupe
        // duplicate force-renders.
        public int ContextPropagationGeneration { get; internal set; }

        // Reconcile depth shared across all Reconciler instances that observe this context.
        // Each fiber owns its own Reconciler (so per-fiber pause/resume state is
        // independent), but the ReconcilerContext-keyed EffectiveKeys registry must
        // only be cleared when the OUTERMOST Reconcile pass across the entire fiber tree completes.
        // Using an instance-local depth would treat each fiber-owned Reconciler.Reconcile call as a
        // fresh top-level, clearing entries for sibling subtrees the surrounding pass has not yet consumed.
        internal int SharedReconcileDepth { get; set; }
        public IReconcilerBridge ReconcilerBridge { get; private set; } = null!;
        public bool IsDisposed { get; private set; }

        // Becomes true when an Error Boundary switches to its fallback.
        // Signal used to abort processing of the remaining sibling nodes in the same Reconcile batch.
        // Reset to false when the top-level Reconcile completes.
        public bool IsAborted { get; internal set; }

        internal void MarkDisposed() => IsDisposed = true;

        // Sets the bridge invoked from internal elements (e.g. FiberVirtualListController) that need access
        // to Reconciler subsystems. A double invocation indicates an initialization-order bug, so this
        // throws fail-fast.
        internal void SetReconcilerBridge(IReconcilerBridge bridge)
        {
            if (ReconcilerBridge != null)
            {
                throw new System.InvalidOperationException("[ReconcilerContext] SetReconcilerBridge called twice");
            }
            ReconcilerBridge = bridge;
        }

        public ReconcilerContext()
        {
            EventManager = new FiberEventBindingManager(BatchScheduler);
            FiberElementFactory = new FiberElementFactory(EventManager);
            ComponentRegistry = new ComponentRegistry(this);
            FiberMemoCache = new FiberMemoCache();
            StyleAnimationScheduler = new StyleAnimationScheduler();
            ComponentContextStack = new ComponentContextStack();
            FiberStack = new FiberStack();
            BufferPool = new ReconcilerBufferPool();
            _pureElementSideTables = new System.Collections.IDictionary[]
            {
                StructuralVariants,
                HasClassVariants,
                AttributeVariants,
                DataAttributes,
                SupportsVariants,
                MotionAppliedClasses,
                MotionChildLabel,
                ElementToLayoutId,
                TextEffects,
                TextRawText,
            };
        }
    }
}
