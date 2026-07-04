#if UNITY_EDITOR
using System;

namespace Velvet
{
    /// <summary>
    /// Marks a static method that prepares the shared environment every preview story in its assembly relies on
    /// — the Storybook global-decorator / <c>preview.js</c> equivalent. It runs once before the first story in
    /// that assembly mounts (font registration, store seeding, a localization resolver, a utility stylesheet) and
    /// its returned handle is disposed when previewing stops or the story source is rescanned.
    /// </summary>
    /// <remarks>
    /// The annotated method must be <c>static</c>, take no parameters, and return either an
    /// <see cref="IDisposable"/> or an <see cref="Action"/> teardown (or <c>void</c> when nothing needs undoing).
    /// Whatever it sets up is torn down symmetrically, so previewing leaves no global state (registered fonts, a
    /// dangling resolver) behind for the next editor operation.
    /// <para>
    /// At most one setup per assembly is honored; a second is ignored with a warning so the environment a story
    /// mounts into stays unambiguous.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class VelvetPreviewSetupAttribute : Attribute
    {
    }
}
#endif
