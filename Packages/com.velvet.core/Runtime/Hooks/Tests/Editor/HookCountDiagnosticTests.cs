using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A render that calls a slot-keeping hook kind more or fewer times than the previous render did fails
    /// with an <see cref="InvalidOperationException"/> naming the component, React's wording and the kind's
    /// two counts.
    /// <list type="bullet">
    /// <item>The call past the committed count throws from itself, before it starts a slot, so a helper method
    /// making that call is on the stack.</item>
    /// <item>A body that returns having made fewer calls throws once it settles.</item>
    /// <item>A fiber unmounted and mounted again is not compared with its render before the unmount.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class HookCountDiagnosticTests
    {
        private sealed class IntStore : Store<int>
        {
            public IntStore(int initial) : base(initial) { }
            protected override void ResetCore() => SetState(_ => 0);
        }

        private static readonly IntStore s_store = new(0);
        private static readonly Ref<object> s_handle = new();

        // Each hook named here is called by the helper that only a render with the sheet open reaches. Keyed
        // by name so a case's name stays plain text rather than a rendered delegate.
        private static readonly Dictionary<string, Action> s_sheetHooks = new()
        {
            ["UseCallback"] = () => Hooks.UseCallback((Action)(() => { })),
            ["UseCallback with deps"] = () => Hooks.UseCallback((Action)(() => { }), 1),
            ["UseBlocker beneath a router"] = () => Hooks.UseBlocker(_ => false),
            ["UseLayoutEffect"] = () => Hooks.UseLayoutEffect((Func<Action>)(() => null)),
            ["UseInsertionEffect"] = () => Hooks.UseInsertionEffect((Func<Action>)(() => null)),
            ["UseEffect"] = () => Hooks.UseEffect((Func<Action>)(() => null)),
            ["UseState"] = () => Hooks.UseState(0),
            ["UseReducer"] = () => Hooks.UseReducer<int, int>((state, action) => state + action, 0),
            ["UseReducer with init"] = () => Hooks.UseReducer<int, int, int>((state, action) => state + action, 0, arg => arg),
            ["UseStore"] = () => Hooks.UseStore(s_store, value => value),
            ["UseSyncExternalStore"] = () => Hooks.UseSyncExternalStore(onStoreChange => () => { }, () => 0),
            ["UseImperativeHandle"] = () => Hooks.UseImperativeHandle(s_handle, () => new object()),
            ["UseImperativeHandle with deps"] = () => Hooks.UseImperativeHandle(s_handle, () => new object(), 1),
            ["UseRef"] = () => Hooks.UseRef<object>(),
            ["UseMutableRef"] = () => Hooks.UseMutableRef(0),
            ["UseMemo"] = () => Hooks.UseMemo(() => 1),
            ["UseMemo with deps"] = () => Hooks.UseMemo(() => 1, 1),
            ["UseId"] = () => Hooks.UseId(),
            ["UseDeferredValue"] = () => Hooks.UseDeferredValue(1),
            ["UseOptimistic"] = () => Hooks.UseOptimistic<int, int>(0, (state, action) => state + action),
            ["UseMutation"] = () => Hooks.UseMutation(new MutationOptions<int, int>(
                MutationFn: (value, _) => VelvetTask.FromResult(value))),
            ["UseTransition"] = () => Hooks.UseTransition(),
            ["Use"] = () => Hooks.Use(() => VelvetTask.FromResult(1), resourceKey: "sheet"),
        };

        // Each counts in s_slotStarts every call of its lazy initializer, reducer init, store selector, external-store
        // subscribe or Use factory, which the refused call past the count makes only if it goes on to its slot.
        private static readonly Dictionary<string, Action> s_countingSheetHooks = new()
        {
            ["UseState"] = () => Hooks.UseState(() => ++s_slotStarts),
            ["UseReducer with init"] = () => Hooks.UseReducer<int, int, int>(
                (state, action) => state + action, 0, arg => ++s_slotStarts),
            ["UseStore"] = () => Hooks.UseStore(s_store, value => ++s_slotStarts),
            ["UseSyncExternalStore"] = () => Hooks.UseSyncExternalStore(onStoreChange =>
            {
                ++s_slotStarts;
                return () => { };
            }, () => 0),
            ["Use"] = () => Hooks.Use(() => VelvetTask.FromResult(++s_slotStarts), resourceKey: "sheet"),
        };

        private VisualElement _root;
        private static Exception s_caught;
        private static int s_slotStarts;
        private static bool s_initiallyOpen;
        private static StateUpdater<bool> s_setOpen;
        private static Action s_sheetHook;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_caught = null;
            s_initiallyOpen = false;
            s_setOpen = default;
            s_sheetHook = null;
            s_slotStarts = 0;
        }

        // Beneath a router, which UseBlocker refuses to run without; it never navigates.
        private static readonly Router s_router = new(Array.Empty<RouteDefinition>());

        private static VNode InBoundary(VNode child)
            => V.ErrorBoundary(exception =>
            {
                s_caught = exception;
                return V.Label(text: "caught");
            }, new VNode[] { V.Provider(RouterContext.Router, s_router, new[] { child }) });

        #region The reported shape: a plain helper calling UseState, reached only once a button opens it

        [Component]
        private static VNode PanelRender()
        {
            var (open, setOpen) = Hooks.UseState(false);
            return V.Div(children: new VNode?[]
            {
                V.Button(name: "open", onClick: () => setOpen.Invoke(true)),
                open ? DetailSheet() : null,
            });
        }

        // The shape VEL102 reports at edit time; this fixture pins what the runtime reports for it.
