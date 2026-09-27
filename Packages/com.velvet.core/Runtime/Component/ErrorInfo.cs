namespace Velvet
{
    /// <summary>
    /// Diagnostic data passed to <c>Hooks.UseFallback</c>'s 2-arg overload and to
    /// <see cref="MountOptions.OnCaughtError"/> when an Error Boundary catches a descendant's exception.
    /// </summary>
    /// <param name="ComponentStack">
    /// Multi-line string listing the throwing fiber and its ancestors up to the root. Each line
    /// follows the format <c>    at TypeName.MethodName</c>. The first line is the component the failing
    /// render, effect or callback belongs to; subsequent lines walk up via <c>ComponentFiber.Parent</c>.
    /// </param>
    public sealed record ErrorInfo(string ComponentStack)
    {
        /// <summary>
        /// The name of the Error Boundary that caught the exception, written as a <see cref="ComponentStack"/>
        /// line is without its <c>    at </c> prefix — React's <c>errorBoundary</c>.
        /// </summary>
        public string ErrorBoundary { get; init; } = string.Empty;
    }
}
