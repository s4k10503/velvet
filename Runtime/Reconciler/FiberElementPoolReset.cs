using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Common reset of UIToolkit-side state that survives RemoveFromHierarchy.
    // Called by per-widget pool helpers (e.g. FiberLabelPoolHelper) before pushing the
    // element back to VNodePool so it appears as a freshly constructed instance to
    // the next consumer.
    // Velvet-managed state (event bindings via FiberEventBindingManager, gesture manipulators, ref cleanups,
    // component registry entries, animation schedules, virtual list controllers, suspense
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
        // Label/Button/Toggle, three for Slider/SliderInt/TextField, whose constructor chains run one field
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
            for (var i = ChildListWrites.ChildCount(container) - 1; i >= 0; i--)
            {
                var child = ChildListWrites.ElementAtPhysical(container, i)!;
                if (child.ClassListContains(inputUssClass) || child.ClassListContains(labelUssClass))
                {
                    continue;
                }
                ChildListWrites.RemoveAt(container, i);
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
            BandColourInheritance.Forget(element);
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

            if (element is ISkewPickTarget skewPick)
            {
                skewPick.PickRegion = null;
            }

            if (element is ILineBoxed lineBoxed)
            {
                lineBoxed.LineBox.Reset();
            }

            // Clearing an inline property restores the matched-rules value THROUGH the transition system, so while
            // the element is still on a panel a clear can become a running animation instead — one that keeps
            // painting what the reset just removed. A zero total time contributes no transition at all, so while
            // the inline zero duration below applies the clears cancel instead of animating. Clear itself removes
            // the zero along with the other inline values, and takes this group of properties last, after that
            // removal; they are nulled under the zero just before it, after the releases below that write rotate and
            // scale back, so none of them is left for it. Scoped to that INLINE
            // clear: the name / enabled-state resets after it change which rules match, and whatever the next styles
            // pass transitions from that is its own. A detached element is not style-initialized, which already
            // takes every clear down the cancel path — which is why no shipped caller can reach the defect, since
            // they all detach first. PooledElementStyleGhostTests holds opacity and rotate against a stylesheet's
            // transition.
            if (element.panel != null)
            {
                element.style.transitionDuration = new StyleList<TimeValue>(s_zeroDuration);
            }

            // A filter tween still ticking would write its frames and its target onto the next consumer.
            StyleFilterTransitionDriver.Release(element);
            // A curve held for the last consumer's ease-* classes would otherwise ease the next consumer's transitions.
            StyleEaseCurve.Release(element);
            // The clock its last mount recorded would otherwise answer for the next consumer's filter tweens.
            MotionClock.Forget(element);
            // A skew composed into the rotate and scale would otherwise outlive the element's last use.
            AuthoredTransform.Release(element);
            ClearLateClearedStyle(element.style);
            element.style.Clear();
            SuspenseHiddenElements.Release(element);

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
            // Assigning the probe's painter is what drops a consumer's: the entry a constructor installs
            // takes its subject off the generation context rather than capturing one, so one instance's is
            // every instance's.
            element.generateVisualContent = defaults.generateVisualContent;
            (element as ILineBoxed)?.LineBox.WatchRepaints();
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

        // The properties IStyle.Clear removes after every other inline value, the zero duration included.
        private static void ClearLateClearedStyle(IStyle style)
        {
            style.cursor = StyleKeyword.Null;
            style.textShadow = StyleKeyword.Null;
            style.unityTextAutoSize = StyleKeyword.Null;
            style.transformOrigin = StyleKeyword.Null;
            style.translate = StyleKeyword.Null;
            style.rotate = StyleKeyword.Null;
            style.scale = StyleKeyword.Null;
            style.backgroundSize = StyleKeyword.Null;
            style.filter = StyleKeyword.Null;
        }

        // Source for the zero transition duration written above. Shared because the inline-style write path
        // copies a list value into its own storage instead of aliasing the one handed to it, and this runs
        // once per recycled element on the reconciler's steady-state hot path.
        private static readonly List<TimeValue> s_zeroDuration = new() { new TimeValue(0f) };
    }
}
