using System;

namespace Velvet
{
    /// <summary>
    /// Registers a functional component (`static VNode XxxComp()`) as a Velvet component.
    /// </summary>
    /// <remarks>
    /// A static method annotated with <c>[Component]</c> must:
    /// <list type="bullet">
    ///   <item>Return VNode and take no arguments (Render is parameterless)</item>
    ///   <item>Access external state only through <see cref="Hooks"/> (UseStore / UseContext / UseState, etc.)</item>
    ///   <item>Follow the Rules of Hooks — call hooks unconditionally, in a stable order, only inside Render. These are enforced at runtime (a violation throws <see cref="InvalidOperationException"/>), not by a compile-time analyzer</item>
    ///   <item>Be referenced from a parent VNode tree as <c>V.Component(MyComp.Render)</c></item>
    /// </list>
    /// <para>
    /// Two independent memoization axes — <see cref="Memoize"/> (props-bail) and <see cref="Compiler"/>
    /// (build-time auto-memo); see each member.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class ComponentAttribute : Attribute
    {
        /// <summary>
        /// Whether this component behaves as an Error Boundary. Default is <c>false</c>.
        /// </summary>
        public bool IsErrorBoundary { get; init; } = false;

        /// <summary>
        /// Opt-in props-bail: skip a parent-driven re-render when this component's props are
        /// shallow-equal to the previous render (each property compared by reference/value identity). Default is <c>false</c>.
        /// <para>
        /// This is a true opt-in gate: only a component with <c>Memoize = true</c> (or one created via
        /// <c>V.Memo</c> with a custom comparator) bails on shallow-equal props. A component without it
        /// re-renders whenever its parent re-renders; only an opted-in component
        /// skips a re-render on equal props.
        /// </para>
        /// </summary>
        public bool Memoize { get; init; } = false;

        /// <summary>
        /// Whether the build-time compiler transform — inner auto-memoization — is woven into this
        /// component. Default is <c>true</c>: the transform caches the component's VNode construction keyed on the
        /// values flowing out of its hook calls (and its props), rebuilding only when one of those inputs changes
        /// by reference/value identity. Every component is auto-memoized with no opt-in, so this is on by
        /// default for all components.
        /// <para>
        /// Set to <c>false</c> to opt this component out of the transform, so its body then runs in full on
        /// every render. This is an escape hatch for
        /// the rare component whose render must not be cached; it is orthogonal to <see cref="Memoize"/>, which
        /// governs the props-bail axis at the reconcile boundary.
        /// </para>
        /// </summary>
        public bool Compiler { get; init; } = true;

        /// <summary>
        /// Optional debug name used in hook-rule violation messages and other diagnostics in place of
        /// the default <c>"DeclaringType.MethodName"</c> form.
        /// When <c>null</c> or empty, the default name is used.
        /// </summary>
        public string? DisplayName { get; init; }
    }
}
