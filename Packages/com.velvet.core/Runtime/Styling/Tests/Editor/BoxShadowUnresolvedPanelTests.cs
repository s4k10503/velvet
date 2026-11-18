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
        private const string ReaderSheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/BoxShadowReaderRule.uss";

        // The properties the cases read, declared on the element.
        private const string Declared = "[--x:0_0_5px_#ff0000] [--a:5px] [--b:#ff0000] [--w:3px] ";

        private VisualElement Mount(string classes, bool style = true)
        {
            _mounted?.Dispose();
            _window.rootVisualElement.Clear();
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "card", className: classes));
            var card = _window.rootVisualElement.Q<VisualElement>("card");
            if (style)
            {
                EditorPanelTestHelpers.ForcePanelUpdate(card.panel);
            }
            return card;
        }

        // The tick on which every reader is read again once its element is on a panel.
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

        [Test]
        public void Given_AReaderOfAStylesheetVariable_When_ASchedulerTickComesBeforeTheFirstStylePass_Then_ItIsReadAtThatTick()
        {
            // Arrange
            _window.rootVisualElement.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(RuleSheet));

            // Act — the tick that reads every reader on attach, ahead of any style pass and layout.
            var card = Mount("css-var-host ring ring-[length:var(--w)]", style: false);
            EditorPanelTestHelpers.DriveSchedulerOnce(card.panel);

            // Assert — --w is 25px on .css-var-host.
            Assert.That(Ring(card), Is.EqualTo(25f));
        }

        // The card is read where no stylesheet declares its properties, then put under a host whose stylesheet does, ahead of
        // that host's style pass on it: what it read before is what it would read again without the styles applied.
        private VisualElement Reparented()
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: new VNode?[]
            {
                V.Div(name: "bare", children: new VNode?[]
                {
                    V.Div(name: "card", className: "ring-[length:var(--w)] shadow-[var(--x)]"),
                }),
                V.Div(name: "declaring", className: "shadow-var-host"),
            }));
            var declaring = _window.rootVisualElement.Q<VisualElement>("declaring");
            declaring.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(ReaderSheet));
            var card = _window.rootVisualElement.Q<VisualElement>("card");
            EditorPanelTestHelpers.ForcePanelUpdate(card.panel);
            ReadReaders(card);
            declaring.Add(card);
            EditorPanelTestHelpers.DriveSchedulerOnce(card.panel);
            return card;
        }

        [Test]
        public void Given_AnElementWithTwoReadersOfStylesheetVariables_When_ItIsMovedUnderTheHostDeclaringThem_Then_ItsRingIsDrawn()
        {
            // Act
            var card = Reparented();

            // Assert
            Assert.That(Ring(card), Is.EqualTo(3f));
        }

        [Test]
        public void Given_AnElementWithTwoReadersOfStylesheetVariables_When_ItIsMovedUnderTheHostDeclaringThem_Then_ItsShadowIsDrawn()
        {
            // Act
            var card = Reparented();

            // Assert
            Assert.That(Shadow(card), Is.True);
        }

        [Test]
        public void Given_AReaderOfAnUndeclaredProperty_When_ItsElementIsReattached_Then_TheBoxShadowStaysDiscarded()
        {
            // Arrange — the box-shadow is discarded once the reader is read.
            var card = Mount("ring-2 shadow-lg shadow-[var(--x)]");
            ReadReaders(card);
            card.RemoveFromHierarchy();

            // Act
            _window.rootVisualElement.Add(card);
            EditorPanelTestHelpers.DriveSchedulerOnce(card.panel);

            // Assert
            Assert.That(Shadow(card), Is.False);
        }

        // GREEN_ON_BASE(characterization): the base keeps the ring and the shadow, as Tailwind orders shadow-lg after the reader.
        [Test]
        public void Given_ASizeTailwindEmitsAfterAnUnresolvedReader_When_BesideARing_Then_TheRingStays()
        {
            // Act
            var card = Mount("ring-2 shadow-lg shadow-[var(--x)]");
            ReadReaders(card);

            // Assert
            Assert.That(Ring(card), Is.EqualTo(2f));
        }

        [Test]
        public void Given_AHoverReaderOfAnUndeclaredProperty_When_Hovered_Then_TheRingGoesWithTheShadow()
        {
            // Arrange — the control gives the property a shadow.
            var control = Mount(Declared + "ring-2 shadow-lg hover:shadow-[var(--x)]");
            using (var over = PointerOverEvent.GetPooled())
            {
                control.SimulateEvent(over);
            }
            var controlRing = Ring(control);
            var card = Mount("ring-2 shadow-lg hover:shadow-[var(--x)]");

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                card.SimulateEvent(over);
            }

            // Assert
            Assert.That(controlRing == 2f ? Ring(card) : float.NaN, Is.EqualTo(0f));
        }
    }
}
