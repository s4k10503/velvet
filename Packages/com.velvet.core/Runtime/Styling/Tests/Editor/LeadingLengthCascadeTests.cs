using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// An em or percentage <c>leading-[…]</c> is a length CSS computes on the element that declares it,
    /// from that element's computed font size, and the text under it inherits the length; a unitless one
    /// is a number each text multiplies by its own size. The expected values are the CSS ones. The bundled
    /// sheet is attached and a panel update runs before each read, since the declaring element's size is
    /// what UI Toolkit resolves. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class LeadingLengthCascadeTests : PanelTestBase
    {
        private const string StyleSheetPath = "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";

        private static StateUpdater<string> s_setOwnerClass;
        private static StateUpdater<string> s_setAncestorClass;

        protected override Rect WindowSize => new Rect(0, 0, 600, 600);

        protected override void LoadStyleSheets()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            _window.rootVisualElement.styleSheets.Add(sheet);
        }

        public override void SetUp()
        {
            s_setOwnerClass = default;
            s_setAncestorClass = default;
            base.SetUp();
        }

        private string MountAndReadText(VNode tree)
        {
            _mounted = V.Mount(_window.rootVisualElement, tree);
            ForcePanelUpdate(_window.rootVisualElement.panel);
            return _window.rootVisualElement.Q<Label>().text;
        }

        private string UpdateAndReadText()
        {
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();
            ForcePanelUpdate(_window.rootVisualElement.panel);
            return _window.rootVisualElement.Q<Label>().text;
        }

        [Test]
        public void Given_APercentLeadingOnAnInlineSizedElement_When_ALargerLabelIsUnderIt_Then_TheLabelGetsTheElementsLength()
        {
            // Act
            var text = MountAndReadText(
                V.Div(className: "text-[20px] leading-[150%]", V.Label(className: "text-[40px]", text: "hi")));

            // Assert
            Assert.That(text, Is.EqualTo("<line-height=30px>hi</line-height>"));
        }

        [Test]
        public void Given_APercentLeadingOnAnElementInheritingItsSize_When_ALargerLabelIsUnderIt_Then_TheLabelGetsTheElementsLength()
        {
            // Act
            var text = MountAndReadText(
                V.Div(className: "text-[20px]",
                    V.Div(className: "leading-[150%]", V.Label(className: "text-[40px]", text: "hi"))));

            // Assert
            Assert.That(text, Is.EqualTo("<line-height=30px>hi</line-height>"));
        }

        [Test]
        public void Given_AnEmLeadingOnAScaleClassSizedElement_When_ALargerLabelIsUnderIt_Then_TheLabelGetsTheElementsLength()
        {
            // Act — text-sm is var(--text-sm), 14px in _tokens.uss.
            var text = MountAndReadText(
                V.Div(className: "text-sm leading-[1.5em]", V.Label(className: "text-[40px]", text: "hi")));

            // Assert
            Assert.That(text, Is.EqualTo("<line-height=21px>hi</line-height>"));
        }

        [Test]
        public void Given_AnEmLeadingOverTextAtTheSameSize_When_Mounted_Then_TheTextGetsTheElementsLength()
        {
            // Act
            var text = MountAndReadText(V.Div(className: "text-[20px] leading-[1.5em]", V.Text("hi")));

            // Assert
            Assert.That(text, Is.EqualTo("<line-height=30px>hi</line-height>"));
        }

        [Test]
        public void Given_AnEmLeadingOnALabelItself_When_Mounted_Then_ItsOwnTextGetsItsLength()
        {
            // Act
            var text = MountAndReadText(V.Label(className: "text-[20px] leading-[150%]", text: "hi"));

            // Assert
            Assert.That(text, Is.EqualTo("<line-height=30px>hi</line-height>"));
        }

        [Test]
        public void Given_AUnitlessLeadingOverALargerLabel_When_Mounted_Then_TheLabelGetsTheFactor()
        {
            // Act
            var text = MountAndReadText(
                V.Div(className: "text-[20px] leading-[1.5]", V.Label(className: "text-[40px]", text: "hi")));

            // Assert
            Assert.That(text, Is.EqualTo("<line-height=1.5em>hi</line-height>"));
        }

        // The declaring element is given a fixed box in both toggle cases, so its geometry does not move
        // when its font size does.
        [Test]
        public void Given_AMountedPercentLeading_When_TheDeclaringElementsSizeChanges_Then_TheLabelGetsTheNewLength()
        {
            // Arrange
            MountAndReadText(V.Component(RenderOwnerToggle));

            // Act
            s_setOwnerClass.Invoke("w-[300px] h-[200px] text-[30px] leading-[150%]");
            var text = UpdateAndReadText();

            // Assert
            Assert.That(text, Is.EqualTo("<line-height=45px>hi</line-height>"));
        }

        [Test]
        public void Given_AMountedPercentLeading_When_AnAncestorsSizeChanges_Then_TheLabelGetsTheNewLength()
        {
            // Arrange
            MountAndReadText(V.Component(RenderAncestorToggle));

            // Act
            s_setAncestorClass.Invoke("text-[30px]");
            var text = UpdateAndReadText();

            // Assert
            Assert.That(text, Is.EqualTo("<line-height=45px>hi</line-height>"));
        }

        [Component]
        private static VNode RenderOwnerToggle()
        {
            var (cls, setCls) = Hooks.UseState("w-[300px] h-[200px] text-[20px] leading-[150%]");
            s_setOwnerClass = setCls;
            return V.Div(className: cls, V.Label(className: "text-[40px]", text: "hi"));
        }

        [Component]
        private static VNode RenderAncestorToggle()
        {
            var (cls, setCls) = Hooks.UseState("text-[20px]");
            s_setAncestorClass = setCls;
            return V.Div(className: cls,
                V.Div(className: "w-[300px] h-[200px] leading-[150%]", V.Label(className: "text-[40px]", text: "hi")));
        }
    }

    /// <summary>
    /// Without the bundled sheet no element holds the custom property CustomStyleResolvedEvent needs, so
    /// the probe reads the declaring element's size from its first layout instead.
    /// </summary>
    [TestFixture]
    internal sealed class LeadingLengthProbeWithoutSheetTests : PanelTestBase
    {
        protected override Rect WindowSize => new Rect(0, 0, 600, 600);

        [Test]
        public void Given_NoBundledSheet_When_APercentLeadingIsLaidOut_Then_TheLabelGetsTheElementsLength()
        {
            // Act
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(className: "text-[20px]",
                    V.Div(className: "leading-[150%]", V.Label(className: "text-[40px]", text: "hi"))));
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(_window.rootVisualElement.Q<Label>().text, Is.EqualTo("<line-height=30px>hi</line-height>"));
        }
    }
}
