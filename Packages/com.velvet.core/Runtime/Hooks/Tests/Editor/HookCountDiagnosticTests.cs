using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A render that calls <c>UseState</c> / <c>UseReducer</c>, <c>UseStore</c> or <c>Use</c> more or fewer times than
    /// the previous render did fails with an <see cref="InvalidOperationException"/> naming the component, React's
    /// wording and the kind's two counts.
    /// <list type="bullet">
    /// <item>A call past the committed count throws from that call, so the helper method making it is on the
    /// stack.</item>
    /// <item>A body that returns having made fewer calls throws once it settles.</item>
    /// </list>
    /// Each case reads the error from an enclosing error boundary's fallback.
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

        // Each hook named here is called by the helper that only a render with the sheet open reaches. Keyed
        // by name so a case's name stays plain text rather than a rendered delegate.
        private static readonly Dictionary<string, Action> s_sheetHooks = new()
        {
            ["UseState"] = () => Hooks.UseState(0),
            ["UseReducer"] = () => Hooks.UseReducer<int, int>((state, action) => state + action, 0),
            ["UseReducer with init"] = () => Hooks.UseReducer<int, int, int>((state, action) => state + action, 0, arg => arg),
            ["UseStore"] = () => Hooks.UseStore(s_store, value => value),
            ["Use"] = () => Hooks.Use(() => VelvetTask.FromResult(1), resourceKey: "sheet"),
        };

        private VisualElement _root;
        private static Exception s_caught;
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

        #endregion
    }
}
