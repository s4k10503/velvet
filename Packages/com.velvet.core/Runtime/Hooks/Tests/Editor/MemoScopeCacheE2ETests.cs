using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the runtime half of per-scope memoization: a component that reserves several scope slots in one
    /// render gets one cache entry per scope, so a scope whose deps are unchanged returns the VNode it cached
    /// while a sibling whose deps changed builds again. The components call the gate API by hand, as the weaver's
    /// output does.
    /// </summary>
    [TestFixture]
    internal sealed class MemoScopeCacheE2ETests
    {
        private const int ScopeCount = 3;

        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            Array.Clear(s_builds, 0, s_builds.Length);
            s_setters = null;
            s_reservations = default;
            s_storeFailure = null;
            s_plainCallGrant = int.MinValue;
        }

        private static readonly int[] s_builds = new int[ScopeCount];
        private static Action<int>[] s_setters;

        private static VNode Scoped(int firstSlot, int scope, int dep)
        {
            var deps = new object[] { dep };
            if (Hooks.TryGetScope(firstSlot, scope, deps, out var cached))
            {
                return cached;
            }
            s_builds[scope]++;
            var result = V.Label(text: $"scope{scope}:{dep}");
            Hooks.StoreScope(firstSlot, scope, deps, result);
            return result;
        }

        [Component(Compiler = false)]
        private static VNode ThreeScopes()
        {
            var (a, setA) = Hooks.UseState(0);
            var (b, setB) = Hooks.UseState(0);
            var (c, setC) = Hooks.UseState(0);
            s_setters = new Action<int>[] { setA.Invoke, setB.Invoke, setC.Invoke };
            var first = Hooks.ReserveScopes(MethodBase.GetCurrentMethod().MethodHandle, ScopeCount);
            return V.Div("scopes", Scoped(first, 0, a), Scoped(first, 1, b), Scoped(first, 2, c));
        }

        private List<string> LabelTexts() => _root.Query<Label>().ToList().Select(label => label.text).ToList();

        [Test]
        public void Given_ThreeScopes_When_OnlyTheMiddleDepChanges_Then_OnlyTheMiddleScopeBuildsAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ThreeScopes, key: "three-scopes"));
            Assume.That(s_builds, Is.EqualTo(new[] { 1, 1, 1 }), "Precondition: the mount built each scope once");

            // Act
            s_setters[1](5);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_builds, Is.EqualTo(new[] { 1, 2, 1 }));
        }

        [Test]
        public void Given_ThreeScopes_When_OnlyTheMiddleDepChanges_Then_EveryLabelShowsItsCurrentValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ThreeScopes, key: "three-scopes"));

            // Act
            s_setters[1](5);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "scope0:0", "scope1:5", "scope2:0" }));
        }

        [Test]
        public void Given_ThreeScopes_When_AllDepsChange_Then_EveryScopeBuildsAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ThreeScopes, key: "three-scopes"));

            // Act
            s_setters[0](1);
            s_setters[1](1);
            s_setters[2](1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_builds, Is.EqualTo(new[] { 2, 2, 2 }));
        }

        private static (int First, int Second) s_reservations;

        [Component(Compiler = false)]
        private static VNode ReservesTwice()
        {
            var handle = MethodBase.GetCurrentMethod().MethodHandle;
            s_reservations = (Hooks.ReserveScopes(handle, 2), Hooks.ReserveScopes(handle, 2));
            return V.Label(text: "reserved");
        }

        [Test]
        public void Given_ARenderThatReservesTwice_When_Rendered_Then_OnlyTheFirstCallIsGrantedSlots()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(ReservesTwice, key: "reserves-twice"));

            // Assert
            Assert.That(s_reservations, Is.EqualTo((0, -1)));
        }

        private static int s_plainCallGrant;

        [Component(Compiler = false)]
        private static VNode CallsThreeScopesAsAPlainMethod()
        {
            s_plainCallGrant = Hooks.ReserveScopes(typeof(MemoScopeCacheE2ETests)
                .GetMethod(nameof(ThreeScopes), BindingFlags.NonPublic | BindingFlags.Static).MethodHandle, 1);
            return V.Label(text: "plain");
        }

        [Test]
        public void Given_AReservationNamingAnotherComponent_When_Rendered_Then_NoSlotIsGranted()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(CallsThreeScopesAsAPlainMethod, key: "plain-call"));

            // Assert
            Assert.That(s_plainCallGrant, Is.EqualTo(-1));
        }

        [Test]
        public void Given_NoRender_When_ScopeGatesAreCalledWithAnUngrantedSlot_Then_NothingIsServedOrStaged()
        {
            // Arrange
            var first = Hooks.ReserveScopes(MethodBase.GetCurrentMethod().MethodHandle, ScopeCount);

            // Act
            var hit = Hooks.TryGetScope(first, 0, Array.Empty<object>(), out _);
            Hooks.StoreScope(first, 0, Array.Empty<object>(), V.Label(text: "unstaged"));

            // Assert
            Assert.That((first, hit), Is.EqualTo((-1, false)));
        }

        private static Type s_storeFailure;

        [Component(Compiler = false)]
        private static VNode StoresPastTheLastScope()
        {
            var first = Hooks.ReserveScopes(MethodBase.GetCurrentMethod().MethodHandle, 2);
            try
            {
                Hooks.StoreScope(first, 2, Array.Empty<object>(), V.Label(text: "past"));
            }
            catch (Exception error)
            {
                s_storeFailure = error.GetType();
            }
            return V.Label(text: "rendered");
        }

        [Test]
        public void Given_AScopeIndexAtTheReservedCount_When_Stored_Then_ReportsAnInvalidSlot()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(StoresPastTheLastScope, key: "store-past"));

            // Assert
            Assert.That(s_storeFailure, Is.EqualTo(typeof(InvalidOperationException)));
        }
    }
}
