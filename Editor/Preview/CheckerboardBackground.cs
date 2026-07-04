using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Editor.Preview
{
    /// <summary>
    /// A tiled two-tone checkerboard that fills its parent, used as the preview stage's transparency backdrop so
    /// a story's own alpha (rounded corners, translucent panels, drop shadows) reads against a grid the way an
    /// image editor shows transparency. Implemented as a tiny 2x2 texture repeated via the background — constant
    /// cost regardless of stage size (no per-cell mesh), so it renders correctly even on a maximized 4K / Retina
    /// window where a per-cell quad mesh would blow the 65k-vertex limit.
    /// </summary>
    internal sealed class CheckerboardBackground : VisualElement
    {
        // One checker cell in px; the 2x2 source texture is scaled so each texel covers a CellSize block.
        private const float CellSize = 12f;
        private static readonly Color32 Light = new(209, 209, 209, 255);
        private static readonly Color32 Dark = new(174, 174, 174, 255);

        private Texture2D _tile;

        public CheckerboardBackground()
        {
            // Fill the parent and sit behind the canvas without intercepting pointer events meant for the story.
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.right = 0;
            style.bottom = 0;
            pickingMode = PickingMode.Ignore;

            _tile = BuildTile();
            style.backgroundImage = Background.FromTexture2D(_tile);
            // Each source texel maps to a CellSize block, and the image repeats across the whole element — so the
            // grid is drawn by texture sampling, not geometry, at any size.
            style.backgroundSize = new BackgroundSize(2f * CellSize, 2f * CellSize);
            style.backgroundRepeat = new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat);

            // The element does not own a panel lifetime of its own; free the generated texture on detach.
            RegisterCallback<DetachFromPanelEvent>(OnDetach);
        }

        // A 2x2 checker: light/dark on the diagonal. Point-filtered so each texel stays a crisp block when the
        // background scales it up to CellSize.
        private static Texture2D BuildTile()
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.HideAndDontSave,
            };
            tex.SetPixels32(new[] { Light, Dark, Dark, Light });
            tex.Apply();
            return tex;
        }

        private void OnDetach(DetachFromPanelEvent evt)
        {
            UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            style.backgroundImage = new StyleBackground(StyleKeyword.Null);
            if (_tile != null)
            {
                Object.DestroyImmediate(_tile);
                _tile = null;
            }
        }
    }
}
