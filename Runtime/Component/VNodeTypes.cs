#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Abstract base for virtual DOM nodes.
    /// The smallest unit of a declarative element tree.
    /// </summary>
    public abstract class VNode
    {
        /// <summary>
        /// Key used by the Reconciler to track node identity across renders. Null when omitted at the call site.
        /// </summary>
        public string? Key { get; internal set; }
    }

    /// <summary>
    /// Base class shared by ElementNode and MotionNode.
    /// ClassNames / Children / Events default to empty arrays so derived nodes can omit them when unused.
    /// </summary>
    public abstract class BaseElementNode : VNode
    {
        /// <summary>The VisualElement type to create (e.g. typeof(Button)).</summary>
        public Type ElementType { get; init; } = typeof(VisualElement);

        /// <summary>VisualElement.name (for USS #selector). Avoid frequent changes.</summary>
        public string? Name { get; init; }

        /// <summary>Array of BEM class names.</summary>
        public string[] ClassNames { get; init; } = Array.Empty<string>();

        /// <summary>Type-safe properties. Null when no element props were supplied.</summary>
        public FiberElementProps? Props { get; init; }

        /// <summary>Array of child nodes.</summary>
        public VNode?[] Children { get; init; } = Array.Empty<VNode>();

        /// <summary>Array of event bindings.</summary>
        public FiberEventBinding[] Events { get; init; } = Array.Empty<FiberEventBinding>();

        /// <summary>
        /// Callback ref invoked when the element is created or updated.
        /// The returned <c>Action</c> is invoked as cleanup when the element is detached from the DOM.
        /// Return null if no cleanup is needed. Coordinates with <c>UseRef</c> and <see cref="Ref{T}.SetElement"/>.
        /// </summary>
        public Func<VisualElement, Action>? RefCallback { get; init; }

        /// <summary>CSS class(es) applied on pointer hover (space-separated).</summary>
        public string? WhileHoverClass { get; init; }

        /// <summary>CSS class(es) applied on pointer tap (space-separated).</summary>
        public string? WhileTapClass { get; init; }

        /// <summary>CSS class(es) applied while the element holds keyboard/UI focus (space-separated).</summary>
        public string? WhileFocusClass { get; init; }
    }

    /// <summary>
    /// Node corresponding to a VisualElement (the host primitive, e.g. div, button).
    /// </summary>
    public sealed class ElementNode : BaseElementNode
    {
        /// <summary>Limited inline styles.</summary>
        public StyleOverrides? Styles { get; init; }

        /// <summary>Callback invoked only on the first creation of the element. Used to add Manipulators, etc.</summary>
        public Action<VisualElement>? OnCreated { get; init; }

        /// <summary>
        /// Function that wraps the element with a wrapper container.
        /// Input: the created VisualElement → output: the wrapper VisualElement.
        /// The Reconciler places the wrapper in the DOM and tracks the inner real element in a dictionary for patching.
        /// Primary use case: wrapping a button with a DropShadow container.
        /// </summary>
        public Func<VisualElement, VisualElement>? WrapElement { get; init; }
    }

    /// <summary>
    /// Element node that participates in animations.
    /// A variant <see cref="Initial"/>/<see cref="Animate"/> pair plays its mount enter on ANY Motion, standalone
    /// or under AnimatePresence; <see cref="Exit"/> requires AnimatePresence (something must defer the unmount
    /// for the removal to animate against) and switches CSS classes on unmount based on the transition definition.
    /// Inline styles (StyleOverrides) are intentionally not supported. Apply styles via USS classes.
    /// </summary>
    public sealed class MotionNode : BaseElementNode
    {
        /// <summary>Transition configuration.</summary>
        public StyleTransitionConfig? Transition { get; init; }

        /// <summary>
        /// Callback invoked when the enter animation completes.
        /// Fires for a variant <see cref="Initial"/>/<see cref="Animate"/> enter whether this Motion sits under
        /// AnimatePresence or mounts standalone. Invoked immediately when StyleTransitionConfig.None.
        /// When initial=false, fires synchronously inside CreateElement.
        /// On asynchronous animation completion via schedule.Execute, called outside of Reconcile, so SetState()
        /// is safe.
        /// </summary>
        public Action? OnEnterComplete { get; init; }

        /// <summary>
        /// Named animation states: each label maps to a utility-class string.
        /// Carried RAW (never baked into <see cref="BaseElementNode.ClassNames"/>): the effective label is
        /// resolved at reconcile time — this node's <see cref="Animate"/>, else the nearest ANCESTOR Motion's
        /// active label (parent→child propagation) — and applied against these variants.
        /// </summary>
        public IReadOnlyDictionary<string, string>? Variants { get; init; }

        /// <summary>The active variant label for this node (a key of <see cref="Variants"/>); null inherits the
        /// nearest ancestor Motion's active label.</summary>
        public string? Animate { get; init; }

        /// <summary>
        /// Mount-time starting variant label. When this Motion sets <see cref="Initial"/> + <see cref="Animate"/> +
        /// <see cref="Variants"/>, the enter starts the element at <c>variants[Initial]</c> and transitions to
        /// <c>variants[Animate]</c> (which it then rests at, persistently) using the <see cref="Transition"/>
        /// timing. Works the same whether this Motion is the direct child of an AnimatePresence or mounts
        /// standalone — Framer parity: <c>initial</c>/<c>animate</c> apply to any motion.* component; AnimatePresence
        /// is only required for <see cref="Exit"/>. Null = no variant initial state.
        /// </summary>
        public string? Initial { get; init; }

        /// <summary>
        /// Exit variant label. When this Motion is the direct child of an AnimatePresence and
        /// sets <see cref="Exit"/> + <see cref="Animate"/> + <see cref="Variants"/>, removal animates from the resting
        /// <c>variants[Animate]</c> to <c>variants[Exit]</c> (using the <see cref="Transition"/> timing) before the
        /// element unmounts. Unlike <see cref="Initial"/>, this genuinely needs AnimatePresence — something must
        /// defer the unmount for the removal to animate against — so it is inert (and logs a warning) outside one.
        /// Null = use the transition's own ExitFrom/ExitTo classes.
        /// </summary>
        public string? Exit { get; init; }

        /// <summary>
        /// Shared-element layout animation identity (Framer Motion's <c>layoutId</c> parity). When a Motion
        /// carrying this same string patches at a resolved layout rect (position and/or size) different from
        /// the rect the SAME id last settled at — including a different physical element entirely, e.g. after
        /// a same-key type flip or a move to a different parent — it tweens from the old rect to the new one
        /// (FLIP: capture the old rect, let layout settle at the new one, apply an inverse transform, then
        /// spring that inverse back to zero) instead of jump-cutting. Independent of <see cref="Variants"/>/
        /// <see cref="Animate"/>: a layoutId tween runs from the ACTUAL rect delta, not a class-defined
        /// from/to pair. Null = no layout animation (ordinary jump-cut on a rect change, matching every other
        /// element). Two Motions in the same tree must never share a live layoutId simultaneously — the
        /// second one to patch silently steals the registration (see MotionLayoutIdDriver).
        /// </summary>
        public string? LayoutId { get; init; }
    }

    /// <summary>
    /// Text-only node.
    /// Converted to a Label.
    /// </summary>
    public sealed class TextNode : VNode
    {
        /// <summary>Display text.</summary>
        public required string Text { get; init; }
    }

    /// <summary>
    /// Node that returns multiple nodes without a wrapper element (a Fragment).
    /// </summary>
    public sealed class FragmentNode : VNode
    {
        /// <summary>Array of child nodes.</summary>
        public required VNode?[] Children { get; init; }
    }

    /// <summary>
    /// Node that embeds a child component into a VNode tree. Holds the function-style component
    /// (<c>[Component] static VNode</c>) as a <see cref="Body"/> delegate and its function identity
    /// (MethodInfo) in <see cref="Identity"/>.
    /// </summary>
    public sealed class ComponentNode : VNode
    {
        /// <summary>
        /// Render body of the function component. The delegate of a static method annotated with `[Component]`.
        /// </summary>
        public required Func<VNode>? Body { get; init; }

        /// <summary>
        /// Identity used as this component's cache key across renders.
        /// Typically a <see cref="System.Reflection.MethodInfo"/> (<c>Body.Method</c>); when left null the
        /// component falls back to <c>Body.Method</c>.
        /// </summary>
        public object? Identity { get; init; }

        /// <summary>
        /// Props value captured by the props-receiving <c>V.Component&lt;TProps&gt;</c> overload.
        /// Stored on the fiber and compared with the previous render's props (shallow per-property identity
        /// comparison) to decide whether to bail the re-render. Null for the refless / ref-forwarding
        /// overloads (props-less Render).
        /// </summary>
        public object? Props { get; init; }

        /// <summary>
        /// Optional custom <c>areEqual(prevProps, nextProps)</c> predicate supplied at the call site.
        /// When non-null it overrides the default shallow per-property identity comparison: returning
        /// <c>true</c> bails the re-render, <c>false</c> forces it. Supplied via <c>V.Memo(component, props, areEqual)</c>.
        /// Null means the default shallow comparison is used.
        /// </summary>
        internal Func<object?, object?, bool>? AreEqual { get; init; }

        /// <summary>
        /// Whether this component opted into the props-bail. <c>true</c> when the component method
        /// carries <c>[Component(Memoize = true)]</c>, or when the node was created via <c>V.Memoized</c>
        /// (a custom <see cref="AreEqual"/> implies memoization). When <c>false</c> the component re-renders
        /// on every parent re-render regardless of props equality: only an opted-in
        /// component bails on shallow-equal props; a plain component always re-renders when its parent does.
        /// The reconcile-boundary bail in <c>ComponentRegistry</c> is gated on
        /// <c>Memoize || AreEqual != null</c>.
        /// </summary>
        internal bool Memoize { get; init; }

        /// <summary>
        /// Identity that prefers <see cref="Identity"/>, falling back to <c>Body.Method</c>.
        /// The non-null identity used by the reconciler / registry when computing the cache key.
        /// </summary>
        internal object ResolvedIdentity => Identity ?? Body!.Method;

        /// <summary>
        /// Ref passed by the parent via <c>V.Component&lt;TRef&gt;(componentRef:)</c>.
        /// The child retrieves it via <c>Hooks.ForwardedRef&lt;THandle&gt;()</c> and passes it to
        /// <c>Hooks.UseImperativeHandle</c>.
        /// </summary>
        internal IHookRefSetter? ExternalRef { get; init; }

        /// <summary>
        /// Runtime hint for whether `[Component(IsErrorBoundary = true)]` is applied.
        /// </summary>
        public bool IsErrorBoundary { get; init; }

        internal ComponentFiber Mount(ComponentRegistry registry, VisualElement wrapper)
            => registry.GetOrCreate(this, wrapper);
    }

    /// <summary>
    /// Node that skips rebuilding its child tree when the dependency array is unchanged.
    /// When omitting key, do not change the order of MemoNodes within the same component, since identity
    /// is resolved by call order.
    /// </summary>
    public sealed class MemoNode : VNode
    {
        /// <summary>Factory function that produces a VNode.</summary>
        public required Func<VNode> Factory { get; init; }

        /// <summary>
        /// Dependency array. Compared element-wise with <c>Object.is</c> semantics
        /// (<see cref="ObjectIs.AreEqualDeps"/>): reference-type elements by identity (a fresh-but-equal record
        /// counts as changed), strings/primitives by value, floats by raw bit pattern. NOT a structural
        /// <c>SequenceEqual</c> — there is no recursion into element contents.
        /// </summary>
        public object?[]? Dependencies { get; init; }
    }

    /// <summary>
    /// Node that renders children into a different VisualElement registered in FiberPortalRegistry, rather than at
    /// their position in the VNode tree.
    /// Used when a component's children are logically scoped to it but should be placed near the DOM root, such
    /// as modals or overlays.
    /// </summary>
    public sealed class PortalNode : VNode
    {
        /// <summary>
        /// ID of the mount target registered in FiberPortalRegistry. Null when the portal targets a
        /// framework-managed layer panel instead (<see cref="Layer"/> is set) — exactly one of the two
        /// is non-null.
        /// </summary>
        public string? TargetId { get; init; }

        /// <summary>
        /// Framework-managed screen-space layer panel this portal's children attach to (null for a
        /// registry-target portal). One host panel exists per layer per reconciler, sorted around the
        /// app's main panel, destroyed on reconciler disposal.
        /// </summary>
        public UILayer? Layer { get; init; }

        /// <summary>
        /// How the layer host panel participates in sequential (Tab) focus order relative to the declaring
        /// panel — see <see cref="PanelFocusOrder"/>. Only meaningful for a layer portal (<see cref="Layer"/>
        /// set); a registry-target portal mounts into the same panel and has no boundary to chain across.
        /// </summary>
        public PanelFocusOrder FocusOrder { get; init; } = PanelFocusOrder.Isolated;

        /// <summary>Array of child nodes to render at the mount target.</summary>
        public VNode?[] Children { get; init; } = Array.Empty<VNode>();
    }

    /// <summary>
    /// A portal into a framework-owned world-space panel positioned by a scene transform — UI that
    /// lives among 3D content (depth-tested), unlike the always-on-top screen-space layers. Children
    /// stay part of the logical tree (context crosses; events do not — the panel boundary is physical).
    /// </summary>
    public sealed class WorldSpaceNode : VNode
    {
        /// <summary>World position of the panel host.</summary>
        public UnityEngine.Vector3 Position { get; init; }

        /// <summary>World rotation of the panel host.</summary>
        public UnityEngine.Quaternion Rotation { get; init; } = UnityEngine.Quaternion.identity;

        /// <summary>Virtual panel resolution in pixels (the world-space size mode is fixed).</summary>
        public UnityEngine.Vector2 PanelSize { get; init; } = new(1920f, 1080f);

        /// <summary>
        /// How the world-space host panel participates in sequential (Tab) focus order relative to the
        /// declaring panel — see <see cref="PanelFocusOrder"/>.
        /// </summary>
        public PanelFocusOrder FocusOrder { get; init; } = PanelFocusOrder.Isolated;

        /// <summary>Array of child nodes to render inside the world-space panel.</summary>
        public VNode?[] Children { get; init; } = Array.Empty<VNode>();
    }

    /// <summary>
    /// Boundary node that displays a fallback when a descendant Use&lt;T&gt; declares pending.
    /// Error handling is delegated to an Error Boundary.
    /// </summary>
    public sealed class SuspenseNode : VNode
    {
        /// <summary>Fallback node displayed while pending.</summary>
        public required VNode Fallback { get; init; }

        /// <summary>Array of child nodes.</summary>
        public VNode?[] Children { get; init; } = Array.Empty<VNode>();
    }

    /// <summary>
    /// Placeholder node that renders the matched child route component of a nested route at this position.
    /// Dynamically renders the next child route in the matched route hierarchy based on RouterContext depth.
    /// </summary>
    public sealed class OutletNode : VNode
    {
        internal IRouteScope? Scope { get; set; }

        /// <summary>
        /// Value supplied to the rendered child route, surfaced via <c>Hooks.UseOutletContext</c>.
        /// </summary>
        internal object? OutletContextValue { get; init; }
    }

    /// <summary>
    /// How an <see cref="AnimatePresenceNode"/> sequences exit and enter when its keyed children change.
    /// </summary>
    /// <remarks>Equivalent to Framer Motion's <c>AnimatePresence mode</c> prop for users migrating from Framer Motion.</remarks>
    public enum AnimatePresenceMode
    {
        /// <summary>
        /// Exiting children animate out while the new children animate in simultaneously (the default).
        /// </summary>
        Sync,

        /// <summary>
        /// The current children finish exiting before any brand-new child is mounted / entered. Intended for
        /// single-child swaps such as route / screen transitions. While an exit
        /// is in flight, a brand-new key is withheld; the exit-completion re-render then mounts and enters it.
        /// </summary>
        Wait,

        /// <summary>
        /// The instant a child starts exiting, it is pulled out of layout flow and pinned via absolute
        /// positioning at the last rect it occupied, so still-present siblings reflow immediately into its
        /// place while its exit animation finishes on top of them. Cancelling the exit (its key re-added
        /// before the animation finishes) restores the child into flow.
        /// </summary>
        PopLayout,
    }

    /// <summary>
    /// Container node that manages mount / unmount animations of its children.
    /// When keyed children become null, it does not delete them immediately and retains them until the exit
    /// animation completes.
    /// Children should preferably be MotionNode. Non-MotionNode children (e.g. ElementNode) also work as
    /// transition-less (immediate deletion), but a Debug.LogWarning is emitted because they are not animation
    /// targets. TextNode is skipped without a warning.
    /// Note: FragmentNode cannot be included directly as a child (it is not expanded). Use MotionNode or a direct
    /// VNode.
    /// Children without a key receive a position-based automatic key, so reordering can cause unintended
    /// exit / enter animations. Set explicit keys on children that participate in animation.
    /// </summary>
    public sealed class AnimatePresenceNode : VNode
    {
        /// <summary>Array of child nodes. May include MotionNode. Null children are treated as removed.</summary>
        public VNode?[] Children { get; init; } = Array.Empty<VNode>();

        /// <summary>
        /// When false, the enter animation on initial mount is skipped.
        /// Defaults to true (animate on initial mount as well).
        /// </summary>
        public bool Initial { get; init; } = true;

        /// <summary>
        /// Delay interval (seconds) applied sequentially to children's enter animations.
        /// The i-th child receives an additional delay of StaggerSec * i.
        /// 0 (default) means no stagger (all children start animating simultaneously).
        /// </summary>
        public float StaggerSec { get; init; }

        /// <summary>
        /// A fixed delay (seconds) added before ANY child animates. Combined with
        /// the per-child stagger: child i is delayed by <c>DelayChildrenSec + StaggerSec * staggerIndex(i)</c>.
        /// </summary>
        /// <remarks>Equivalent to Framer Motion's <c>delayChildren</c> for users migrating from Framer Motion.</remarks>
        public float DelayChildrenSec { get; init; }

        /// <summary>
        /// Direction the stagger sweeps. <c>1</c> (default) staggers
        /// first-to-last; <c>-1</c> staggers last-to-first (the last child animates first).
        /// </summary>
        /// <remarks>Equivalent to Framer Motion's <c>staggerDirection</c> for users migrating from Framer Motion.</remarks>
        public int StaggerDirection { get; init; } = 1;

        /// <summary>
        /// The total additional delay for the child at <paramref name="index"/> of <paramref name="count"/>
        /// siblings: <see cref="DelayChildrenSec"/> plus the stagger offset, swept per <see cref="StaggerDirection"/>.
        /// </summary>
        public float StaggerDelaySec(int index, int count)
        {
            var effective = StaggerDirection < 0 && count > 0 ? count - 1 - index : index;
            return DelayChildrenSec + StaggerSec * effective;
        }

        /// <summary>
        /// Exit / enter sequencing. Defaults to <see cref="AnimatePresenceMode.Sync"/> (exit and enter overlap).
        /// <see cref="AnimatePresenceMode.Wait"/> holds a brand-new child back until in-flight exits finish.
        /// <see cref="AnimatePresenceMode.PopLayout"/> pulls an exiting child out of flow so siblings reflow
        /// around it immediately.
        /// </summary>
        public AnimatePresenceMode Mode { get; init; } = AnimatePresenceMode.Sync;

        /// <summary>
        /// Invoked once when every in-flight exit animation has finished (the exiting set empties).
        /// Not fired when an exit is cancelled by the key re-entering,
        /// nor for children removed without an exit animation.
        /// </summary>
        /// <remarks>Equivalent to Framer Motion's <c>onExitComplete</c> for users migrating from Framer Motion.</remarks>
        public Action? OnExitComplete { get; init; }
    }

    /// <summary>
    /// List virtualization node for large item collections.
    /// Renders a ScrollView of fixed-height items and keeps only the items in view present in the DOM to ensure
    /// performance.
    /// </summary>
    public sealed class VirtualListNode : VNode
    {
        /// <summary>Item list (type-erased).</summary>
        public IReadOnlyList<object> Items { get; }

        /// <summary>Function that returns a unique key for each item.</summary>
        public Func<object, string> KeySelector { get; }

        /// <summary>Fixed height (px) of each item.</summary>
        public float ItemHeight { get; }

        /// <summary>Function that produces a VNode from each item.</summary>
        public Func<object, VNode> Renderer { get; }

        /// <summary>Number of extra items to render outside the visible range.</summary>
        public int Overscan { get; }

        /// <summary>Array of USS class names applied to the ScrollView.</summary>
        public string[] ClassNames { get; init; } = Array.Empty<string>();

        /// <summary>ScrollView.name (for USS #selector).</summary>
        public string? Name { get; init; }

        /// <summary>
        /// Creates a virtualized list. Prefer the <see cref="V.VirtualList{T}"/> factory; this is the
        /// type-erased form it builds.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="items"/>, <paramref name="keySelector"/>, or <paramref name="renderer"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="itemHeight"/> is &lt;= 0, or <paramref name="overscan"/> is &lt; 0.</exception>
        public VirtualListNode(
            IReadOnlyList<object> items,
            Func<object, string> keySelector,
            float itemHeight,
            Func<object, VNode> renderer,
            int overscan)
        {
            Items = items ?? throw new ArgumentNullException(nameof(items));
            KeySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
            if (itemHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(itemHeight), "ItemHeight must be greater than 0.");
            }

            ItemHeight = itemHeight;
            Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            if (overscan < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(overscan), "Overscan must be >= 0.");
            }

            Overscan = overscan;
        }
    }

    /// <summary>
    /// Non-generic base for a Context Provider.
    /// Note: a single wrapper VisualElement is added to the DOM for the Provider (each VNode maps to one DOM
    /// element), so consider the impact on USS selectors and layout.
    /// </summary>
    public abstract class ContextProviderNode : VNode
    {
        /// <summary>Array of child nodes.</summary>
        public required VNode?[] Children { get; init; }

        internal abstract void PushContext(ComponentContextStack stack);
        internal abstract void PopContext(ComponentContextStack stack);
        internal abstract bool HasValueChanged(ContextProviderNode other);

        /// <summary>
        /// Key that identifies which ComponentContext&lt;T&gt; this Provider corresponds to in fiber-based context
        /// live tracking. Returns the ComponentContext instance reference.
        /// </summary>
        internal abstract object ContextKey { get; }

        // The provided value when it is a VNode root (slot-injection via context): the recycle
        // mark must treat it as live — a consumer may have committed the value's node into its own
        // tree — while the sweep never returns it (leaking a provider-held node is recoverable;
        // recycling a consumed one is not).
        internal abstract object? BoxedValueForRecycleMark { get; }
    }

    /// <summary>
    /// Node that provides a context value to a subtree, read by descendants via <c>UseContext</c>.
    /// </summary>
    public sealed class ContextProviderNode<T> : ContextProviderNode
    {
        /// <summary>Context definition to provide.</summary>
        public required ComponentContext<T> Context { get; init; }

        /// <summary>Value to provide.</summary>
        public required T Value { get; init; }

        internal override void PushContext(ComponentContextStack stack) => stack.Push(Context, Value);

        internal override void PopContext(ComponentContextStack stack) => stack.Pop(Context);

        private static readonly bool s_canHoldNodes = !typeof(T).IsValueType;

        internal override object? BoxedValueForRecycleMark
            => s_canHoldNodes ? HookSlotRecycleProbe.Probe(Value) : null;

        internal override bool HasValueChanged(ContextProviderNode other)
        {
            if (other is not ContextProviderNode<T> otherTyped)
            {
                return true;
            }

            if (!ReferenceEquals(Context, otherTyped.Context))
            {
                return true;
            }

            return !ObjectIs.AreEqual(Value, otherTyped.Value);
        }

        internal override object ContextKey => Context;
    }
}
