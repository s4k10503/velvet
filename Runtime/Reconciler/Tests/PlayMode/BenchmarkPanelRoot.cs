using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests.Performance
{
    // The root a live-panel benchmark builds on.
    internal static class BenchmarkPanelRoot
    {
        private static readonly Font s_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // A font the labels inherit, since TestPanelSettings' empty theme gives them none, and a root that grows with
        // its children: shrunk into the panel's height, hand-built labels collapse, where Velvet's keep their text's
        // height as CSS's automatic minimum (StyleFlexMinSizeManipulator). A fixture refuses a frame that left a label
        // without a height through LaidOut.
        internal static void Prepare(VisualElement root)
        {
            root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(s_font));
            root.style.flexShrink = 0f;
        }

        internal static bool LaidOut(VisualElement element)
            => element.layout.width > 0 && element.layout.height > 0;
    }
}
