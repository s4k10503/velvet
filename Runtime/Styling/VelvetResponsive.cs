namespace Velvet
{
    /// <summary>
    /// Public entry points for Velvet's responsive system, for consumers that need to reference its conventions
    /// from code rather than only as utility-class strings.
    /// </summary>
    public static class VelvetResponsive
    {
        /// <summary>
        /// The utility class that marks an element as a responsive scope — the CSS <c>container-type:
        /// inline-size</c> analog. Add it to an element and its descendants' <c>sm:</c>/<c>md:</c>/… breakpoints
        /// evaluate against THAT element's width instead of the panel root's. Exposed so tooling (e.g. a preview
        /// viewport switcher) can apply the marker without hardcoding the raw string.
        /// <para>
        /// Attach-time caveat (the scope is structural, like a real CSS container): a descendant binds its
        /// responsive width source ONCE, when it attaches to the panel. Adding or removing this class on an
        /// already-attached ancestor at runtime does NOT re-point descendants that are already attached — they
        /// keep the source resolved at their attach until they re-attach. Supported usage: put the class on the
        /// scope element before its subtree mounts, or re-mount the subtree after toggling it.
        /// </para>
        /// </summary>
        public const string ContainerClass = "@container";
    }
}
