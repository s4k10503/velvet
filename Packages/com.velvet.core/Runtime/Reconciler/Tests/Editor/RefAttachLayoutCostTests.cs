using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what the callback refs a commit attaches cost the panel's layout, and what they still read.
    /// <list type="bullet">
    /// <item>Refs that each write a colour to their element compute the panel's layout once between them, whether a
    /// commit attaches them, the pass that queued them does, or a <c>V.VirtualList</c> commits them row by row; a read
    /// behind the panel's own layout pass and a colour write computes nothing.</item>
    /// <item>A ref or a layout effect behind a ref that widened an element reads the widened box, and a ref behind one
    /// that moved a child of the panel's root reads where the move put its sibling.</item>
    /// <item>A pick a layout effect makes finds an element the commit widened in place, and one the commit added
    /// outside its parent's box.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RefAttachLayoutCostTests : PanelTestBase
    {
        private const int Refs = 8;
        private const float MountWidth = 120f;
        private const float WidenedWidth = 200f;
        private const float NarrowWidth = 50f;
        private const float RootChildSize = 30f;

        private static VisualElement s_root;
        private static float s_widthRead;
        private static string s_pickedName;
        private static StateUpdater<float> s_setPickedWidth;
        private static StateUpdater<bool> s_setOutsideShown;
        private static VisualElement s_movedRootChild;
        private static VisualElement s_stayingRootChild;
        private static Vector2 s_stayingPositionBefore;
        private static float s_stayingMovedBy;

        public override void SetUp()
        {
            base.SetUp();
            s_root = _window.rootVisualElement;
            s_widthRead = float.NaN;
            s_pickedName = null;
            s_setPickedWidth = default;
            s_setOutsideShown = default;
            s_movedRootChild = null;
            s_stayingRootChild = null;
            s_stayingPositionBefore = default;
            s_stayingMovedBy = float.NaN;
        }

        [Test]
        public void Given_CallbackRefsThatEachWriteAColourToTheirElement_When_TheTreeMounts_Then_TheCommitComputesThePanelsLayoutOnce()
        {
            // Arrange
            var computations = CountLayoutComputations(_window.rootVisualElement);

            // Act
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: ColouringDivs()));

            // Assert
            Assert.That(computations.Calls, Is.EqualTo(1));
        }

        [Test]
        public void Given_CallbackRefsThatEachWriteAColourToTheirElement_When_APassNoCommitFollowsAttachesThem_Then_ItComputesThePanelsLayoutOnce()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            _window.rootVisualElement.Add(scope.Root);
            var computations = CountLayoutComputations(scope.Root);

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), ColouringDivs());

            // Assert
            Assert.That(computations.Calls, Is.EqualTo(1));
        }

        [Test]
        public void Given_AVirtualListWhoseRowsRefsEachWriteAColour_When_ThePanelLaysTheListOut_Then_TheRowsComputeThePanelsLayoutOnce()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.VirtualList(new List<string> { "a", "b", "c", "d", "e" },
                item => item, itemHeight: 30f, renderer: item => V.Component(ColouredRowRender, item, key: item),
                name: "vlist", className: "h-[200px]"));
            var computations = CountLayoutComputations(_window.rootVisualElement);

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(computations.Calls, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base computes nothing on a panel its own layout pass left clean.
        // This pins that a hierarchy change the panel has laid out since the last computation leaves a read nothing
        // to compute; the cut it answers is dropping `(bool)_members.Dirty.GetValue(node) &&` from
        // `PanelLayout.EnsureLaidOut`.
        [Test]
        public void Given_AnElementAddedAfterAComputationAndLaidOutByThePanel_When_AReadFollowsAColourWrite_Then_ItComputesNothing()
        {
            // Arrange
            var root = _window.rootVisualElement;
            var computations = CountLayoutComputations(root);
            var added = new VisualElement();
            root.Add(added);
            ForcePanelUpdate(root.panel);
            added.style.backgroundColor = Color.red;

            // Act
            using (var scope = new ReconcilerScope())
            {
                FiberLayoutReflow.LayOutFor(scope.Reconciler.Context, root, null);
            }

            // Assert
            Assert.That(computations.Calls, Is.Zero);
        }

        // GREEN_ON_BASE(characterization): the base computes the layout again before a ref behind a write.
        // This pins that refs sharing one computation still compute again behind a write that moves a box; the cut
        // it answers is dropping the loop over the root's children from `PanelLayout.OnlyLeftDirty`.
        [Test]
        public void Given_ACallbackRefThatWidensItsElement_When_TheTreeMounts_Then_ALaterSiblingsRefReadsTheWidenedWidth()
        {
            // Arrange
            var root = _window.rootVisualElement;

            // Act
            _mounted = V.Mount(root, V.Div(children: new VNode[]
            {
                V.Div(className: $"w-[{MountWidth}px] h-[20px]", name: "widened", refCallback: s_widen),
                V.Div(refCallback: s_readWidened),
            }));

            // Assert
            Assert.That(s_widthRead, Is.EqualTo(WidenedWidth).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base computes the layout again before a layout effect behind a write.
        // This pins that a layout effect after its element's ref reads what the ref wrote; the cut it answers is
        // dropping the loop over the root's children from `PanelLayout.OnlyLeftDirty`.
        [Test]
        public void Given_ACallbackRefThatWidensItsElement_When_TheTreeMounts_Then_TheLayoutEffectOfTheComponentThatRenderedItReadsTheWidenedWidth()
        {
            // Arrange
            var root = _window.rootVisualElement;

            // Act
            _mounted = V.Mount(root, V.Component(WidenedByRefRender, key: "widened"));

            // Assert
            Assert.That(s_widthRead, Is.EqualTo(WidenedWidth).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base computes the layout again before a ref behind a write.
        // This pins that a move among the panel root's own children still sends a later ref through the computation;
        // the cut it answers is dropping the hierarchy-version line from `PanelLayout.OnlyLeftDirty`.
        [Test]
        public void Given_TwoChildrenOfThePanelsRoot_When_ARefMovesTheFirstBehindTheSecond_Then_ALaterRefReadsTheSecondWhereItMoved()
        {
            // Arrange
            var panelRoot = _window.rootVisualElement.panel.visualTree;
            s_movedRootChild = FixedSizeElement();
            s_stayingRootChild = FixedSizeElement();
            panelRoot.Add(s_movedRootChild);
            panelRoot.Add(s_stayingRootChild);

            // Act
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: new VNode[]
            {
                V.Div(refCallback: s_moveRootChild),
                V.Div(refCallback: s_readStayingRootChild),
            }));

            // Assert
            Assert.That(s_stayingMovedBy, Is.EqualTo(RootChildSize).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base finds an element a commit widened in place with a pick.
        // This pins that a computation leaves the panel's own layout pass to a pick made from a callback; the cut it
        // answers is dropping `_members.Dirty.SetValue(node, true)` from `PanelLayout.EnsureLaidOut`.
        [Test]
        public void Given_AnElementThePanelLaidOut_When_AnUpdateWidensItInPlace_Then_APickInTheLayoutEffectFindsItInTheWidenedPart()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Component(PickedBoxRender, key: "picked"));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            s_setPickedWidth.Invoke(WidenedWidth);

            // Act
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_pickedName, Is.EqualTo("picked"));
        }

        // GREEN_ON_BASE(characterization): the base finds a child a commit added outside its parent's box with a pick.
        // This pins that a computation leaves the panel's own layout pass to a pick made from a callback; the cut it
        // answers is dropping `_members.Dirty.SetValue(node, true)` from `PanelLayout.EnsureLaidOut`.
        [Test]
        public void Given_AParentThePanelLaidOut_When_AnUpdateAddsAChildOutsideItsBox_Then_APickInTheLayoutEffectFindsTheChild()
        {
            // Arrange
            VelvetStyleUtilities.AttachTo(_window.rootVisualElement);
            _mounted = V.Mount(_window.rootVisualElement, V.Component(OutsideChildRender, key: "outside"));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            s_setOutsideShown.Invoke(true);

            // Act
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_pickedName, Is.EqualTo("outside-child"));
        }

        private static VNode[] ColouringDivs()
        {
            var divs = new VNode[Refs];
            for (var i = 0; i < Refs; i++) divs[i] = V.Div(className: "w-[20px] h-[20px]", refCallback: s_colour);
            return divs;
        }

        private static VisualElement FixedSizeElement()
        {
            var element = new VisualElement();
            element.style.width = RootChildSize;
            element.style.height = RootChildSize;
            element.style.flexShrink = 0f;
            return element;
        }

        private static readonly Func<VisualElement, Action> s_colour = element =>
        {
            element.style.backgroundColor = Color.red;
            return null;
        };

        private static readonly Func<VisualElement, Action> s_widen = element =>
        {
            element.style.width = WidenedWidth;
            return null;
        };

        private static readonly Func<VisualElement, Action> s_readWidened = _ =>
        {
            s_widthRead = s_root.Q<VisualElement>("widened").layout.width;
            return null;
        };

        private static readonly Func<VisualElement, Action> s_moveRootChild = _ =>
        {
            s_stayingPositionBefore = s_stayingRootChild.layout.position;
            s_movedRootChild.BringToFront();
            return null;
        };

        private static readonly Func<VisualElement, Action> s_readStayingRootChild = _ =>
        {
            s_stayingMovedBy = (s_stayingRootChild.layout.position - s_stayingPositionBefore).magnitude;
            return null;
        };

        [Component]
        private static VNode ColouredRowRender(string label)
            => V.Div(className: "h-[30px]", name: $"row-{label}", refCallback: s_colour);

        [Component]
        private static VNode WidenedByRefRender()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_widthRead = s_root.Q<VisualElement>("widened").layout.width;
                return null;
            }), Array.Empty<object>());
            return V.Div(className: $"w-[{MountWidth}px] h-[20px]", name: "widened", refCallback: s_widen);
        }

        // A box at the start of its parent, whose layout effect picks a point past the box's narrow width.
        [Component]
        private static VNode PickedBoxRender()
        {
            var (width, setWidth) = Hooks.UseState(NarrowWidth);
            s_setPickedWidth = setWidth;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                var box = s_root.Q<VisualElement>("picked");
                var point = box.parent.worldBound.position + new Vector2(NarrowWidth + 20f, 10f);
                s_pickedName = box.panel.PickAll(point, null)?.name;
                return null;
            }), new object[] { width });
            return V.Div(className: "w-[300px] h-[100px]", name: "picked-parent", children: new VNode[]
            {
                V.Div(className: $"w-[{width}px] h-[20px]", name: "picked"),
            });
        }

        // A parent whose shown child sits below its box, and whose layout effect picks a point inside the child.
        [Component]
        private static VNode OutsideChildRender()
        {
            var (shown, setShown) = Hooks.UseState(false);
            s_setOutsideShown = setShown;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                var parent = s_root.Q<VisualElement>("outside-parent");
                var point = parent.worldBound.position + new Vector2(10f, 40f);
                s_pickedName = parent.panel.PickAll(point, null)?.name;
                return null;
            }), new object[] { shown });
            return V.Div(className: "w-[100px] h-[20px]", name: "outside-parent", children: new VNode[]
            {
                shown
                    ? V.Div(className: "absolute top-[30px] left-[0px] w-[20px] h-[20px]", name: "outside-child")
                    : null,
            });
        }

        // Counts the layout computations FiberLayoutReflow makes on element's panel from here on: the panel's entry is
        // handed a copy of the members it computes with, whose computation counts its calls.
        private static CountingMethod CountLayoutComputations(VisualElement element)
        {
            using (var scope = new ReconcilerScope())
            {
                FiberLayoutReflow.LayOutFor(scope.Reconciler.Context, element, null);
            }
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var panels = typeof(FiberLayoutReflow).GetField("s_panels", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null);
            var lookup = new object[] { element.panel, null };
            panels.GetType().GetMethod("TryGetValue")!.Invoke(panels, lookup);
            var panelLayout = lookup[1];
            var membersField = panelLayout.GetType().GetField("_members", instance)!;
            var members = membersField.GetValue(panelLayout);
            var counted = Activator.CreateInstance(members.GetType(), nonPublic: true);
            foreach (var field in members.GetType().GetFields(instance))
            {
                field.SetValue(counted, field.GetValue(members));
            }
            var calculate = members.GetType().GetField("Calculate", instance)!;
            var counter = new CountingMethod((MethodInfo)calculate.GetValue(members));
            calculate.SetValue(counted, counter);
            membersField.SetValue(panelLayout, counted);
            return counter;
        }

        // Passes each call on to the method it stands in for, counting them.
        private sealed class CountingMethod : MethodInfo
        {
            private readonly MethodInfo _method;

            internal CountingMethod(MethodInfo method) => _method = method;

            internal int Calls { get; private set; }

            public override object Invoke(
                object obj, BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture)
            {
                Calls++;
                return _method.Invoke(obj, invokeAttr, binder, parameters, culture);
            }

            public override ICustomAttributeProvider ReturnTypeCustomAttributes => _method.ReturnTypeCustomAttributes;
            public override MethodAttributes Attributes => _method.Attributes;
            public override RuntimeMethodHandle MethodHandle => _method.MethodHandle;
            public override Type DeclaringType => _method.DeclaringType;
            public override string Name => _method.Name;
            public override Type ReflectedType => _method.ReflectedType;
            public override MethodInfo GetBaseDefinition() => _method.GetBaseDefinition();
            public override ParameterInfo[] GetParameters() => _method.GetParameters();
            public override object[] GetCustomAttributes(bool inherit) => _method.GetCustomAttributes(inherit);

            public override MethodImplAttributes GetMethodImplementationFlags()
                => _method.GetMethodImplementationFlags();

            public override object[] GetCustomAttributes(Type attributeType, bool inherit)
                => _method.GetCustomAttributes(attributeType, inherit);

            public override bool IsDefined(Type attributeType, bool inherit)
                => _method.IsDefined(attributeType, inherit);
        }
    }
}
