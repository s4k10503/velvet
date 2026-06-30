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

        /// <summary>Color computed at runtime.</summary>
        public StyleColor? BackgroundColor { get; init; }

        /// <summary>Dynamic text color.</summary>
        public StyleColor? Color { get; init; }

        /// <summary>A shared instance with every override unset.</summary>
        public static readonly StyleOverrides Empty = new();
    }
}
