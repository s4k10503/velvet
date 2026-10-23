using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// <c>V.FocusScope</c> renders no element around its children, as React Aria's <c>FocusScope</c> renders none:
    /// they stand among the parent's own children between two undisplayed elements, and containment, a
    /// single-tab-stop group's arrows, autoFocus, restoreFocus and <c>UseFocusManager</c> read the scope from
    /// those two. FocusScopeTests owns the behaviour itself, on a container element's scope.
    /// </summary>
    internal sealed class FocusScopeRangeTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        private static StateUpdater<bool> s_setShowScope;
        private static FocusManager s_manager;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            s_setShowScope = default;
            s_manager = null;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        private VisualElement Q(string name) => _host.Root.Q<VisualElement>(name);

        private Focusable Focused => _host.Panel.focusController.focusedElement;

        // Focus-ring membership needs resolved display state, for the reason FocusScopeTests' Mount gives.
        private void Mount(System.Func<VNode> body)
        {
            _mounted = V.Mount(_host.Root, V.Component(body, key: "root"));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        private static void SendMove(VisualElement target, NavigationMoveEvent.Direction direction)
        {
            using var move = NavigationMoveEvent.GetPooled(direction);
            move.target = target;
            target.SendEvent(move);
        }

        [Component]
        private static VNode ContainedRow() => V.Div(name: "row", children: new VNode[]
        {
            V.Button(name: "before"),
            V.FocusScope(contain: true, children: new VNode[]
            {
                V.Button(name: "s1"),
                V.Button(name: "s2"),
            }),
            V.Button(name: "after"),
        });

        [Component]
        private static VNode ScopeDeclaringAZIndexedChild() => V.Div(name: "row", children: new VNode[]
        {
            V.FocusScope(contain: true, children: new VNode[]
            {
                V.Button(name: "s1"),
                V.Button(name: "zp", className: "absolute z-10"),
            }),
            V.Button(name: "after"),
        });

        private FocusScopeStart StartInRow()
        {
            foreach (var child in Q("row").hierarchy.Children())
            {
                if (child is FocusScopeStart start)
                {
                    return start;
                }
            }
            return null;
        }

        [Test]
        public void Given_AZIndexedChildOfAFocusScope_When_TheRangeIsAskedWhetherItContainsIt_Then_ItDoes()
        {
            // Arrange
            Mount(ScopeDeclaringAZIndexedChild);
            var zp = Q("zp");

            // Act
            var contains = FocusScopeRange.Contains(StartInRow(), zp);

            // Assert
            Assert.That((_mounted.Root.Reconciler.Context.ZLayerMembers.ContainsKey(zp), contains), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AZIndexedChildOfAFocusScope_When_TheStartsAroundItAreAppended_Then_ItsScopeIsTheOnlyOne()
        {
            // Arrange
            Mount(ScopeDeclaringAZIndexedChild);
            var zp = Q("zp");
            var starts = new List<FocusScopeStart>();

            // Act
            FocusScopeRange.AppendStartsAround(zp, starts);

            // Assert
            Assert.That(
                (_mounted.Root.Reconciler.Context.ZLayerMembers.ContainsKey(zp), starts.Count == 1 && ReferenceEquals(starts[0], StartInRow())),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AZIndexedChildOfAFocusScope_When_TheContentIsAppended_Then_ItHoldsTheRealElementAtItsDeclaredPlace()
        {
            // Arrange
            Mount(ScopeDeclaringAZIndexedChild);
            var content = new List<VisualElement>();

            // Act
            FocusScopeRange.AppendContent(StartInRow(), content);

            // Assert
            Assert.That(
                (_mounted.Root.Reconciler.Context.ZLayerMembers.ContainsKey(Q("zp")),
                    string.Join(",", content.ConvertAll(element => element.name))),
                Is.EqualTo((true, "s1,zp")));
        }

        [Test]
        public void Given_AFocusScope_When_Mounted_Then_ItsChildrenStandAmongItsParentsChildrenBetweenTwoUndisplayedElements()
        {
            // Arrange & Act
            Mount(ContainedRow);

            // Assert
            var children = new List<string>();
            foreach (var child in Q("row").hierarchy.Children())
            {
                children.Add(string.IsNullOrEmpty(child.name) ? child.style.display.value.ToString() : child.name);
            }
            Assert.That(string.Join(",", children), Is.EqualTo("before,None,s1,s2,None,after"));
        }

        // GREEN_ON_BASE(characterization): the base's V.FocusScope wraps its children in an element, whose scope
        // wraps Tab the same way. This case pins that a scope with no element still does.
        [Test]
        public void Given_AContainedFocusScope_When_TabDispatchesFromItsLastElement_Then_FocusWrapsToItsFirst()
        {
            // Arrange
            Mount(ContainedRow);
            var s2 = Q("s2");
            s2.Focus();
            var held = Focused == s2;

            // Act
            SendMove(s2, NavigationMoveEvent.Direction.Next);

            // Assert
            Assert.That((held, Focused), Is.EqualTo((true, (Focusable)Q("s1"))));
        }

        // GREEN_ON_BASE(characterization): the base's V.FocusScope wraps its children in an element, whose scope
        // pulls focus back the same way. This case pins that a scope with no element still does.
        [Test]
        public void Given_AContainedFocusScope_When_FocusMovesToAnElementBesideIt_Then_FocusReturnsToTheScope()
        {
            // Arrange
            Mount(ContainedRow);
            var s1 = Q("s1");
            s1.Focus();
            var held = Focused == s1;

            // Act
            Q("after").Focus();

            // Assert
            Assert.That((held, Focused), Is.EqualTo((true, (Focusable)s1)));
        }

        // The inner scope is a container element's, so the walk out from "n2" meets that element's scope before
        // the sentinels bracketing it.
        [Component]
        private static VNode ContainedRowAroundAnElementScope() => V.Div(children: new VNode[]
        {
            V.Button(name: "before"),
            V.FocusScope(contain: true, children: new VNode[]
            {
                V.Button(name: "n1"),
                FocusScopeElement.Div(name: "inner", children: new VNode[] { V.Button(name: "n2") }),
            }),
            V.Button(name: "after"),
        });

        // GREEN_ON_BASE(characterization): the base's V.FocusScope wraps its children in an element, whose scope
        // contains the inner one's elements the same way. This case pins that a scope with no element still does.
        [Test]
        public void Given_AContainedFocusScopeAroundAnElementScope_When_TabDispatchesFromTheInnerScopesLastElement_Then_FocusWrapsToTheOuterScopesFirst()
        {
            // Arrange
            Mount(ContainedRowAroundAnElementScope);
            var n2 = Q("n2");
            n2.Focus();
            var held = Focused == n2;

            // Act
            SendMove(n2, NavigationMoveEvent.Direction.Next);

            // Assert
            Assert.That((held, Focused), Is.EqualTo((true, (Focusable)Q("n1"))));
        }

        [Component]
        private static VNode GroupRow() => V.Div(children: new VNode[]
        {
            V.Button(name: "before"),
            V.FocusScope(singleTabStop: true, children: new VNode[]
            {
                V.Button(name: "g1"),
                V.Button(name: "g2"),
            }),
            V.Button(name: "after"),
        });

        [Test]
        public void Given_ASingleTabStopFocusScope_When_AnArrowMovesFromItsFirstElement_Then_FocusMovesToItsSecond()
        {
            // Arrange
            Mount(GroupRow);
            var g1 = Q("g1");
            g1.Focus();
            var held = Focused == g1;

            // Act
            SendMove(g1, NavigationMoveEvent.Direction.Right);

            // Assert
            Assert.That((held, Focused), Is.EqualTo((true, (Focusable)Q("g2"))));
        }

        [Component]
        private static VNode AutoFocusRow() => V.Div(children: new VNode[]
        {
            V.Button(name: "before"),
            V.FocusScope(autoFocus: true, children: new VNode[]
            {
                V.Button(name: "a1"),
                V.Button(name: "a2"),
            }),
        });

        // GREEN_ON_BASE(characterization): the base's V.FocusScope wraps its children in an element, which focuses
        // its first descendant as it attaches. This case pins that a scope with no element still does.
        [Test]
        public void Given_AFocusScopeWithAutoFocus_When_Mounted_Then_ItsFirstElementHoldsFocus()
        {
            // Arrange & Act
            Mount(AutoFocusRow);

            // Assert
            Assert.That(Focused, Is.EqualTo(Q("a1")));
        }

        [Component]
        private static VNode RestoringRow()
        {
            var (shown, setShown) = Hooks.UseState(false);
            s_setShowScope = setShown;
            return V.Div(children: new VNode[]
            {
                V.Button(name: "opener"),
                shown
                    ? V.FocusScope(key: "scope", restoreFocus: true, children: new VNode[] { V.Button(name: "inner") })
                    : null,
            });
        }

        // GREEN_ON_BASE(characterization): the base's V.FocusScope wraps its children in an element, which restores
        // focus as it unmounts. This case pins that a scope with no element still does.
        [Test]
        public void Given_AFocusScopeWithRestoreFocus_When_ItUnmountsWhileHoldingFocus_Then_TheElementFocusedAtItsMountRegainsFocus()
        {
            // Arrange
            Mount(RestoringRow);
            var opener = Q("opener");
            opener.Focus();
            s_setShowScope.Invoke(true);
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            Q("inner").Focus();
            var heldInside = Focused == Q("inner");

            // Act
            s_setShowScope.Invoke(false);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That((heldInside, Focused), Is.EqualTo((true, (Focusable)opener)));
        }

        private static VNode Probe()
        {
            s_manager = Hooks.UseFocusManager();
            return V.Button(name: "r1");
        }

        [Component]
        private static VNode ManagedRow() => V.Div(children: new VNode[]
        {
            V.Button(name: "before"),
            V.FocusScope(children: new VNode[]
            {
                V.Component(Probe, key: "probe"),
                V.Button(name: "r2"),
            }),
            V.Button(name: "after"),
        });

        // GREEN_ON_BASE(characterization): the base's V.FocusScope wraps its children in an element, under which
        // the manager finds the same last element. This case pins that it finds it in a scope with no element.
        [Test]
        public void Given_AFocusManagerInsideAFocusScope_When_FocusLastIsCalled_Then_TheScopesLastElementTakesFocus()
        {
            // Arrange
            Mount(ManagedRow);
            Q("r1").Focus();

            // Act
            var moved = s_manager.FocusLast();

            // Assert
            Assert.That((moved, Focused), Is.EqualTo((Q("r2"), (Focusable)Q("r2"))));
        }
    }
}