#pragma warning disable VEL102
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static VNode DetailSheet()
        {
            var (tab, _) = Hooks.UseState(0);
            return V.Text(tab.ToString());
        }
#pragma warning restore VEL102

        // GREEN_ON_BASE(refactor): the VEL102 suppression around DetailSheet changes no code that this case runs.
        [Test]
        public void Given_AHelperCallingUseStateOnlyWhileOpen_When_TheButtonOpensIt_Then_TheErrorNamesTheComponentAndTheRule()
        {
            // Arrange
            using var mounted = V.Mount(_root, InBoundary(V.Component(PanelRender, key: "panel")), CaughtErrors.Unlogged);

            // Act
            _root.Q<Button>("open").SimulateClick();

            // Assert
            Assert.That(s_caught?.Message, Does.StartWith(
                "HookCountDiagnosticTests.PanelRender: Rendered more hooks than during the previous render" +
                " (UseState / UseReducer: 1 before, 2 now)."));
        }

        // GREEN_ON_BASE(refactor): the VEL102 suppression around DetailSheet changes no code that this case runs.
        [Test]
        public void Given_AHelperCallingUseStateOnlyWhileOpen_When_TheButtonOpensIt_Then_TheErrorIsThrownFromTheHelper()
        {
            // Arrange
            using var mounted = V.Mount(_root, InBoundary(V.Component(PanelRender, key: "panel")), CaughtErrors.Unlogged);

            // Act
            _root.Q<Button>("open").SimulateClick();

            // Assert
            Assert.That(s_caught?.StackTrace, Does.Contain(nameof(DetailSheet)));
        }

        #endregion

        #region The same shape in a props component, whose fiber body is a closure over the method

        private sealed record PanelProps(string Title);

        [Component]
        private static VNode PropsPanelRender(PanelProps props)
        {
            var (open, setOpen) = Hooks.UseState(false);
            return V.Div(children: new VNode?[]
            {
                V.Button(name: "open", text: props.Title, onClick: () => setOpen.Invoke(true)),
                open ? DetailSheet() : null,
            });
        }

        // GREEN_ON_BASE(refactor): the VEL102 suppression around DetailSheet changes no code that this case runs.
        [TestCase("V.Component")]
        [TestCase("V.Memo")]
        public void Given_APropsComponentWhoseHelperCallsUseStateOnlyWhileOpen_When_Opened_Then_TheErrorNamesTheComponent(
            string factory)
        {
            // Arrange
            var props = new PanelProps("open");
            var node = factory == "V.Memo"
                ? V.Memo(PropsPanelRender, props, (previous, next) => previous == next)
                : V.Component(PropsPanelRender, props);
            using var mounted = V.Mount(_root, InBoundary(node), CaughtErrors.Unlogged);

            // Act
            _root.Q<Button>("open").SimulateClick();

            // Assert
            Assert.That(s_caught?.Message, Does.StartWith(
                "HookCountDiagnosticTests.PropsPanelRender: Rendered more hooks than during the previous render"));
        }

        #endregion

        #region Each hook kind, called from a helper only an open render reaches

        // Compiler = false keeps the auto-memo weave out of these cases, which measure the hook count alone.
        [Component(Compiler = false)]
        private static VNode HostRender()
        {
            var (open, setOpen) = Hooks.UseState(s_initiallyOpen);
            s_setOpen = setOpen;
            return V.Div(children: new VNode?[] { open ? HookedSheet() : null });
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static VNode HookedSheet()
        {
            s_sheetHook();
            return V.Text("sheet");
        }

        private static string Describe(Exception exception)
        {
            if (exception == null) return "no exception";
            var closingParen = exception.Message.IndexOf(')');
            var head = closingParen < 0 ? exception.Message : exception.Message.Substring(0, closingParen + 1);
            return $"{head} | thrown inside {nameof(HookedSheet)}: {exception.StackTrace?.Contains(nameof(HookedSheet)) == true}";
        }

        // GREEN_ON_BASE(refactor): this case never reaches DetailSheet, and a pragma around it is this file's only code change.
        [TestCase("UseCallback", "UseCallback", 0, 1)]
        [TestCase("UseCallback with deps", "UseCallback", 0, 1)]
        [TestCase("UseBlocker beneath a router", "UseBlocker", 0, 1)]
        [TestCase("UseLayoutEffect", "UseLayoutEffect", 0, 1)]
        [TestCase("UseInsertionEffect", "UseInsertionEffect", 0, 1)]
        [TestCase("UseEffect", "UseEffect", 0, 1)]
        [TestCase("UseState", "UseState / UseReducer", 1, 2)]
        [TestCase("UseReducer", "UseState / UseReducer", 1, 2)]
        [TestCase("UseReducer with init", "UseState / UseReducer", 1, 2)]
        [TestCase("UseStore", "UseStore / UseSyncExternalStore", 0, 1)]
        [TestCase("UseSyncExternalStore", "UseStore / UseSyncExternalStore", 0, 1)]
        [TestCase("UseImperativeHandle", "UseImperativeHandle", 0, 1)]
        [TestCase("UseImperativeHandle with deps", "UseImperativeHandle", 0, 1)]
        [TestCase("UseRef", "UseRef / UseMutableRef", 0, 1)]
        [TestCase("UseMutableRef", "UseRef / UseMutableRef", 0, 1)]
        [TestCase("UseMemo", "UseMemo", 0, 1)]
        [TestCase("UseMemo with deps", "UseMemo", 0, 1)]
        [TestCase("UseId", "UseId", 0, 1)]
        [TestCase("UseDeferredValue", "UseDeferredValue", 0, 1)]
        [TestCase("UseOptimistic", "UseOptimistic", 0, 1)]
        [TestCase("UseMutation", "UseMutation", 0, 1)]
        [TestCase("UseTransition", "UseTransition", 0, 1)]
        [TestCase("Use", "Use", 0, 1)]
        public void Given_AHookOnlyAnOpenRenderCalls_When_Opened_Then_ItThrowsMoreHooksFromThatCall(
            string hook, string kind, int before, int now)
        {
            // Arrange
            s_sheetHook = s_sheetHooks[hook];
            using var mounted = V.Mount(_root, InBoundary(V.Component(HostRender, key: "host")), CaughtErrors.Unlogged);

            // Act
            s_setOpen.Invoke(true);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Describe(s_caught), Is.EqualTo(
                $"HookCountDiagnosticTests.HostRender: Rendered more hooks than during the previous render" +
                $" ({kind}: {before} before, {now} now) | thrown inside {nameof(HookedSheet)}: True"));
        }

        // GREEN_ON_BASE(refactor): this case never reaches DetailSheet, and a pragma around it is this file's only code change.
        [TestCase("UseCallback", "UseCallback", 1, 0)]
        [TestCase("UseBlocker beneath a router", "UseBlocker", 1, 0)]
        [TestCase("UseLayoutEffect", "UseLayoutEffect", 1, 0)]
        [TestCase("UseInsertionEffect", "UseInsertionEffect", 1, 0)]
        [TestCase("UseEffect", "UseEffect", 1, 0)]
        [TestCase("UseState", "UseState / UseReducer", 2, 1)]
        [TestCase("UseStore", "UseStore / UseSyncExternalStore", 1, 0)]
        [TestCase("UseSyncExternalStore", "UseStore / UseSyncExternalStore", 1, 0)]
        [TestCase("UseImperativeHandle", "UseImperativeHandle", 1, 0)]
        [TestCase("UseRef", "UseRef / UseMutableRef", 1, 0)]
        [TestCase("UseMemo", "UseMemo", 1, 0)]
        [TestCase("UseId", "UseId", 1, 0)]
        [TestCase("UseDeferredValue", "UseDeferredValue", 1, 0)]
        [TestCase("UseOptimistic", "UseOptimistic", 1, 0)]
        [TestCase("UseMutation", "UseMutation", 1, 0)]
        [TestCase("UseTransition", "UseTransition", 1, 0)]
        [TestCase("Use", "Use", 1, 0)]
        public void Given_AHookOnlyAnOpenRenderCalls_When_Closed_Then_ItThrowsFewerHooks(
            string hook, string kind, int before, int now)
        {
            // Arrange
            s_sheetHook = s_sheetHooks[hook];
            s_initiallyOpen = true;
            using var mounted = V.Mount(_root, InBoundary(V.Component(HostRender, key: "host")), CaughtErrors.Unlogged);

            // Act
            s_setOpen.Invoke(false);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_caught?.Message, Does.StartWith(
                $"HookCountDiagnosticTests.HostRender: Rendered fewer hooks than expected ({kind}: {before} before, {now} now)."));
        }

        // GREEN_ON_BASE(refactor): this case never reaches DetailSheet, and a pragma around it is this file's only code change.
        [TestCase("UseState")]
        [TestCase("UseReducer with init")]
        [TestCase("UseStore")]
        [TestCase("UseSyncExternalStore")]
        [TestCase("Use")]
        public void Given_AHookOnlyAnOpenRenderCalls_When_Opened_Then_TheRefusedCallStartsNoSlot(string hook)
        {
            // Arrange
            s_sheetHook = s_countingSheetHooks[hook];
            using var mounted = V.Mount(_root, InBoundary(V.Component(HostRender, key: "host")), CaughtErrors.Unlogged);

            // Act
            s_setOpen.Invoke(true);
            mounted.FlushStateForTest();

            // Assert — the refusal is folded in, since a helper that never ran would start nothing either
            Assert.That($"{s_caught?.Message.Contains("Rendered more hooks") == true} | slots started: {s_slotStarts}",
                Is.EqualTo("True | slots started: 0"));
        }

        #endregion

        #region A render refused for fewer hooks commits none of its hook deps

        private static readonly List<int> s_effectRuns = new();
        private static StateUpdater<int> s_setCount;
        private static StateUpdater<bool> s_setDetail;

        [Component(Compiler = false)]
        private static VNode RefusedRender()
        {
            var (count, setCount) = Hooks.UseState(0);
            var (detail, setDetail) = Hooks.UseState(true);
            s_setCount = setCount;
            s_setDetail = setDetail;
            Hooks.UseEffect((Func<Action>)(() =>
            {
                s_effectRuns.Add(count);
                return null;
            }), new object[] { count });
            if (detail) Hooks.UseMemo(() => 1);
            return V.Label(text: count.ToString());
        }

        // GREEN_ON_BASE(refactor): this case never reaches DetailSheet, and a pragma around it is this file's only code change.
        [Test]
        public void Given_ARenderRefusedForFewerHooksWithNoBoundary_When_TheCountsMatchAgain_Then_TheEffectRunsForTheNewDeps()
        {
            // Arrange — no boundary, so the fiber keeps rendering after the refusal is logged
            s_effectRuns.Clear();
            using var mounted = V.Mount(_root, V.Component(RefusedRender, key: "refused"));
            mounted.FlushEffectsForTest();
            LogAssert.Expect(LogType.Exception, new Regex("Rendered fewer hooks than expected"));
            s_setCount.Invoke(1);
            s_setDetail.Invoke(false);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Act
            s_setDetail.Invoke(true);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(string.Join(",", s_effectRuns), Is.EqualTo("0,1"));
        }

        #endregion

        #region A fiber unmounted and mounted again is compared as a mount

        private static bool s_remountExtraHook;

        [Component(Compiler = false)]
        private static VNode RemountRender()
        {
            Hooks.UseState(0);
            if (s_remountExtraHook) Hooks.UseState(1);
            return V.Label(name: "remounted", text: s_remountExtraHook ? "two hooks" : "one hook");
        }

        // GREEN_ON_BASE(characterization): the base also resets its hook-count baselines on unmount.
        // HasCommittedHookCounts takes that reset over, and deleting its line in Unmount reddens this case. The
        // VEL102 suppression around DetailSheet changes no code that this case runs.
        [Test]
        public void Given_AFiberUnmounted_When_MountedAgainWithAnotherHookCount_Then_ItRendersAsAMount()
        {
            // Arrange — the Unmount then Mount pair reuses one fiber, as UseDeferredValueTests' remount case does
            s_remountExtraHook = false;
            var fiber = FiberRenderer.CreateRoot(RemountRender);
            try
            {
                FiberRenderer.Mount(fiber, _root);
                FiberRenderer.Unmount(fiber);
                s_remountExtraHook = true;

                // Act
                FiberRenderer.Mount(fiber, _root);

                // Assert
                Assert.That(_root.Q<Label>("remounted")?.text, Is.EqualTo("two hooks"));
            }
            finally
            {
                FiberRenderer.Dispose(fiber);
            }
        }

        #endregion
    }
}
