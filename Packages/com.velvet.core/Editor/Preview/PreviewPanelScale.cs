using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Editor.Preview
{
    // The layout units per screen pixel a runtime panel on these settings takes for a screen of this size, as
    // PanelSettings resolves it. PanelSettings keeps its own resolution internal; PreviewPanelScaleTests compares
    // this against it for each scale and match mode.
    internal static class PreviewPanelScale
    {
        internal static float Resolve(PanelSettings settings, Vector2 screen, float screenDpi)
        {
            var unitsPerPixel = 1f;
            switch (settings.scaleMode)
            {
                case PanelScaleMode.ConstantPhysicalSize:
                {
                    var dpi = screenDpi == 0f ? settings.fallbackDpi : screenDpi;
                    if (dpi != 0f) unitsPerPixel = settings.referenceDpi / dpi;
                    break;
                }
                case PanelScaleMode.ScaleWithScreenSize:
                {
                    var reference = settings.referenceResolution;
                    if (reference.x * reference.y == 0) break;
                    var ratio = new Vector2(screen.x / reference.x, screen.y / reference.y);
                    var factor = settings.screenMatchMode switch
                    {
                        PanelScreenMatchMode.Expand => Mathf.Min(ratio.x, ratio.y),
                        PanelScreenMatchMode.Shrink => Mathf.Max(ratio.x, ratio.y),
                        _ => Mathf.Lerp(ratio.x, ratio.y, Mathf.Clamp01(settings.match)),
                    };
                    if (factor != 0f) unitsPerPixel = 1f / factor;
                    break;
                }
            }

            return settings.scale > 0f ? unitsPerPixel / settings.scale : 0f;
        }
    }
}
