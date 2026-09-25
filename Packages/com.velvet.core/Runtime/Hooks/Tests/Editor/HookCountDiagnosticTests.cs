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
    /// A render that calls <c>UseState</c> / <c>UseReducer</c>, <c>UseStore</c> or <c>Use</c> more or fewer times than
    /// the previous render did fails with an <see cref="InvalidOperationException"/> naming the component, React's
    /// wording and the kind's two counts.
    /// <list type="bullet">
    /// <item>The call past the committed count throws from itself, before it starts a slot, so a helper method
    /// making that call is on the stack.</item>
    /// <item>A body that returns having made fewer calls throws once it settles.</item>
    /// <item>The kinds the editor-only <c>FiberBeginWork.ValidateEditorHookCounts</c> counts log the same wording and
    /// let the render commit.</item>
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
            ["UseState"] = () => Hooks.UseState(0),
            ["UseReducer"] = () => Hooks.UseReducer<int, int>((state, action) => state + action, 0),
            ["UseReducer with init"] = () => Hooks.UseReducer<int, int, int>((state, action) => state + action, 0, arg => arg),
            ["UseStore"] = () => Hooks.UseStore(s_store, value => value),
            ["Use"] = () => Hooks.Use(() => VelvetTask.FromResult(1), resourceKey: "sheet"),
            ["UseCallback"] = () => Hooks.UseCallback((Action)(() => { })),
            ["UseBlocker"] = () => Hooks.UseBlocker(_ => false),
            ["UseInsertionEffect"] = () => Hooks.UseInsertionEffect((Func<Action>)(() => null)),
            ["UseEffect"] = () => Hooks.UseEffect((Func<Action>)(() => null)),
            ["UseImperativeHandle"] = () => Hooks.UseImperativeHandle(s_handle, () => new object()),
            ["UseId"] = () => Hooks.UseId(),
            ["UseDeferredValue"] = () => Hooks.UseDeferredValue(1),
            ["UseOptimistic"] = () => Hooks.UseOptimistic<int, int>(0, (state, action) => state + action),
            ["UseMutation"] = () => Hooks.UseMutation(new MutationOptions<int, int>(
                MutationFn: (value, _) => VelvetTask.FromResult(value))),
        };

        // Each counts in s_slotStarts every call of its lazy initializer, reducer init, store selector or Use
        // factory, which the refused call past the count makes only if it goes on to its slot.
        private static readonly Dictionary<string, Action> s_countingSheetHooks = new()
        {
            ["UseState"] = () => Hooks.UseState(() => ++s_slotStarts),
            ["UseReducer with init"] = () => Hooks.UseReducer<int, int, int>(
                (state, action) => state + action, 0, arg => ++s_slotStarts),
            ["UseStore"] = () => Hooks.UseStore(s_store, value => ++s_slotStarts),
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

        private static VNode InBoundary(VNode child)
            => V.ErrorBoundary(exception =>
            {
                s_caught = exception;
                return V.Label(text: "caught");
            }, new VNode[] { child });

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

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static VNode DetailSheet()
        {
            var (tab, _) = Hooks.UseState(0);
            return V.Text(tab.ToString());
        }

        [Test]
        public void Given_AHelperCallingUseStateOnlyWhileOpen_When_TheButtonOpensIt_Then_TheErrorNamesTheComponentAndTheRule()
        {
            // Arrange
            using var mounted = V.Mount(_root, InBoundary(V.Component(PanelRender, key: "panel")));

            // Act
            _root.Q<Button>("open").SimulateClick();

            // Assert
            Assert.That(s_caught?.Message, Does.StartWith(
                "HookCountDiagnosticTests.PanelRender: Rendered more hooks than during the previous render" +
                " (UseState / UseReducer: 1 before, 2 now)."));
        }

        [Test]
        public void Given_AHelperCallingUseStateOnlyWhileOpen_When_TheButtonOpensIt_Then_TheErrorIsThrownFromTheHelper()
        {
            // Arrange
            using var mounted = V.Mount(_root, InBoundary(V.Component(PanelRender, key: "panel")));

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
            using var mounted = V.Mount(_root, InBoundary(node));

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

        [TestCase("UseState", "UseState / UseReducer", 1, 2)]
        [TestCase("UseReducer", "UseState / UseReducer", 1, 2)]
        [TestCase("UseReducer with init", "UseState / UseReducer", 1, 2)]
        [TestCase("UseStore", "UseStore", 0, 1)]
        [TestCase("Use", "Use", 0, 1)]
        public void Given_AHookOnlyAnOpenRenderCalls_When_Opened_Then_ItThrowsMoreHooksFromThatCall(
            string hook, string kind, int before, int now)
        {
            // Arrange
            s_sheetHook = s_sheetHooks[hook];
            using var mounted = V.Mount(_root, InBoundary(V.Component(HostRender, key: "host")));

            // Act
            s_setOpen.Invoke(true);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Describe(s_caught), Is.EqualTo(
                $"HookCountDiagnosticTests.HostRender: Rendered more hooks than during the previous render" +
                $" ({kind}: {before} before, {now} now) | thrown inside {nameof(HookedSheet)}: True"));
        }

        [TestCase("UseState", "UseState / UseReducer", 2, 1)]
        [TestCase("UseStore", "UseStore", 1, 0)]
        [TestCase("Use", "Use", 1, 0)]
        public void Given_AHookOnlyAnOpenRenderCalls_When_Closed_Then_ItThrowsFewerHooks(
            string hook, string kind, int before, int now)
        {
            // Arrange
            s_sheetHook = s_sheetHooks[hook];
            s_initiallyOpen = true;
            using var mounted = V.Mount(_root, InBoundary(V.Component(HostRender, key: "host")));

            // Act
            s_setOpen.Invoke(false);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_caught?.Message, Does.StartWith(
                $"HookCountDiagnosticTests.HostRender: Rendered fewer hooks than expected ({kind}: {before} before, {now} now)."));
        }

        [TestCase("UseState")]
        [TestCase("UseReducer with init")]
        [TestCase("UseStore")]
        [TestCase("Use")]
        public void Given_AHookOnlyAnOpenRenderCalls_When_Opened_Then_TheRefusedCallStartsNoSlot(string hook)
        {
            // Arrange
            s_sheetHook = s_countingSheetHooks[hook];
            using var mounted = V.Mount(_root, InBoundary(V.Component(HostRender, key: "host")));

            // Act
            s_setOpen.Invoke(true);
            mounted.FlushStateForTest();

            // Assert — the refusal is folded in, since a helper that never ran would start nothing either
            Assert.That($"{s_caught?.Message.Contains("Rendered more hooks") == true} | slots started: {s_slotStarts}",
                Is.EqualTo("True | slots started: 0"));
        }

        // UseLayoutEffect's log is pinned in UseLayoutEffectTests.
        [TestCase("UseCallback", "UseCallback")]
        [TestCase("UseBlocker", "UseBlocker")]
        [TestCase("UseInsertionEffect", "UseInsertionEffect")]
        [TestCase("UseEffect", "UseEffect")]
        [TestCase("UseImperativeHandle", "UseImperativeHandle")]
        [TestCase("UseId", "UseId")]
        [TestCase("UseDeferredValue", "UseDeferredValue")]
        [TestCase("UseOptimistic", "UseOptimistic")]
        [TestCase("UseMutation", "UseMutation")]
        public void Given_AnEditorCheckedHookOnlyAnOpenRenderCalls_When_Opened_Then_ItLogsMoreHooksNamingTheComponent(
            string hook, string kind)
        {
            // Arrange
            s_sheetHook = s_sheetHooks[hook];
            using var mounted = V.Mount(_root, InBoundary(V.Component(HostRender, key: "host")));
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                $"HookCountDiagnosticTests.HostRender: Rendered more hooks than during the previous render ({kind}: 0 before, 1 now).")));

            // Act
            s_setOpen.Invoke(true);
            mounted.FlushStateForTest();

            // Assert — LogAssert.Expect verifies the log
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
