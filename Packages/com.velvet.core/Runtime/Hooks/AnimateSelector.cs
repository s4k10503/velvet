#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // The selectors Animator.Animate resolves inside a scope: comma-separated groups of compound selectors joined by a
    // space (a descendant), each compound a type name or *, then any number of #name and .class parts:
    // ".card", "#hero", "Button.primary", ".list .row, #footer".
    internal static class AnimateSelector
    {
        public static List<VisualElement> Resolve(VisualElement scope, string selector)
        {
            var found = new List<VisualElement>();
            var descendants = scope.Query<VisualElement>().ToList();
            foreach (var group in selector.Split(','))
            {
                var parts = group.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    continue;
                }
                foreach (var element in descendants)
                {
                    if (!found.Contains(element) && MatchesChain(element, parts, parts.Length - 1, scope))
                    {
                        found.Add(element);
                    }
                }
            }
            return found;
        }

        // The last part must match the element, and each earlier one an ancestor below the scope, in order.
        private static bool MatchesChain(VisualElement element, string[] parts, int index, VisualElement scope)
        {
            if (!Matches(element, parts[index]))
            {
                return false;
            }
            if (index == 0)
            {
                return true;
            }
            for (var ancestor = element.hierarchy.parent; ancestor != null && ancestor != scope; ancestor = ancestor.hierarchy.parent)
            {
                if (MatchesChain(ancestor, parts, index - 1, scope))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool Matches(VisualElement element, string compound)
        {
            var position = 0;
            if (compound[0] != '.' && compound[0] != '#')
            {
                var end = compound.IndexOfAny(new[] { '.', '#' });
                var type = end < 0 ? compound : compound.Substring(0, end);
                if (type != "*" && element.GetType().Name != type)
                {
                    return false;
                }
                position = type.Length;
            }
            while (position < compound.Length)
            {
                var next = compound.IndexOfAny(new[] { '.', '#' }, position + 1);
                var part = compound.Substring(position + 1, (next < 0 ? compound.Length : next) - position - 1);
                if (!(compound[position] == '#' ? element.name == part : element.ClassListContains(part)))
                {
                    return false;
                }
                position = next < 0 ? compound.Length : next;
            }
            return true;
        }
    }
}
