#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;

namespace Velvet
{
    /// <summary>
    /// Editor-only opt-in that double-invokes render and effect commit to
    /// surface impure renders and non-symmetric effect cleanup. Exposed as a single editor-wide switch and
    /// compiled out of player builds entirely.
    /// </summary>
    /// <remarks>Equivalent to React's StrictMode for users migrating from React.</remarks>
    public static class FiberStrictMode
    {
        /// <summary>
        /// When true, double-invoke is active for every fiber rendered in the Editor. Defaults to false so the
        /// behavior never changes unless a developer (or a test) opts in. Toggle in test setup / teardown to
        /// scope the check to a single fixture.
        /// </summary>
        public static bool Enabled { get; set; }

        /// <summary>
        /// Computes a structural signature of a settled render output for impure-render detection. The signature
        /// captures node kind, key, element identity, and text content recursively; it intentionally ignores
        /// delegate identity (event handlers / ref callbacks are allowed to differ between passes since closures
        /// allocate fresh each render). A divergence between the two passes' signatures indicates the render body
        /// produced different structure on identical hook state, i.e. an impure render.
        /// </summary>
        internal static string ComputeSignature(VNode[] tree)
        {
            var sb = new StringBuilder();
            AppendTree(sb, tree);
            return sb.ToString();
        }

        private static void AppendTree(StringBuilder sb, VNode[] tree)
        {
            if (tree == null)
            {
                sb.Append("∅");
                return;
            }
            sb.Append('[');
            for (var i = 0; i < tree.Length; i++)
            {
                if (i > 0) sb.Append(',');
                AppendNode(sb, tree[i]);
            }
            sb.Append(']');
        }

        private static void AppendNode(StringBuilder sb, VNode node)
        {
            if (node == null)
            {
                sb.Append("null");
                return;
            }

            switch (node)
            {
                case TextNode text:
                    sb.Append("T(").Append(node.Key).Append('|').Append(text.Text).Append(')');
                    break;
                case ElementNode elem:
                    sb.Append("E(").Append(node.Key).Append('|').Append(elem.ElementType?.Name)
                        .Append('|').Append(elem.Name).Append('|');
                    AppendProps(sb, elem.Props);
                    AppendClassNames(sb, elem.ClassNames);
                    AppendTree(sb, elem.Children);
                    sb.Append(')');
                    break;
                case MotionNode motion:
                    sb.Append("M(").Append(node.Key).Append('|').Append(motion.ElementType?.Name)
                        .Append('|').Append(motion.Name).Append('|');
                    AppendProps(sb, motion.Props);
                    AppendClassNames(sb, motion.ClassNames);
                    AppendTree(sb, motion.Children);
                    sb.Append(')');
                    break;
                case FragmentNode fragment:
                    sb.Append("F(").Append(node.Key).Append('|');
                    AppendTree(sb, fragment.Children);
                    sb.Append(')');
                    break;
                case ComponentNode component:
                    // The child component renders on its own fiber; comparing its identity and props is enough
                    // to detect a structural swap without recursing into its render body.
                    sb.Append("C(").Append(node.Key).Append('|').Append(component.ResolvedIdentity)
                        .Append('|').Append(component.Props).Append(')');
                    break;
                case MemoNode memo:
                    sb.Append("Memo(").Append(node.Key).Append('|');
                    AppendDeps(sb, memo.Dependencies);
                    sb.Append(')');
                    break;
                case PortalNode portal:
                    sb.Append("Portal(").Append(node.Key).Append('|').Append(portal.TargetId).Append('|');
                    AppendTree(sb, portal.Children);
                    sb.Append(')');
                    break;
                case SuspenseNode suspense:
                    sb.Append("S(").Append(node.Key).Append('|');
                    AppendTree(sb, suspense.Children);
                    sb.Append(')');
                    break;
                case AnimatePresenceNode presence:
                    sb.Append("AP(").Append(node.Key).Append('|');
                    AppendTree(sb, presence.Children);
                    sb.Append(')');
                    break;
                default:
                    sb.Append(node.GetType().Name).Append('(').Append(node.Key).Append(')');
                    break;
            }
        }

        private static void AppendProps(StringBuilder sb, FiberElementProps props)
        {
            // Value-bearing content of the element. Impure renders most often diverge here (e.g. a
            // Label whose text is derived from mutated module state), so the signature must capture
            // these alongside the structural shape. Callbacks / events / styles are excluded: they are
            // allocated fresh each render and their identity is not a purity signal.
            sb.Append("p(");
            if (props != null)
            {
                sb.Append(props.Text).Append('|')
                    .Append(props.Tooltip).Append('|')
                    .Append(props.Enabled).Append('|')
                    .Append(props.Visible).Append('|')
                    .Append(props.FieldValue).Append('|')
                    .Append(props.Focusable).Append('|');
                // Data / aria attributes are value-bearing content too, so the signature must capture their
                // VALUES, not just the entry count: an impure render may flip data-[state] from "open" to
                // "closed" (or rewrite aria-[label]) while the count stays constant. Entries are emitted
                // sorted by key so the signal stays independent of a map's non-deterministic iteration order —
                // two equal maps hash identically, while any key OR value difference diverges.
                AppendAttributes(sb, props.Data);
                sb.Append('|');
                AppendAttributes(sb, props.Aria);
            }
            sb.Append(')');
        }

        // Appends an attribute map as a key-sorted "k=v;" run: captures every key and value yet stays
        // independent of the map's iteration order (sorting makes two equal maps produce the same text).
        private static void AppendAttributes(StringBuilder sb, IReadOnlyDictionary<string, string> attrs)
        {
            sb.Append('a').Append('(');
            if (attrs != null && attrs.Count > 0)
            {
                var keys = new string[attrs.Count];
                var i = 0;
                foreach (var key in attrs.Keys)
                {
                    keys[i++] = key;
                }
                Array.Sort(keys, StringComparer.Ordinal);
                for (var k = 0; k < keys.Length; k++)
                {
                    sb.Append(keys[k]).Append('=').Append(attrs[keys[k]]).Append(';');
                }
            }
            sb.Append(')');
        }

        private static void AppendClassNames(StringBuilder sb, string[] classNames)
        {
            sb.Append('{');
            if (classNames != null)
            {
                for (var i = 0; i < classNames.Length; i++)
                {
                    if (i > 0) sb.Append(' ');
                    sb.Append(classNames[i]);
                }
            }
            sb.Append('}');
        }

        private static void AppendDeps(StringBuilder sb, object[] deps)
        {
            sb.Append('<');
            if (deps != null)
            {
                for (var i = 0; i < deps.Length; i++)
                {
                    if (i > 0) sb.Append(' ');
                    sb.Append(deps[i]);
                }
            }
            sb.Append('>');
        }
    }
}
#endif
