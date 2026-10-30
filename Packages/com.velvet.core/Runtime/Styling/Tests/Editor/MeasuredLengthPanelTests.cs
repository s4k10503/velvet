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
    /// Specifies a length only an element can measure — here an em under a 20px font — in each utility family that
    /// writes its value through its own path rather than an inline length: border widths (snapped to device
    /// pixels as the browser snaps them), radii, translate,
    /// transform-origin, blur, gap and space margins, divide widths, ring and outline offsets, and leading.
    /// </summary>
    internal sealed class MeasuredLengthPanelTests
    {
        private static string s_parentClass;
        private static string s_firstClass;
        private static string s_secondClass;

        private EditorPanelSimulator _sim;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(400, 300) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            // The layout classes (flex, flex-row) and the marker a measured leading reads its font through are the sheet's.
            VelvetStyleUtilities.AttachTo(_sim.rootVisualElement);
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _sim?.Dispose();
            _sim = null;
        }

        [Component]
        private static VNode Host()
            => V.Div(name: "frame", className: "w-[300px] h-[200px]", children: new VNode[]
            {
                V.Div(name: "parent", className: s_parentClass, children: new VNode[]
                {
                    V.Div(name: "first", className: s_firstClass),
                    V.Div(name: "second", className: s_secondClass),
                }),
            });

        private VisualElement Parent => _sim.rootVisualElement.Q<VisualElement>("parent");

        private VisualElement First => _sim.rootVisualElement.Q<VisualElement>("first");

        private VisualElement Second => _sim.rootVisualElement.Q<VisualElement>("second");

        private void Mount(string parentClass, string firstClass, string secondClass = "w-[10px] h-[10px]")
        {
            (s_parentClass, s_firstClass, s_secondClass) = (parentClass, firstClass, secondClass);
            _mounted = V.Mount(_sim.rootVisualElement, V.Component(Host, key: "host"));
            _mounted.FlushStateForTest();
            for (var i = 0; i < 4; i++)
            {
                _sim.FrameUpdateMs(16);
            }
        }

        private static VisualElement BandAfter(VisualElement element)
        {
            var host = element.parent;
            var next = host.IndexOf(element) + 1;
            return next < host.childCount && host[next].ClassListContains(RingOverlay.MarkerClass) ? host[next] : null;
        }

        [Test]
        public void Given_AnElementTrackedAndReleasedRepeatedly_When_TrackedOnceMore_Then_ThePanelWatchHoldsOneReference()
        {
            // Arrange
            var element = new VisualElement();
            _sim.rootVisualElement.Add(element);
            var owner = new object();
            for (var i = 0; i < 5; i++)
            {
                PanelScaleWatch.Track(element, owner, _ => { });
                PanelScaleWatch.Untrack(element, owner);
            }

            // Act
            PanelScaleWatch.Track(element, owner, _ => { });

            // Assert
            Assert.That(WatchedOn(element.panel), Is.EqualTo(1));
        }

        // The references the panel's watch holds, read from the private table.
        private static int WatchedOn(IPanel panel)
        {
            var table = typeof(PanelScaleWatch).GetField("s_watches", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var args = new object[] { panel, null };
            if (!(bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, args)!)
            {
                return -1;
            }
            return ((System.Collections.IList)args[1]!.GetType().GetField("Elements")!.GetValue(args[1])!).Count;
        }

        [Test]
        public void Given_ABorderWidthOfAFifthOfAnEm_When_Settled_Then_ItIsFourPixels()
        {
            // Act
            Mount("text-[20px] w-[200px] h-[100px]", "w-[50px] h-[50px] border-solid border-[0.2em]");

            // Assert
            Assert.That(First.resolvedStyle.borderTopWidth, Is.EqualTo(4f).Within(0.01f));
        }

        [Test]
        public void Given_AFractionalBorderWidth_When_Settled_Then_ItIsSnappedDownToDevicePixels()
        {
            // Act
            Mount("text-[20px] w-[200px] h-[100px]", "w-[50px] h-[50px] border-solid border-[2.75px]");

            // Assert
            var perPixel = First.panel.scaledPixelsPerPoint;
            Assert.That(First.resolvedStyle.borderTopWidth, Is.EqualTo(Mathf.Floor(2.75f * perPixel) / perPixel).Within(0.001f));
        }

        [TestCase(3f, 1.5f, 2.6666667f)]
        [TestCase(2.75f, 2f, 2.5f)]
        [TestCase(0.25f, 2f, 0.5f)]
        [TestCase(-3f, 1.5f, -2.6666667f)]
        [TestCase(3f, 1f, 3f)]
        public void Given_ALengthAndAPanelScale_When_SnappedToDevicePixels_Then_ItIsTheBrowsersBorderWidth(
            float length, float perPixel, float expected)
        {
            // Act
            var snapped = StyleRelativeLengths.SnapToDevicePixels(length, perPixel);

            // Assert
            Assert.That(snapped, Is.EqualTo(expected).Within(1e-5f));
        }

        [Test]
        public void Given_ABorderWidthUnderADevicePixel_When_Settled_Then_ItIsOneDevicePixel()
        {
            // Act
            Mount("text-[20px] w-[200px] h-[100px]", "w-[50px] h-[50px] border-solid border-[0.25px]");

            // Assert
            Assert.That(First.resolvedStyle.borderTopWidth, Is.EqualTo(1f / First.panel.scaledPixelsPerPoint).Within(0.001f));
        }

        [Test]
        public void Given_ARadiusOfHalfAnEm_When_Settled_Then_ItIsTenPixels()
        {
            // Act
            Mount("text-[20px] w-[200px] h-[100px]", "w-[50px] h-[50px] rounded-[0.5em]");

            // Assert
            Assert.That(First.resolvedStyle.borderTopLeftRadius, Is.EqualTo(10f).Within(0.01f));
        }

        [Test]
        public void Given_ATranslateOfAnEm_When_Settled_Then_ItMovesTwentyPixels()
        {
            // Act
            Mount("text-[20px] w-[200px] h-[100px]", "w-[50px] h-[50px] translate-x-[1em]");

            // Assert
            Assert.That(First.resolvedStyle.translate.x, Is.EqualTo(20f).Within(0.01f));
        }

        [Test]
        public void Given_ATranslateOfHalfTheBoxPlusAnEm_When_Settled_Then_ItMovesFortyFivePixels()
        {
            // Act — half of the element's own 50px box, and 20px.
            Mount("text-[20px] w-[200px] h-[100px]", "w-[50px] h-[50px] translate-x-[calc(50%+1em)]");

            // Assert
            Assert.That(First.resolvedStyle.translate.x, Is.EqualTo(45f).Within(0.01f));
        }

        [Test]
        public void Given_AnOriginOfOneAndTwoEms_When_Settled_Then_ItIsTwentyAndFortyPixels()
        {
            // Act
            Mount("text-[20px] w-[200px] h-[100px]", "w-[50px] h-[50px] origin-[1em_2em]");

            // Assert
            var origin = First.resolvedStyle.transformOrigin;
            Assert.That(Mathf.Approximately(origin.x, 20f) ? origin.y : float.NaN, Is.EqualTo(40f));
        }

        [TestCase("bg-radial-[circle_2em_at_center,#ff0000,#0000ff]")]
        [TestCase("bg-radial-[ellipse_2em_3em_at_1em_50%,#ff0000,#0000ff]")]
        [TestCase("bg-conic-[from_0deg_at_1em_50%,#ff0000,#0000ff]")]
        public void Given_AGradientShapeInEms_When_Mounted_Then_ItPaintsOnceMeasured(string gradient)
        {
            // Act
            Mount("text-[20px] w-[200px] h-[100px]", "w-[50px] h-[50px] " + gradient);

            // Assert
            Assert.That(First.style.backgroundImage.value.texture, Is.Not.Null);
        }

        [Test]
        public void Given_AnEmRadialRadius_When_Mounted_Then_TheBoundGradientIsReadAgainstTheElement()
        {
            // Act — the radius is bound on a provisional basis while the element is detached, and the poll reads it again.
            Mount("text-[20px] w-[200px] h-[100px]", "w-[50px] h-[50px] bg-radial-[circle_2em_at_center,#ff0000,#0000ff]");

            // Assert — two ems of the element's own 20px, not of the 16px a provisional basis gives.
            var spec = _mounted.Root.Reconciler.Context.GradientBackgrounds[First].Spec;
            Assert.That(GradientBackground.RadialRadii(spec, 50f, 50f).x, Is.EqualTo(40f));
        }

        [TestCase("bg-linear-[to_right,#ff0000_1em,#0000ff_3em]")]
        [TestCase("bg-linear-[to_right,#ff0000,2em,#0000ff]")]
        public void Given_AnEmStopPositionInAList_When_Mounted_Then_ItPaintsOnceMeasured(string gradient)
        {
            // Act
            Mount("text-[20px] w-[200px] h-[100px]", "w-[50px] h-[50px] " + gradient);

            // Assert
            Assert.That(First.style.backgroundImage.value.texture, Is.Not.Null);
        }

        // GREEN_ON_BASE(characterization): a blur of an em was measured on the element before the other utilities were.
        // Dropping the measured branch of the blur parse reddens it.
        [Test]
        public void Given_ABlurOfAQuarterEm_When_Settled_Then_ItsRadiusIsFivePixels()
        {
            // Act
            Mount("text-[20px] w-[200px] h-[100px]", "w-[50px] h-[50px] blur-[0.25em]");

            // Assert
            var filters = First.style.filter.value;
            Assert.That(filters != null && filters.Count == 1 ? filters[0].GetParameter(0).floatValue : float.NaN,
                Is.EqualTo(5f).Within(0.01f));
        }

        [Test]
        public void Given_AGapOfAnEm_When_Settled_Then_TheChildrenAreTwentyPixelsApart()
        {
            // Act
            Mount("text-[20px] w-[200px] h-[100px] flex flex-row gap-[1em]", "w-[10px] h-[10px]");

            // Assert
            Assert.That(Second.layout.x - First.layout.xMax, Is.EqualTo(20f).Within(0.01f));
        }

        [Test]
        public void Given_ASpaceMarginOfAnEmOnAChildOfItsOwnFont_When_Settled_Then_ItIsThatChildsEm()
        {
            // Act — the margin is the child's, so its own 10px font measures the em.
            Mount("text-[20px] w-[200px] h-[100px] flex flex-row space-x-[1em]", "text-[10px] w-[10px] h-[10px]");

            // Assert
            Assert.That(Second.layout.x - First.layout.xMax, Is.EqualTo(10f).Within(0.01f));
        }

        [Test]
        public void Given_ASpaceMarginOfAnEm_When_TheChildsFontSizeChanges_Then_ItIsMeasuredAgain()
        {
            // Arrange
            Mount("text-[20px] w-[200px] h-[100px] flex flex-row space-x-[1em]", "text-[10px] w-[10px] h-[10px]");

            // Act — the container and its size are as they were; only the child's own font moves.
            First.style.fontSize = 30f;
            for (var i = 0; i < 4; i++)
            {
                _sim.FrameUpdateMs(16);
            }

            // Assert
            Assert.That(Second.layout.x - First.layout.xMax, Is.EqualTo(30f).Within(0.01f));
        }

        [Test]
        public void Given_ADivideWidthOfATenthOfAnEm_When_Settled_Then_TheDividerIsTwoPixels()
        {
            // Act
            Mount("text-[20px] w-[200px] h-[100px] flex flex-row divide-x-[0.1em]", "w-[10px] h-[10px]");

            // Assert
            Assert.That(First.resolvedStyle.borderRightWidth, Is.EqualTo(2f).Within(0.01f));
        }

        [Test]
        public void Given_ARingOfHalfAnEm_When_Settled_Then_TheBandIsTenPixelsWide()
        {
            // Act
            Mount("text-[20px] w-[200px] h-[100px]", "w-[50px] h-[50px] ring-[0.5em]");

            // Assert
            Assert.That(BandAfter(First)?.resolvedStyle.borderTopWidth, Is.EqualTo((float?)10f));
        }

        [Test]
        public void Given_ARingOffsetOfHalfAnEm_When_Settled_Then_TheBandStartsTwelvePixelsOut()
        {
            // Act — the 10px offset and the 2px band.
            Mount("text-[20px] w-[200px] h-[100px]", "w-[50px] h-[50px] ring-2 ring-offset-[0.5em]");

            // Assert
            Assert.That(BandAfter(First)?.layout.x - First.layout.x, Is.EqualTo((float?)-12f));
        }

        // GREEN_ON_BASE(characterization): a leading mixing an em with pixels was measured on its element before the
        // other utilities were. Reading it as the pixel length alone reddens it.
        [Test]
        public void Given_ALeadingOfAnEmAndTwoPixels_When_Settled_Then_TheTextCarriesTwentyTwoPixels()
        {
            // Act
            _mounted = V.Mount(_sim.rootVisualElement, V.Div(className: "text-[20px] leading-[calc(1em+2px)]", V.Text("hi")));
            _mounted.FlushStateForTest();
            for (var i = 0; i < 4; i++)
            {
                _sim.FrameUpdateMs(16);
            }

            // Assert
            Assert.That(_sim.rootVisualElement.Q<Label>().text, Is.EqualTo("<line-height=22px>hi</line-height>"));
        }
    }
}
