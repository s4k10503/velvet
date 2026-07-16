using UnityEngine.UIElements;

namespace Velvet
{
    // Common reset of UIToolkit-side state that survives RemoveFromHierarchy.
    // Called by per-widget pool helpers (e.g. FiberLabelPoolHelper) before pushing the
    // element back to VNodePool so it appears as a freshly constructed instance to
    // the next consumer.
    // Velvet-managed state (event bindings via FiberEventBindingManager, gesture manipulators, ref cleanups,
    // component registry entries, animation schedules, virtual list controllers, outlet scopes, suspense
    // fallback flags) is released earlier in FiberElementCleaner.CleanupElementResources.
    // This helper handles the residual UIToolkit-side state (inline style / userData / focusable / etc.)
    // that would otherwise ghost into the next caller of the pool.
    // Limitations:
    // - Sub-element inline style (Toggle.checkmark / Slider.dragger etc.) is NOT touched here.
    //   Velvet's ApplyClassNames overwrites USS-driven styling on every mount, so this is
    //   structurally safe for Velvet builder API paths. User refCallback code that mutates
    //   sub-element inline style is responsible for restoring it: a callback ref returns a cleanup
    //   delegate that runs when the element detaches, which is where such mutations must be undone.
    // - Callbacks registered directly via element.RegisterCallback<TEvent> from user
    //   code are NOT tracked here; user code must unregister such callbacks before the element
    //   returns to the pool.
    // - Modern Bindings (element.bindings, dataSourcePath) are not reset.
    internal static class FiberElementPoolReset
    {
        // Restores the base USS classes (stripped by ClearClassList) and resets the shared
        // UIToolkit-side state in one call. Per-widget helpers (FiberLabelPoolHelper,
        // FiberButtonPoolHelper, ...) chain widget-specific resets after this.
        // Widgets inheriting from TextElement (Label / Button / TextField etc.) require both
        // TextElement.ussClassName and the widget-specific class name to be restored, because
        // TextElement's constructor adds unity-text-element alongside the subclass's own
        // USS class (see Unity reference TextElement.cs:178). Passing the base classes in
        // constructor-call order (base first, subclass last) keeps the resulting class list identical
        // to a freshly constructed instance.
        // element: Pooled element to reset. Null is a no-op.
        // baseUssClasses: USS classes required by Unity built-in styling. Order is preserved.
        public static void ResetClassListAndCommon(VisualElement element, params string[] baseUssClasses)
        {
            if (element == null) return;

            element.ClearClassList();
            if (baseUssClasses != null)
            {
                foreach (var ussClass in baseUssClasses)
                {
                    if (!string.IsNullOrEmpty(ussClass))
                    {
                        element.AddToClassList(ussClass);
                    }
                }
            }

            ResetCommonState(element);
        }

        // Resets the element's UIToolkit-side state shared by all pooled widgets.
        // Caller (the widget-specific helper) is responsible for the widget-specific state
        // (e.g. Toggle.value, Slider.lowValue/highValue, TextField.isPasswordField).
        public static void ResetCommonState(VisualElement element)
        {
            if (element == null) return;

            ResetInlineStyle(element.style);

            element.userData = null;
            element.name = string.Empty;
            element.tooltip = string.Empty;
            element.focusable = false;
            // tabIndex and delegatesFocus are prop-settable (FiberElementProps.TabIndex/DelegatesFocus), so
            // a pooled element must not carry them into its next consumer's focus order.
            element.tabIndex = 0;
            element.delegatesFocus = false;
            element.pickingMode = PickingMode.Position;
            element.viewDataKey = null;
            element.SetEnabled(true);
        }

