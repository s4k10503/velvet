#if UNITY_EDITOR
using System;

namespace Velvet
{
    /// <summary>
    /// Marks a static method as a Velvet preview "story" — a named, self-contained snippet of UI that the
    /// Velvet Preview window can mount and live-render without entering Play Mode.
    /// </summary>
    /// <remarks>
    /// The annotated method must be <c>static</c>, return a <see cref="VNode"/>, and take either no parameters
    /// or one supported args value. It is invoked whenever the preview host builds the story tree.
    /// <para>
    /// Cross-cutting setup shared by stories belongs on a <see cref="VelvetPreviewSetupAttribute"/> method.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class VelvetPreviewAttribute : Attribute
    {
        /// <summary>
        /// Display name shown in the preview window's story list. When <c>null</c> or empty, the method name is
        /// used.
        /// </summary>
        public string? Name { get; init; }

        /// <summary>
        /// Optional grouping label so related stories collapse under one heading in the list. When <c>null</c>
        /// or empty, the declaring type's name is used.
        /// </summary>
        public string? Group { get; init; }

        /// <summary>
        /// Preferred mount width in reference pixels. <c>0</c> (the default) means "fill the window".
        /// </summary>
        public int Width { get; init; }

        /// <summary>
        /// Preferred mount height in reference pixels. <c>0</c> (the default) means "fill the window".
        /// </summary>
        public int Height { get; init; }
    }
}
#endif
