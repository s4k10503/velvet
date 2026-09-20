using System;

namespace Velvet
{
    /// <summary>
    /// Registers a static method returning <see cref="VNode"/> as a Velvet function component.
    /// </summary>
    /// <remarks>
    /// A static method annotated with <c>[Component]</c>:
    /// <list type="bullet">
    ///   <item>takes no parameter, mounted with <c>V.Component(MyComp.Render)</c>, or one props parameter,
    ///   mounted with <c>V.Component(MyComp.Render, props)</c> or <c>V.Memo</c>;</item>
    ///   <item>reads what it renders from its props and its hooks — <see cref="Compiler"/> states what the
    ///   build-time transform caches on;</item>
    ///   <item>follows the Rules of Hooks: hooks are called unconditionally, in a stable order, and only while
    ///   it renders.</item>
    /// </list>
    /// <para>
    /// <see cref="Memoize"/> (props-bail) and <see cref="Compiler"/> (build-time auto-memo) are its two
    /// memoization axes; see each member.
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
        /// shallow-equal to the previous render — the props bag compared member by member, each member
        /// decided by the per-type rule <see cref="MemoNode.Dependencies"/> states. A props value that is
        /// not a props bag — a value type, a string, a collection — is compared whole under that same rule
        /// instead of through a member set, so two distinct collections of equal content are a change.
        /// Any other value type is decided by its own <c>Equals</c>, which reads what it holds, and then — on this
        /// path alone — the <c>float</c> and <c>double</c> fields it carries, directly or inside a value
        /// type it holds, are compared by raw bit pattern on top of that, so <c>+0</c> and <c>-0</c> in one
        /// of those are a change as two <c>float</c> members are. That last reading is not part of the
        /// cited rule and the dependency comparison does not take it, so the same pair reaching a hook as a
        /// dependency is unchanged. <c>ComponentPropsComparerTests</c> is what fails if either half of the
        /// props answer stops holding, and <c>ObjectIsTests</c> if the dependency answer starts taking it.
        /// Default is <c>false</c>.
        /// <para>
        /// Only a component with <c>Memoize = true</c>, or one mounted with <c>V.Memo</c> and its comparer, skips a
        /// parent-driven render on props judged equal. Whether a component's body runs on a render that reaches it
        /// is <see cref="Compiler"/>'s to say.
        /// </para>
        /// <para>
        /// Unrelated to <see cref="MemoizeMethodAttribute"/>: that attribute drives per-method wrapping by the Source
        /// Generator, while this property is the per-component props-bail flag at the reconcile boundary. The
        /// shared <c>Memoize</c> root is all the two have in common.
        /// </para>
        /// </summary>
        public bool Memoize { get; init; } = false;

        /// <summary>
        /// Whether the build-time compiler transform — inner auto-memoization — is woven into this
        /// component. Default is <c>true</c>: the transform keys the component's VNode construction on its props
        /// and the values flowing out of its hook calls, compared as <see cref="MemoNode.Dependencies"/> states,
        /// and a render whose inputs all compare equal returns the cached tree instead of running the body. On
        /// such a render a difference that comparison does not see — a list mutated in place, a field a value
        /// type's own <c>Equals</c> ignores — does not reach the tree, nor does anything else the body reads. The
        /// cache serves the component's own render; called as a plain method, the body runs uncached.
        /// A component that takes props and calls no hook is keyed on its props alone, and left unwoven where it
        /// also sets <see cref="Memoize"/>; one with neither props nor a hook has nothing to key on and is left
        /// unwoven. Auto-memoization needs no opt-in: set this to <c>false</c> to opt
        /// out. The weaver also declines a component silently, with no diagnostic, where it finds a hook it
        /// cannot memoize safely, and <c>VelvetCompilerILPostProcessor.WillProcess</c> decides which
        /// assemblies it reaches at all.
        /// <para>
        /// Set to <c>false</c> to opt this component out of the transform, so its body then runs in full on
        /// every render. This is an escape hatch for
        /// the rare component whose render must not be cached. <see cref="Memoize"/> governs the separate
        /// props-bail axis at the reconcile boundary.
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
