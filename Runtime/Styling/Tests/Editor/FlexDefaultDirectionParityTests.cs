using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Default-direction contract for the bare <c>flex</c> utility.
    /// In CSS, <c>flex</c> implies <c>flex-direction: row</c>, but UI Toolkit's Yoga
    /// layout defaults a flex container to <c>column</c>. This fixture mounts an element whose
    /// className is exactly <c>"flex"</c>, resolves Velvet's bundled <c>StyleUtilities.uss</c>
    /// against it inside a real panel (via <see cref="PanelTestBase"/>), and asserts the resolved
    /// direction is <see cref="FlexDirection.Row"/> so a bare <c>flex</c> lays children out HORIZONTALLY.
    /// </summary>
    [TestFixture]
    internal sealed class FlexDefaultDirectionParityTests : PanelTestBase
    {
        private const string StyleUtilitiesPath =
            "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";

        protected override void LoadStyleSheets()
        {
            // A real panel is required for USS resolution; an EditorWindow's rootVisualElement provides one.
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleUtilitiesPath);
            Assert.That(styleSheet, Is.Not.Null,
                $"Could not load Velvet's StyleUtilities.uss at '{StyleUtilitiesPath}'.");
            _window.rootVisualElement.styleSheets.Add(styleSheet);
        }

        [Test]
        public void Given_BareFlexClass_When_StylesResolved_Then_FlexDirectionIsRow()
        {
            var host = _window.rootVisualElement;

            _mounted = V.Mount(host, V.Div(
                "flex",
                V.Div("a"),
                V.Div("b")));

            using var rowProbe = V.Mount(host, V.Div("flex flex-row"));

            // EditMode batchmode never ticks the panel's update phases, so resolvedStyle stays at
            // engine defaults until styling is applied explicitly. Force the style pass.
            ForcePanelUpdate(host.panel);

            // V.Mount renders the tree as children of the host; the "flex" div is host[0],
            // the flex-row probe is host[1].
            var flex = host[0];
            Assert.That(flex.ClassListContains("flex"), Is.True,
                "Expected the mounted element to carry the 'flex' class.");

            // Guard: prove StyleUtilities.uss actually resolves against this panel — `flex-row`
            // must yield Row. Column is also Yoga's default, so without this guard a missing
            // sheet would be indistinguishable from a missing `flex-direction` on `.flex`.
            Assert.That(host[1].resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Row),
                "StyleUtilities.uss did not resolve against the test panel (flex-row should be Row).");

            Assert.That(flex.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Row),
                "Tailwind parity: a bare `flex` must resolve to flex-direction: row (horizontal), " +
                "not Yoga's default column.");
        }
    }
}
