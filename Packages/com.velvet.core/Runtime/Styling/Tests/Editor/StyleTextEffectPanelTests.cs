using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// End-to-end coverage for text-transform / text-decoration on a real reconcile: the effect cascades from
    /// the class-bearing element onto descendant text leaves (a TextNode has no class of its own), applies to an
    /// element's own Text prop, resets via normal-case, and re-applies when the text changes. Reading
    /// <c>Label.text</c> after mount/patch (no layout needed — the effect mutates the string). GWT, one assert.
    /// </summary>
    [TestFixture]
    internal sealed class StyleTextEffectPanelTests : PanelTestBase
    {
        private static StateUpdater<string> s_setText;
        private static StateUpdater<string> s_setInnerText;

        protected override Rect WindowSize => new Rect(0, 0, 400, 400);

        public override void SetUp()
        {
            s_setText = default;
            base.SetUp();
        }

        private Label MountAndFindLabel(VNode tree)
        {
            _mounted = V.Mount(_window.rootVisualElement, tree);
            return _window.rootVisualElement.Q<Label>();
        }

        [Test]
        public void Given_UppercaseOnParent_When_Mounted_Then_ChildTextLeafIsUppercased()
        {
            // The text leaf has no class; the transform cascades from the parent (CSS inheritance).
            var label = MountAndFindLabel(V.Div(className: "uppercase", V.Text("hello")));

            Assert.That(label.text, Is.EqualTo("HELLO"));
        }

        [Test]
        public void Given_LowercaseOnLabelItself_When_Mounted_Then_OwnTextIsLowercased()
        {
            var label = MountAndFindLabel(V.Label(className: "lowercase", text: "HELLO"));

            Assert.That(label.text, Is.EqualTo("hello"));
        }

        [Test]
        public void Given_UnderlineOnParent_When_Mounted_Then_ChildTextWrappedInUTag()
        {
            var label = MountAndFindLabel(V.Div(className: "underline", V.Text("hi")));

            Assert.That(label.text, Is.EqualTo("<u>hi</u>"));
        }

        [Test]
        public void Given_NormalCaseUnderUppercase_When_Mounted_Then_InnerResetsToRawText()
        {
            // normal-case on a nearer ancestor overrides an outer uppercase (explicit reset stops inheritance).
            var label = MountAndFindLabel(
                V.Div(className: "uppercase", V.Div(className: "normal-case", V.Text("hi"))));

            Assert.That(label.text, Is.EqualTo("hi"));
        }

        [Test]
        public void Given_UppercaseParent_When_TextChanges_Then_NewTextIsReTransformed()
        {
            // A text change must re-cascade the transform onto the leaf, from the new raw value.
            Mount(V.Component(RenderCard));
            Assume.That(_window.rootVisualElement.Q<Label>().text, Is.EqualTo("ONE"), "Precondition: initial transform");

            s_setText.Invoke("two");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            Assert.That(_window.rootVisualElement.Q<Label>().text, Is.EqualTo("TWO"));
        }

        [Test]
        public void Given_UppercaseAncestor_When_InnerComponentReRendersInIsolation_Then_LeafStaysUppercased()
        {
            // The text leaf lives in an inner component (its own state) under an uppercase ancestor that does NOT
            // re-render when the inner state changes. The leaf must still re-apply the inherited transform on its
            // isolated re-render (RED before PatchText resolves the cascade itself: would show "two").
            Mount(V.Div(className: "uppercase", V.Component(RenderInner)));
            Assume.That(_window.rootVisualElement.Q<Label>().text, Is.EqualTo("ONE"), "Precondition: initial cascade");

            s_setInnerText.Invoke("two");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            Assert.That(_window.rootVisualElement.Q<Label>().text, Is.EqualTo("TWO"));
        }

        private void Mount(VNode tree)
        {
            _mounted = V.Mount(_window.rootVisualElement, tree);
        }

        [Component]
        private static VNode RenderCard()
        {
            var (text, setText) = Hooks.UseState("one");
            s_setText = setText;
            return V.Div(className: "uppercase", V.Text(text));
        }

        [Component]
        private static VNode RenderInner()
        {
            var (text, setText) = Hooks.UseState("one");
            s_setInnerText = setText;
            return V.Text(text);
        }
    }
}
