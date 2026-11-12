using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements.TestFramework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a gradient's palette stop reads as each mount's sheets give it: a mount under sheets setting the
    /// colour namespace to initial binds no colour for it, a mount without them binds the palette colour, whichever
    /// renders first, and a re-measure of the clearing mount's gradient reads it as that mount does. GWT, one assert per
    /// case.
    /// </summary>
    internal sealed class GradientStopThemeScopeTests
    {
        private const string ClearedSheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/ThemeTokenClearedSheet.uss";

        private EditorPanelSimulator _sim;
        private MountedTree _first;
        private MountedTree _second;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(400, 300) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            // The colour namespace's initial removes the palette names that the bundled sheets above a mount declare and
            // the project's sheets there do not (StyleThemeTokens.Scope.ClearsBundled).
            VelvetStyleUtilities.AttachTo(_sim.rootVisualElement);
        }

        [TearDown]
        public void TearDown()
        {
            _first?.Dispose();
            _first = null;
            _second?.Dispose();
            _second = null;
            _sim?.Dispose();
            _sim = null;
        }

        private void Settle()
        {
            for (var i = 0; i < 3; i++)
            {
                _sim.FrameUpdateMs(16);
            }
        }

        // A mount on a root of its own under the panel's, which carries the clearing sheet where cleared is set; the
        // element the gradient is bound to.
        private VisualElement MountGradient(bool cleared, string className, out MountedTree mounted)
        {
            var root = new VisualElement();
            _sim.rootVisualElement.Add(root);
            if (cleared)
            {
                root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(ClearedSheet));
            }
            mounted = V.Mount(root, V.Div(name: "gradient", className: className));
            Settle();
            return root.Q<VisualElement>("gradient");
        }

        // The colour of the first stop of the gradient the mount bound, as RGBA hex, or "unbound".
        private static string FirstStopColour(MountedTree mounted, VisualElement element)
        {
            return element != null && mounted.Root.Reconciler.Context.GradientBackgrounds.TryGetValue(element, out var binding)
                && binding.Spec.Stops is { Length: > 0 } stops
                ? ColorUtility.ToHtmlStringRGBA(stops[0].Color)
                : "unbound";
        }

        private static string Palette(string name)
        {
            VelvetPalette.TryResolveColorToken(name, out var colour);
            return ColorUtility.ToHtmlStringRGBA(colour);
        }

        [Test]
        public void Given_AMountClearingTheColourNamespaceRenderedFirst_When_AnotherMountRendersTheSameStop_Then_ItBindsThePaletteColour()
        {
            // Arrange
            MountGradient(true, "w-[40px] h-[20px] bg-linear-to-r from-lime-950 to-[#0000ff]", out _first);

            // Act
            var element = MountGradient(false, "w-[40px] h-[20px] bg-linear-to-r from-lime-950 to-[#0000ff]", out _second);

            // Assert
            Assert.That(FirstStopColour(_second, element), Is.EqualTo(Palette("lime-950")));
        }

        // With the from colour gone, the stop fades from the far end's colour, as an absent end does.
        [Test]
        public void Given_AMountRenderedFirst_When_AMountClearingTheColourNamespaceRendersTheSameStop_Then_ItBindsNoPaletteColour()
        {
            // Arrange
            MountGradient(false, "w-[40px] h-[20px] bg-linear-to-r from-teal-950 to-[#0000ff]", out _first);

            // Act
            var element = MountGradient(true, "w-[40px] h-[20px] bg-linear-to-r from-teal-950 to-[#0000ff]", out _second);

            // Assert
            Assert.That(FirstStopColour(_second, element), Is.EqualTo("0000FF00"));
        }

        // GREEN_ON_BASE(characterization): the base hands the re-measure the reading the clearing mount took at bind, from its memo.
        [Test]
        public void Given_AClearingMountsGradientMeasuredInEms_When_ItsFontChanges_Then_ItIsReadAgainWithNoPaletteColour()
        {
            // Arrange — the radius is 2em, so a new font re-binds the gradient from what its classes read now.
            var element = MountGradient(true, "w-[40px] h-[20px] bg-radial-[circle_2em_at_center] from-cyan-950 to-[#0000ff]",
                out _first);

            // Act
            element.style.fontSize = 30f;
            Settle();

            // Assert
            Assert.That(FirstStopColour(_first, element), Is.EqualTo("0000FF00"));
        }
    }
}
