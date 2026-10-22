using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    // The outline a silhouette layer paints an element's face along, read as the shared path builder emits it.
    internal static class FaceOutline
    {
        // From where the top-left corner meets the top edge, clockwise.
        public static List<Vector2> Of(VisualElement element)
        {
            var points = new List<Vector2>();
            SilhouetteFace.BuildShearedRoundedRectPolyline(points,
                ShearedRoundedRect.ForElement(element, 0f, element.layout.width, element.layout.height, 0f, 0f));
            return points;
        }

        // The top-left corner's vertical radius: where the outline leaves the left edge for that corner's arc, eight
        // samples before its end, on an outline whose four corners are all round.
        public static float TopLeftVerticalRadius(List<Vector2> outline) => outline[outline.Count - 9].y;
    }
}
