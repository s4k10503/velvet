using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class MotionSlotContextTests : PanelTestBase
    {
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        private VisualElement MountInFixedParent()
        {
            var parent = new VisualElement();
            parent.style.width = 200f;
            parent.style.height = 100f;
            var element = new VisualElement();
            parent.Add(element);
            _window.rootVisualElement.Add(parent);
            EditorPanelTestHelpers.ForcePanelUpdate(element.panel);
            return element;
        }

        private static MotionSpringClassParser.SpringPlan Resolve(VisualElement element, string[] from, string[] to,
            ArbitraryProperty? driven = null)
            => MotionSpringClassParser.Resolve(from, to, 0f, 0f,
                MotionSlotContext.Read(element, from, to, slot => slot == driven));

        [TestCase(false)]
        [TestCase(true)]
        [TestCase(true, true)]
        public void Given_ALengthOnlyTheToSideNames_When_Resolved_Then_ItStartsFromTheResolvedValue(bool clippedNative, bool unwrap = false)
        {
            // Arrange
            var element = MountInFixedParent();
            var now = 100.0;
            if (clippedNative)
            {
                var parent = element.parent;
                element.RemoveFromHierarchy();
                _mounted = V.Mount(parent, V.Div(name: "clipped-current", className: "clip-path-[inset(0)]"));
                element = parent.Q<VisualElement>("clipped-current");
                ForcePanelUpdate(element.panel);
                EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => now);
                StartNativeWidth(ClipPathLayoutBox.Of(element));
                now += 0.5;
                EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
                ForcePanelUpdate(element.panel);
            }
            var owner = ClipPathLayoutBox.Of(element);
            var before = owner.resolvedStyle.width;

            // Act
            var plan = Resolve(element, new string[0], new[] { "w-[100px]" });
            var width = plan.Lengths?.Find(channel => channel.Property == ArbitraryProperty.Width);
            var startPaint = before;
            var framePaint = before;
            var frameInline = before;
            if (clippedNative)
            {
                var state = BezierTweenDriver.Create(plan, 0f, 0f, 1f, 1f, 1f);
                BezierTweenDriver.ApplyCurrentValues(element, state);
                ForcePanelUpdate(element.panel);
                startPaint = owner.resolvedStyle.width;
                if (unwrap)
                {
                    _mounted.Render(V.Div(name: "clipped-current"));
                    owner = ClipPathLayoutBox.Of(element);
                    ConfigureNativeTransition(owner, "width");
                }
                BezierTweenDriver.Step(element, state, 0.25f);
                ForcePanelUpdate(element.panel);
                framePaint = owner.resolvedStyle.width;
                frameInline = owner.style.width.value.value;
                BezierTweenDriver.ClearInlineOverrides(element, state);
            }
            var ownerMatches = ReferenceEquals(owner, element) == (!clippedNative || unwrap);

            // Assert
            Assert.That(new[] { before, width?.From ?? float.NaN, startPaint, frameInline, framePaint, ownerMatches ? 1f : 0f },
                Is.EqualTo(clippedNative ? new[] { 160f, 160f, 160f, 145f, 145f, 1f } : new[] { 200f, 200f, 200f, 200f, 200f, 1f }).Within(0.02f));
        }

        [Test]
        public void Given_APercentWidthFromSideAndAPixelToSide_When_Resolved_Then_ItStartsFromThePercentInPixels()
        {
            // Arrange
            var element = MountInFixedParent();
            StyleArbitraryValueResolver.ApplyClassToken(element, "w-1/2", StyleLayerPriority.Base);

            // Act
            var plan = Resolve(element, new[] { "w-1/2" }, new[] { "w-[60px]" });

            // Assert
            Assert.That(Lengths(plan), Is.EqualTo("Width:100->60"));
        }

        [Test]
        public void Given_AnInlineLonghandOfTheElementsOwnUnderAStylesheetShorthandSwap_When_Resolved_Then_ThatEdgeIsNotAnimated()
        {
            // Arrange
            var element = MountInFixedParent();
            StyleArbitraryValueResolver.ApplyClassToken(element, "pt-[2px]", StyleLayerPriority.Base);

            // Act
            var plan = Resolve(element, new[] { "p-0" }, new[] { "p-8" });

            // Assert
            Assert.That(Lengths(plan), Is.EqualTo("PaddingBottom:0->32 PaddingLeft:0->32 PaddingRight:0->32"));
        }

        [Test]
        public void Given_AStylesheetLonghandOfTheElementsOwnDeclaredAfterTheSwapsShorthand_When_Resolved_Then_ThatEdgeKeepsItsValue()
        {
            // Arrange
            var element = MountInFixedParent();
            element.AddToClassList("pt-4");
            ForcePanelUpdate(element.panel);

            // Act
            var plan = Resolve(element, new[] { "p-0" }, new[] { "p-8" });

            var top = plan.Lengths?.Find(channel => channel.Property == ArbitraryProperty.PaddingTop);

            // Assert
            Assert.That((element.resolvedStyle.paddingTop, top?.From, top?.To), Is.EqualTo((16f, (float?)16f, (float?)16f)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Given_AnAxisARunningPlayDrivesAndOnlyTheToSideNames_When_Resolved_Then_ItStartsFromThePlaysFrame(bool translate)
        {
            // Arrange
            var element = MountInFixedParent();
            element.AddToClassList(translate ? "translate-y-8" : "opacity-25");
            if (translate) element.style.translate = new Translate(0f, 60f, 0f);
            else element.style.opacity = 0.3f;
            var from = translate ? new[] { "translate-x-[10px]" } : new string[0];
            var to = translate ? new[] { "translate-x-[20px]" } : new[] { "opacity-50" };

            // Act
            var plan = Resolve(element, from, to, translate ? ArbitraryProperty.TranslateY : ArbitraryProperty.Opacity);

            // Assert
            Assert.That(new[] { (translate ? plan.TranslateX?.from : plan.Opacity?.from) ?? float.NaN, plan.TranslateY?.from ?? 0f },
                Is.EqualTo(translate ? new[] { 10f, 60f } : new[] { 0.3f, 0f }).Within(1e-5f));
        }

        [TestCase("w-[40px]", "w-[50%]", "Width:20->50")]
        [TestCase("h-[40px]", "h-[50%]", "Height:40->50")]
        [TestCase("min-w-[50%]", "min-w-[60px]", "MinWidth:100->60")]
        [TestCase("max-h-[50%]", "max-h-[60px]", "MaxHeight:50->60")]
        public void Given_AMixedUnitDimension_When_Resolved_Then_TheStartUsesTheTargetsUnit(string from, string to, string expected)
        {
            // Arrange
            var element = MountInFixedParent();
            StyleArbitraryValueResolver.ApplyClassToken(element, from, StyleLayerPriority.Base);

            // Act
            var plan = Resolve(element, new[] { from }, new[] { to });

            // Assert
            Assert.That(Lengths(plan), Is.EqualTo(expected));
        }

        [Test]
        public void Given_ABackgroundColorOnlyTheToSideNames_When_Resolved_Then_ItStartsFromTheCurrentColor()
        {
            // Arrange
            var element = MountInFixedParent();
            element.style.backgroundColor = Color.red;

            // Act
            var plan = Resolve(element, new string[0], new[] { "bg-[#0000ff]" });
            var channel = plan.Colors?.Find(c => c.Property == ArbitraryProperty.BackgroundColor);

            // Assert
            Assert.That(channel?.From, Is.EqualTo((Color?)Color.red));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Given_AUniformCurrentBorderColor_When_OnlyTheTargetNamesIt_Then_ItStartsFromThatColor(bool nativeTransition)
        {
            // Arrange
            var element = MountInFixedParent();
            element.style.borderTopColor = element.style.borderRightColor = element.style.borderBottomColor = element.style.borderLeftColor = Color.red;
            var now = 100.0;
            if (nativeTransition)
            {
                ForcePanelUpdate(element.panel);
                EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => now);
                ConfigureNativeTransition(element, "border-color");
                element.style.borderTopColor = element.style.borderRightColor = element.style.borderBottomColor = element.style.borderLeftColor = Color.blue;
                ForcePanelUpdate(element.panel);
                now += 0.5;
                EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
                ForcePanelUpdate(element.panel);
            }
            var paint = element.resolvedStyle.borderTopColor;

            // Act
            var plan = Resolve(element, new string[0], new[] { "border-[#0000ff]" });
            var channel = plan.Colors?.Find(c => c.Property == ArbitraryProperty.BorderColor);

            // Assert
            Assert.That((channel?.From, !nativeTransition || paint != element.style.borderTopColor.value),
                Is.EqualTo(((Color?)(nativeTransition ? paint : Color.red), true)));
        }

        // GREEN_ON_BASE(characterization, nativeHolder=false): the base already leaves target-only colors undriven.
        [TestCase(false)]
        [TestCase(true)]
        public void Given_CurrentBorderColorsWithAnIncompatibleEdge_When_OnlyTheTargetNamesBorderColor_Then_NoUniformChannelIsBuilt(bool nativeHolder)
        {
            // Arrange
            var element = MountInFixedParent();
            element.style.borderTopColor = element.style.borderBottomColor = element.style.borderLeftColor = Color.red;
            element.style.borderRightColor = Color.blue;
            ForcePanelUpdate(element.panel);
            var now = 100.0;
            if (nativeHolder)
            {
                EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => now);
                ConfigureNativeTransition(element, "border-top-color");
                element.style.borderTopColor = Color.blue;
                ForcePanelUpdate(element.panel);
                now += 0.5;
                EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
                ForcePanelUpdate(element.panel);
                var top = element.resolvedStyle.borderTopColor;
                element.style.borderRightColor = element.style.borderBottomColor = element.style.borderLeftColor = top;
                ForcePanelUpdate(element.panel);
            }
            var painted = element.resolvedStyle;
            var control = nativeHolder
                ? painted.borderTopColor != element.style.borderTopColor.value
                    && painted.borderTopColor == painted.borderRightColor
                    && painted.borderTopColor == painted.borderBottomColor
                    && painted.borderTopColor == painted.borderLeftColor
                : painted.borderTopColor != painted.borderRightColor;

            // Act
            var plan = Resolve(element, new string[0], new[] { nativeHolder ? "border-black" : "border-[#0000ff]" });

            // Assert
            Assert.That((plan.Colors, control), Is.EqualTo(((List<MotionSpringClassParser.ColorChannelPlan>)null, true)));
        }

        [TestCase(0.75f, false)]
        [TestCase(0.50000006f, false)]
        [TestCase(0.75f, true)]
        public void Given_ANonuniformCurrentScale_When_OnlyTheTargetNamesUniformScale_Then_NoUniformChannelIsBuilt(float scaleY, bool nativeTransition)
        {
            // Arrange
            var element = MountInFixedParent();
            var now = 100.0;
            if (nativeTransition)
            {
                EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => now);
                ConfigureNativeTransition(element, "scale");
            }
            element.style.scale = new Scale(new Vector3(0.5f, scaleY, 1f));
            ForcePanelUpdate(element.panel);
            if (nativeTransition)
            {
                now += 0.5;
                EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
                ForcePanelUpdate(element.panel);
            }
            var paint = element.resolvedStyle.scale.value;

            // Act
            var plan = Resolve(element, new string[0], new[] { "scale-[1.4]" });

            // Assert
            Assert.That(plan.Scale == null ? new[] { paint.x, paint.y } : new[] { float.NaN, float.NaN },
                Is.EqualTo(nativeTransition ? new[] { 0.875f, 0.9375f } : new[] { 0.5f, scaleY }).Within(1e-5f));
        }

        [Test]
        public void Given_ARestingInlineOpacity_When_ASwappedStylesheetUtilityNamesOpacity_Then_TheInlineHolderIsNotDriven()
        {
            // Arrange
            var element = MountInFixedParent();
            element.style.opacity = 0.75f;

            // Act
            var plan = Resolve(element, new string[0], new[] { "opacity-50" });

            // Assert
            Assert.That(plan.Opacity, Is.Null);
        }

        [Test]
        public void Given_ARestingInlinePaddingEdge_When_TheTargetIsAnImportantStylesheetShorthand_Then_ItStartsFromTheEdge()
        {
            // Arrange
            var element = MountInFixedParent();
            StyleArbitraryValueResolver.ApplyClassToken(element, "pt-[2px]", StyleLayerPriority.Base);

            // Act
            var plan = Resolve(element, new[] { "p-0" }, new[] { "!p-8" });
            var top = plan.Lengths?.Find(channel => channel.Property == ArbitraryProperty.PaddingTop);

            // Assert
            Assert.That((top?.From, top?.To), Is.EqualTo(((float?)2f, (float?)32f)));
        }

        // GREEN_ON_BASE(characterization): the base already declines mixed-unit padding; computed sampling retains that unsupported conversion boundary.
        [Test]
        public void Given_APercentPaddingStartAndAPixelTarget_When_Resolved_Then_TheUnsupportedConversionBuildsNoChannel()
        {
            // Arrange
            var element = MountInFixedParent();
            StyleArbitraryValueResolver.ApplyClassToken(element, "pt-[10%]", StyleLayerPriority.Base);

            // Act
            var plan = Resolve(element, new[] { "pt-[10%]" }, new[] { "pt-[20px]" });

            // Assert
            Assert.That(plan.Lengths, Is.Null);
        }

        [Test]
        public void Given_AnOffPanelElement_When_TheContextIsRead_Then_NoContextIsCreated()
        {
            // Arrange
            var element = new VisualElement();

            // Act
            var context = MotionSlotContext.Read(element, new string[0], new[] { "w-[100px]" }, _ => false);

            // Assert
            Assert.That(context, Is.Null);
        }

        [TestCase(TransitionType.Spring, false)]
        [TestCase(TransitionType.Bezier, false)]
        [TestCase(TransitionType.Spring, true)]
        [TestCase(TransitionType.Bezier, true)]
        public void Given_ARunningOpacityChannel_When_ANewTargetOnlyPlayStarts_Then_ItStartsFromTheFrame(TransitionType type, bool exit)
        {
            // Arrange
            var element = MountInFixedParent();
            var scheduler = new StyleAnimationScheduler();
            Play(scheduler, element, type, exit, new[] { "opacity-0" }, new[] { "opacity-100" });
            element.RemoveFromClassList("opacity-100");
            element.style.opacity = 0.3f;

            // Act
            Play(scheduler, element, type, exit, new string[0], new[] { "opacity-50" });
            var start = element.style.opacity.value;
            var map = (System.Collections.IDictionary)typeof(StyleAnimationScheduler)
                .GetField(exit ? "_pendingExits" : "_pendingEnters",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(scheduler);
            var running = map.Contains(element);
            scheduler.CancelAll();

            // Assert
            Assert.That((start, running), Is.EqualTo((0.3f, true)));
        }

        private static void Play(StyleAnimationScheduler scheduler, VisualElement element, TransitionType type,
            bool exit, string[] from, string[] to)
        {
            if (exit)
            {
                scheduler.PlayExit(element, new StyleTransitionConfig
                {
                    Type = type,
                    DurationSec = 1f,
                    ExitFromClass = string.Join(" ", from),
                    ExitToClass = string.Join(" ", to),
                }, null);
            }
            else
            {
                scheduler.PlayVariantEnter(element, from, to, 1f, EasingMode.Linear, 0f, type: type);
            }
        }

        [Test]
        public void Given_ARestingImportantStylesheetWidth_When_APlainInlineTargetIsResolved_Then_ItKeepsTheImportantWidth()
        {
            // Arrange
            var element = MountInFixedParent();
            StyleClassProjection.Add(element, "w-40", StyleLayerPriority.ImportantOf(StyleLayerPriority.Base));
            ForcePanelUpdate(element.panel);

            // Act
            var plan = Resolve(element, new string[0], new[] { "w-[20px]" });
            var width = plan.Lengths?.Find(channel => channel.Property == ArbitraryProperty.Width);

            // Assert
            Assert.That((element.resolvedStyle.width, width?.From, width?.To), Is.EqualTo((160f, (float?)160f, (float?)160f)));
        }

        [TestCase("w-[20px]")]
        [TestCase("!w-20")]
        public void Given_ARestingImportantInlineWidth_When_AWeakerTargetIsResolved_Then_NoWidthChannelIsBuilt(string target)
        {
            // Arrange
            var element = MountInFixedParent();
            StyleArbitraryValueResolver.ApplyClassToken(element, "w-[40px]", StyleLayerPriority.ImportantOf(StyleLayerPriority.Base));

            // Act
            var plan = Resolve(element, new string[0], new[] { target });

            // Assert
            Assert.That(plan.Lengths, Is.Null);
        }

        [Test]
        public void Given_ARestingImportantOpacityAndPlainWidth_When_AnInlineWidthTargetIsResolved_Then_TheWidthCanChange()
        {
            // Arrange
            var element = MountInFixedParent();
            StyleClassProjection.Add(element, "opacity-50", StyleLayerPriority.ImportantOf(StyleLayerPriority.Base));
            element.AddToClassList("w-40");
            ForcePanelUpdate(element.panel);

            // Act
            var plan = Resolve(element, new string[0], new[] { "w-[20px]" });
            var width = plan.Lengths?.Find(channel => channel.Property == ArbitraryProperty.Width);

            // Assert
            Assert.That((width?.From, width?.To), Is.EqualTo(((float?)160f, (float?)20f)));
        }

        [Test]
        public void Given_ARestingImportantInlineHeight_When_AWidthTargetIsResolved_Then_TheWidthCanChange()
        {
            // Arrange
            var element = MountInFixedParent();
            element.style.width = 40f;
            StyleArbitraryValueResolver.ApplyClassToken(element, "h-[20px]", StyleLayerPriority.ImportantOf(StyleLayerPriority.Base));

            // Act
            var plan = Resolve(element, new string[0], new[] { "w-[20px]" });
            var width = plan.Lengths?.Find(channel => channel.Property == ArbitraryProperty.Width);

            // Assert
            Assert.That((width?.From, width?.To), Is.EqualTo(((float?)40f, (float?)20f)));
        }

        [Test]
        public void Given_AnImportantInlineWidthTheSwapRemoves_When_AnImportantWidthTargetIsResolved_Then_TheWidthCanChange()
        {
            // Arrange
            var element = MountInFixedParent();
            StyleArbitraryValueResolver.ApplyClassToken(element, "w-[40px]", StyleLayerPriority.ImportantOf(StyleLayerPriority.Base));

            // Act
            var plan = Resolve(element, new[] { "!w-[40px]" }, new[] { "!w-[20px]" });
            var width = plan.Lengths?.Find(channel => channel.Property == ArbitraryProperty.Width);

            // Assert
            Assert.That((width?.From, width?.To), Is.EqualTo(((float?)40f, (float?)20f)));
        }

        [Test]
        public void Given_AnImportantHoverWidthOutsideTheSwap_When_AnImportantBaseWidthTargetIsResolved_Then_NoWidthChannelIsBuilt()
        {
            // Arrange
            var element = MountInFixedParent();
            StyleArbitraryValueResolver.ApplyClassToken(element, "w-[40px]", StyleLayerPriority.ImportantOf(StyleLayerPriority.Hover));

            // Act
            var plan = Resolve(element, new string[0], new[] { "!w-[20px]" });

            // Assert
            Assert.That(plan.Lengths, Is.Null);
        }

        [Test]
        public void Given_EmptyTokensAroundALengthTarget_When_ResolvedWithContext_Then_TheEmptyTokensAreIgnored()
        {
            // Arrange
            var element = MountInFixedParent();

            // Act
            var plan = Resolve(element, new[] { null, string.Empty }, new[] { string.Empty, null, "w-[100px]" });

            // Assert
            Assert.That(Lengths(plan), Is.EqualTo("Width:200->100"));
        }

        [TestCase(TransitionType.Spring, false)]
        [TestCase(TransitionType.Bezier, false)]
        [TestCase(TransitionType.Spring, true)]
        [TestCase(TransitionType.Bezier, true)]
        [TestCase(TransitionType.Bezier, false, true)]
        public void Given_ANumericDriverSwap_When_TheClassesLand_Then_TheInlineCommitCallbackRunsOnce(TransitionType type, bool exit, bool nativeTransition = false)
        {
            // Arrange
            var element = MountInFixedParent();
            var scheduler = new StyleAnimationScheduler();
            var commits = 0;
            var now = 100.0;
            if (nativeTransition)
            {
                EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => now);
                StartNativeWidth(element);
                now += 0.5;
                EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
                ForcePanelUpdate(element.panel);
            }
            var initialWidth = element.resolvedStyle.width;
            var config = new StyleTransitionConfig
            {
                Type = type,
                DurationSec = 1f,
                ExitFromClass = "w-[40px]",
                ExitToClass = "w-[100px]",
            };

            // Act
            if (exit)
            {
                scheduler.PlayExit(element, config, null, onSwap: () => commits++);
            }
            else
            {
                scheduler.PlayVariantEnter(element, new[] { "w-[40px]" }, new[] { "w-[100px]" },
                    config, onSwap: () => commits++);
            }
            var startWidth = element.style.width.value.value;
            scheduler.CancelAll();

            // Assert
            Assert.That(new[] { initialWidth, commits, startWidth },
                Is.EqualTo(new[] { nativeTransition ? 160f : 200f, 1f, 40f }).Within(0.02f));
        }

        private static StateUpdater<bool> s_setWidthTarget;
        private static TransitionType s_widthTransitionType;
        private static string s_widthTargetClass;
        private static string s_widthRestingClass;

        [Component]
        private static VNode WidthTargetRender()
        {
            var (target, setTarget) = Hooks.UseState(false);
            s_setWidthTarget = setTarget;
            return V.Motion(name: "computed-width", className: s_widthRestingClass, animate: target ? "target" : "rest",
                variants: new Dictionary<string, MotionVariant>
                {
                    ["rest"] = "",
                    ["target"] = s_widthTargetClass,
                },
                transition: new StyleTransitionConfig { Type = s_widthTransitionType, DurationSec = 1f });
        }

        [TestCase(TransitionType.Spring, false)]
        [TestCase(TransitionType.Bezier, false)]
        [TestCase(TransitionType.Bezier, true)]
        [TestCase(TransitionType.Spring, true)]
        [TestCase(TransitionType.Bezier, true, "stylesheet")]
        [TestCase(TransitionType.Bezier, true, "completed")]
        [TestCase(TransitionType.Bezier, true, "cancelled")]
        [TestCase(TransitionType.Bezier, true, "detached")]
        [TestCase(TransitionType.Bezier, true, "restarted")]
        [TestCase(TransitionType.Bezier, true, "unrelated")]
        [TestCase(TransitionType.Bezier, false, "fresh")]
        public void Given_AMountedMotionWithoutAWidthPose_When_AnInlineWidthTargetIsSelected_Then_ItStartsAtThePreviouslyResolvedWidth(TransitionType type, bool nativeTransition, string phase = "running")
        {
            // Arrange
            var now = 100.0;
            if (nativeTransition)
            {
                EditorPanelTestHelpers.SetPanelTimeFunction(_window.rootVisualElement.panel, () => now);
            }
            var parent = new VisualElement();
            parent.style.width = 200f;
            _window.rootVisualElement.Add(parent);
            s_widthTransitionType = type;
            s_widthTargetClass = phase == "stylesheet" ? "w-20" : "w-[100px]";
            s_widthRestingClass = RestingWidthClass(nativeTransition, phase);
            _mounted = V.Mount(parent, V.Component(WidthTargetRender));
            ForcePanelUpdate(parent.panel);
            var element = parent.Q<VisualElement>("computed-width");
            if (nativeTransition)
            {
                ArrangeNativeBoundary(element, parent, phase, ref now);
            }
            var settled = phase is "completed" or "cancelled" or "detached";
            var fresh = settled || phase is "unrelated" or "fresh";
            if (settled)
            {
                element.style.transitionDuration = new List<TimeValue> { new(0f, TimeUnit.Second) };
                ForcePanelUpdate(element.panel);
            }
            if (fresh)
            {
                element.style.width = 80f;
            }
            var priorPaint = element.resolvedStyle.width;
            var priorInline = element.style.width.value.value;
            var unrelatedPaint = UnrelatedHeight(element, phase);

            // Act
            s_setWidthTarget.Invoke(true);
            _mounted.FlushStateForTest();
            var start = element.style.width.value.value;
            ForcePanelUpdate(element.panel);
            var paint = element.resolvedStyle.width;
            var unintendedHeight = UnrelatedHeightChannel(element, phase);
            Debug.Log($"[computed-start-boundary] type={type} native={nativeTransition} phase={phase} beforePaint={priorPaint} beforeInline={priorInline} start={start} paint={paint}");

            // Assert
            var expectedPaint = settled ? 40f : fresh || !nativeTransition ? 200f : phase == "restarted" ? 150f : 160f;
            var expectedInline = fresh ? 80f : !nativeTransition ? 0f : phase == "restarted" ? 120f : 40f;
            var expectedStart = fresh ? 80f : expectedPaint;
            var expectedHeight = phase == "unrelated" ? 80f : 0f;
            Assert.That(new[] { priorPaint, priorInline, start, paint, unrelatedPaint, unintendedHeight ? 1f : 0f },
                Is.EqualTo(new[] { expectedPaint, expectedInline, expectedStart, expectedStart, expectedHeight, 0f }).Within(0.02f));
        }

        private static string RestingWidthClass(bool nativeTransition, string phase)
            => phase == "unrelated" ? "h-10" : nativeTransition && phase == "running" ? "w-10" : null;

        private bool UnrelatedHeightChannel(VisualElement element, string phase)
        {
            if (phase != "unrelated") return false;
            var scheduler = _mounted.Root.Reconciler.Context.StyleAnimationScheduler;
            var entries = (System.Collections.IDictionary)typeof(StyleAnimationScheduler)
                .GetField("_pendingEnters", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(scheduler);
            var pending = entries[element];
            var state = (BezierTweenState)pending.GetType().GetField("Bezier").GetValue(pending);
            return state.Lengths.Exists(channel => channel.Property == ArbitraryProperty.Height);
        }

        private static float UnrelatedHeight(VisualElement element, string phase)
            => phase == "unrelated" ? element.resolvedStyle.height : 0f;

        private static void ArrangeNativeBoundary(VisualElement element, VisualElement parent, string phase, ref double now)
        {
            if (phase == "unrelated")
            {
                element.style.height = 100f;
                ForcePanelUpdate(element.panel);
                ConfigureNativeTransition(element, "height");
                element.style.height = 20f;
                ForcePanelUpdate(element.panel);
                EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            }
            else
            {
                StartNativeWidth(element);
            }
            now += 0.5;
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
            if (phase == "completed")
            {
                now += 2;
                EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
                ForcePanelUpdate(element.panel);
            }
            else if (phase == "cancelled")
            {
                element.style.transitionProperty = new List<StylePropertyName> { new("none") };
                ForcePanelUpdate(element.panel);
            }
            else if (phase == "detached")
            {
                element.RemoveFromHierarchy();
                parent.Add(element);
                ForcePanelUpdate(element.panel);
            }
            else if (phase == "restarted")
            {
                element.style.width = 120f;
                ForcePanelUpdate(element.panel);
                EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
                now += 0.5;
                EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
                ForcePanelUpdate(element.panel);
            }
        }

        private static void ConfigureNativeTransition(VisualElement element, string property)
        {
            element.style.transitionProperty = new List<StylePropertyName> { new(property) };
            element.style.transitionDuration = new List<TimeValue> { new(2f, TimeUnit.Second) };
            element.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.Linear) };
            EditorPanelTestHelpers.ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
        }

        private static void StartNativeWidth(VisualElement element)
        {
            element.style.width = 200f;
            EditorPanelTestHelpers.ForcePanelUpdate(element.panel);
            ConfigureNativeTransition(element, "width");
            element.style.width = 40f;
            EditorPanelTestHelpers.ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
        }

        private static string Lengths(MotionSpringClassParser.SpringPlan plan)
            => string.Join(" ", (plan.Lengths ?? new List<MotionSpringClassParser.LengthChannelPlan>())
                .Select(channel => $"{channel.Property}:{channel.From}->{channel.To}")
                .OrderBy(text => text, System.StringComparer.Ordinal));
    }
}
