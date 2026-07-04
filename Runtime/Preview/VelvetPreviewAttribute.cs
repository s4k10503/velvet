#if UNITY_EDITOR
using System;

namespace Velvet
{
    /// <summary>
    /// Marks a static method as a Velvet preview "story" — a named, self-contained snippet of UI that the
    /// Velvet Preview window can mount and live-render without entering Play Mode (the Storybook equivalent of
    /// a single exported story).
    /// </summary>
    /// <remarks>
    /// The annotated method must be <c>static</c>, take no parameters, and return a <see cref="VNode"/>
    /// (typically a <c>V.Component(...)</c> call or any <c>V.*</c> tree). It is invoked once per mount, so it
    /// may freely construct fresh props; the rendered tree's own hooks then drive any subsequent updates.
    /// <para>
    /// A story carries no environment of its own. Cross-cutting setup that several stories share — registering
    /// fonts, seeding a store, wiring a localization resolver — belongs on a
    /// <see cref="VelvetPreviewSetupAttribute"/> method, which runs once before any story in its assembly mounts.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class VelvetPreviewAttribute : Attribute
    {
        /// <summary>
        /// Display name shown in the preview window's story list. When <c>null</c> or empty, the method name is
        /// used.
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Optional grouping label so related stories collapse under one heading in the list (the Storybook
        /// "title" segment). When <c>null</c> or empty, the declaring type's name is used.
        /// </summary>
        public string Group { get; init; }

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
