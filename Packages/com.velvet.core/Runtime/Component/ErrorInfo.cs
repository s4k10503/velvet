namespace Velvet
{
    /// <summary>
    /// Diagnostic data passed to <c>Hooks.UseFallback</c>'s 2-arg overload when an Error Boundary
    /// catches a descendant render exception. Carries the caught error together with the
    /// component stack of the throwing subtree.
    /// </summary>
    /// <param name="ComponentStack">
    /// Multi-line string listing the throwing fiber and its ancestors up to the root. Each line
    /// follows the format <c>    at TypeName.MethodName</c>. The first line is the deepest fiber
    /// (the one whose <c>Render()</c> threw); subsequent lines walk up via <c>ComponentFiber.Parent</c>.
    /// </param>
    public sealed record ErrorInfo(string ComponentStack);
}
