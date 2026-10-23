#nullable enable
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // The hidden element opening a V.FocusScope's content among its parent's children, as React Aria's FocusScope
    // opens its content with a hidden span: the scope renders no element that holds its content. The scope's
    // binding sits here.
    internal sealed class FocusScopeStart : VisualElement
    {
        public FocusScopeStart()
        {
            style.display = DisplayStyle.None;
        }

        internal FocusScopeBinding? Binding { get; set; }
    }

    // The hidden element closing a V.FocusScope's content. It is attached after the content, so its attach is
    // where the scope's autoFocus finds the content in place.
    internal sealed class FocusScopeEnd : VisualElement
    {
        public FocusScopeEnd()
        {
            style.display = DisplayStyle.None;
            RegisterCallback<AttachToPanelEvent>(_ => FocusScopeDriver.OnRangeEndAttached(this));
        }
    }

    internal sealed record FocusScopeRangeProps(FocusScopeSettings Settings, VNode?[] Children);

    // A scope V.FocusScope declares: the elements between a FocusScopeStart and its matching FocusScopeEnd among
    // one parent's children, and everything under them.
    internal static class FocusScopeRange
    {
        private static readonly ConditionalWeakTable<ComponentFiber, StartHolder> s_starts = new();

        // Bindings on mounted starts, across every tree. While there are none, a walk asks no parent for its
        // ranges.
        internal static int Live { get; set; }

        // Its props are a new record whenever the scope's parent renders, so the weaver's cache could not hit.
        [Component(Compiler = false)]
        public static VNode Render(FocusScopeRangeProps props)
        {
            var holder = s_starts.GetValue(FiberAmbientStack.Current!, static _ => new StartHolder());
            return V.Fragment(new VNode?[]
            {
                new ElementNode
                {
                    Key = "start",
                    ElementType = typeof(FocusScopeStart),
                    Props = new FiberElementProps { FocusScope = props.Settings },
                    RefCallback = holder.Track,
                },
                V.Fragment(props.Children, key: "content"),
                new ElementNode { Key = "end", ElementType = typeof(FocusScopeEnd) },
            });
        }

        // The start a FocusScope component's fiber rendered, while it is mounted.
        internal static FocusScopeStart? StartRenderedBy(ComponentFiber fiber)
            => s_starts.TryGetValue(fiber, out var holder) ? holder.Element : null;

        internal static FocusScopeStart? StartOf(FocusScopeEnd end)
        {
            var parent = end.hierarchy.parent;
            if (parent == null)
            {
                return null;
            }
            var depth = 0;
            for (var i = parent.hierarchy.IndexOf(end) - 1; i >= 0; i--)
            {
                switch (parent.hierarchy[i])
                {
                    case FocusScopeEnd:
                        depth++;
                        break;
                    case FocusScopeStart start when depth == 0:
                        return start;
                    case FocusScopeStart:
                        depth--;
                        break;
                }
            }
            return null;
        }

        // Whether `child`, one of the start's siblings, lies between the start and its end.
        internal static bool Brackets(FocusScopeStart start, VisualElement child)
        {
            var parent = start.hierarchy.parent;
            if (parent == null || !ReferenceEquals(child.hierarchy.parent, parent))
            {
                return false;
            }
            var depth = 0;
            for (var i = parent.hierarchy.IndexOf(start) + 1; i < parent.hierarchy.childCount; i++)
            {
                var sibling = parent.hierarchy[i];
                if (sibling is FocusScopeEnd && depth == 0)
                {
                    return false;
                }
                if (ReferenceEquals(sibling, child))
                {
                    return true;
                }
                if (sibling is FocusScopeStart)
                {
                    depth++;
                }
                else if (sibling is FocusScopeEnd)
                {
                    depth--;
                }
            }
            return false;
        }

        // Whether `element` is inside the range: it, or the ancestor of it among the start's siblings, lies
        // between the start and its end.
        internal static bool Contains(FocusScopeStart start, VisualElement? element)
        {
            var parent = start.hierarchy.parent;
            for (var current = element; current != null && parent != null; current = current.hierarchy.parent)
            {
                if (ReferenceEquals(current.hierarchy.parent, parent))
                {
                    return Brackets(start, current);
                }
            }
            return false;
        }

        // Appends the starts of the ranges bracketing `child` among its parent's children, outermost first.
        internal static void AppendStartsAround(VisualElement child, List<FocusScopeStart> into)
        {
            var parent = child.hierarchy.parent;
            if (parent == null)
            {
                return;
            }
            var floor = into.Count;
            for (var i = 0; i < parent.hierarchy.childCount; i++)
            {
                var sibling = parent.hierarchy[i];
                if (ReferenceEquals(sibling, child))
                {
                    return;
                }
                if (sibling is FocusScopeStart start)
                {
                    into.Add(start);
                }
                else if (sibling is FocusScopeEnd && into.Count > floor)
                {
                    into.RemoveAt(into.Count - 1);
                }
            }
            into.RemoveRange(floor, into.Count - floor);
        }

        // The elements between the start and its end, in order.
        internal static void AppendContent(FocusScopeStart start, List<VisualElement> into)
        {
            var parent = start.hierarchy.parent;
            if (parent == null)
            {
                return;
            }
            var depth = 0;
            for (var i = parent.hierarchy.IndexOf(start) + 1; i < parent.hierarchy.childCount; i++)
            {
                var sibling = parent.hierarchy[i];
                if (sibling is FocusScopeEnd && depth == 0)
                {
                    return;
                }
                if (sibling is FocusScopeStart)
                {
                    depth++;
                }
                else if (sibling is FocusScopeEnd)
                {
                    depth--;
                }
                into.Add(sibling);
            }
        }

        // One per fiber, so the ref callback the start carries is the same delegate on every render.
        private sealed class StartHolder
        {
            internal readonly System.Func<VisualElement, System.Action> Track;

            internal StartHolder()
            {
                Track = element =>
                {
                    Element = element as FocusScopeStart;
                    return () =>
                    {
                        if (ReferenceEquals(Element, element))
                        {
                            Element = null;
                        }
                    };
                };
            }

            internal FocusScopeStart? Element { get; private set; }
        }
    }
}
