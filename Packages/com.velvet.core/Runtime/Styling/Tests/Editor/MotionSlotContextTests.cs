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

        [TestCase(false)]
        [TestCase(true)]
        public void Given_AnUnusableNativeMemberReceiver_When_Read_Then_ItReturnsNull(bool method)
        {
            // Arrange
            System.Reflection.MemberInfo member = method
                ? EngineMember.HasRunningStyleAnimation.ResolveMethod()
                : EngineMember.StylePropertyNameId.ResolveProperty();
            var reader = typeof(MotionSlotContext).GetMethod("ReadNativeMemberOrNull",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var id = EngineMember.StylePropertyNameId.ResolveProperty()?.GetValue(new StylePropertyName("width"));

            // Act
            var value = reader.Invoke(null, new object[] { member, new object(), method ? new[] { id } : null });

            // Assert
            Assert.That((member != null, value), Is.EqualTo((true, (object)null)));
        }

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

        [Test]
        public void Given_AMountedTranslateWithOnlyAYTarget_When_Resolved_Then_XKeepsItsFrameAndYStartsFromItsFrame()
        {
            // Arrange
            var element = MountInFixedParent();
            element.style.translate = new Translate(60f, 30f, 0f);
            var from = new string[0];
            var to = new[] { "translate-y-[20px]" };
            var context = MotionSlotContext.Read(element, from, to, slot => slot == ArbitraryProperty.TranslateY);

            // Act
            var plan = MotionSpringClassParser.Resolve(from, to, 60f, 30f, context);

            // Assert
            Assert.That(new[] { plan.TranslateX?.from ?? float.NaN, plan.TranslateX?.to ?? float.NaN,
                    plan.TranslateY?.from ?? float.NaN, plan.TranslateY?.to ?? float.NaN },
                Is.EqualTo(new[] { 60f, 60f, 30f, 20f }).Within(1e-5f));
        }

        [Test]
        public void Given_ANullLeavingClassArray_When_AWidthTargetIsResolved_Then_ItStartsFromTheCurrentWidth()
        {
            // Arrange
            var element = MountInFixedParent();

            // Act
            var plan = Resolve(element, null, new[] { "w-[100px]" });

            // Assert
            Assert.That(Lengths(plan), Is.EqualTo("Width:200->100"));
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

        [TestCase(TransitionType.Spring)]
        [TestCase(TransitionType.Bezier)]
        public void Given_RunningChannelsBesideOtherNativeSlots_When_NewTargetsAreSelected_Then_OwnedFramesAndNativePaintStayDistinct(TransitionType type)
        {
            // Arrange
            var pairs = new[]
            {
                (owned: ArbitraryProperty.Opacity, other: ArbitraryProperty.TranslateX),
                (owned: ArbitraryProperty.TranslateX, other: ArbitraryProperty.Opacity),
                (owned: ArbitraryProperty.TranslateY, other: ArbitraryProperty.Scale),
                (owned: ArbitraryProperty.Scale, other: ArbitraryProperty.Rotate),
                (owned: ArbitraryProperty.Rotate, other: ArbitraryProperty.Opacity),
                (owned: ArbitraryProperty.BackgroundColor, other: ArbitraryProperty.Width),
                (owned: ArbitraryProperty.Width, other: ArbitraryProperty.BackgroundColor),
            };
            var elements = new VisualElement[pairs.Length];
            var schedulers = new StyleAnimationScheduler[pairs.Length];
            var protectedSlots = new ArbitraryProperty[pairs.Length];
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(_window.rootVisualElement.panel, () => now);
            for (var i = 0; i < pairs.Length; i++)
            {
                var pair = pairs[i];
                var element = elements[i] = MountInFixedParent();
                var scheduler = schedulers[i] = new StyleAnimationScheduler();
                var oldTarget = ProbeChannelClass(pair.owned, initial: true);
                Play(scheduler, element, type, false, new[] { ProbeChannelClass(pair.owned, initial: false) }, new[] { oldTarget });
                element.RemoveFromClassList(oldTarget);
                protectedSlots[i] = pair.owned == ArbitraryProperty.Width || pair.other == ArbitraryProperty.Width
                    ? ArbitraryProperty.Opacity : ArbitraryProperty.Width;
                WriteProbeChannel(element, protectedSlots[i], protectedSlots[i] == ArbitraryProperty.Width ? 40f : 0.25f);
                WriteProbeChannel(element, pair.owned, pair.owned == ArbitraryProperty.Width ? 40f : 0.2f);
                WriteProbeChannel(element, pair.other, pair.other == ArbitraryProperty.Width ? 40f : 0.2f);
            }
            ForcePanelUpdate(_window.rootVisualElement.panel);
            for (var i = 0; i < pairs.Length; i++)
            {
                var element = elements[i];
                element.style.transitionProperty = new List<StylePropertyName>
                {
                    new(ProbeNativeProperty(pairs[i].owned)), new(ProbeNativeProperty(pairs[i].other)),
                };
                element.style.transitionDuration = new List<TimeValue> { new(1f, TimeUnit.Second) };
                element.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.Linear) };
                WriteProbeChannel(element, pairs[i].owned, pairs[i].owned == ArbitraryProperty.Width ? 100f : 0.8f);
                WriteProbeChannel(element, pairs[i].other, pairs[i].other == ArbitraryProperty.Width ? 100f : 0.8f);
            }
            ForcePanelUpdate(_window.rootVisualElement.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(_window.rootVisualElement.panel);
            now += 0.5;
            EditorPanelTestHelpers.DriveAnimationsOnce(_window.rootVisualElement.panel);
            ForcePanelUpdate(_window.rootVisualElement.panel);
            var before = new (bool ownedNative, bool otherNative, bool ownedDiffers, bool otherDiffers,
                float ownedInline, float otherPaint, float protectedInline)[pairs.Length];
            for (var i = 0; i < pairs.Length; i++)
            {
                var element = elements[i];
                var ownedInline = ReadProbeChannel(element, pairs[i].owned, resolved: false);
                var otherInline = ReadProbeChannel(element, pairs[i].other, resolved: false);
                var ownedPaint = ReadProbeChannel(element, pairs[i].owned, resolved: true);
                var otherPaint = ReadProbeChannel(element, pairs[i].other, resolved: true);
                before[i] = (ProbeNativeRunning(element, pairs[i].owned), ProbeNativeRunning(element, pairs[i].other),
                    !ownedInline.Equals(ownedPaint), !otherInline.Equals(otherPaint), ownedInline, otherPaint,
                    ReadProbeChannel(element, protectedSlots[i], resolved: false));
            }

            // Act
            var observed = new List<(ArbitraryProperty, bool, bool, bool, bool, bool, bool, bool, bool)>();
            for (var i = 0; i < pairs.Length; i++)
            {
                var pair = pairs[i];
                var element = elements[i];
                var scheduler = schedulers[i];
                Play(scheduler, element, type, false, new string[0], new[]
                {
                    ProbeChannelClass(pair.owned), ProbeChannelClass(pair.other), ProbeChannelClass(protectedSlots[i]),
                });
                var entries = (System.Collections.IDictionary)typeof(StyleAnimationScheduler)
                    .GetField("_pendingEnters", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .GetValue(scheduler);
                var registered = entries.Contains(element);
                var ownedStart = ReadProbeChannel(element, pair.owned, resolved: false);
                var otherStart = ReadProbeChannel(element, pair.other, resolved: false);
                if (registered)
                {
                    var pending = entries[element];
                    if (type == TransitionType.Spring)
                    {
                        MotionSpringDriver.Step(element, (MotionSpringState)pending.GetType().GetField("Spring").GetValue(pending), 0.1f);
                    }
                    else
                    {
                        BezierTweenDriver.Step(element, (BezierTweenState)pending.GetType().GetField("Bezier").GetValue(pending), 0.1f);
                    }
                }
                observed.Add((pair.owned, before[i].ownedNative, before[i].otherNative, before[i].ownedDiffers,
                    before[i].otherDiffers, registered, ownedStart.Equals(before[i].ownedInline),
                    otherStart.Equals(before[i].otherPaint),
                    ReadProbeChannel(element, protectedSlots[i], resolved: false).Equals(before[i].protectedInline)));
                scheduler.CancelAll();
            }

            // Assert
            Assert.That(observed, Is.EqualTo(System.Array.ConvertAll(pairs,
                pair => (pair.owned, true, true, true, true, true, true, true, true))));
        }

        [TestCase(TransitionType.Spring)]
        [TestCase(TransitionType.Bezier)]
        public void Given_AMountedClipWithCustomLengthAndOpacity_When_ChannelsAreReleased_Then_LayoutTransitionsReturnWhileTheWrapperStaysMounted(TransitionType type)
        {
            // Arrange
            var placeholder = MountInFixedParent();
            var parent = placeholder.parent;
            placeholder.RemoveFromHierarchy();
            var operations = new[] { "clear", "last-length" };
            _mounted = V.Mount(parent, V.Fragment(operations.Select(operation =>
                V.Div(name: operation, className: "clip-path-[inset(0)] transition-all duration-1000")).ToArray()));
            ForcePanelUpdate(parent.panel);
            var elements = operations.Select(operation => parent.Q<VisualElement>(operation)).ToArray();
            var wrappers = elements.Select(ClipPathLayoutBox.Of).ToArray();
            var states = new object[elements.Length];
            var held = new bool[elements.Length];
            for (var i = 0; i < elements.Length; i++)
            {
                var plan = new MotionSpringClassParser.SpringPlan
                {
                    Opacity = (0.25f, 0.75f),
                    Lengths = new List<MotionSpringClassParser.LengthChannelPlan>
                    {
                        new(ArbitraryProperty.Width, 40f, 100f, LengthUnit.Pixel),
                    },
                };
                if (type == TransitionType.Spring)
                {
                    var state = MotionSpringDriver.Create(plan, 100f, 10f, 1f);
                    states[i] = state;
                    MotionSpringDriver.ApplyCurrentValues(elements[i], state);
                }
                else
                {
                    var state = BezierTweenDriver.Create(plan, 0f, 0f, 1f, 1f, 1f);
                    states[i] = state;
                    BezierTweenDriver.ApplyCurrentValues(elements[i], state);
                }
                var properties = wrappers[i].style.transitionProperty.value;
                held[i] = properties is { Count: 1 } && properties[0] == new StylePropertyName("none");
            }

            // Act
            var observed = new List<(string, bool, bool, bool, bool, bool)>();
            for (var i = 0; i < elements.Length; i++)
            {
                var element = elements[i];
                var wrapper = wrappers[i];
                var releaseLength = i == 1;
                if (type == TransitionType.Spring)
                {
                    var state = (MotionSpringState)states[i];
                    if (releaseLength) MotionSpringDriver.ReleaseChannels(element, state, StyleLonghandSet.Of(StyleLonghand.Width));
                    else MotionSpringDriver.ClearInlineOverrides(element, state);
                }
                else
                {
                    var state = (BezierTweenState)states[i];
                    if (releaseLength) BezierTweenDriver.ReleaseChannels(element, state, StyleLonghandSet.Of(StyleLonghand.Width));
                    else BezierTweenDriver.ClearInlineOverrides(element, state);
                }
                var opacity = element.style.opacity;
                observed.Add((operations[i], !ReferenceEquals(element, wrapper), wrapper.parent == parent,
                    held[i], wrapper.style.transitionProperty.keyword == StyleKeyword.Null,
                    releaseLength ? opacity.keyword == StyleKeyword.Undefined && opacity.value.Equals(0.25f)
                        : opacity.keyword == StyleKeyword.Null));
                if (releaseLength)
                {
                    if (type == TransitionType.Spring) MotionSpringDriver.ClearInlineOverrides(element, (MotionSpringState)states[i]);
                    else BezierTweenDriver.ClearInlineOverrides(element, (BezierTweenState)states[i]);
                }
            }

            // Assert
            Assert.That(observed, Is.EqualTo(System.Array.ConvertAll(operations,
                operation => (operation, true, true, true, true, true))));
        }

        [Test]
        public void Given_ARunningNativeTween_When_ACustomPlayRetargets_Then_ItStartsFromPaintAndKeepsTheUnrelatedHolder()
        {
            // Arrange
            var types = new[] { TransitionType.Spring, TransitionType.Bezier };
            var elements = new VisualElement[types.Length];
            var schedulers = new StyleAnimationScheduler[types.Length];
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(_window.rootVisualElement.panel, () => now);
            for (var i = 0; i < types.Length; i++)
            {
                elements[i] = MountInFixedParent();
                elements[i].AddToClassList("opacity-0");
                elements[i].style.width = 40f;
                schedulers[i] = new StyleAnimationScheduler();
            }
            ForcePanelUpdate(_window.rootVisualElement.panel);
            for (var i = 0; i < types.Length; i++)
            {
                Play(schedulers[i], elements[i], TransitionType.Tween, false, new[] { "opacity-0" }, new[] { "opacity-100" });
            }
            now += 0.04;
            EditorPanelTestHelpers.DriveSchedulerOnce(_window.rootVisualElement.panel);
            ForcePanelUpdate(_window.rootVisualElement.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(_window.rootVisualElement.panel);
            now += 0.5;
            EditorPanelTestHelpers.DriveAnimationsOnce(_window.rootVisualElement.panel);
            ForcePanelUpdate(_window.rootVisualElement.panel);
            foreach (var element in elements) element.style.opacity = 0.8f;
            ForcePanelUpdate(_window.rootVisualElement.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(_window.rootVisualElement.panel);
            now += 0.25;
            EditorPanelTestHelpers.DriveAnimationsOnce(_window.rootVisualElement.panel);
            ForcePanelUpdate(_window.rootVisualElement.panel);
            var pendingField = typeof(StyleAnimationScheduler).GetField("_pendingEnters",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var before = new (bool oldPending, bool nativeOnly, bool nativeRunning, bool paintDiffers, float paint)[types.Length];
            for (var i = 0; i < types.Length; i++)
            {
                var entries = (System.Collections.IDictionary)pendingField.GetValue(schedulers[i]);
                var pending = entries[elements[i]];
                var paint = elements[i].resolvedStyle.opacity;
                before[i] = (pending != null, pending != null
                    && pending.GetType().GetField("Spring").GetValue(pending) == null
                    && pending.GetType().GetField("Bezier").GetValue(pending) == null,
                    ProbeNativeRunning(elements[i], ArbitraryProperty.Opacity),
                    !paint.Equals(elements[i].style.opacity.value), paint);
            }

            // Act
            var observed = new List<(TransitionType, bool, bool, bool, bool, bool, bool, bool)>();
            for (var i = 0; i < types.Length; i++)
            {
                Play(schedulers[i], elements[i], types[i], false, new string[0], new[] { "opacity-50" });
                var entries = (System.Collections.IDictionary)pendingField.GetValue(schedulers[i]);
                observed.Add((types[i], before[i].oldPending, before[i].nativeOnly, before[i].nativeRunning,
                    before[i].paintDiffers, entries.Contains(elements[i]),
                    elements[i].style.opacity.value.Equals(before[i].paint), elements[i].style.width.value.value.Equals(40f)));
                schedulers[i].CancelAll();
            }

            // Assert
            Assert.That(observed, Is.EqualTo(System.Array.ConvertAll(types,
                type => (type, true, true, true, true, true, true, true))));
        }

        [Test]
        public void Given_ARestingImportantUniformScale_When_AnImportantUniformTargetAlsoNamesAPlainAxis_Then_ThePerAxisPaintIsRetained()
        {
            // Arrange
            var placeholder = MountInFixedParent();
            var parent = placeholder.parent;
            placeholder.RemoveFromHierarchy();
            var names = new[] { "x-target", "y-target" };
            var axes = new[] { "scale-x-[.5]", "scale-y-[.5]" };
            _mounted = V.Mount(parent, V.Fragment(names.Select(name =>
                V.Div(name: name, className: "!scale-[1.4]")).ToArray()));
            ForcePanelUpdate(parent.panel);
            var elements = names.Select(name => parent.Q<VisualElement>(name)).ToArray();
            foreach (var element in elements) element.AddToClassList("!scale-[1.4]");
            var plans = new MotionSpringClassParser.SpringPlan[elements.Length];
            var validHolders = new bool[elements.Length];
            for (var i = 0; i < elements.Length; i++)
            {
                var to = new[] { "!scale-[2]", axes[i] };
                var context = MotionSlotContext.Read(elements[i], new string[0], to, _ => false);
                validHolders[i] = context.RestingClasses().Contains("!scale-[1.4]")
                    && StyleArbitraryValueResolver.HasImportantInlineOutside(elements[i],
                    StyleLonghandSet.Of(StyleLonghand.Scale), new HashSet<ArbitraryProperty>())
                    && !context.HasImportantInlineOutsideSwap(ArbitraryProperty.Scale);
                plans[i] = MotionSpringClassParser.Resolve(new string[0], to, 0f, 0f, context);
            }

            // Act
            _mounted.Render(V.Fragment(names.Select((name, i) =>
                V.Div(name: name, className: "!scale-[1.4] !scale-[2] " + axes[i])).ToArray()));
            var observed = new float[elements.Length * 2];
            for (var i = 0; i < elements.Length; i++)
            {
                var state = MotionSpringDriver.Create(plans[i], 100f, 10f, 1f);
                if (state != null) MotionSpringDriver.ApplyCurrentValues(elements[i], state);
                var scale = elements[i].style.scale.value.value;
                var noUniformOverride = validHolders[i] && plans[i].Scale == null;
                observed[i * 2] = noUniformOverride ? scale.x : float.NaN;
                observed[i * 2 + 1] = noUniformOverride ? scale.y : float.NaN;
                if (state != null) MotionSpringDriver.ClearInlineOverrides(elements[i], state);
            }

            // Assert
            Assert.That(observed, Is.EqualTo(new[] { 0.5f, 2f, 2f, 0.5f }).Within(1e-5f));
        }

        [Test]
        public void Given_AMountedClipWrapperWithACustomWidthPlay_When_AForeignTransitionListIsWritten_Then_TheNextFramePreservesItsNamesAndTimings()
        {
            // Arrange
            var placeholder = MountInFixedParent();
            var parent = placeholder.parent;
            placeholder.RemoveFromHierarchy();
            var types = new[] { TransitionType.Spring, TransitionType.Bezier };
            _mounted = V.Mount(parent, V.Fragment(types.Select(type =>
                V.Div(name: type.ToString(), className: "clip-path-[inset(0)] transition-all duration-1000")).ToArray()));
            ForcePanelUpdate(parent.panel);
            var properties = new[] { new StylePropertyName("height"), new StylePropertyName("opacity") };
            var durations = new[] { new TimeValue(0.5f), new TimeValue(1.5f) };
            var delays = new[] { new TimeValue(0.25f), new TimeValue(0.75f) };
            var easing = new[] { new EasingFunction(EasingMode.Linear), new EasingFunction(EasingMode.EaseOut) };
            var observed = new List<(TransitionType, bool, bool, bool, bool, bool, bool, bool, bool, bool)>();
            foreach (var type in types)
            {
                var element = parent.Q<VisualElement>(type.ToString());
                var wrapper = ClipPathLayoutBox.Of(element);
                var plan = new MotionSpringClassParser.SpringPlan
                {
                    Lengths = new List<MotionSpringClassParser.LengthChannelPlan>
                    {
                        new(ArbitraryProperty.Width, 40f, 100f, LengthUnit.Pixel),
                    },
                };
                var spring = type == TransitionType.Spring ? MotionSpringDriver.Create(plan, 100f, 10f, 1f) : null;
                var bezier = type == TransitionType.Bezier ? BezierTweenDriver.Create(plan, 0f, 0f, 1f, 1f, 1f) : null;
                if (spring != null) MotionSpringDriver.ApplyCurrentValues(element, spring);
                else BezierTweenDriver.ApplyCurrentValues(element, bezier);
                var held = wrapper.style.transitionProperty.value.SequenceEqual(new[] { new StylePropertyName("none") });
                var initialOwner = spring != null ? spring.NativeLayoutOwner : bezier.NativeLayoutOwner;
                wrapper.style.transitionProperty = new List<StylePropertyName>(properties);
                wrapper.style.transitionDuration = new List<TimeValue>(durations);
                wrapper.style.transitionDelay = new List<TimeValue>(delays);
                wrapper.style.transitionTimingFunction = new List<EasingFunction>(easing);
                var foreignWrite = wrapper.style.transitionProperty.value.SequenceEqual(properties)
                    && wrapper.style.transitionDuration.value.SequenceEqual(durations)
                    && wrapper.style.transitionDelay.value.SequenceEqual(delays)
                    && wrapper.style.transitionTimingFunction.value.SequenceEqual(easing);

                // Act
                if (spring != null) MotionSpringDriver.Step(element, spring, 0.05f);
                else BezierTweenDriver.Step(element, bezier, 0.05f);
                var nextOwner = spring != null ? spring.NativeLayoutOwner : bezier.NativeLayoutOwner;
                observed.Add((type, !ReferenceEquals(element, wrapper), held, foreignWrite,
                    ReferenceEquals(initialOwner, wrapper) && ReferenceEquals(nextOwner, wrapper),
                    wrapper.style.width.value.value != 40f,
                    wrapper.style.transitionProperty.value.SequenceEqual(properties),
                    wrapper.style.transitionDuration.value.SequenceEqual(durations),
                    wrapper.style.transitionDelay.value.SequenceEqual(delays),
                    wrapper.style.transitionTimingFunction.value.SequenceEqual(easing)));
                if (spring != null) MotionSpringDriver.ClearInlineOverrides(element, spring);
                else BezierTweenDriver.ClearInlineOverrides(element, bezier);
            }

            // Assert
            Assert.That(observed, Is.EqualTo(System.Array.ConvertAll(types,
                type => (type, true, true, true, true, true, true, true, true, true))));
        }

        [Test]
        public void Given_AMountedInlineOpacityWithAConstantBezierExit_When_TheKeyIsRemoved_Then_TheExitDoesNotCompleteInTheRemovalRender()
        {
            // Arrange
            var placeholder = MountInFixedParent();
            var parent = placeholder.parent;
            placeholder.RemoveFromHierarchy();
            var variants = new Dictionary<string, MotionVariant>
            {
                ["rest"] = "opacity-50",
                ["gone"] = "opacity-0",
            };
            var transition = new StyleTransitionConfig { Type = TransitionType.Bezier, DurationSec = 1f };
            var completions = 0;
            var motion = V.Motion(key: "entry", name: "constant-exit", className: "opacity-[0.7]",
                variants: variants, animate: "rest", exit: "gone", transition: transition);
            _mounted = V.Mount(parent, V.AnimatePresence(key: "presence", initial: false,
                onExitComplete: () => completions++, children: new VNode[] { motion }));
            ForcePanelUpdate(parent.panel);
            var element = parent.Q<VisualElement>("constant-exit");
            element.AddToClassList("opacity-[0.7]");
            var context = MotionSlotContext.Read(element, new[] { "opacity-50" }, new[] { "opacity-0" }, _ => false);
            var rawHolder = context.RestingClasses().Contains("opacity-[0.7]");
            var registeredHolder = context.HoldsInlineOutsideSwap(ArbitraryProperty.Opacity);
            var inlineAtRest = element.style.opacity.value;
            var attachedAtRest = element.parent == parent;

            // Act
            _mounted.Render(V.AnimatePresence(key: "presence", initial: false,
                onExitComplete: () => completions++, children: new VNode[0]));

            // Assert
            Assert.That((rawHolder, registeredHolder, inlineAtRest, attachedAtRest,
                    ReferenceEquals(parent.Q<VisualElement>("constant-exit"), element), element.parent == parent,
                    completions), Is.EqualTo((true, true, 0.7f, true, true, true, 0)));
        }

        private sealed class PresenceProbeStore : Store<int>
        {
            public PresenceProbeStore() : base(0) { }
            public void Set(int stage) => SetState(_ => stage);
            protected override void ResetCore() => SetState(_ => 0);
        }

        private readonly record struct PresenceProbeProps(PresenceProbeStore Store, System.Func<int, VNode> Render);

        [Component(Compiler = false)]
        private static VNode PresenceProbeHost(PresenceProbeProps props)
        {
            var stage = Hooks.UseStore(props.Store, value => value);
            return props.Render(stage);
        }

        [Test]
        public void Given_ACompletedFragmentExit_When_ReentryHasOnlyATweenedInitialPose_Then_AnInstantClassicEnterKeepsThePoseHeldUntilItsSwap()
        {
            // Arrange
            var placeholder = MountInFixedParent();
            var parent = placeholder.parent;
            placeholder.RemoveFromHierarchy();
            var variants = new Dictionary<string, MotionVariant>
            {
                ["rest"] = "pose-rest",
                ["right"] = new MotionVariant("translate-x-[40px]", new StyleTransitionConfig { DurationSec = 0.3f }),
                ["gone"] = new MotionVariant("pose-exit", new StyleTransitionConfig { Type = TransitionType.Spring }),
            };
            MotionNode reentry = null;
            var originalMotion = V.Motion(key: "variant", name: "variant", className: "translate-x-[0px]",
                variants: variants, animate: "rest", exit: "gone", transition: new StyleTransitionConfig { DurationSec = 0.3f });
            VNode Entry(int stage) => V.AnimatePresence(key: "presence", initial: false, children: stage == 1
                ? new VNode[0]
                : new VNode[] { V.Fragment(new[] { stage == 0 ? originalMotion : reentry }, key: "entry") });
            using var store = new PresenceProbeStore();
            _mounted = V.Mount(parent, V.Component(PresenceProbeHost, new PresenceProbeProps(store, Entry)));
            ForcePanelUpdate(parent.panel);
            var element = parent.Q<VisualElement>("variant");
            var ctx = _mounted.Root.Reconciler.Context;
            store.Set(1);
            _mounted.FlushStateForTest();
            var presence = ctx.PresenceStates.Values.Single();
            var completedGhost = presence.ExitComplete.Contains("entry");
            var attachedGhost = element.parent == parent;
            var memoBefore = ReferenceEquals(presence.MotionElements.GetValueOrDefault("entry"), element);
            var instantEnters = 0;
            var enterTargetMatched = false;
            MotionHeldInline enterHold = null;
            var enterSwapPending = false;
            var enterInline = float.NaN;

            // Act
            reentry = V.Motion(key: "variant", name: "variant", className: "translate-x-[0px]",
                variants: variants, initial: "right", transition: StyleTransitionConfig.None,
                onEnterComplete: () =>
                {
                    instantEnters++;
                    enterTargetMatched = ReferenceEquals(ctx.MotionNodes.GetValueOrDefault(element), reentry)
                        && ReferenceEquals(presence.MotionElements.GetValueOrDefault("entry"), element)
                        && ReferenceEquals(parent.hierarchy[0], element);
                    ctx.MotionHeldInline.TryGetValue(element, out enterHold);
                    enterSwapPending = enterHold != null && ctx.StyleAnimationScheduler.IsSwapPending(element, enterHold.Release);
                    enterInline = element.style.translate.value.x.value;
                });
            store.Set(2);
            _mounted.FlushStateForTest();
            var holdTargetsInitial = enterHold != null && enterHold.Target.Contains("translate-x-[40px]");
            var retainedHold = enterHold != null && ReferenceEquals(ctx.MotionHeldInline.GetValueOrDefault(element), enterHold);
            var swapPending = enterHold != null && ctx.StyleAnimationScheduler.IsSwapPending(element, enterHold.Release);

            // Assert
            Assert.That((completedGhost, attachedGhost, memoBefore, enterTargetMatched,
                    instantEnters, holdTargetsInitial, enterSwapPending, enterInline,
                    retainedHold, swapPending, element.style.translate.value.x.value),
                Is.EqualTo((true, true, true, true, 1, true, true, 0f, true, true, 0f)));
        }

        private static string ProbeChannelClass(ArbitraryProperty slot, bool? initial = null)
        {
            (string start, string end, string target) classes = slot switch
            {
                ArbitraryProperty.Opacity => (start: "opacity-0", end: "opacity-100", target: "opacity-50"),
                ArbitraryProperty.TranslateX => ("translate-x-[0px]", "translate-x-[100px]", "translate-x-[30px]"),
                ArbitraryProperty.TranslateY => ("translate-y-[0px]", "translate-y-[100px]", "translate-y-[30px]"),
                ArbitraryProperty.Scale => ("scale-50", "scale-150", "scale-100"),
                ArbitraryProperty.Rotate => ("rotate-0", "rotate-90", "rotate-45"),
                ArbitraryProperty.BackgroundColor => ("bg-[#000000]", "bg-[#ffffff]", "bg-[#00ff00]"),
                ArbitraryProperty.Width => ("w-[40px]", "w-[200px]", "w-20"),
                _ => throw new System.ArgumentOutOfRangeException(nameof(slot)),
            };
            return initial.HasValue ? initial.Value ? classes.end : classes.start : classes.target;
        }

        private static string ProbeNativeProperty(ArbitraryProperty slot) => slot switch
        {
            ArbitraryProperty.Opacity => "opacity",
            ArbitraryProperty.TranslateX or ArbitraryProperty.TranslateY => "translate",
            ArbitraryProperty.Scale => "scale",
            ArbitraryProperty.Rotate => "rotate",
            ArbitraryProperty.BackgroundColor => "background-color",
            ArbitraryProperty.Width => "width",
            _ => throw new System.ArgumentOutOfRangeException(nameof(slot)),
        };

        private static void WriteProbeChannel(VisualElement element, ArbitraryProperty slot, float value)
        {
            switch (slot)
            {
                case ArbitraryProperty.Opacity: element.style.opacity = value; break;
                case ArbitraryProperty.TranslateX:
                case ArbitraryProperty.TranslateY: element.style.translate = new Translate(value, value, 0f); break;
                case ArbitraryProperty.Scale: element.style.scale = new Scale(new Vector2(value, value)); break;
                case ArbitraryProperty.Rotate: element.style.rotate = new Rotate(new Angle(value)); break;
                case ArbitraryProperty.BackgroundColor: element.style.backgroundColor = new Color(value, value, value, 1f); break;
                case ArbitraryProperty.Width: element.style.width = value; break;
                default: throw new System.ArgumentOutOfRangeException(nameof(slot));
            }
        }

        private static float ReadProbeChannel(VisualElement element, ArbitraryProperty slot, bool resolved) => slot switch
        {
            ArbitraryProperty.Opacity => resolved ? element.resolvedStyle.opacity : element.style.opacity.value,
            ArbitraryProperty.TranslateX => resolved ? element.resolvedStyle.translate.x : element.style.translate.value.x.value,
            ArbitraryProperty.TranslateY => resolved ? element.resolvedStyle.translate.y : element.style.translate.value.y.value,
            ArbitraryProperty.Scale => resolved ? element.resolvedStyle.scale.value.x : element.style.scale.value.value.x,
            ArbitraryProperty.Rotate => resolved ? element.resolvedStyle.rotate.angle.value : element.style.rotate.value.angle.value,
            ArbitraryProperty.BackgroundColor => resolved ? element.resolvedStyle.backgroundColor.r : element.style.backgroundColor.value.r,
            ArbitraryProperty.Width => resolved ? element.resolvedStyle.width : element.style.width.value.value,
            _ => throw new System.ArgumentOutOfRangeException(nameof(slot)),
        };

        private static bool ProbeNativeRunning(VisualElement element, ArbitraryProperty slot)
        {
            var method = EngineMember.HasRunningStyleAnimation.ResolveMethod();
            var id = EngineMember.StylePropertyNameId.ResolveProperty()?.GetValue(new StylePropertyName(ProbeNativeProperty(slot)));
            return id != null && method?.Invoke(element, new[] { id }) is true;
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

        [TestCase(ArbitraryProperty.Opacity, "opacity-[0.25]", 0.25f)]
        [TestCase(ArbitraryProperty.TranslateX, "translate-x-[17px]", 17f)]
        [TestCase(ArbitraryProperty.TranslateY, "translate-y-[19px]", 19f)]
        [TestCase(ArbitraryProperty.Scale, "scale-[0.5]", 0.5f)]
        [TestCase(ArbitraryProperty.Rotate, "rotate-[15deg]", 15f)]
        [TestCase(ArbitraryProperty.Width, "w-[23px]", 23f)]
        [TestCase(ArbitraryProperty.Height, "h-[29px]", 29f)]
        [TestCase(ArbitraryProperty.PaddingTop, "pt-[7px]", 7f)]
        [TestCase(ArbitraryProperty.BorderTopWidth, "border-t-[3px]", 3f)]
        [TestCase(ArbitraryProperty.BorderRightWidth, "border-r-[4px]", 4f)]
        [TestCase(ArbitraryProperty.BorderBottomWidth, "border-b-[5px]", 5f)]
        [TestCase(ArbitraryProperty.BorderLeftWidth, "border-l-[6px]", 6f)]
        public void Given_AnInlineNumericSlot_When_TheProviderSamplesIt_Then_ItReturnsTheDeclaredValueAndHolder(
            ArbitraryProperty slot, string token, float expected)
        {
            // Arrange
            var element = MountInFixedParent();
            StyleArbitraryValueResolver.ApplyClassToken(element, token, StyleLayerPriority.Base);
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(slot, LengthUnit.Pixel, out var value);
            var holder = context.HoldsInlineOutsideSwap(slot);

            // Assert
            Assert.That(new[] { read && value.Property == slot && value.Unit == LengthUnit.Pixel ? value.Value : float.NaN, holder ? 1f : 0f },
                Is.EqualTo(new[] { expected, 1f }).Within(0.001f));
        }

        [TestCase(ArbitraryProperty.TextColor, "text-[#ff0000]")]
        [TestCase(ArbitraryProperty.BackgroundColor, "bg-[#ff0000]")]
        [TestCase(ArbitraryProperty.BorderColor, "border-[#ff0000]")]
        public void Given_AnInlineColor_When_TheProviderSamplesIt_Then_ItReturnsThatColor(ArbitraryProperty slot, string token)
        {
            // Arrange
            var element = MountInFixedParent();
            StyleArbitraryValueResolver.ApplyClassToken(element, token, StyleLayerPriority.Base);
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(slot, LengthUnit.Percent, out var value);

            // Assert
            Assert.That((read, value.Property, value.Color), Is.EqualTo((true, slot, Color.red)));
        }

        [TestCase(ArbitraryProperty.Width, "w-[50%]", LengthUnit.Pixel, 100f)]
        [TestCase(ArbitraryProperty.Width, "w-[40px]", LengthUnit.Percent, 20f)]
        [TestCase(ArbitraryProperty.Height, "h-[25%]", LengthUnit.Pixel, 25f)]
        [TestCase(ArbitraryProperty.Height, "h-[40px]", LengthUnit.Percent, 40f)]
        public void Given_ADimensionInAnotherUnit_When_TheProviderSamplesIt_Then_ItUsesTheParentBasis(
            ArbitraryProperty slot, string token, LengthUnit unit, float expected)
        {
            // Arrange
            var element = MountInFixedParent();
            StyleArbitraryValueResolver.ApplyClassToken(element, token, StyleLayerPriority.Base);
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(slot, unit, out var value);

            // Assert
            Assert.That(read && value.Unit == unit ? value.Value : float.NaN, Is.EqualTo(expected).Within(0.02f));
        }

        [TestCase("pt-[10%]", false)]
        [TestCase("w-[50%]", true)]
        [TestCase("w-[40px]", false, true)]
        public void Given_AConversionWithoutASupportedBasis_When_TheProviderSamplesIt_Then_ItDeclinesTheValue(
            string token, bool zeroBasis, bool infiniteBasis = false)
        {
            // Arrange
            var element = MountInFixedParent();
            if (zeroBasis)
            {
                element.parent.style.width = 0f;
                ForcePanelUpdate(element.panel);
            }
            StyleArbitraryValueResolver.ApplyClassToken(element, token, StyleLayerPriority.Base);
            if (infiniteBasis)
            {
                element.parent.style.width = StyleKeyword.Auto;
                element.parent.style.alignSelf = Align.FlexStart;
                element.parent.style.flexDirection = FlexDirection.Row;
                element.style.borderLeftWidth = float.PositiveInfinity;
                ForcePanelUpdate(element.panel);
            }
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(zeroBasis || infiniteBasis ? ArbitraryProperty.Width : ArbitraryProperty.PaddingTop,
                infiniteBasis ? LengthUnit.Percent : LengthUnit.Pixel, out _);
            var basisPresent = !infiniteBasis || float.IsPositiveInfinity(element.parent.contentRect.width);

            // Assert
            Assert.That((basisPresent, read), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_FlaggedLengths_When_ReadInline_Then_TheGettersNormalizeNonNumericKeywords()
        {
            // Arrange
            var element = new VisualElement();
            var readers = (Dictionary<ArbitraryProperty, System.Func<IStyle, StyleLength>>)typeof(MotionSlotContext)
                .GetField("s_inlineLengths", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .GetValue(null);
            var properties = typeof(IStyle).GetProperties().Where(property => property.PropertyType == typeof(StyleLength)).ToArray();
            var samples = new[] { (new Length(17f), StyleKeyword.Undefined),
                (Length.Auto(), StyleKeyword.Auto), (Length.None(), StyleKeyword.None) };
            var normalized = new List<bool>();

            // Act
            foreach (var sample in samples)
            {
                var input = new StyleLength(sample.Item1) { keyword = StyleKeyword.Undefined };
                foreach (var property in properties) property.SetValue(element.style, input);
                foreach (var read in readers.Values)
                {
                    var inline = read(element.style);
                    normalized.Add(inline.keyword == sample.Item2 && inline.value.Equals(input.value));
                }
            }

            // Assert
            Assert.That(normalized, Is.EqualTo(Enumerable.Repeat(true, 75)));
        }

        [TestCase(ArbitraryProperty.Width, 200f)]
        [TestCase(ArbitraryProperty.Opacity, 1f)]
        [TestCase(ArbitraryProperty.Scale, 1f)]
        [TestCase(ArbitraryProperty.Rotate, 0f)]
        [TestCase(ArbitraryProperty.TranslateX, 0f)]
        [TestCase(ArbitraryProperty.TranslateY, 0f)]
        [TestCase(ArbitraryProperty.Opacity, 0.5f, true)]
        [TestCase(ArbitraryProperty.Width, 100f, false, LengthUnit.Percent)]
        public void Given_AComputedSlotWithoutAnInlineOverride_When_TheProviderSamplesIt_Then_ItReadsTheResolvedValue(
            ArbitraryProperty slot, float expected, bool unrelatedInlineScale = false, LengthUnit unit = LengthUnit.Pixel)
        {
            // Arrange
            var element = MountInFixedParent();
            if (unrelatedInlineScale)
            {
                element.AddToClassList("opacity-50");
                element.style.scale = new Scale(new Vector3(2f, 2f, 1f));
                ForcePanelUpdate(element.panel);
            }
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(slot, unit, out var value);
            var scalePresent = !unrelatedInlineScale || element.style.scale.keyword == StyleKeyword.Undefined;

            // Assert
            Assert.That(new[] { read && value.Property == slot && value.Unit == unit && scalePresent ? value.Value : float.NaN,
                    context.HoldsInlineOutsideSwap(slot) ? 1f : 0f, context.HasImportantInlineOutsideSwap(slot) ? 1f : 0f },
                Is.EqualTo(new[] { expected, 0f, 0f }).Within(0.02f));
        }

        [TestCase(0.75f)]
        [TestCase(0.50000006f)]
        public void Given_ANonuniformInlineScale_When_TheProviderSamplesIt_Then_ItDeclinesTheValue(float scaleY)
        {
            // Arrange
            var element = MountInFixedParent();
            element.style.scale = new Scale(new Vector3(0.5f, scaleY, 1f));
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(ArbitraryProperty.Scale, LengthUnit.Pixel, out _);

            // Assert
            Assert.That(read, Is.False);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void Given_OnlyOneInlineBorderEdge_When_TheProviderClassifiesIt_Then_ItFindsThatHolder(int edge)
        {
            // Arrange
            var element = MountInFixedParent();
            SetBorderEdge(element, edge, Color.red);
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var holder = context.HoldsInlineOutsideSwap(ArbitraryProperty.BorderColor);

            // Assert
            Assert.That(holder, Is.True);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void Given_OneDifferentBorderEdge_When_TheProviderSamplesUniformColor_Then_ItDeclinesTheValue(int edge)
        {
            // Arrange
            var element = MountInFixedParent();
            for (var i = 0; i < 4; i++) SetBorderEdge(element, i, Color.red);
            SetBorderEdge(element, edge, Color.blue);
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(ArbitraryProperty.BorderColor, LengthUnit.Pixel, out _);

            // Assert
            Assert.That(read, Is.False);
        }

        [Test]
        public void Given_DifferentSlotsOnBothSwapSides_When_TheProviderClassifiesHolders_Then_ItRegistersBothSides()
        {
            // Arrange
            var element = MountInFixedParent();
            StyleArbitraryValueResolver.ApplyClassToken(element, "w-[40px]", StyleLayerPriority.Base);
            StyleArbitraryValueResolver.ApplyClassToken(element, "h-[20px]", StyleLayerPriority.Base);
            element.style.opacity = 0.5f;

            // Act
            var context = MotionSlotContext.Read(element, new[] { null, string.Empty, "w-[40px]" }, new[] { "h-[20px]", string.Empty, null }, _ => false);

            // Assert
            Assert.That((context.HoldsInlineOutsideSwap(ArbitraryProperty.Width), context.HoldsInlineOutsideSwap(ArbitraryProperty.Height), context.HoldsInlineOutsideSwap(ArbitraryProperty.Opacity)),
                Is.EqualTo((false, false, true)));
        }

        [Test]
        public void Given_ImportantAndPlainRestingClasses_When_TheProviderReadsThem_Then_ItKeepsTheirPriorityAndOmitsTheSwap()
        {
            // Arrange
            var element = MountInFixedParent();
            StyleClassProjection.Add(element, "opacity-50", StyleLayerPriority.ImportantOf(StyleLayerPriority.Base));
            StyleClassProjection.Add(element, "pt-4", StyleLayerPriority.Base);
            StyleClassProjection.Add(element, "w-40", StyleLayerPriority.Base);

            // Act
            var context = MotionSlotContext.Read(element, new[] { "w-40" }, null, _ => false);
            var resting = string.Join(" ", context.RestingClasses().OrderBy(c => c, System.StringComparer.Ordinal));

            // Assert
            Assert.That(resting, Is.EqualTo("!opacity-50 pt-4"));
        }

        [Test]
        public void Given_ARemovedImportantProjection_When_TheProviderReadsTheReaddedPlainClass_Then_ItDoesNotRestoreTheRemovedPriority()
        {
            // Arrange
            var element = MountInFixedParent();
            var priority = StyleLayerPriority.ImportantOf(StyleLayerPriority.Base);
            StyleClassProjection.Add(element, "w-40", priority);
            StyleClassProjection.Remove(element, "w-40", priority);
            StyleClassProjection.Add(element, "w-40", StyleLayerPriority.Base);

            // Act
            var resting = string.Join(" ", MotionSlotContext.Read(element, null, null, _ => false).RestingClasses());

            // Assert
            Assert.That(resting, Is.EqualTo("w-40"));
        }

        [Test]
        public void Given_ASuppressedImportantClass_When_ItIsReaddedToTheLiveList_Then_TheProviderReadsItWithoutImportantPriority()
        {
            // Arrange
            var element = MountInFixedParent();
            StyleClassProjection.Add(element, "w-40", StyleLayerPriority.ImportantOf(StyleLayerPriority.Base));
            StyleClassProjection.Add(element, "w-80", StyleLayerPriority.ImportantOf(StyleLayerPriority.Hover));
            var suppressed = !element.ClassListContains("w-40");

            // Act
            element.AddToClassList("w-40");
            var resting = string.Join(" ", MotionSlotContext.Read(element, null, null, _ => false).RestingClasses()
                .OrderBy(c => c, System.StringComparer.Ordinal));

            // Assert
            Assert.That((suppressed, resting), Is.EqualTo((true, "!w-80 w-40")));
        }

        [TestCase(false, false, true)]
        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        public void Given_AnImportantInlineWidth_When_TheProviderClassifiesASwap_Then_OnlyASwappedBaseLayerIsReleased(bool swapped, bool hover, bool expected)
        {
            // Arrange
            var element = MountInFixedParent();
            var priority = StyleLayerPriority.ImportantOf(hover ? StyleLayerPriority.Hover : StyleLayerPriority.Base);
            StyleArbitraryValueResolver.ApplyClassToken(element, "w-[40px]", priority);
            var context = MotionSlotContext.Read(element, swapped ? new[] { "!w-[40px]" } : null, new[] { "h-[20px]" }, _ => false);

            // Act
            var width = context.HasImportantInlineOutsideSwap(ArbitraryProperty.Width);
            var height = context.HasImportantInlineOutsideSwap(ArbitraryProperty.Height);

            // Assert
            Assert.That((width, height), Is.EqualTo((expected, false)));
        }

        [TestCase("running", 160f, false)]
        [TestCase("running", 80f, false, LengthUnit.Percent)]
        [TestCase("restarted", 150f, false)]
        [TestCase("completed", 80f, true)]
        [TestCase("cancelled", 80f, true)]
        [TestCase("detached", 80f, true)]
        [TestCase("unrelated", 80f, true)]
        public void Given_ANativeTransitionBoundary_When_TheProviderSamplesWidth_Then_ItDistinguishesThePaintFromAPlainHolder(string phase, float expected, bool holder, LengthUnit unit = LengthUnit.Pixel)
        {
            // Arrange
            var element = MountInFixedParent();
            var parent = element.parent;
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => now);
            if (phase == "unrelated")
            {
                element.style.height = 100f;
                ForcePanelUpdate(element.panel);
                ConfigureNativeTransition(element, "height");
                element.style.height = 20f;
                ForcePanelUpdate(element.panel);
                EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            }
            else StartNativeWidth(element);
            now += 0.5;
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
            if (phase == "completed")
            {
                now += 2;
                EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
                ForcePanelUpdate(element.panel);
            }
            if (phase == "cancelled")
            {
                element.style.transitionProperty = new List<StylePropertyName> { new("none") };
                ForcePanelUpdate(element.panel);
            }
            if (phase == "detached")
            {
                element.RemoveFromHierarchy();
                parent.Add(element);
                ForcePanelUpdate(element.panel);
            }
            if (phase == "restarted")
            {
                element.style.width = 120f;
                ForcePanelUpdate(element.panel);
                EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
                now += 0.5;
                EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
                ForcePanelUpdate(element.panel);
            }
            var boundaryHolder = MotionSlotContext.Read(element, null, null, _ => false).HoldsInlineOutsideSwap(ArbitraryProperty.Width);
            if (holder)
            {
                if (phase is "completed" or "detached")
                {
                    element.style.transitionDuration = new List<TimeValue> { new(0f, TimeUnit.Second) };
                    ForcePanelUpdate(element.panel);
                }
                element.style.width = 80f;
                ForcePanelUpdate(element.panel);
            }
            var painted = element.resolvedStyle.width;
            var unrelatedRunning = phase == "unrelated" && element.resolvedStyle.height != element.style.height.value.value;
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(ArbitraryProperty.Width, unit, out var value);

            // Assert
            Assert.That(new[] { painted, read && value.Unit == unit ? value.Value : float.NaN, context.HoldsInlineOutsideSwap(ArbitraryProperty.Width) ? 1f : 0f, unrelatedRunning ? 1f : 0f, boundaryHolder ? 1f : 0f },
                Is.EqualTo(new[] { unit == LengthUnit.Percent ? expected * 2f : expected, expected, holder ? 1f : 0f, phase == "unrelated" ? 1f : 0f, holder && phase != "unrelated" ? 1f : 0f }).Within(0.02f));
        }

        [Test]
        public void Given_ACallbackOwnedFrameDuringNativeActivity_When_TheProviderSamplesIt_Then_ItReadsTheInlineFrame()
        {
            // Arrange
            var element = MountInFixedParent();
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => now);
            StartNativeWidth(element);
            now += 0.5;
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
            var paint = element.resolvedStyle.width;
            element.style.width = 26f;
            var context = MotionSlotContext.Read(element, null, null, slot => slot == ArbitraryProperty.Width);

            // Act
            var read = context.TryReadCurrent(ArbitraryProperty.Width, LengthUnit.Pixel, out var value);

            // Assert
            Assert.That(new[] { paint, read ? value.Value : float.NaN, context.HoldsInlineOutsideSwap(ArbitraryProperty.Width) ? 1f : 0f },
                Is.EqualTo(new[] { 160f, 26f, 0f }).Within(0.02f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Given_AClippedLayoutSlot_When_TheProviderSamplesIt_Then_ItReadsTheLayoutOwnersCurrentValue(bool nativeTransition)
        {
            // Arrange
            var parent = MountInFixedParent().parent;
            _mounted = V.Mount(parent, V.Div(name: "provider-clipped", className: "clip-path-[inset(0)]"));
            var element = parent.Q<VisualElement>("provider-clipped");
            ForcePanelUpdate(element.panel);
            var owner = ClipPathLayoutBox.Of(element);
            var now = 100.0;
            if (nativeTransition)
            {
                EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => now);
                StartNativeWidth(owner);
                now += 0.5;
                EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
                ForcePanelUpdate(element.panel);
            }
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(ArbitraryProperty.Width, LengthUnit.Pixel, out var value);

            // Assert
            Assert.That(new[] { ReferenceEquals(owner, element) ? 0f : 1f, owner.resolvedStyle.width, read ? value.Value : float.NaN },
                Is.EqualTo(new[] { 1f, nativeTransition ? 160f : 200f, nativeTransition ? 160f : 200f }).Within(0.02f));
        }

        [TestCase(ArbitraryProperty.TextColor, "text-white")]
        [TestCase(ArbitraryProperty.BackgroundColor, "bg-white")]
        [TestCase(ArbitraryProperty.BorderColor, "border-white")]
        public void Given_AComputedColorWithoutAnInlineOverride_When_TheProviderSamplesIt_Then_ItReturnsTheStylesheetColor(ArbitraryProperty slot, string cls)
        {
            // Arrange
            var element = MountInFixedParent();
            element.AddToClassList(cls);
            ForcePanelUpdate(element.panel);
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(slot, LengthUnit.Percent, out var value);

            // Assert
            Assert.That((read, value.Property, value.Color), Is.EqualTo((true, slot, Color.white)));
        }

        [Test]
        public void Given_ANonnumericComputedMaximum_When_TheProviderSamplesIt_Then_ItDeclinesTheValue()
        {
            // Arrange
            var element = MountInFixedParent();
            element.style.maxWidth = StyleKeyword.None;
            ForcePanelUpdate(element.panel);
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(ArbitraryProperty.MaxWidth, LengthUnit.Pixel, out _);

            // Assert
            Assert.That((element.resolvedStyle.maxWidth.keyword, read), Is.EqualTo((StyleKeyword.None, false)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Given_ANativeOpacityFrame_When_TheProviderSamplesIt_Then_ItAcceptsOnlyTheFinitePaint(bool infinite)
        {
            // Arrange
            var element = MountInFixedParent();
            element.style.opacity = 0.5f;
            ForcePanelUpdate(element.panel);
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => now);
            ConfigureNativeTransition(element, "opacity");
            element.style.opacity = infinite ? float.PositiveInfinity : 0.25f;
            ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            now += 0.5;
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
            var paint = element.resolvedStyle.opacity;
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(ArbitraryProperty.Opacity, LengthUnit.Pixel, out var value);

            // Assert
            Assert.That(new[] { infinite ? (float.IsPositiveInfinity(paint) ? 1f : 0f) : paint,
                    read ? 1f : 0f, infinite ? 0f : value.Property == ArbitraryProperty.Opacity && value.Unit == LengthUnit.Pixel
                        ? value.Value : float.NaN,
                    context.HoldsInlineOutsideSwap(ArbitraryProperty.Opacity) ? 1f : 0f },
                Is.EqualTo(new[] { infinite ? 1f : 0.4375f, infinite ? 0f : 1f,
                    infinite ? 0f : 0.4375f, 0f }).Within(0.02f));
        }

        [Test]
        public void Given_AnUnavailableNativeBorderIdentity_When_TheProviderSamplesIt_Then_ItUsesTheInlineTarget()
        {
            // Arrange
            var element = MountInFixedParent();
            for (var i = 0; i < 4; i++) SetBorderEdge(element, i, Color.red);
            ForcePanelUpdate(element.panel);
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => now);
            ConfigureNativeTransition(element, "border-color");
            for (var i = 0; i < 4; i++) SetBorderEdge(element, i, Color.blue);
            ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            now += 0.5;
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
            var paint = element.resolvedStyle.borderTopColor;
            var ids = (object[])typeof(MotionSlotContext).GetField("s_nativePropertyIds",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            var saved = ids.ToArray();
            var edges = new[] { StyleLonghand.BorderTopColor, StyleLonghand.BorderRightColor,
                StyleLonghand.BorderBottomColor, StyleLonghand.BorderLeftColor };
            var native = (bool)EngineMember.HasRunningStyleAnimation.ResolveMethod()
                .Invoke(element, new[] { saved[(int)StyleLonghand.BorderTopColor] });
            var context = MotionSlotContext.Read(element, null, null, _ => false);
            bool read;
            bool holder;
            ArbitraryStyle value;

            // Act
            try
            {
                foreach (var edge in edges) ids[(int)edge] = null;
                read = context.TryReadCurrent(ArbitraryProperty.BorderColor, LengthUnit.Pixel, out value);
                holder = context.HoldsInlineOutsideSwap(ArbitraryProperty.BorderColor);
            }
            finally
            {
                System.Array.Copy(saved, ids, saved.Length);
            }

            // Assert
            Assert.That(new[] { native ? 1f : 0f, paint.r, paint.b,
                    read ? value.Color.r : float.NaN, read ? value.Color.b : float.NaN, holder ? 1f : 0f },
                Is.EqualTo(new[] { 1f, 0.75f, 0.25f, 0f, 1f, 1f }).Within(0.02f));
        }

        [TestCase(ArbitraryProperty.TextColor, "color")]
        [TestCase(ArbitraryProperty.BackgroundColor, "background-color")]
        public void Given_ARunningNativeColor_When_TheProviderSamplesIt_Then_ItReadsThePaintRegardlessOfLengthUnit(ArbitraryProperty slot, string property)
        {
            // Arrange
            var element = MountInFixedParent();
            if (slot == ArbitraryProperty.TextColor) element.style.color = Color.red;
            else element.style.backgroundColor = Color.red;
            ForcePanelUpdate(element.panel);
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => now);
            ConfigureNativeTransition(element, property);
            if (slot == ArbitraryProperty.TextColor) element.style.color = Color.blue;
            else element.style.backgroundColor = Color.blue;
            ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            now += 0.5;
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
            var paint = slot == ArbitraryProperty.TextColor ? element.resolvedStyle.color : element.resolvedStyle.backgroundColor;
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(slot, LengthUnit.Percent, out var value);

            // Assert
            Assert.That(new[] { paint.r, paint.b, read ? value.Color.r : float.NaN, read ? value.Color.b : float.NaN,
                    context.HoldsInlineOutsideSwap(slot) ? 1f : 0f },
                Is.EqualTo(new[] { 0.75f, 0.25f, 0.75f, 0.25f, 0f }).Within(0.02f));
        }

        [TestCase(false)]
        [TestCase(true)]
        [TestCase(true, true)]
        public void Given_ANativeBorderFrameWithUniformPaint_When_TheProviderSamplesIt_Then_ItSeparatesTheSampleFromInactiveEdgeHolders(
            bool allEdges, bool drivenByPlay = false)
        {
            // Arrange
            var element = MountInFixedParent();
            for (var i = 0; i < 4; i++) SetBorderEdge(element, i, Color.red);
            ForcePanelUpdate(element.panel);
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => now);
            ConfigureNativeTransition(element, allEdges ? "border-color" : "border-top-color");
            for (var i = 0; i < (allEdges ? 4 : 1); i++) SetBorderEdge(element, i, Color.blue);
            ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            now += 0.5;
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
            var paint = element.resolvedStyle.borderTopColor;
            if (!allEdges)
            {
                for (var i = 1; i < 4; i++) SetBorderEdge(element, i, paint);
                ForcePanelUpdate(element.panel);
            }
            var context = MotionSlotContext.Read(element, null, null, slot => drivenByPlay && slot == ArbitraryProperty.BorderColor);

            // Act
            var read = context.TryReadCurrent(ArbitraryProperty.BorderColor, LengthUnit.Pixel, out var value);

            // Assert
            Assert.That(new[] { paint.r, paint.b, read ? value.Color.r : float.NaN, read ? value.Color.b : float.NaN,
                    context.HoldsInlineOutsideSwap(ArbitraryProperty.BorderColor) ? 1f : 0f },
                Is.EqualTo(new[] { 0.75f, 0.25f, drivenByPlay ? 0f : 0.75f, drivenByPlay ? 1f : 0.25f,
                    allEdges ? 0f : 1f }).Within(0.02f));
        }

        [Test]
        public void Given_ANonuniformNativeScaleWithAUniformInlineTarget_When_TheProviderSamplesIt_Then_ItDeclinesThePaint()
        {
            // Arrange
            var element = MountInFixedParent();
            element.style.scale = new Scale(new Vector3(0.5f, 0.75f, 1f));
            ForcePanelUpdate(element.panel);
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => now);
            ConfigureNativeTransition(element, "scale");
            element.style.scale = new Scale(new Vector3(1.5f, 1.5f, 1f));
            ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            now += 0.5;
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
            var paint = element.resolvedStyle.scale.value;
            var context = MotionSlotContext.Read(element, null, null, _ => false);

            // Act
            var read = context.TryReadCurrent(ArbitraryProperty.Scale, LengthUnit.Pixel, out _);

            // Assert
            Assert.That(read ? new[] { float.NaN, float.NaN } : new[] { paint.x, paint.y },
                Is.EqualTo(new[] { 0.75f, 0.9375f }).Within(0.02f));
        }

        private static void SetBorderEdge(VisualElement element, int edge, Color color)
        {
            switch (edge)
            {
                case 0: element.style.borderTopColor = color; break;
                case 1: element.style.borderRightColor = color; break;
                case 2: element.style.borderBottomColor = color; break;
                case 3: element.style.borderLeftColor = color; break;
            }
        }
    }
}
