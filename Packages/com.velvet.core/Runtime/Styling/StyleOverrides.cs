using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Limited set of inline styles applied directly to an element.
    /// Intentionally inconvenient to encourage class-based styling.
    /// </summary>
    /// <remarks>
    /// Styling priority:
    /// 1. USS class (BEM) — for all static, predictable styles.
    /// 2. USS Custom Properties — for theme / config values.
    /// 3. StyleOverrides — only for values that cannot be expressed via classes (dynamic textures, etc.).
    /// </remarks>
    public sealed class StyleOverrides
    {
        /// <summary>For dynamic textures.</summary>
        public StyleBackground? BackgroundImage { get; init; }

        /// <summary>How the background image repeats (<c>background-repeat</c>).</summary>
        public StyleBackgroundRepeat? BackgroundRepeat { get; init; }

        /// <summary>The background image's top nine-slice inset (<c>-unity-slice-top</c>).</summary>
        public StyleInt? UnitySliceTop { get; init; }

        /// <summary>The background image's right nine-slice inset (<c>-unity-slice-right</c>).</summary>
        public StyleInt? UnitySliceRight { get; init; }

        /// <summary>The background image's bottom nine-slice inset (<c>-unity-slice-bottom</c>).</summary>
        public StyleInt? UnitySliceBottom { get; init; }

        /// <summary>The background image's left nine-slice inset (<c>-unity-slice-left</c>).</summary>
        public StyleInt? UnitySliceLeft { get; init; }

        /// <summary>The factor the nine-slice insets are scaled by (<c>-unity-slice-scale</c>).</summary>
        public StyleFloat? UnitySliceScale { get; init; }

        /// <summary>The nine-slice fill mode, sliced or tiled (<c>-unity-slice-type</c>).</summary>
        public StyleEnum<SliceType>? UnitySliceType { get; init; }

        /// <summary>Color computed at runtime.</summary>
        public StyleColor? BackgroundColor { get; init; }

        /// <summary>Dynamic text color.</summary>
        public StyleColor? Color { get; init; }

        /// <summary>A shared instance with every override unset.</summary>
        public static readonly StyleOverrides Empty = new();
    }
}
