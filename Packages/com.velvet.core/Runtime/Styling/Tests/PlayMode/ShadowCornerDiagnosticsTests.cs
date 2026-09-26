using System.Collections;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;
using static Velvet.TestUtilities.PlayModeRealtimeTestHelpers;

namespace Velvet.Tests
{
    /// <summary>
    /// TEMPORARY diagnostic: logs where an offset shadow's edges and corner land in the frame, to compare a
    /// CI graphics device with a local one. Removed once read.
    /// </summary>
    [Timeout(600000)]
    internal sealed class ShadowCornerDiagnosticsTests
    {
        private const int Width = 360;
        private const int Height = 290;

        private static IEnumerator Log(string label, string className, float offset)
        {
            var host = new RenderTexturePanelHost(label, Width, Height);
            VelvetStyleUtilities.AttachTo(host.Root);
            var mounted = V.Mount(host.Root, V.Div(name: "box", className: className));
            var box = host.Root.Q<VisualElement>("box");
            yield return WaitRealtimeDraining(0.8, host.TargetTexture);
            var px = RenderTexturePixelReader.ReadPixels(host.TargetTexture, new RectInt(0, 0, Width, Height));
            Color32 At(int c, int r) => px[((Height - 1 - r) * Width) + c];
            var b = box.worldBound;
            var cornerX = b.xMax + offset;
            var cornerY = b.yMax + offset;
            var sb = new StringBuilder();
            sb.Append($"CORNERDIAG {label}: device={SystemInfo.graphicsDeviceType} uvTop={SystemInfo.graphicsUVStartsAtTop} ");
            sb.Append($"box=({b.xMin},{b.yMin},{b.width},{b.height}) layout=({box.layout.x},{box.layout.y},{box.layout.width},{box.layout.height}) ");
            sb.Append($"rs={box.resolvedStyle.borderTopLeftRadius} corner=({cornerX},{cornerY})\n");
            // Red and alpha of the pixel whose centre is k+0.5 px in along the corner's diagonal.
            sb.Append("  diag r/a:");
            for (var k = 0; k <= 50; k++)
            {
                var col = Mathf.RoundToInt(cornerX - k - 1f);
                var row = Mathf.RoundToInt(cornerY - k - 1f);
                var p = At(col, row);
                sb.Append($" {k}:{p.r}/{p.a}");
            }
            // The shadow's right and bottom edges at the middle of its extent, read as the last red pixel.
            var midRow = Mathf.RoundToInt((b.yMin + offset + cornerY) * 0.5f);
            var midCol = Mathf.RoundToInt((b.xMin + offset + cornerX) * 0.5f);
            sb.Append("\n  row@mid r:");
            for (var c = Mathf.RoundToInt(cornerX) - 4; c <= Mathf.RoundToInt(cornerX) + 3; c++)
            {
                sb.Append($" {c}:{At(c, midRow).r}");
            }
            sb.Append("\n  col@mid r:");
            for (var r = Mathf.RoundToInt(cornerY) - 4; r <= Mathf.RoundToInt(cornerY) + 3; r++)
            {
                sb.Append($" {r}:{At(midCol, r).r}");
            }
            sb.Append("\n  row@top r:");
            var topY = Mathf.RoundToInt(b.yMin + offset);
            for (var r = topY - 3; r <= topY + 3; r++)
            {
                sb.Append($" {r}:{At(midCol, r).r}");
            }
            Debug.Log(sb.ToString());
            mounted.Dispose();
            host.Dispose();
            yield return null;
        }

        // GREEN_ON_BASE(characterization): a temporary diagnostic that asserts nothing and is removed once read.
        [UnityTest]
        public IEnumerator Given_OffsetShadows_When_Painted_Then_TheirEdgesAreLogged()
        {
            yield return Log("old_full", "w-[160px] h-[120px] mt-[10px] ml-[20px] bg-[#ffffff] rounded-full shadow-[20px_20px_1px_#ff0000]", 20f);
            yield return Log("old_lg", "w-[160px] h-[120px] mt-[10px] ml-[20px] bg-[#ffffff] rounded-lg shadow-[20px_20px_1px_#ff0000]", 20f);
            yield return Log("old_none", "w-[160px] h-[120px] mt-[10px] ml-[20px] bg-[#ffffff] rounded-none shadow-[20px_20px_1px_#ff0000]", 20f);
            yield return Log("new_full", "w-[240px] h-[200px] mt-[10px] ml-[10px] bg-[#ffffff] rounded-full shadow-[60px_60px_1px_#ff0000]", 60f);
            yield return Log("new_3xl", "w-[240px] h-[200px] mt-[10px] ml-[10px] bg-[#ffffff] rounded-3xl shadow-[60px_60px_1px_#ff0000]", 60f);
            Assert.Pass();
        }
    }
}
