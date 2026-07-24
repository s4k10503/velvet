using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Pool for VNode-composing objects (FiberElementProps / FiberEventBinding[] / VNode[]) and recyclable
    // VisualElement instances. Reduces GC pressure from short-lived objects created on every
    // reconcile. Main thread only (not thread-safe).
    internal static class VNodePool
    {
        private const int MaxPoolSize = 8;

        #region pass-scoped release staging

        // While a reconcile pass is on the stack, returned objects are STAGED instead of pushed to
        // their pools, and flushed when the outermost pass ends. This guarantees an object retired
        // mid-pass (an error boundary swapping in its fallback, a mid-pass unmount) cannot be
        // re-rented by a later factory call in the SAME pass: a second retired tree reaching the
        // same object later in the pass must find it already out of the rented set (idempotent
        // no-op) — if the object were re-rented in between, that second return would recycle the
        // NEW renter's live object. Depth-counted so nested top-level entries (a portal drain's
        // nested reconcile) flush only at the true outermost boundary. Rent never reads the
        // staging lists, so a staged object is unreachable until the flush.
        private static int s_releaseScopeDepth;
        private static readonly List<FiberElementProps> s_stagedProps = new();
        private static readonly List<FiberEventBinding[]> s_stagedEventArrays = new();
        private static readonly List<VNode?[]> s_stagedNodeArrays = new();

        internal static void BeginReleaseScope() => s_releaseScopeDepth++;

        internal static void EndReleaseScope()
        {
            if (s_releaseScopeDepth > 0 && --s_releaseScopeDepth > 0) return;
            if (s_stagedProps.Count > 0)
            {
                for (var i = 0; i < s_stagedProps.Count; i++) ReleaseProps(s_stagedProps[i]);
                s_stagedProps.Clear();
            }
            if (s_stagedEventArrays.Count > 0)
            {
                for (var i = 0; i < s_stagedEventArrays.Count; i++) ReleaseEventArray(s_stagedEventArrays[i]);
                s_stagedEventArrays.Clear();
            }
            if (s_stagedNodeArrays.Count > 0)
            {
                for (var i = 0; i < s_stagedNodeArrays.Count; i++) ReleaseNodeArray(s_stagedNodeArrays[i]);
                s_stagedNodeArrays.Clear();
            }
        }

        #endregion
        // Sized to absorb peak reconcile churn (typically ~30 mounts/unmounts in deep reflows).
        private const int MaxLabelPoolSize = 32;
        private const int MaxButtonPoolSize = 32;
        private const int MaxTogglePoolSize = 32;
        private const int MaxSliderPoolSize = 32;
        private const int MaxTextFieldPoolSize = 32;

        #region FiberElementProps

        private static readonly Stack<FiberElementProps> s_propsPool = new();

        // Identity set of props bags currently RENTED OUT by this pool. ReturnProps clears + recycles
        // ONLY these — never a caller-supplied instance: the V.* factories accept a raw `props:`
        // argument a consumer may cache and reuse across renders, and clearing it would wipe an object
        // the caller still holds and alias it into the shared pool. Membership is rent-scoped (added on
        // Rent, removed on Return) rather than created-scoped, so the return is idempotent: the recycle
        // sweep can encounter one bag through several retired trees (a baseline retired by its owner and
        // again by a parent expansion) and only the first return recycles it. Mirrors s_ownedNodeArrays.
        private static readonly HashSet<FiberElementProps> s_ownedProps = new();

        public static FiberElementProps RentProps()
        {
            var rented = s_propsPool.Count > 0 ? s_propsPool.Pop() : new FiberElementProps();
            s_ownedProps.Add(rented);
            return rented;
        }

        public static void ReturnProps(FiberElementProps? props)
        {
            if (props == null || ReferenceEquals(props, FiberElementProps.Empty)) return;
            // Only recycle bags rented out by the pool; a caller-owned or already-returned bag is left
            // untouched (not cleared, not pooled) — Remove doubles as the membership test.
            if (!s_ownedProps.Remove(props)) return;
            if (s_releaseScopeDepth > 0)
            {
                // Mid-pass return: keep the bag unreachable until the pass ends (see the staging region).
                s_stagedProps.Add(props);
                return;
            }
            ReleaseProps(props);
        }

        private static void ReleaseProps(FiberElementProps props)
        {
            if (s_propsPool.Count >= MaxPoolSize)
            {
                // Pool is full: drop this bag (let it be GC'd); it already left the rented-out set.
                return;
            }

            props.Text = null;
            props.Tooltip = null;
            props.Enabled = null;
            props.Visible = null;
            props.FieldValue = null;
            props.Focusable = null;
            props.TabIndex = null;
            props.DelegatesFocus = null;
            props.FocusScope = null;
            props.DndContext = null;
            props.Draggable = null;
            props.Droppable = null;
            props.DragOverlay = null;
            props.Slider = null;
            props.ScrollView = null;
            props.TextField = null;
            props.Choices = null;
            props.SceneView = null;
            props.Particles = null;
            props.Anchored = null;
            props.Data = null;
            props.Aria = null;

            s_propsPool.Push(props);
        }

        #endregion

        #region FiberEventBinding[]

        private static readonly Stack<FiberEventBinding[]> s_singleEventPool = new();

        // Identity set mirroring s_ownedProps (rent-scoped membership): only arrays currently rented
        // out by RentSingleEventArray may be cleared and recycled — V.Motion accepts a raw `events:`
        // array a consumer may cache and reuse across renders, and clearing slot 0 would sever the
        // caller's handler in place.
        private static readonly HashSet<FiberEventBinding[]> s_ownedSingleEventArrays = new();

        public static FiberEventBinding[] RentSingleEventArray()
        {
            var rented = s_singleEventPool.Count > 0 ? s_singleEventPool.Pop() : new FiberEventBinding[1];
            s_ownedSingleEventArrays.Add(rented);
            return rented;
        }

        public static void ReturnEventArray(FiberEventBinding[] array)
        {
            if (array == null || array.Length != 1) return;
            // Only recycle arrays rented out by the pool; a caller-owned or already-returned array is
            // left untouched — Remove doubles as the membership test.
            if (!s_ownedSingleEventArrays.Remove(array)) return;
            if (s_releaseScopeDepth > 0)
            {
                s_stagedEventArrays.Add(array);
                return;
            }
            ReleaseEventArray(array);
        }

        private static void ReleaseEventArray(FiberEventBinding[] array)
        {
            if (s_singleEventPool.Count >= MaxPoolSize)
            {
                return;
            }

            array[0] = null!;
            s_singleEventPool.Push(array);
        }

        #endregion

        #region VNode[] (List results)

        private static readonly Dictionary<int, Stack<VNode?[]>> s_nodeArrayPools = new();

        // Identity set of arrays currently rented out by RentNodeArray (rent-scoped membership, like
        // s_ownedProps). ReturnNodeArray clears + recycles ONLY these — never an array the caller owns.
        // The recycle path returns every elem.Children / motion.Children, and a consumer may pass a
        // cached / reused array there (e.g. `V.Div(children: s_static)`); clearing it would wipe the
        // consumer's children on the next render. Reference equality (default for VNode[]).
        private static readonly HashSet<VNode?[]> s_ownedNodeArrays = new();

        public static VNode?[] RentNodeArray(int length)
        {
            var rented = s_nodeArrayPools.TryGetValue(length, out var pool) && pool.Count > 0
                ? pool.Pop()
                : new VNode?[length];
            s_ownedNodeArrays.Add(rented);
            return rented;
        }

        public static void ReturnNodeArray(VNode?[] array)
        {
            if (array == null || array.Length == 0) return;
            // Only recycle arrays rented out by the pool; a caller-owned or already-returned array is
            // left untouched (not cleared, not pooled) — Remove doubles as the membership test.
            if (!s_ownedNodeArrays.Remove(array)) return;
            if (s_releaseScopeDepth > 0)
            {
                s_stagedNodeArrays.Add(array);
                return;
            }
            ReleaseNodeArray(array);
        }

        private static void ReleaseNodeArray(VNode?[] array)
        {
            if (!s_nodeArrayPools.TryGetValue(array.Length, out var pool))
            {
                pool = new Stack<VNode?[]>();
                s_nodeArrayPools[array.Length] = pool;
            }

            if (pool.Count >= MaxPoolSize)
            {
                // Pool is full: drop this array (let it be GC'd); it already left the rented-out set.
                return;
            }

            Array.Clear(array, 0, array.Length);
            pool.Push(array);
        }

        #endregion

        // A pool of recyclable VisualElement leaves. The cap check and the reset-only-when-pooling discipline
        // (a full pool drops the element WITHOUT resetting, matching the per-type behavior these collapsed)
        // live here once; each leaf kind supplies only its factory and its type-specific reset helper.
        private sealed class ElementPool<T> where T : VisualElement
        {
            private readonly Stack<T> _pool = new();
            private readonly Func<T> _factory;
            private readonly Action<T> _reset;
            private readonly int _max;

            public ElementPool(Func<T> factory, Action<T> reset, int max)
            {
                _factory = factory;
                _reset = reset;
                _max = max;
            }

            public int Count => _pool.Count;

            public T Rent() => _pool.Count > 0 ? _pool.Pop() : _factory();

            public void Return(T element)
            {
                if (element == null) return;
                if (_pool.Count >= _max) return;
                _reset(element);
                _pool.Push(element);
            }

            public void Clear() => _pool.Clear();
        }

        #region Label pool

        // Pooled labels have been reset via FiberLabelPoolHelper.ResetLabelForReuse.
        private static readonly ElementPool<Label> s_labelPool =
            new(() => new Label(string.Empty), FiberLabelPoolHelper.ResetLabelForReuse, MaxLabelPoolSize);

        public static Label RentLabel(string text)
        {
            var label = s_labelPool.Rent();
            label.text = text ?? string.Empty;
            return label;
        }

        // Called by FiberElementCleaner after the label has been removed from the DOM
        // hierarchy and Velvet-managed resources have been released.
        public static void ReturnLabel(Label label) => s_labelPool.Return(label);

        #endregion

        #region Button pool

        // Pooled buttons have been reset via FiberButtonPoolHelper.ResetButtonForReuse.
        private static readonly ElementPool<Button> s_buttonPool =
            new(() => new Button(), FiberButtonPoolHelper.ResetButtonForReuse, MaxButtonPoolSize);

        public static Button RentButton() => s_buttonPool.Rent();

        // Called by FiberElementCleaner after the button has been removed from the DOM
        // hierarchy and Velvet-managed resources (event bindings, gesture manipulators) have been released.
        public static void ReturnButton(Button button) => s_buttonPool.Return(button);

        #endregion

        #region Toggle pool

        // Pooled toggles have been reset via FiberTogglePoolHelper.ResetToggleForReuse.
        private static readonly ElementPool<Toggle> s_togglePool =
            new(() => new Toggle(), FiberTogglePoolHelper.ResetToggleForReuse, MaxTogglePoolSize);

        public static Toggle RentToggle() => s_togglePool.Rent();

        public static void ReturnToggle(Toggle toggle) => s_togglePool.Return(toggle);

        #endregion

        #region Slider pool

        // Pooled sliders have been reset via FiberSliderPoolHelper.ResetSliderForReuse.
        private static readonly ElementPool<Slider> s_sliderPool =
            new(() => new Slider(), FiberSliderPoolHelper.ResetSliderForReuse, MaxSliderPoolSize);

        public static Slider RentSlider() => s_sliderPool.Rent();

        public static void ReturnSlider(Slider slider) => s_sliderPool.Return(slider);

        #endregion

        #region TextField pool

        // Pooled TextFields have been reset via FiberTextFieldPoolHelper.ResetTextFieldForReuse,
        // guaranteeing no PII (password / email / player name) ghosting from a prior consumer.
        private static readonly ElementPool<TextField> s_textFieldPool =
            new(() => new TextField(), FiberTextFieldPoolHelper.ResetTextFieldForReuse, MaxTextFieldPoolSize);

        public static TextField RentTextField() => s_textFieldPool.Rent();

        public static void ReturnTextField(TextField textField) => s_textFieldPool.Return(textField);

        #endregion

#if UNITY_EDITOR
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticFields()
        {
            s_releaseScopeDepth = 0;
            s_stagedProps.Clear();
            s_stagedEventArrays.Clear();
            s_stagedNodeArrays.Clear();
            s_propsPool.Clear();
            s_ownedProps.Clear();
            s_singleEventPool.Clear();
            s_ownedSingleEventArrays.Clear();
            s_nodeArrayPools.Clear();
            s_ownedNodeArrays.Clear();
            s_labelPool.Clear();
            s_buttonPool.Clear();
            s_togglePool.Clear();
            s_sliderPool.Clear();
            s_textFieldPool.Clear();
        }

        // Drains the Label pool. Intended for EditMode tests that must start from an empty pool
        // to make boundary assertions deterministic across test methods.
        internal static void ClearLabelPoolForTesting()
        {
            s_labelPool.Clear();
        }

        // Current Label pool size. Intended for EditMode tests that assert a returned element was
        // reclaimed into the pool rather than dropped.
        internal static int LabelPoolCountForTesting => s_labelPool.Count;

        // Drains the Button pool. Intended for EditMode tests that must start from an empty pool
        // to make boundary assertions deterministic across test methods.
        internal static void ClearButtonPoolForTesting()
        {
            s_buttonPool.Clear();
        }

        // Current Button pool size. Intended for EditMode tests that assert a child-bearing Button
        // orphan is NOT reclaimed into the pool (only childless poolable leaves are).
        internal static int ButtonPoolCountForTesting => s_buttonPool.Count;

        // Drains the Toggle pool. EditMode tests use this to start from an empty pool.
        internal static void ClearTogglePoolForTesting()
        {
            s_togglePool.Clear();
        }

        // Drains the Slider pool. EditMode tests use this to start from an empty pool.
        internal static void ClearSliderPoolForTesting()
        {
            s_sliderPool.Clear();
        }

        // Drains the TextField pool. EditMode tests use this to start from an empty pool.
        internal static void ClearTextFieldPoolForTesting()
        {
            s_textFieldPool.Clear();
        }
#endif
    }
}
