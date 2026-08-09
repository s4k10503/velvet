using System.Collections.Generic;
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
    // - Callbacks a consumer registered directly, whether through element.RegisterCallback<TEvent> or
    //   through AddManipulator, are NOT tracked here; user code must unregister them before the element
    //   returns to the pool. The Clickable swap in FiberButtonPoolHelper covers only the manipulator a
    //   Button holds itself, not one a consumer added alongside it.
    // - An item a consumer put on element.schedule survives the pool cycle and resumes when the next
    //   consumer attaches the element. The animation schedules FiberElementCleaner releases are Velvet's
    //   own, not these.
    // - A binding registered through element.bindings is not released here.
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
        // Fixed-arity overloads cover every call shape in this codebase (two USS classes for
        // Label/Button/Toggle, three for Slider/TextField, whose constructor chains run one field
        // class deeper). Do not collapse them into a `params` signature: this runs once per recycled
        // element on the reconciler's steady-state hot path, and a params array would put a heap
        // allocation on every widget pool-return.
        // element: Pooled element to reset. Null is a no-op.
        public static void ResetClassListAndCommon(VisualElement element, string ussClassA, string ussClassB)
        {
            if (element == null) return;

            ClearProjectedClassList(element);
            AddClassIfNotEmpty(element, ussClassA);
            AddClassIfNotEmpty(element, ussClassB);

            ResetCommonState(element);
        }

        // element: Pooled element to reset. Null is a no-op.
        public static void ResetClassListAndCommon(VisualElement element, string ussClassA, string ussClassB, string ussClassC)
        {
            if (element == null) return;

            ClearProjectedClassList(element);
            AddClassIfNotEmpty(element, ussClassA);
            AddClassIfNotEmpty(element, ussClassB);
            AddClassIfNotEmpty(element, ussClassC);

            ResetCommonState(element);
        }

        // A composite field builds its own input, and its label, into the very container children are expanded
        // into, so its pool return cannot empty that container the way the childless primitives do —
        // FiberButtonPoolHelper states that split. Nor can it count: CompositeFieldChildPoolReuseTests pins
        // that an expanded child takes the container's FIRST slot, ahead of what the constructor left, so
        // neither end of a count separates the two. What the control made is identified by the two classes
        // passed here instead, which PoolableWidgetChildBaselineTests pins as accounting for all of it.
        // field: Pooled composite to strip. Null is a no-op.
        public static void DetachForeignChildren(VisualElement field, string inputUssClass, string labelUssClass)
        {
            if (field == null) return;

            var container = FiberNodePatcher.GetChildContainer(field);
            for (var i = container.childCount - 1; i >= 0; i--)
            {
                var child = container.ElementAt(i);
                if (child.ClassListContains(inputUssClass) || child.ClassListContains(labelUssClass))
                {
                    continue;
                }
                container.RemoveAt(i);
            }
        }

        // Empties the class list and the per-element style model that decides what may be on it, in one step.
        // Dropping only the classes would leave the model holding a prior consumer's layers and its record of
        // which of them it had suppressed, so the next consumer's first recompute could take a class off the
        // element that nothing on this mount ever asked for.
        private static void ClearProjectedClassList(VisualElement element)
        {
            element.ClearClassList();
            StyleArbitraryValueResolver.ClearAll(element);
        }

        private static void AddClassIfNotEmpty(VisualElement element, string ussClass)
        {
            if (!string.IsNullOrEmpty(ussClass))
            {
                element.AddToClassList(ussClass);
            }
        }

        // Resets the element's UIToolkit-side state shared by all pooled widgets.
        // Caller (the widget-specific helper) is responsible for the widget-specific state
        // (e.g. Toggle.value, Slider.lowValue/highValue, TextField.isPasswordField).
        public static void ResetCommonState(VisualElement element)
        {
            if (element == null) return;

            // Clearing ANY inline property restores the matched-rules value THROUGH the transition system, so
            // while the element is still on a panel every clear below can become a running animation instead —
            // one that keeps painting what the reset just removed, and that the transition-longhand nulls at
            // the end of the scrub cannot stop (nulling restores the matched-rules value rather than disabling
            // the transition). Writing a transition longhand recomputes that data, and a zero total time
            // contributes no transition at all, so the clears cancel instead of animating. Must precede the
            // scrub to cover all of it. Scoped to that INLINE scrub: the name / enabled-state resets after it
            // change which rules match, and whatever the next styles pass transitions from that is its own.
            // A detached element is not style-initialized, which already takes every clear down the cancel
            // path — which is why no shipped caller can reach the defect, since they all detach first.
            // Skipping the write there also keeps the pool return free of the managed entry it would otherwise
            // create for the trailing null to tear down again.
            if (element.panel != null)
            {
                element.style.transitionDuration = new StyleList<TimeValue>(s_zeroDuration);
            }

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
            element.usageHints = UsageHints.None;
            element.languageDirection = LanguageDirection.Inherit;
            element.disablePlayModeTint = false;
            element.cacheAsBitmap = false;
            element.dataSource = null;
            element.dataSourcePath = default;
            element.dataSourceType = null;

            // Every pooled primitive is a BindableElement; test fixtures reach this with plain
            // VisualElements too, which the cast is what admits.
            if (element is BindableElement bindable)
            {
                bindable.binding = null;
                bindable.bindingPath = null;
            }
        }

        // Read off one probe rather than restated, for the same reason FiberTextFieldPoolHelper keeps one:
        // a colour or a flag copied out of Unity's constructor into a literal here is a mirror that drifts,
        // and a freshly constructed instance is the very thing the pool's contract is stated against.
        // Built on first use, which puts it on the reconciler's thread during a pool return.
        private static Label s_textDefaults;

        private static Label TextDefaults => s_textDefaults ??= new Label();

        // Velvet writes none of these, which is why they ghost — nothing on the mount path takes off what
        // a consumer holding the element put on. The selection half is reached through a get-only handle,
        // which is why a walk over the element's own properties finds none of it.
        public static void ResetTextElementState(TextElement element)
        {
            if (element == null) return;

            element.enableRichText = true;
            element.emojiFallbackSupport = true;
            element.parseEscapeSequences = false;
            element.displayTooltipWhenElided = true;
            element.PostProcessTextVertices = null;

            var defaults = TextDefaults;
            // A text element paints itself through the same delegate anything else adds a painter to, and
            // Velvet's own silhouette painters use it. Assigning the probe's is what drops a consumer's:
            // the entry a constructor installs takes its subject off the generation context rather than
            // capturing one, so one instance's is every instance's.
            element.generateVisualContent = defaults.generateVisualContent;
            // Writes focusable false on its way through, so anything restoring a type's own focusable — the
            // line in FiberButtonPoolHelper — has to come after this call.
            element.selection.isSelectable = defaults.selection.isSelectable;
            element.selection.doubleClickSelectsWord = defaults.selection.doubleClickSelectsWord;
            element.selection.tripleClickSelectsLine = defaults.selection.tripleClickSelectsLine;
            element.selection.selectAllOnFocus = defaults.selection.selectAllOnFocus;
            element.selection.selectAllOnMouseUp = defaults.selection.selectAllOnMouseUp;
            element.selection.cursorColor = defaults.selection.cursorColor;
            element.selection.selectionColor = defaults.selection.selectionColor;
        }

        // Source for the zero transition duration written above. Shared because the inline-style write path
        // copies a list value into its own storage instead of aliasing the one handed to it, and this runs
        // once per recycled element on the reconciler's steady-state hot path.
        private static readonly List<TimeValue> s_zeroDuration = new() { new TimeValue(0f) };

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
