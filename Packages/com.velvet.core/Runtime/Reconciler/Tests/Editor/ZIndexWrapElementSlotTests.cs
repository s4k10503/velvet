using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a z change which arrives with no patch of the element — a hover variant, a custom property —
    /// stacks the user <c>wrapElement</c> wrapper among its siblings, as it does the wrapper of a patched element.
    /// </summary>
    internal sealed class ZIndexWrapElementSlotTests : PanelTestBase
    {
        private static StateUpdater<string> s_setOuter;

        public override void SetUp()
        {
            s_setOuter = default;
            base.SetUp();
        }

        private static VisualElement Wrap(VisualElement inner)
        {
            var wrapper = new VisualElement { name = "wrapper" };
            wrapper.Add(inner);
            return wrapper;
        }

        [Component]
        private static VNode HoverHostRender() => V.Div(name: "parent", children: new VNode[]
        {
            V.Button(name: "inner", className: "hover:z-10", wrapElement: Wrap),
            V.Div(name: "sibling"),
        });

        [Component]
        private static VNode VariableHostRender()
        {
            var (outer, setOuter) = Hooks.UseState("");
            s_setOuter = setOuter;
            return V.Div(name: "parent", className: outer, children: new VNode[]
            {
                V.Button(name: "inner", className: "z-(--z)", wrapElement: Wrap),
                V.Div(name: "sibling", className: "z-10"),
            });
        }

        [Test]
        public void Given_AWrapElementButtonWithAHoverZClass_When_HoverStarts_Then_TheWrapperPaintsAfterItsSibling()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Component(HoverHostRender));
            var inner = _window.rootVisualElement.Q<VisualElement>("inner");

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                inner.SimulateEvent(over);
            }

            // Assert
            Assert.That(ZBandProbe.PaintOrder(_window.rootVisualElement.Q<VisualElement>("parent")), Is.EqualTo("sibling,wrapper"));
        }

        [Test]
        public void Given_AWrapElementButtonWhoseZReadsADeclaration_When_TheDeclarationArrives_Then_TheWrapperPaintsAfterItsSibling()
        {
            // Arrange — unstacked while nothing declares --z, so it paints ahead of the sibling declared after it
            _mounted = V.Mount(_window.rootVisualElement, V.Component(VariableHostRender));

            // Act
            s_setOuter.Invoke("[--z:20]");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            // Assert
            Assert.That(ZBandProbe.PaintOrder(_window.rootVisualElement.Q<VisualElement>("parent")), Is.EqualTo("sibling,wrapper"));
        }
    }
}
