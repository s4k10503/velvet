namespace Velvet
{
    /// <summary>
    /// Public entry points for Velvet's responsive system, for consumers that need to reference its conventions
    /// from code rather than only as utility-class strings.
    /// </summary>
    public static class VelvetResponsive
    {
        /// <summary>
        /// The utility class that marks an element as a query container — Tailwind v4's <c>@container</c>, the CSS
        /// <c>container-type: inline-size</c> analog. Its descendants' <c>@sm:</c>…<c>@7xl:</c>,
        /// <c>@min-*:</c> and <c>@max-*:</c> variants evaluate against THAT element's width. The viewport
        /// variants <c>sm:</c>…<c>2xl:</c> are unaffected: they never read a container. Exposed so tooling can
        /// apply the marker without hardcoding the raw string.
        /// <para>
        /// A container can be named, <c>@container/name</c>, which only <c>@sm/name:</c>-style variants of the
        /// same name ask for; an unnamed variant asks for the nearest container whatever its name. Adding or
        /// removing the class, or changing its name, on an already-attached ancestor re-points the descendants
        /// that are already attached.
        /// </para>
        /// </summary>
        public const string ContainerClass = "@container";

        /// <summary>
        /// The utility class that makes an element stand in for the viewport: its descendants' <c>sm:</c>…
        /// <c>2xl:</c> variants evaluate against THAT element's width instead of the panel root's. It is for
        /// tooling that simulates a viewport inside a larger panel, such as the preview window's Viewport addon;
        /// an application that wants a component to respond to its own width marks it
        /// <see cref="ContainerClass"/> and writes <c>@sm:</c>-style variants. Adding or removing the class on
        /// an already-attached ancestor re-points the descendants that are already attached.
        /// </summary>
        public const string ViewportClass = "velvet-viewport";
    }
}