        private static void ResetInlineStyle(IStyle style)
        {
            // Common inline style properties that Velvet's ApplyStyles / StyleArbitraryValueResolver may set.
            // Listed exhaustively so the next consumer cannot inherit ghosting.
            style.color = StyleKeyword.Null;
            style.backgroundColor = StyleKeyword.Null;
            style.backgroundImage = StyleKeyword.Null;
            // GradientBackground sets backgroundSize (stretch-to-fill) alongside backgroundImage; scrub it
            // too so a pooled element cannot ghost a 100%/100% size onto its next consumer's image.
            style.backgroundSize = StyleKeyword.Null;
            // animate-gradient / animate-shimmer pan the background-position (and disable repeat) each frame;
            // scrub them so a pooled element does not ghost a panned offset / no-repeat onto its next consumer.
            style.backgroundPositionX = StyleKeyword.Null;
            style.backgroundPositionY = StyleKeyword.Null;
            style.backgroundRepeat = StyleKeyword.Null;
            // animate-pulse drives opacity each frame; scrubbing it here keeps a pooled element from ghosting a
            // mid-pulse opacity onto its next consumer (also covers opacity-* arbitrary values).
            style.opacity = StyleKeyword.Null;
            style.display = StyleKeyword.Null;
            style.visibility = StyleKeyword.Null;
            style.overflow = StyleKeyword.Null;
            style.width = StyleKeyword.Null;
            style.height = StyleKeyword.Null;
            style.minWidth = StyleKeyword.Null;
            style.minHeight = StyleKeyword.Null;
            style.maxWidth = StyleKeyword.Null;
            style.maxHeight = StyleKeyword.Null;
            style.marginLeft = StyleKeyword.Null;
            style.marginRight = StyleKeyword.Null;
            style.marginTop = StyleKeyword.Null;
            style.marginBottom = StyleKeyword.Null;
            style.paddingLeft = StyleKeyword.Null;
            style.paddingRight = StyleKeyword.Null;
            style.paddingTop = StyleKeyword.Null;
            style.paddingBottom = StyleKeyword.Null;
            style.borderLeftWidth = StyleKeyword.Null;
            style.borderRightWidth = StyleKeyword.Null;
            style.borderTopWidth = StyleKeyword.Null;
            style.borderBottomWidth = StyleKeyword.Null;
            style.borderLeftColor = StyleKeyword.Null;
            style.borderRightColor = StyleKeyword.Null;
            style.borderTopColor = StyleKeyword.Null;
            style.borderBottomColor = StyleKeyword.Null;
            style.borderTopLeftRadius = StyleKeyword.Null;
            style.borderTopRightRadius = StyleKeyword.Null;
            style.borderBottomLeftRadius = StyleKeyword.Null;
            style.borderBottomRightRadius = StyleKeyword.Null;
            style.flexGrow = StyleKeyword.Null;
            style.flexShrink = StyleKeyword.Null;
            style.flexBasis = StyleKeyword.Null;
            style.flexDirection = StyleKeyword.Null;
            style.flexWrap = StyleKeyword.Null;
            style.alignSelf = StyleKeyword.Null;
            style.alignItems = StyleKeyword.Null;
            style.alignContent = StyleKeyword.Null;
            style.justifyContent = StyleKeyword.Null;
            style.position = StyleKeyword.Null;
            style.left = StyleKeyword.Null;
            style.right = StyleKeyword.Null;
            style.top = StyleKeyword.Null;
            style.bottom = StyleKeyword.Null;
            style.fontSize = StyleKeyword.Null;
            // tracking-[Npx] writes inline letterSpacing (StyleArbitraryValueResolver). Without nulling it
            // here a pooled element keeps its old letter spacing and ghosts it onto the next consumer whose
            // node declares no tracking-* (the new node's empty oldClasses diff never clears it) — the same
            // pooled-reuse ghosting class as the Button-children bug.
            style.letterSpacing = StyleKeyword.Null;
            style.unityFontDefinition = StyleKeyword.Null;
            style.unityFontStyleAndWeight = StyleKeyword.Null;
            style.unityTextAlign = StyleKeyword.Null;
            style.whiteSpace = StyleKeyword.Null;
            style.translate = StyleKeyword.Null;
            style.rotate = StyleKeyword.Null;
            style.scale = StyleKeyword.Null;
            // aspect-[w/h] writes inline aspectRatio (StyleArbitraryValueResolver); null it so a pooled element
            // does not ghost a prior aspect ratio onto the next consumer whose node declares no aspect-* class.
            style.aspectRatio = StyleKeyword.Null;
            // blur-/grayscale-/etc. write inline filter (StyleArbitraryValueResolver); same pool-ghost reason.
            style.filter = StyleKeyword.Null;
            // This editor's inline-filter setter clears the wrong internal has-inline flag on a Null
            // assignment, so the stored filter list survives the line above and would still ghost onto the
            // next consumer. Empty the surviving list in place: an empty inline filter computes to "no
            // filter", a consumer's filter classes replace the list wholesale, and on editors where the
            // Null assignment works the getter already reads back a null list, making this a no-op.
            style.filter.value?.Clear();
            style.transformOrigin = StyleKeyword.Null;
            style.transitionDuration = StyleKeyword.Null;
            style.transitionDelay = StyleKeyword.Null;
            style.transitionProperty = StyleKeyword.Null;
            style.transitionTimingFunction = StyleKeyword.Null;
        }
    }
}
