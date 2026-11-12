using System;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what the callback refs a commit attaches cost the panel's layout, and what they still read. Refs that each
    /// write a colour to their element compute the panel's layout once between them, whether a commit attaches them or
    /// the pass that queued them does, and once a commit has attached them the reflow holds none of the panels they
    /// computed for the end of their reads. The panel's next layout pass still sends what they laid out its
    /// <see cref="GeometryChangedEvent"/>, and a ref or a layout effect after a ref that widened an element reads the
    /// widened box.
    /// </summary>
    [TestFixture]
    internal sealed class RefAttachLayoutCostTests : PanelTestBase
    {
        private const int Refs = 8;
        private const float MountWidth = 120f;
        private const float WidenedWidth = 200f;

        private static VisualElement s_root;
        private static float s_widthRead;
        private static int s_geometryEventsReceived;

        public override void SetUp()
        {
            base.SetUp();
            s_root = _window.rootVisualElement;
            s_widthRead = float.NaN;
            s_geometryEventsReceived = 0;
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
        public void Given_CallbackRefsThatComputedThePanelsLayout_When_TheCommitEnds_Then_TheReflowHoldsNoPanel()
        {
            // Arrange
            var root = _window.rootVisualElement;

            // Act
            _mounted = V.Mount(root, V.Div(children: ColouringDivs()));

            // Assert
            Assert.That(PanelsHeldForTheReads(), Is.Zero);
        }

        // GREEN_ON_BASE(characterization): the base leaves the tree dirty after each computation a ref makes.
        // This pins that a pass no commit follows still leaves it dirty once its refs are attached; the cut it answers
        // is dropping `FiberLayoutReflow.EndReads()` from `ReconcilerContext.DrainRefAttaches`.
        [Test]
        public void Given_APassNoCommitFollowsWhoseRefLaidThePanelOut_When_ThePanelUpdates_Then_TheElementReceivesItsGeometryChangedEvent()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            _window.rootVisualElement.Add(scope.Root);
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(),
                new VNode[] { V.Div(className: $"w-[{MountWidth}px] h-[20px]", refCallback: s_watchGeometry) });

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(s_geometryEventsReceived, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base computes the layout again before a ref behind a write.
        // This pins that refs sharing one computation still compute again behind a write that moves a box; the cut
        // it answers is a return from `PanelLayout.EnsureLaidOut` once `s_computedDuringReads` holds the panel.
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
        // This pins that a layout effect after its element's ref reads what the ref wrote; the cut it answers is a
        // return from `PanelLayout.EnsureLaidOut` once `s_computedDuringReads` holds the panel.
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

        private static VNode[] ColouringDivs()
        {
            var divs = new VNode[Refs];
            for (var i = 0; i < Refs; i++) divs[i] = V.Div(className: "w-[20px] h-[20px]", refCallback: s_colour);
            return divs;
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

        private static readonly Func<VisualElement, Action> s_watchGeometry = element =>
        {
            element.RegisterCallback<GeometryChangedEvent>(_ => s_geometryEventsReceived++);
            return null;
        };

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

        // How many panels FiberLayoutReflow holds to leave dirty when the reads open now end, or int.MaxValue where it
        // keeps no such list, which the expected count cannot equal.
        private static int PanelsHeldForTheReads()
            => typeof(FiberLayoutReflow).GetField("s_computedDuringReads", BindingFlags.Static | BindingFlags.NonPublic)
                ?.GetValue(null) is System.Collections.ICollection held ? held.Count : int.MaxValue;

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
