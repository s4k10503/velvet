namespace Velvet
{
    /// <summary>
    /// Diagnostic data passed to <c>Hooks.UseFallback</c>'s 2-arg overload and to
    /// <see cref="MountOptions.OnCaughtError"/> when an Error Boundary catches a descendant's exception, and to
    /// <see cref="MountOptions.OnUncaughtError"/> when none does.
    /// </summary>
    /// <param name="ComponentStack">
    /// Multi-line string listing the throwing fiber and its ancestors up to the outermost component of the tree
    /// passed to <c>V.Mount</c>; the root <c>V.Mount</c> makes is not listed, as React's stack leaves out the
    /// root, and an error the root itself raises has an empty stack. Each line
    /// follows the format <c>    at TypeName.MethodName</c>. The first line is the component the failing
    /// render, effect or callback belongs to; subsequent lines walk up via <c>ComponentFiber.Parent</c>.
    /// </param>
    public sealed record ErrorInfo(string ComponentStack)
    {
        /// <summary>
        /// The name of the Error Boundary that caught the exception, written as a <see cref="ComponentStack"/>
        /// line is without its <c>    at </c> prefix — React's <c>errorBoundary</c>. Empty for an error no boundary
        /// caught.
        /// </summary>
        public string ErrorBoundary { get; init; } = string.Empty;
    }
}
