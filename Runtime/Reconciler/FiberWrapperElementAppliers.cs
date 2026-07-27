using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // The className-driven effect appliers: the wrapper-less PAINT layers (skew silhouettes, gradient
    // backgrounds, drop shadow, animate-* motion — each the element's own generateVisualContent / inline
    // style, no structural element added) and the two structural-WRAPPER layers (ring/outline, clip-path),
    // plus the gesture (whileHover/whileTap/whileFocus) manipulator. The patcher's PatchElement/PatchMotion
    // and the node factory orchestrate these at patch/create time; the shared wrap/unwrap surgery and
    // wrapper<->inner resolution live in WrapperInfrastructure, which both this and the patcher reference.
    // Each subsystem's own logic lives in its own FiberXxxApplier (constructed once here and held for the
    // life of this instance); this class is the stable per-subsystem dispatch surface the factory/patcher
    // call into. Helpers genuinely shared by more than one subsystem live on WrapperInfrastructure instead,
    // so each FiberXxxApplier depends only on ReconcilerContext and WrapperInfrastructure — the sole
    // exception is the RingWrapperClass/ClipPathWrapperClass constants, kept on this facade because tests
    // reference them through this type; do not add any other applier-to-facade dependency.
    internal sealed class FiberWrapperElementAppliers
    {
        private readonly ReconcilerContext _ctx;

        private readonly FiberSkewApplier _skew;
        private readonly FiberGradientApplier _gradient;
        private readonly FiberAnimateMotionApplier _animate;
        private readonly FiberFilterTransitionApplier _filterTransition;
        private readonly FiberDropShadowApplier _dropShadow;
        private readonly FiberBorderStyleApplier _borderStyle;
        private readonly FiberRingApplier _ring;
        private readonly FiberClipPathApplier _clipPath;
        private readonly FiberGestureApplier _gesture;

        public FiberWrapperElementAppliers(ReconcilerContext ctx, WrapperInfrastructure wrappers)
        {
            _ctx = ctx;
            _skew = new FiberSkewApplier(ctx);
            _gradient = new FiberGradientApplier(ctx);
            _animate = new FiberAnimateMotionApplier(ctx);
            _filterTransition = new FiberFilterTransitionApplier(ctx);
            _dropShadow = new FiberDropShadowApplier(ctx);
            _borderStyle = new FiberBorderStyleApplier(ctx);
            _ring = new FiberRingApplier(ctx, wrappers);
            _clipPath = new FiberClipPathApplier(ctx, wrappers);
            _gesture = new FiberGestureApplier(ctx);
        }

        internal void ApplySkewOnCreate(VisualElement element, string[] classNames)
            => _skew.ApplySkewOnCreate(element, classNames);

        internal float ApplySkewOnPatch(VisualElement element, string[] classNames, bool classesChanged,
            bool canReleaseFace)
            => _skew.ApplySkewOnPatch(element, classNames, classesChanged, canReleaseFace);

        internal void ApplyGradientOnCreate(VisualElement element, string[] classNames)
            => _gradient.ApplyGradientOnCreate(element, classNames);

        internal void ApplyGradientOnPatch(VisualElement element, string[] classNames, bool skewable)
            => _gradient.ApplyGradientOnPatch(element, classNames, skewable);

        internal void ApplyAnimateOnCreate(VisualElement element, string[] classNames)
            => _animate.ApplyAnimateOnCreate(element, classNames);

        internal void ApplyAnimateOnPatch(VisualElement element, string[] classNames)
            => _animate.ApplyAnimateOnPatch(element, classNames);

        internal void ApplyFilterTransitionOnCreate(VisualElement element, string[] classNames)
            => _filterTransition.ApplyFilterTransitionOnCreate(element, classNames);

        internal void ApplyFilterTransitionOnPatch(VisualElement element, string[] classNames)
            => _filterTransition.ApplyFilterTransitionOnPatch(element, classNames);

        internal void ApplyShadowOnCreate(VisualElement element, string[] classNames)
            => _dropShadow.ApplyShadowOnCreate(element, classNames);

        internal void ApplyShadowOnPatch(VisualElement element, string[] classNames, bool clipActive,
            float skewXDeg, bool canReleaseFace)
            => _dropShadow.ApplyShadowOnPatch(element, classNames, clipActive, skewXDeg, canReleaseFace);

        internal void ApplyBorderStyleOnCreate(VisualElement element, string[] classNames)
            => _borderStyle.ApplyBorderStyleOnCreate(element, classNames);

        internal void ApplyBorderStyleOnPatch(VisualElement element, string[] classNames, bool classesChanged,
            bool canReleaseFace)
            => _borderStyle.ApplyBorderStyleOnPatch(element, classNames, classesChanged, canReleaseFace);

        // USS class on the structural wrapper Velvet emits to host a ring-*/outline-* overlay. UI Toolkit has
        // no CSS box-shadow / outline, so the outset (or inset) HARD border these utilities describe is drawn
        // as a native rounded-border OVERLAY element — hardware-rendered, follows rounded-* corners, with no
        // custom material / draw-order hazard (unlike the soft, blurred drop shadow, which needs an SDF shader).
        // Lower precedence of the two structural-WRAPPER layers: clip-path takes the wrapper first, so a
        // clipped element carries no ring wrapper (the two wrappers are mutually exclusive — one per element).
        // The drop shadow is a wrapper-less paint, so a ring composes with a shadow (it does not compete).
        internal const string RingWrapperClass = "velvet-ring-wrapper";

        internal VisualElement ApplyRingOnCreate(VisualElement element, string[] classNames)
            => _ring.ApplyRingOnCreate(element, classNames);

        internal void ApplyRingOnPatch(VisualElement element, string[] classNames, bool suppress, bool allowWrap)
            => _ring.ApplyRingOnPatch(element, classNames, suppress, allowWrap);

        // USS class on the structural wrapper Velvet emits to host a clip-path-* element. UI Toolkit
        // (6000.3) has no USS clip-path; the supported arbitrary-shape mask is an overflow-hidden
        // element whose background-image is a VECTOR image (UIR stencil-clips the subtree to the
        // vector geometry). The wrapper carries that baked VectorImage (ClipPathVectorImageBaker), so
        // the inner element's own background, borders, text and children are ALL clipped to the shape
        // — CSS clip-path's "clips everything, including descendants" semantics. Limitations vs CSS:
        // pointer picking is unchanged (the clipped-away corners still hit-test), and world-space
        // panels (which only support rectangle clipping) ignore the mask.
        internal const string ClipPathWrapperClass = "velvet-clip-path-wrapper";

        internal VisualElement ApplyClipPathOnCreate(VisualElement element, string[] classNames)
            => _clipPath.ApplyClipPathOnCreate(element, classNames);

        internal bool ApplyClipPathOnPatch(VisualElement element, string[] classNames)
            => _clipPath.ApplyClipPathOnPatch(element, classNames);

        internal void ReResolveClipPathLive(VisualElement element)
            => _clipPath.ReResolveClipPathLive(element);

        internal void ApplyGestureManipulator(VisualElement element, string? whileHoverClass, string? whileTapClass, string? whileFocusClass)
            => _gesture.ApplyGestureManipulator(element, whileHoverClass, whileTapClass, whileFocusClass);

        #region Element bindings (SceneView / Particles)

        // Binds / re-binds / releases a SceneView element's camera-output machinery — the mount paths
        // (plain element AND Motion host) and the props diff all land here, beside the sibling binding
        // lifecycles above, because the binding owns live resources (a framework-created RenderTexture,
        // a registered geometry callback, an editor-panel repaint tick) tracked per element for the
        // cleaner and the reconciler dispose sweep to release.
        internal void ApplySceneView(VisualElement element, SceneViewSettings? settings)
        {
            if (element is not SceneViewElement)
            {
                return;
            }
            ApplyElementBinding(element, settings, _ctx.SceneViewBindings,
                s_sceneViewAttach, s_sceneViewUpdate, s_sceneViewDetach);
        }

        // Binds / re-binds / releases a Particles element's simulation-and-draw machinery, on the same
        // dispatch as the SceneView binding: live resources here are the hidden simulation host
        // GameObject, the painter callback and the repaint tick.
        internal void ApplyParticles(VisualElement element, ParticlesSettings? settings)
        {
            if (element is not ParticlesElement)
            {
                return;
            }
            ApplyElementBinding(element, settings, _ctx.ParticlesBindings,
                s_particlesAttach, s_particlesUpdate, s_particlesDetach);
        }

        // A filter clips the particle quads drawn beyond the host rect (it renders the element through an
        // offscreen tree sized to the layout box); the driver reserves render bounds to keep them, gated on
        // the same variant-peeling filter check as the skew / shadow paints. Driven on every patch rather than
        // the Particles-settings diff, because a filter comes and goes independent of the effect — a class
        // swap, or a state variant the reconcile pass never sees active.
        internal void ApplyParticlesSpacer(VisualElement element, string[] classNames)
        {
            if (element is ParticlesElement && _ctx.ParticlesBindings.TryGetValue(element, out var binding))
            {
                ParticlesDriver.SetWantSpacer(element, binding, WrapperInfrastructure.CarriesFilter(classNames), classNames);
            }
        }

        // Unlike SceneView/Particles, Anchored has no dedicated element type to gate on — V.Anchored builds
        // a plain ElementNode (any host type is valid; the binding only ever writes inline left/top), so
        // this dispatches straight to the shared binding logic with no type check.
        internal void ApplyAnchored(VisualElement element, AnchoredSettings? settings)
        {
            ApplyElementBinding(element, settings, _ctx.AnchoredBindings,
                s_anchoredAttach, s_anchoredUpdate, s_anchoredDetach);
        }

        // Same attach/update/detach shape as the bindings above, inlined rather than routed through the
        // shared generic dispatch: FocusScopeDriver.Attach needs the ReconcilerContext (the navigator's
        // scope registry and lazy panel attach live there), which the cached static delegates cannot carry.
        internal void ApplyFocusScope(VisualElement element, FocusScopeSettings? settings)
        {
            if (_ctx.FocusScopeBindings.TryGetValue(element, out var binding))
            {
                if (settings == null)
                {
                    FocusScopeDriver.Detach(element, binding);
                    _ctx.FocusScopeBindings.Remove(element);
                    return;
                }
                FocusScopeDriver.Update(element, binding, settings);
            }
            else if (settings != null)
            {
                _ctx.FocusScopeBindings[element] = FocusScopeDriver.Attach(element, settings, _ctx);
            }
        }

        // The four DnD slots share ApplyFocusScope's hand-inlined shape: the drivers need the
        // ReconcilerContext (scope resolution, the active-session interlock), which the cached static
        // delegates of the shared generic dispatch cannot carry.
        internal void ApplyDndContext(VisualElement element, DndContextSettings? settings)
        {
            if (_ctx.DndScopeBindings.TryGetValue(element, out var binding))
            {
                if (settings == null)
                {
                    DndScopeDriver.Detach(element, _ctx);
                    _ctx.DndScopeBindings.Remove(element);
                    return;
                }
                DndScopeDriver.Update(binding, settings);
            }
            else if (settings != null)
            {
                _ctx.DndScopeBindings[element] = DndScopeDriver.Attach(element, settings);
            }
        }

        internal void ApplyDraggable(VisualElement element, DraggableSettings? settings)
        {
            if (_ctx.DraggableBindings.TryGetValue(element, out var binding))
            {
                if (settings == null)
                {
                    DndDraggableDriver.Detach(element, binding, _ctx);
                    _ctx.DraggableBindings.Remove(element);
                    return;
                }
                DndDraggableDriver.Update(element, binding, settings, _ctx);
            }
            else if (settings != null)
            {
                _ctx.DraggableBindings[element] = DndDraggableDriver.Attach(element, settings, _ctx);
            }
        }

        internal void ApplyDroppable(VisualElement element, DroppableSettings? settings)
        {
            if (_ctx.DroppableBindings.TryGetValue(element, out var binding))
            {
                if (settings == null)
                {
                    DndDroppableDriver.Detach(element, _ctx);
                    _ctx.DroppableBindings.Remove(element);
                    return;
                }
                DndDroppableDriver.Update(element, binding, settings, _ctx);
            }
            else if (settings != null)
            {
                _ctx.DroppableBindings[element] = DndDroppableDriver.Attach(element, settings);
            }
        }

        internal void ApplyDragOverlay(VisualElement element, DragOverlaySettings? settings)
        {
            if (_ctx.DragOverlayBindings.TryGetValue(element, out _))
            {
                if (settings == null)
                {
                    DndOverlayDriver.Detach(element, _ctx);
                    _ctx.DragOverlayBindings.Remove(element);
                }
                // The marker record carries no updatable state, so a live binding has nothing to refresh.
                return;
            }
            if (settings != null)
            {
                _ctx.DragOverlayBindings[element] = DndOverlayDriver.Attach(element, _ctx);
            }
        }

        // Cached method-group delegates so the shared dispatch below adds no per-call allocation.
        private static readonly Func<VisualElement, SceneViewSettings, SceneViewBinding> s_sceneViewAttach = SceneViewDriver.Attach;
        private static readonly Action<VisualElement, SceneViewBinding, SceneViewSettings> s_sceneViewUpdate = SceneViewDriver.Update;
        private static readonly Action<VisualElement, SceneViewBinding> s_sceneViewDetach = SceneViewDriver.Detach;
        private static readonly Func<VisualElement, ParticlesSettings, ParticlesBinding> s_particlesAttach = ParticlesDriver.Attach;
        private static readonly Action<VisualElement, ParticlesBinding, ParticlesSettings> s_particlesUpdate = ParticlesDriver.Update;
        private static readonly Action<VisualElement, ParticlesBinding> s_particlesDetach = ParticlesDriver.Detach;
        private static readonly Func<VisualElement, AnchoredSettings, AnchoredBinding> s_anchoredAttach = AnchoredDriver.Attach;
        private static readonly Action<VisualElement, AnchoredBinding, AnchoredSettings> s_anchoredUpdate = AnchoredDriver.Update;
        private static readonly Action<VisualElement, AnchoredBinding> s_anchoredDetach = AnchoredDriver.Detach;

        // The attach/update/detach dispatch both element bindings share. A vanished settings prop only
        // happens on a hand-built ElementNode (the factories always carry settings, even for a null
        // camera/effect): release everything and drop the binding — the element stays mounted, inert.
        private static void ApplyElementBinding<TSettings, TBinding>(
            VisualElement element,
            TSettings? settings,
            Dictionary<VisualElement, TBinding> bindings,
            Func<VisualElement, TSettings, TBinding> attach,
            Action<VisualElement, TBinding, TSettings> update,
            Action<VisualElement, TBinding> detach)
            where TSettings : class
        {
            if (bindings.TryGetValue(element, out var binding))
            {
                if (settings == null)
                {
                    detach(element, binding);
                    bindings.Remove(element);
                    return;
                }
                update(element, binding, settings);
            }
            else if (settings != null)
            {
                bindings[element] = attach(element, settings);
            }
        }

        #endregion
    }
}
