using System.Reflection;
using NUnit.Framework;
using UnityEditor.UIElements.TestFramework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how a bracketed length only an element can measure lands on a simulated panel: an em against the
    /// element's computed font size (the parent's, for font-size), a viewport unit against the panel, and a math
    /// function mixing a percentage with a pixel length against the box the native percentage of the same
    /// longhand is taken of — compared with that native percentage on a sibling, so the reference box is the
    /// engine's own — each re-resolved once what it measures changes.
    /// </summary>
    internal sealed class RelativeLengthPanelTests
    {
        private readonly record struct ClassNameState(string ClassName);

        private sealed class ClassNameStore : Store<ClassNameState>
        {
            public ClassNameStore(string initial) : base(new ClassNameState(initial)) { }
            public void Set(string className) => SetState(_ => new ClassNameState(className));
            protected override void ResetCore() => SetState(_ => new ClassNameState(string.Empty));
        }

        private static ClassNameStore s_parentClass;
        private static string s_nativeClass;
        private static string s_childClass;
        private static bool s_showChild;
        private static string s_frameClass;

        private EditorPanelSimulator _sim;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(400, 300) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            s_parentClass = null;
            s_nativeClass = "h-[10px] w-[10px]";
            s_childClass = null;
            s_showChild = true;
            s_frameClass = "w-[600px] h-[600px]";
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _sim?.Dispose();
            _sim = null;
            s_parentClass?.Dispose();
            s_parentClass = null;
        }

        // The frame keeps the parent off the panel root, whose size the panel decides.
        [Component]
        private static VNode Host()
            => V.Div(name: "frame", className: s_frameClass, children: new VNode[]
            {
                V.Div(name: "parent", className: Hooks.UseStore(s_parentClass, s => s.ClassName), children: new VNode[]
                {
                    V.Div(name: "native", className: s_nativeClass),
                    s_showChild ? V.Div(name: "child", className: s_childClass) : null,
                }),
            });

        private VisualElement Child => _sim.rootVisualElement.Q<VisualElement>("child");

        private VisualElement Native => _sim.rootVisualElement.Q<VisualElement>("native");

        private float ViewportWidth => _sim.rootVisualElement.panel.visualTree.layout.width;

        private float ViewportHeight => _sim.rootVisualElement.panel.visualTree.layout.height;

        // Enough frames for the poll to measure a laid-out tree and for what it wrote to be laid out in turn.
        private void Settle()
        {
            _mounted?.FlushStateForTest();
            for (var i = 0; i < 3; i++)
            {
                _sim.FrameUpdateMs(16);
            }
        }

        private void Mount(string parentClass, string childClass)
        {
            s_parentClass = new ClassNameStore(parentClass);
            s_childClass = childClass;
            _mounted = V.Mount(_sim.rootVisualElement, V.Component(Host, key: "host"));
            Settle();
        }

        private void MountBesideNative(string parentClass, string nativeClass, string childClass)
        {
            s_nativeClass = nativeClass;
            Mount(parentClass, childClass);
        }

        // The element's re-measuring poll, or a non-null stand-in where the layer state cannot be read.
        private static object RelativePollOf(VisualElement element)
        {
            var layers = typeof(StyleArbitraryValueResolver)
                .GetField("s_layers", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            var args = new object[] { element, null };
            var found = layers?.GetType().GetMethod("TryGetValue")?.Invoke(layers, args) as bool?;
            var field = found == true ? args[1].GetType().GetField("RelativePoll") : null;
            return field == null ? "unreadable" : field.GetValue(args[1]);
        }

        // Whether the poll's scheduled item still runs, or null where it cannot be read.
        private static bool? IsTicking(object poll)
            => (poll?.GetType().GetField("_item", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(poll)
                as IVisualElementScheduledItem)?.isActive;

        // Renders the host again, so it reads the statics again, without changing the parent's classes.
        private void Rerender()
        {
            s_parentClass.Set(s_parentClass.Current.ClassName + " ");
            Settle();
        }

        [Test]
        public void Given_AnEmWidthUnderATwelvePixelFont_When_Settled_Then_ItIsTwentyFourPixelsWide()
        {
            // Act
            Mount("text-[12px] w-[300px] h-[200px]", "w-[2em] h-[10px]");

            // Assert
            Assert.That(Child.layout.width, Is.EqualTo(24f).Within(0.01f));
        }

        [Test]
        public void Given_AnEmWidthAsTheOnlyArbitraryValue_When_Settled_Then_ItIsTwentyFourPixelsWide()
        {
            // Act
            Mount("text-[12px] w-[300px] h-[200px]", "w-[2em]");

            // Assert
            Assert.That(Child.layout.width, Is.EqualTo(24f).Within(0.01f));
        }

        [Test]
        public void Given_AnEmWidth_When_TheInheritedFontGrows_Then_ItIsRemeasured()
        {
            // Arrange
            Mount("text-[12px] w-[300px] h-[200px]", "w-[2em] h-[10px]");

            // Act
            s_parentClass.Set("text-[20px] w-[300px] h-[200px]");
            Settle();

            // Assert
            Assert.That(Child.layout.width, Is.EqualTo(40f).Within(0.01f));
        }

        [Test]
        public void Given_AnEmWidth_When_APixelWidthReplacesIt_Then_ThePollItHeldIsReleased()
        {
            // Arrange
            Mount("text-[12px] w-[300px] h-[200px]", "w-[2em] h-[10px]");
            var held = RelativePollOf(Child);
            s_childClass = "w-[30px] h-[10px]";

            // Act
            s_parentClass.Set("text-[12px] w-[301px] h-[200px]");
            Settle();

            // Assert
            Assert.That((IsTicking(held), RelativePollOf(Child) == null), Is.EqualTo(((bool?)false, true)));
        }

        [Test]
        public void Given_AnEmFontSize_When_Settled_Then_ItIsTakenOfTheParentsFontSize()
        {
            // Act
            Mount("text-[10px] w-[300px] h-[200px]", "text-[2em] w-[10px] h-[10px]");

            // Assert
            Assert.That(Child.resolvedStyle.fontSize, Is.EqualTo(20f).Within(0.01f));
        }

        [Test]
        public void Given_AHalfViewportWidth_When_Settled_Then_ItIsHalfThePanelsWidth()
        {
            // Act
            Mount("w-[500px] h-[200px]", "w-[50vw] h-[10px]");

            // Assert — NaN unless the panel has a width to take half of.
            var expected = ViewportWidth > 0f ? ViewportWidth / 2f : float.NaN;
            Assert.That(Child.layout.width, Is.EqualTo(expected).Within(0.01f));
        }

        [Test]
        public void Given_AHalfViewportWidth_When_ThePanelResizes_Then_ItIsRemeasured()
        {
            // Arrange
            Mount("w-[500px] h-[200px]", "w-[50vw] h-[10px]");
            var before = ViewportWidth;

            // Act
            _sim.panelSize = new Vector2(200, 300);
            Settle();

            // Assert — NaN unless the panel's width really moved.
            var expected = ViewportWidth > 0f && ViewportWidth != before ? ViewportWidth / 2f : float.NaN;
            Assert.That(Child.layout.width, Is.EqualTo(expected).Within(0.01f));
        }

        [Test]
        public void Given_AHalfViewportMinHeight_When_Settled_Then_ItIsHalfThePanelsShorterSide()
        {
            // Act
            Mount("w-[500px] h-[500px]", "w-[10px] h-[50vmin]");

            // Assert — NaN unless the panel has two sides to take the shorter of.
            var shorter = Mathf.Min(ViewportWidth, ViewportHeight);
            var expected = shorter > 0f ? shorter / 2f : float.NaN;
            Assert.That(Child.layout.height, Is.EqualTo(expected).Within(0.01f));
        }

        [Test]
        public void Given_APercentagePlusPixelsWidth_When_Settled_Then_ItIsTheNativePercentagePlusThosePixels()
        {
            // Act
            MountBesideNative("w-[300px] h-[100px] p-[20px]", "w-[50%] h-[10px]", "w-[calc(50%+10px)] h-[10px]");

            // Assert — NaN unless the native percentage measured something.
            var expected = Native.layout.width > 0f ? Native.layout.width + 10f : float.NaN;
            Assert.That(Child.layout.width, Is.EqualTo(expected).Within(0.01f));
        }

        [Test]
        public void Given_APercentagePlusPixelsTopPadding_When_Settled_Then_ItIsTheNativePercentagePlusThosePixels()
        {
            // Act
            MountBesideNative("w-[300px] h-[100px]", "pt-[10%] w-[10px]", "pt-[calc(10%+1px)] w-[10px]");

            // Assert — NaN unless the native percentage measured something.
            var native = Native.resolvedStyle.paddingTop;
            var expected = native > 0f ? native + 1f : float.NaN;
            Assert.That(Child.resolvedStyle.paddingTop, Is.EqualTo(expected).Within(0.01f));
        }

        [Test]
        public void Given_APercentagePlusPixelsHeight_When_Settled_Then_ItIsTheNativePercentagePlusThosePixels()
        {
            // Act
            MountBesideNative("w-[300px] h-[100px]", "h-[25%] w-[10px]", "h-[calc(25%+1px)] w-[10px]");

            // Assert — NaN unless the native percentage measured something.
            var expected = Native.layout.height > 0f ? Native.layout.height + 1f : float.NaN;
            Assert.That(Child.layout.height, Is.EqualTo(expected).Within(0.01f));
        }

        [Test]
        public void Given_APercentagePlusPixelsBasisInAColumn_When_Settled_Then_ItIsTheNativePercentagePlusThosePixels()
        {
            // Act
            MountBesideNative("w-[300px] h-[400px]", "basis-[25%] w-[10px]", "basis-[calc(25%+1px)] w-[10px]");

            // Assert — NaN unless the native percentage measured something.
            var expected = Native.layout.height > 0f ? Native.layout.height + 1f : float.NaN;
            Assert.That(Child.layout.height, Is.EqualTo(expected).Within(0.01f));
        }

        [TestCase("absolute h-[10px] w-[50%]", "absolute h-[10px] w-[calc(50%+0.01px)]")]
        [TestCase("absolute w-[10px] h-[50%]", "absolute w-[10px] h-[calc(50%+0.01px)]")]
        public void Given_AnAbsoluteExpressionBesideTheSamePercentage_When_Settled_Then_TheyAgree(string native, string child)
        {
            // Arrange — the bundled sheet carries the absolute class.
            VelvetStyleUtilities.AttachTo(_sim.rootVisualElement);

            // Act
            MountBesideNative("w-[300px] h-[100px] p-[20px] border-[5px]", native, child);

            // Assert — NaN unless the native percentage measured something.
            var measured = Native.layout.width + Native.layout.height > 20f;
            Assert.That(new[] { Child.layout.width, Child.layout.height },
                Is.EqualTo(measured ? new[] { Native.layout.width, Native.layout.height } : new[] { float.NaN, float.NaN })
                    .Within(0.011f));
        }

        [Test]
        public void Given_AnAbsoluteExpression_When_TheParentTradesPaddingForBorder_Then_ItStillAgreesWithTheSamePercentage()
        {
            // Arrange — the bundled sheet carries the absolute class.
            VelvetStyleUtilities.AttachTo(_sim.rootVisualElement);
            MountBesideNative("w-[300px] h-[100px] p-[20px] border-[5px]", "absolute h-[10px] w-[50%]",
                "absolute h-[10px] w-[calc(50%+0.01px)]");

            // Act
            s_parentClass.Set("w-[300px] h-[100px] p-[15px] border-[10px]");
            Settle();

            // Assert — NaN unless the native percentage measured something.
            var expected = Native.layout.width > 0f ? Native.layout.width : float.NaN;
            Assert.That(Child.layout.width, Is.EqualTo(expected).Within(0.011f));
        }

        // GREEN_ON_BASE(characterization): the base never read the expression, so it behaved as the same percentage.
        [Test]
        public void Given_APercentagePlusPixelsHeightInAParentSizedByItsContent_When_SettledAgain_Then_ItBehavesAsTheSamePercentage()
        {
            // Arrange
            MountBesideNative("w-[300px]", "w-[10px] h-[100%]", "w-[10px] h-[calc(100%+1px)]");

            // Act
            Settle();
            Settle();

            // Assert
            Assert.That(Child.layout.height, Is.EqualTo(Native.layout.height).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base never read the expression, so it behaved as the same percentage.
        [Test]
        public void Given_APercentagePlusPixelsWidthInAParentSizedByItsContent_When_SettledAgain_Then_ItBehavesAsTheSamePercentage()
        {
            // Arrange — the bundled sheet carries the absolute class, which sizes the parent by its content.
            VelvetStyleUtilities.AttachTo(_sim.rootVisualElement);
            MountBesideNative("absolute h-[100px]", "h-[10px] w-[100%]", "h-[10px] w-[calc(100%+1px)]");

            // Act
            Settle();
            Settle();

            // Assert
            Assert.That(Child.layout.width, Is.EqualTo(Native.layout.width).Within(0.01f));
        }

        [Test]
        public void Given_AnIndefinitePercentageHeight_When_TheElementBecomesAbsolute_Then_ItIsTakenOfTheParent()
        {
            // Arrange — the bundled sheet carries the absolute class.
            VelvetStyleUtilities.AttachTo(_sim.rootVisualElement);
            Mount("w-[300px] min-h-[100px]", "w-[10px] h-[calc(50%+1px)]");
            s_childClass = "absolute w-[10px] h-[calc(50%+1px)]";

            // Act
            Rerender();

            // Assert — NaN unless the parent laid out a height.
            var parent = _sim.rootVisualElement.Q<VisualElement>("parent");
            var expected = parent.contentRect.height > 0f ? (parent.contentRect.height / 2f) + 1f : float.NaN;
            Assert.That(Child.layout.height, Is.EqualTo(expected).Within(0.01f));
        }

        [Test]
        public void Given_APercentagePlusPixelsTopPaddingOnAClippedElement_When_Settled_Then_ItIsTakenOfTheRealParent()
        {
            // Act
            MountBesideNative("w-[300px] h-[100px]", "pt-[10%] w-[10px]", "clip-path-[inset(0)] w-[100px] pt-[calc(10%+1px)]");

            // Assert — NaN unless the native percentage measured something.
            var native = Native.resolvedStyle.paddingTop;
            var expected = native > 0f ? native + 1f : float.NaN;
            Assert.That(Child.resolvedStyle.paddingTop, Is.EqualTo(expected).Within(0.01f));
        }

        [Test]
        public void Given_AnEmWidth_When_TheElementsOwnFontGrows_Then_ItIsRemeasured()
        {
            // Arrange
            Mount("w-[300px] h-[200px]", "text-[12px] w-[2em] h-[10px]");
            s_childClass = "text-[20px] w-[2em] h-[10px]";

            // Act
            Rerender();

            // Assert
            Assert.That(Child.layout.width, Is.EqualTo(40f).Within(0.01f));
        }

        [Test]
        public void Given_AViewportWidthUnderATransition_When_Mounted_Then_ItLandsWithoutAnimating()
        {
            // Arrange — the bundled sheet carries the transition class.
            VelvetStyleUtilities.AttachTo(_sim.rootVisualElement);

            // Act
            Mount("w-[500px] h-[200px]", "transition-all duration-[1000ms] w-[50vw] h-[10px]");

            // Assert — NaN unless the panel has a width to take half of.
            var expected = ViewportWidth > 0f ? ViewportWidth / 2f : float.NaN;
            Assert.That(Child.layout.width, Is.EqualTo(expected).Within(0.01f));
        }

        [Test]
        public void Given_AViewportWidthUnderATransition_When_ThePanelResizes_Then_ItLandsWithoutAnimating()
        {
            // Arrange — the bundled sheet carries the transition class.
            VelvetStyleUtilities.AttachTo(_sim.rootVisualElement);
            Mount("w-[500px] h-[200px]", "transition-all duration-[1000ms] w-[50vw] h-[10px]");
            var before = ViewportWidth;

            // Act
            _sim.panelSize = new Vector2(200, 300);
            Settle();

            // Assert — NaN unless the panel's width really moved.
            var expected = ViewportWidth > 0f && ViewportWidth != before ? ViewportWidth / 2f : float.NaN;
            Assert.That(Child.layout.width, Is.EqualTo(expected).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base wrote nothing for a class it did not parse.
        [Test]
        public void Given_AViewportWidth_When_MountedBeforeAnyFrame_Then_NoZeroOrKeywordIsWrittenInline()
        {
            // Arrange
            s_parentClass = new ClassNameStore("w-[500px] h-[200px]");
            s_childClass = "w-[50vw] h-[10px]";

            // Act
            _mounted = V.Mount(_sim.rootVisualElement, V.Component(Host, key: "host"));

            // Assert
            var width = Child.style.width;
            Assert.That(width.keyword is StyleKeyword.Null || (width.keyword is StyleKeyword.Undefined && width.value.value > 0f),
                Is.True);
        }

        // GREEN_ON_BASE(characterization): the base never read the expression, so it behaved as the same percentage.
        [Test]
        public void Given_APercentagePlusPixelsHeight_When_TheParentLosesItsHeight_Then_ItBehavesAsTheSamePercentage()
        {
            // Arrange
            MountBesideNative("w-[300px] h-[100px]", "w-[10px] h-[50%]", "w-[10px] h-[calc(50%+1px)]");

            // Act
            s_parentClass.Set("w-[300px]");
            Settle();
            Settle();

            // Assert
            Assert.That(Child.layout.height, Is.EqualTo(Native.layout.height).Within(0.01f));
        }

        [TestCase("", "h-[100px]", "h-[10px] w-[50%]", "h-[10px] w-[calc(50%+1px)]", false)]
        [TestCase("w-[600px]", "w-[300px] h-[100px]", "w-[10px] h-[25%]", "w-[10px] h-[calc(25%+1px)]", true)]
        [TestCase("w-[600px] h-[600px] shrink-[0]", "w-[300px] h-[50%]", "w-[10px] h-[25%]", "w-[10px] h-[calc(25%+1px)]", true)]
        [TestCase("w-[600px] h-[600px]", "w-[300px] h-24", "w-[10px] h-[25%]", "w-[10px] h-[calc(25%+1px)]", true)]
        [TestCase("w-[600px] h-[600px]", "absolute inset-0", "w-[10px] h-[25%]", "w-[10px] h-[calc(25%+1px)]", true)]
        [TestCase("w-[600px] h-[600px]", "absolute inset-0", "h-[10px] w-[25%]", "h-[10px] w-[calc(25%+1px)]", false)]
        [TestCase("w-[600px] min-h-[200px]", "absolute h-full w-[300px]", "w-[10px] h-[25%]", "w-[10px] h-[calc(25%+1px)]", true)]
        [TestCase("flex-row flex-wrap w-[600px] h-[600px] shrink-[0]", "w-[300px]", "w-[10px] h-[25%]", "w-[10px] h-[calc(25%+1px)]", true)]
        public void Given_AParentWhoseSizeIsDefinite_When_Settled_Then_ThePercentageIsTakenOfIt(
            string frame, string parent, string native, string child, bool vertical)
        {
            // Arrange — the bundled sheet carries h-24.
            VelvetStyleUtilities.AttachTo(_sim.rootVisualElement);
            s_frameClass = frame;

            // Act
            MountBesideNative(parent, native, child);

            // Assert — NaN unless the native percentage measured something.
            var measured = vertical ? Native.layout.height : Native.layout.width;
            Assert.That(vertical ? Child.layout.height : Child.layout.width,
                Is.EqualTo(measured > 0f ? measured + 1f : float.NaN).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base never read the expression, so it behaved as the same percentage.
        [Test]
        public void Given_AParentGrownAlongAColumnOfNoHeight_When_SettledAgain_Then_ItsPercentageBehavesAsTheSamePercentage()
        {
            // Arrange
            s_frameClass = "w-[600px]";
            MountBesideNative("w-[300px] grow-[1]", "w-[10px] h-[50%]", "w-[10px] h-[calc(50%+1px)]");

            // Act
            Settle();
            Settle();

            // Assert
            Assert.That(Child.layout.height, Is.EqualTo(Native.layout.height).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base never read the expression, so it behaved as the same percentage.
        [TestCase("w-[600px]", "w-[300px] h-full")]
        [TestCase("w-[600px] h-[600px]", "w-[300px] h-auto")]
        public void Given_AParentWhoseHeightIsIndefinite_When_SettledAgain_Then_APercentageInItBehavesAsTheSamePercentage(
            string frame, string parent)
        {
            // Arrange — the bundled sheet carries the classes the frame and the parent are sized with.
            VelvetStyleUtilities.AttachTo(_sim.rootVisualElement);
            s_frameClass = frame;
            MountBesideNative(parent, "w-[10px] h-[50%]", "w-[10px] h-[calc(50%+1px)]");

            // Act
            Settle();
            Settle();

            // Assert
            Assert.That(Child.layout.height, Is.EqualTo(Native.layout.height).Within(0.01f));
        }

        [TestCase("absolute h-[100px]", "absolute h-[100px] w-[40px]", "h-[10px] w-[40px]", "h-[10px] w-[calc(50%+1px)]", false)]
        [TestCase("absolute w-[100px]", "absolute w-[100px] h-[40px]", "w-[10px] h-[40px] shrink-[0]", "w-[10px] h-[calc(50%+1px)] shrink-[0]", true)]
        public void Given_AParentSizedByItsContent_When_ItDeclaresTheSizeItHad_Then_ThePercentageIsTakenOfIt(
            string before, string after, string native, string child, bool vertical)
        {
            // Arrange — the bundled sheet carries the absolute class.
            VelvetStyleUtilities.AttachTo(_sim.rootVisualElement);
            MountBesideNative(before, native, child);

            // Act
            s_parentClass.Set(after);
            Settle();

            // Assert
            Assert.That(vertical ? Child.layout.height : Child.layout.width, Is.EqualTo(21f).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base never re-measured, so nothing it wrote touched the duration.
        [Test]
        public void Given_AnInlineTransitionDuration_When_TheViewportWidthIsRemeasured_Then_TheDurationIsKept()
        {
            // Arrange
            Mount("w-[500px] h-[200px]", "duration-[1000ms] w-[50vw] h-[10px]");
            _sim.panelSize = new Vector2(200, 300);

            // Act
            Settle();

            // Assert
            var duration = Child.style.transitionDuration.value;
            Assert.That(duration?.Count == 1 ? duration[0] : default, Is.EqualTo(new TimeValue(1f, TimeUnit.Second)));
        }

        [Test]
        public void Given_AClippedViewportWidthUnderATransition_When_ThePanelResizes_Then_ItsWrapperLandsWithoutAnimating()
        {
            // Arrange — the bundled sheet carries the transition class.
            VelvetStyleUtilities.AttachTo(_sim.rootVisualElement);
            Mount("w-[500px] h-[200px]", "clip-path-[inset(0)] transition-all duration-[1000ms] w-[50vw] h-[10px]");
            var before = ViewportWidth;

            // Act
            _sim.panelSize = new Vector2(200, 300);
            Settle();

            // Assert — NaN unless the panel's width really moved.
            var expected = ViewportWidth > 0f && ViewportWidth != before ? ViewportWidth / 2f : float.NaN;
            Assert.That(ClipPathLayoutBox.Of(Child).layout.width, Is.EqualTo(expected).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base never read an em margin, so it handed the slot to the cascade.
        [Test]
        public void Given_AnEmMarginThatOverflowsUnderItsFont_When_ItsSlotIsHandedBack_Then_TheSlotIsLeftToTheCascade()
        {
            // Arrange — 1e37em is finite at the 1px em it is parsed against and overflows at 100px.
            Mount("w-[300px] h-[200px]", "text-[100px] ml-[1e37em] w-[10px] h-[10px]");

            // Act
            StyleArbitraryValueResolver.HandBack(Child, HeldSlot.MarginLeft);

            // Assert
            Assert.That(Child.style.marginLeft.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_APercentageMarginOfAnIndefiniteWidth_When_ItsSlotIsHandedBack_Then_ItIsItsFixedPart()
        {
            // Arrange — an absolute parent with no width or insets is sized by its content.
            VelvetStyleUtilities.AttachTo(_sim.rootVisualElement);
            Mount("absolute h-[100px]", "ml-[calc(10%+1px)] w-[10px] h-[10px]");

            // Act
            StyleArbitraryValueResolver.HandBack(Child, HeldSlot.MarginLeft);

            // Assert
            var margin = Child.style.marginLeft;
            Assert.That((margin.keyword, margin.value.value), Is.EqualTo((StyleKeyword.Undefined, 1f)));
        }

        [Test]
        public void Given_APercentagePaddingOfAnIndefiniteWidth_When_Settled_Then_ItIsItsFixedPart()
        {
            // Arrange — an absolute parent with no width or insets is sized by its content.
            VelvetStyleUtilities.AttachTo(_sim.rootVisualElement);

            // Act
            Mount("absolute h-[100px]", "pt-[calc(5%+8px)] w-[10px] h-[10px]");

            // Assert
            Assert.That(Child.resolvedStyle.paddingTop, Is.EqualTo(8f).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base wrote nothing for an em it did not parse.
        [Test]
        public void Given_AnEmWidth_When_MountedBeforeAnyFrame_Then_NothingIsWrittenInline()
        {
            // Arrange
            s_parentClass = new ClassNameStore("text-[12px] w-[300px] h-[200px]");
            s_childClass = "w-[2em] h-[10px]";

            // Act
            _mounted = V.Mount(_sim.rootVisualElement, V.Component(Host, key: "host"));

            // Assert
            Assert.That(Child.style.width.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AViewportWidthUnderATransitionDelay_When_ThePanelResizes_Then_ItLandsWithoutWaiting()
        {
            // Arrange — the bundled sheet carries the transition class.
            VelvetStyleUtilities.AttachTo(_sim.rootVisualElement);
            Mount("w-[500px] h-[200px]", "transition-all duration-[1000ms] w-[50vw] h-[10px]");
            Child.style.transitionDelay = new System.Collections.Generic.List<TimeValue> { new(1f, TimeUnit.Second) };
            var before = ViewportWidth;

            // Act
            _sim.panelSize = new Vector2(200, 300);
            Settle();

            // Assert — NaN unless the panel's width really moved.
            var expected = ViewportWidth > 0f && ViewportWidth != before ? ViewportWidth / 2f : float.NaN;
            Assert.That(Child.layout.width, Is.EqualTo(expected).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base never read the expression, so it behaved as the same percentage.
        [Test]
        public void Given_AFixedHeightAParentHoverTradesForAuto_When_Hovered_Then_APercentageInItBehavesAsTheSamePercentage()
        {
            // Arrange — the bundled sheet carries h-16 and h-auto; hover: projects h-auto onto the class list.
            VelvetStyleUtilities.AttachTo(_sim.rootVisualElement);
            MountBesideNative("w-[300px] h-16 hover:h-auto", "w-[10px] h-[50%]", "w-[10px] h-[calc(50%+1px)]");
            var parent = _sim.rootVisualElement.Q<VisualElement>("parent");

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                parent.SimulateEvent(over);
            }
            Settle();
            Settle();

            // Assert
            Assert.That(Child.layout.height, Is.EqualTo(Native.layout.height).Within(0.01f));
        }

        // The allocation case below asserts a count of zero, which an instrument stuck at zero also gives.
        // GREEN_ON_BASE(characterization): the probe already counts this canary's allocation.
        [Test]
        public void Given_ADelegateAllocatingAKnownArray_When_Probed_Then_TheProbeCountsIt()
        {
            // Arrange
            System.Action canary = () => System.GC.KeepAlive(new byte[16]);
            canary();

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(canary);

            // Assert
            Assert.That(blocks, Is.GreaterThan(0));
        }

        [Test]
        public void Given_APercentageWidthOfAnIndefiniteWidth_When_ItsSlotIsHandedBack_Then_ItIsAuto()
        {
            // Arrange — an absolute parent with no width or insets is sized by its content.
            VelvetStyleUtilities.AttachTo(_sim.rootVisualElement);
            Mount("absolute h-[100px]", "w-[calc(50%+1px)] h-[10px]");

            // Act
            StyleArbitraryValueResolver.HandBack(Child, HeldSlot.Width);

            // Assert
            Assert.That(Child.style.width.keyword, Is.EqualTo(StyleKeyword.Auto));
        }

        [Test]
        public void Given_AnIndefinitePercentageAtRest_When_ThePollTicksThrice_Then_NothingIsAllocated()
        {
            // Arrange — the parent declares no height, so every tick walks its ancestors' declarations.
            MountBesideNative("w-[300px]", "w-[10px] h-[10px]", "w-[10px] h-[calc(50%+1px)]");
            var method = typeof(StyleArbitraryValueResolver)
                .GetMethod("ReresolveRelativeLengths", BindingFlags.NonPublic | BindingFlags.Static);
            System.Action<VisualElement> tick = method == null
                ? _ => System.GC.KeepAlive(new byte[16])
                : (System.Action<VisualElement>)System.Delegate.CreateDelegate(typeof(System.Action<VisualElement>), method);
            var child = Child;
            System.Action ticks = () =>
            {
                tick(child);
                tick(child);
                tick(child);
            };
            ticks();

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(ticks);

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_AnEmWidth_When_ItsLayersAreClearedForThePool_Then_ItsPollStopsTicking()
        {
            // Arrange
            Mount("text-[12px] w-[300px] h-[200px]", "w-[2em] h-[10px]");
            var poll = RelativePollOf(Child);

            // Act
            StyleArbitraryValueResolver.ClearAll(Child);

            // Assert
            Assert.That(IsTicking(poll), Is.EqualTo((bool?)false));
        }

        [Test]
        public void Given_AnEmWidth_When_ItsElementIsRemovedAndMountedAgain_Then_OnlyTheNewPollTicks()
        {
            // Arrange
            Mount("text-[12px] w-[300px] h-[200px]", "w-[2em] h-[10px]");
            var first = RelativePollOf(Child);
            s_showChild = false;
            Rerender();
            s_showChild = true;

            // Act
            Rerender();

            // Assert
            Assert.That((IsTicking(first), IsTicking(RelativePollOf(Child))), Is.EqualTo(((bool?)false, (bool?)true)));
        }

        [Test]
        public void Given_APercentagePlusPixelsBasisInARow_When_Settled_Then_ItIsTheNativePercentagePlusThosePixels()
        {
            // Arrange — the bundled sheet carries the flex class, which lays the children out in a row.
            VelvetStyleUtilities.AttachTo(_sim.rootVisualElement);

            // Act
            MountBesideNative("flex w-[400px] h-[100px]", "basis-[25%] h-[10px]", "basis-[calc(25%+1px)] h-[10px]");

            // Assert — NaN unless the native percentage measured something.
            var expected = Native.layout.width > 0f ? Native.layout.width + 1f : float.NaN;
            Assert.That(Child.layout.width, Is.EqualTo(expected).Within(0.01f));
        }

        [Test]
        public void Given_APercentagePlusPixelsWidth_When_TheParentNarrows_Then_ItIsRemeasured()
        {
            // Arrange
            Mount("w-[300px] h-[100px]", "w-[calc(50%+10px)] h-[10px]");

            // Act
            s_parentClass.Set("w-[200px] h-[100px]");
            Settle();

            // Assert
            Assert.That(Child.layout.width, Is.EqualTo(110f).Within(0.01f));
        }

        [Test]
        public void Given_AMinOfAPercentageAndPixels_When_ThePixelsAreSmaller_Then_TheWidthIsThePixels()
        {
            // Act
            Mount("w-[300px] h-[100px]", "w-[min(50%,100px)] h-[10px]");

            // Assert
            Assert.That(Child.layout.width, Is.EqualTo(100f).Within(0.01f));
        }

        [Test]
        public void Given_AClampWhosePreferredPercentageIsBelowItsMinimum_When_Settled_Then_TheWidthIsTheMinimum()
        {
            // Act
            Mount("w-[300px] h-[100px]", "w-[clamp(50px,10%,80px)] h-[10px]");

            // Assert
            Assert.That(Child.layout.width, Is.EqualTo(50f).Within(0.01f));
        }
    }
}
