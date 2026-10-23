using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what the <c>backdrop-*</c> utilities compose and where their layer is hosted: directly before the
    /// element, among its parent's children, and invisible to the reconciler's slots. What the layer paints is
    /// measured in <c>BackdropFilterPlaybackTests</c>.
    /// </summary>
    internal sealed class BackdropFilterLayerTests : PanelTestBase
    {
        // The layer and its spec are read by name, so this fixture reads "none" rather than failing to build where
        // they do not exist.
        private const string LayerClass = "velvet-backdrop-layer";

        // transition-all, duration-* and ease-* are the sheet's.
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        private sealed class Flag : Store<bool>
        {
            public Flag() : base(false) { }

            public void Set(bool value) => SetState(_ => value);

            protected override void ResetCore() => SetState(_ => false);
        }

        private static Flag s_dim;

        [Component]
        private static VNode Dimming()
        {
            var dim = Hooks.UseStore(s_dim, x => x);
            return V.Div(name: "glass", className: "w-[40px] h-[40px] transition-all duration-1000 ease-linear "
                + (dim ? "backdrop-brightness-50" : "backdrop-brightness-100"));
        }

        // The first function's amount the layer paints now, NaN where there is no layer.
        private static float ShownAmount(VisualElement element)
        {
            var layer = LayerBefore(element);
            var shown = layer?.GetType().GetMethod("Shown", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(layer, null) as Array;
            var step = shown is { Length: > 0 } ? shown.GetValue(0) : null;
            return step?.GetType().GetField("A")?.GetValue(step) is float amount ? amount : float.NaN;
        }

        private static VisualElement LayerBefore(VisualElement element)
        {
            var host = element.parent;
            var index = host.IndexOf(element) - 1;
            return index >= 0 && host[index].ClassListContains(LayerClass) ? host[index] : null;
        }

        private static string SpecOf(VisualElement element)
        {
            var layer = LayerBefore(element);
            var field = layer?.GetType().GetField("_spec", BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(layer)?.ToString() ?? "none";
        }

        private VisualElement Mount(string className, string name = "glass")
        {
            _mounted?.Dispose();
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: name, className: $"w-[40px] h-[40px] {className}"));
            var element = _window.rootVisualElement.Q<VisualElement>(name);
            ForcePanelUpdate(element.panel);
            return element;
        }

        [Test]
        public void Given_EveryBackdropFunctionInClassOrderReversed_When_Mounted_Then_TheyComposeInTailwindsOrder()
        {
            // Arrange & Act — a bracket, a bare form, Tailwind v4's bare integers, a clamp and a negative hue each.
            var glass = Mount("backdrop-sepia-[0.25] -backdrop-hue-rotate-90 backdrop-opacity-50 backdrop-saturate-150 "
                + "backdrop-invert backdrop-grayscale-200 backdrop-contrast-[125%] backdrop-brightness-75 "
                + "backdrop-blur-[1rem]");

            // Assert
            Assert.That(SpecOf(glass), Is.EqualTo("blur(16px) brightness(0.75) contrast(1.25) grayscale(1) "
                + "hue-rotate(-90deg) invert(1) opacity(0.5) saturate(1.5) sepia(0.25)"));
        }

        [Test]
        public void Given_ABracketTheBrowserRejects_When_Mounted_Then_TheElementGetsNoBackdropFilter()
        {
            // Arrange — the same element without the rejected bracket is the control.
            var accepted = SpecOf(Mount("backdrop-grayscale"));

            // Act
            var rejected = SpecOf(Mount("backdrop-grayscale backdrop-blur-[-4px]"));

            // Assert
            Assert.That((accepted, rejected), Is.EqualTo(("grayscale(1)", "none")));
        }

        [Test]
        public void Given_TwoBlursWrittenInReverse_When_Mounted_Then_TheOneTailwindEmitsLastHoldsTheBlur()
        {
            // Act — Tailwind emits backdrop-blur-sm after backdrop-blur-lg, as it does shadow-sm after shadow-lg.
            var glass = Mount("backdrop-blur-sm backdrop-blur-lg");

            // Assert
            Assert.That(SpecOf(glass), Is.EqualTo("blur(8px)"));
        }

        [Test]
        public void Given_AnImportantBlurBeforeAPlainOne_When_Mounted_Then_TheImportantOneHoldsTheBlur()
        {
            // Act
            var glass = Mount("backdrop-blur-lg! backdrop-blur-sm");

            // Assert
            Assert.That(SpecOf(glass), Is.EqualTo("blur(16px)"));
        }

        [Test]
        public void Given_BackdropFilterNoneBesideFunctionUtilities_When_Mounted_Then_TheClassTailwindEmitsLastHoldsTheDeclaration()
        {
            // Arrange — Tailwind emits backdrop-filter-none after backdrop-blur-sm and before backdrop-sepia.
            var afterBlur = SpecOf(Mount("backdrop-filter-none backdrop-blur-sm"));

            // Act
            var beforeSepia = SpecOf(Mount("backdrop-sepia backdrop-filter-none backdrop-blur-sm"));

            // Assert
            Assert.That((afterBlur, beforeSepia), Is.EqualTo(("none", "blur(8px) sepia(1)")));
        }

        [Test]
        public void Given_ABackdropFilterList_When_Mounted_Then_ItsFunctionsRunInTheOrderWritten()
        {
            // Act
            var glass = Mount("backdrop-filter-[invert(1)_brightness(50%)_drop-shadow(0_4px_8px_#ff0000)_blur(2px)]");

            // Assert
            Assert.That(SpecOf(glass),
                Is.EqualTo("invert(1) brightness(0.5) drop-shadow(0px 4px 8px #FF0000FF) blur(2px)"));
        }

        [Test]
        public void Given_ABlurReadingACustomProperty_When_Mounted_Then_ItBlursByThePropertysValue()
        {
            // Act
            var glass = Mount("[--b:4px] backdrop-blur-(--b)");

            // Assert
            Assert.That(SpecOf(glass), Is.EqualTo("blur(4px)"));
        }

        [Test]
        public void Given_TransitionAll_When_TheBackdropBrightnessChanges_Then_HalfTheDurationInItIsHalfWay()
        {
            // Arrange
            var clock = new HeldMotionClock();
            s_dim = new Flag();
            _mounted = V.Mount(_window.rootVisualElement, V.Component(Dimming, key: "root"),
                new MountOptions { MotionClock = clock });
            var glass = _window.rootVisualElement.Q<VisualElement>("glass");
            ForcePanelUpdate(glass.panel);

            // Act
            s_dim.Set(true);
            _mounted.FlushStateForTest();
            clock.Now += 0.5;

            // Assert — brightness 1 to 0.5, linear.
            Assert.That(ShownAmount(glass), Is.EqualTo(0.75f).Within(1e-3f));
        }

        [Test]
        public void Given_ABackdropElementBetweenSiblings_When_Mounted_Then_ItsLayerSitsBeforeItAndTakesNoSlot()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "row", children: new VNode[]
            {
                V.Div(name: "a", className: "w-[10px] h-[10px]"),
                V.Div(name: "glass", className: "w-[40px] h-[40px] backdrop-blur-sm"),
                V.Div(name: "b", className: "w-[10px] h-[10px]"),
            }));
            var row = _window.rootVisualElement.Q<VisualElement>("row");

            // Act
            ForcePanelUpdate(row.panel);

            // Assert — four children, three slots, and the layer at the glass's own index less one.
            var glass = row.Q<VisualElement>("glass");
            Assert.That((row.childCount, LogicalChildSlots.Count(row), LayerBefore(glass) != null),
                Is.EqualTo((4, 3, true)));
        }

        [Test]
        public void Given_AHoverBackdropVariant_When_ThePointerIsOver_Then_TheLayerIsHosted()
        {
            // Arrange
            var glass = Mount("hover:backdrop-blur-sm");
            var before = SpecOf(glass);

            // Act
            using (var e = PointerOverEvent.GetPooled()) { glass.SimulateEvent(e); }

            // Assert
            Assert.That((before, SpecOf(glass)), Is.EqualTo(("none", "blur(8px)")));
        }
    }
}
