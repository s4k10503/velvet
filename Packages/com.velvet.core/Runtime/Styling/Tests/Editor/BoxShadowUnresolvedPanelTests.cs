using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// What an element on a panel paints of the box-shadow Tailwind composes where a shadow, ring or inset ring reads a
    /// custom property that resolves to nothing, and when it first paints it. Every expected outcome is Chromium's
    /// computed box-shadow for the same classes under Tailwind v4.3.3: none where the declaration is invalid. GWT, one
    /// assert per case.
    /// </summary>
    internal sealed class BoxShadowUnresolvedPanelTests : PanelTestBase
    {
        private const string RuleSheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/CustomPropertyRule.uss";

        // The properties the cases read, declared on the element.
        private const string Declared = "[--x:0_0_5px_#ff0000] [--a:5px] [--b:#ff0000] [--w:3px] ";

        private VisualElement Mount(string classes)
        {
            _mounted?.Dispose();
            _window.rootVisualElement.Clear();
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "card", className: classes));
            var card = _window.rootVisualElement.Q<VisualElement>("card");
            EditorPanelTestHelpers.ForcePanelUpdate(card.panel);
            return card;
        }

        // The scheduler tick on which every reader is read again once its element is on a panel.
        private static void ReadReaders(VisualElement card) => EditorPanelTestHelpers.DriveSchedulerOnce(card.panel);

        private float Ring(VisualElement card)
        {
            var binding = RingOverlay.TryGet(card);
            return binding == null ? 0f : binding.Spec.RingSpread;
        }

        private bool Shadow(VisualElement card) => _mounted.Root.Reconciler.Context.ShadowBindings.ContainsKey(card);

        // The ring is drawn where the properties are declared, which is the control: where it is not, the value is one
        // the expectation cannot equal.
        [TestCase("ring-2 shadow-[var(--x)]")]
        [TestCase("ring-2 shadow-(--x)")]
        [TestCase("ring-2 inset-shadow-[0_0_var(--a)_var(--b)]")]
        [TestCase("ring-2 shadow-2xl/150 shadow-[var(--x)] shadow-red-500")]
        [TestCase("ring-2 shadow-2xs shadow-[var(--x)]")]
        [TestCase("ring-2 shadow-[var(--x)]/50")]
        [TestCase("ring-2 !shadow-[var(--x)]")]
        public void Given_ABoxShadowLayerReadingAnUndeclaredProperty_When_BesideARing_Then_TheRingGoesWithIt(string classes)
        {
            // Act
            var control = Ring(Mount(Declared + classes));
            var card = Mount(classes);
            ReadReaders(card);
            var ring = control == 2f ? Ring(card) : float.NaN;

            // Assert
            Assert.That(ring, Is.EqualTo(0f));
        }

        // A size's /N holds the alpha of the layers' colours, so a var() there that resolves to nothing leaves the whole
        // box-shadow invalid; the control gives it a percentage.
        [TestCase("ring-2 shadow-lg/[var(--a)]")]
        [TestCase("ring-2 shadow-lg/(--a)")]
        [TestCase("ring-2 inset-shadow-sm/[var(--a)]")]
        [TestCase("ring-2 shadow/[var(--a)]")]
        public void Given_ASizeWhoseModifierReadsAnUndeclaredProperty_When_BesideARing_Then_TheRingGoesWithIt(string classes)
        {
            // Act
            var control = Ring(Mount("[--a:50%] " + classes));
            var card = Mount(classes);
            ReadReaders(card);
            var ring = control == 2f ? Ring(card) : float.NaN;

            // Assert
            Assert.That(ring, Is.EqualTo(0f));
        }

        [TestCase("shadow-lg ring-[length:var(--w)]")]
        [TestCase("shadow-lg ring-(length:--w)")]
        [TestCase("shadow-lg inset-ring-[length:var(--w)] inset-ring-red-500")]
        public void Given_AnUndeclaredPropertyInAWidth_When_ABoxShadowIsBeside_Then_NoShadowIsDrawn(string classes)
        {
            // Act
            var control = Shadow(Mount(Declared + classes));
            var card = Mount(classes);
            ReadReaders(card);
            var drawn = control ? Shadow(card) : true;

            // Assert
            Assert.That(drawn, Is.False);
        }

        [Test]
        public void Given_AReaderOfAStylesheetVariable_When_MountedBeforeItsFirstRead_Then_TheRingIsStillDrawn()
        {
            // Arrange — the sheet declares --w on .css-var-host, which no reader can see until the element is on a panel
            // and has had its style pass.
            _window.rootVisualElement.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(RuleSheet));

            // Act
            var card = Mount("css-var-host ring ring-[length:var(--w)]");

            // Assert — a ring written beside it, which the reader's unset would take with it where it was written ahead
            // of that read.
            Assert.That(Ring(card), Is.EqualTo(1f));
        }
    }
}
