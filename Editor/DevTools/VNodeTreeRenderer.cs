using System;
using System.Text;

namespace Velvet.Editor.DevTools
{
    /// <summary>
    /// Utility that converts a VNode tree into indented text.
    /// </summary>
    internal static class VNodeTreeRenderer
    {
        private const int IndentWidth = 2;
        private const int MaxDepth = 50;

        /// <summary>
        /// Converts a VNode array into an indented text representation.
        /// </summary>
        /// <param name="nodes">VNode array to render. Returns "(empty tree)" when null or empty.</param>
        /// <returns>Indented display string.</returns>
        public static string Render(VNode[] nodes)
        {
            if (nodes == null || nodes.Length == 0)
            {
                return "(empty tree)";
            }

            var sb = new StringBuilder();
            foreach (var node in nodes)
            {
                AppendNode(sb, node, 0);
            }
            return sb.ToString();
        }

        private static void AppendNode(StringBuilder sb, VNode node, int depth)
        {
            if (depth > MaxDepth)
            {
                AppendLine(sb, "...(depth limit reached)", depth);
                return;
            }

            if (node == null)
            {
                AppendLine(sb, "(null)", depth);
                return;
            }

            switch (node)
            {
                case ElementNode elementNode:
                    AppendElementNode(sb, elementNode, depth);
                    break;

                case TextNode textNode:
                    AppendLine(sb, $"[Text] \"{TruncateText(textNode.Text)}\"", depth);
                    break;

                case ComponentNode componentNode:
                    AppendLine(sb, $"[Component] {GetGenericTypeName(componentNode)}", depth);
                    break;

                case MemoNode:
                    AppendLine(sb, "[Memo]", depth);
                    break;

                case FragmentNode fragmentNode:
                    AppendLine(sb, "[Fragment]", depth);
                    AppendChildren(sb, fragmentNode.Children, depth);
                    break;

                case AnimatePresenceNode apNode:
                    AppendLine(sb, "[AnimatePresence]", depth);
                    AppendChildren(sb, apNode.Children, depth);
                    break;

                case ContextProviderNode contextNode:
                    AppendLine(sb, $"[ContextProvider<{GetGenericTypeName(contextNode)}>]", depth);
                    AppendChildren(sb, contextNode.Children, depth);
                    break;

                case PortalNode portalNode:
                    // TargetId and Layer are a one-of pair; whichever is set names the target. The
                    // children live in the logical tree even though they attach elsewhere, so the
                    // dump walks them like every other container's.
                    AppendLine(sb, $"[Portal] target={portalNode.TargetId ?? portalNode.Layer?.ToString()}", depth);
                    AppendChildren(sb, portalNode.Children, depth);
                    break;

                case WorldSpaceNode worldSpaceNode:
                    AppendLine(sb, $"[WorldSpace] position={worldSpaceNode.Position} panelSize={worldSpaceNode.PanelSize}", depth);
                    AppendChildren(sb, worldSpaceNode.Children, depth);
                    break;

                case SuspenseNode:
                    AppendLine(sb, "[Suspense]", depth);
                    break;

                case VirtualListNode virtualListNode:
                    AppendLine(sb, $"[VirtualList] items={virtualListNode.Items?.Count ?? 0}", depth);
                    break;

                case MotionNode motionNode:
                    AppendElementNode(sb, motionNode, depth, prefix: "[Motion] ");
                    break;

                case OutletNode:
                    AppendLine(sb, "[Outlet]", depth);
                    break;

                default:
                    AppendLine(sb, $"[{node.GetType().Name}]", depth);
                    break;
            }
        }

        private static void AppendElementNode(StringBuilder sb, BaseElementNode node, int depth, string prefix = "")
        {
            var typeName = node.ElementType?.Name ?? "VisualElement";
            var namePart = !string.IsNullOrEmpty(node.Name) ? $" #{node.Name}" : string.Empty;
            var keyPart = !string.IsNullOrEmpty(node.Key) ? $" key={node.Key}" : string.Empty;
            var classPart = node.ClassNames != null && node.ClassNames.Length > 0
                ? " ." + string.Join(" .", node.ClassNames)
                : string.Empty;

            AppendLine(sb, $"{prefix}<{typeName}{namePart}{keyPart}{classPart}>", depth);
            AppendChildren(sb, node.Children, depth);
        }

        // The shared null-guarded child walk every container case uses.
        private static void AppendChildren(StringBuilder sb, VNode[] children, int depth)
        {
            if (children == null)
            {
                return;
            }
            foreach (var child in children)
            {
                AppendNode(sb, child, depth + 1);
            }
        }

        private static void AppendLine(StringBuilder sb, string content, int depth)
        {
            sb.Append(' ', depth * IndentWidth);
            sb.AppendLine(content);
        }

        private static string TruncateText(string text)
        {
            if (text == null)
            {
                return string.Empty;
            }

            return text.Length > 80 ? text.Substring(0, 77) + "..." : text;
        }

        // Eliminates copy-paste across ComponentNode / ContextProviderNode and similar nodes.
        private static string GetGenericTypeName(object node)
        {
            var typeArgs = node.GetType().GetGenericArguments();
            return typeArgs.Length > 0 ? typeArgs[0].Name : node.GetType().Name;
        }
    }
}
