#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet
{
    // The longhands a class of the application's own writes, which the bundled stylesheets' table cannot name. They are
    // read once per class string and panel, from a probe element carrying the class beside one that carries none: a
    // longhand whose computed value differs between the two is one the class sets. A class that declares a longhand at
    // the value it would compute without it is not seen to set it, and a declaration behind a pseudo-class is not seen
    // either, since the probe is in no state.
    //
    // The probe pair sits in a container hosted beside the element that asked, so the stylesheets the element sees are
    // the ones the probe resolves against. The container is a reconciler-invisible child (SilhouetteBoundsSpacer.IsSpacer)
    // and goes at its first geometry change, which StyleUserClassPropertiesPanelTests reads the result from. Until then
    // the class is ranked as before, by no property at all, and the elements that asked are told once it is read.
    internal static class StyleUserClassProperties
    {
        internal const string MarkerClass = "velvet-class-probe";

        private sealed class PanelCache
        {
            public readonly Dictionary<string, StyleLonghandSet> Known = new(StringComparer.Ordinal);
            public readonly Dictionary<string, List<WeakReference<VisualElement>>> Waiting = new(StringComparer.Ordinal);
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IPanel, PanelCache> s_caches = new();

        // The classes an element asked for before it had a panel, asked again once it has one.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, List<string>> s_unattached = new();

        private static readonly EventCallback<AttachToPanelEvent> s_onAttach = OnAttach;

        // Whether the properties of cls are known; false while its probe is out, with properties empty. The element is
        // told through StyleClassProjection once they are read.
        internal static bool TryGet(VisualElement element, string cls, out StyleLonghandSet properties)
        {
            properties = StyleLonghandSet.Empty;
            if (IsEngineOwned(cls))
            {
                return true;
            }
            if (element.panel == null)
            {
                Defer(element, cls);
                return false;
            }
            var cache = s_caches.GetOrCreateValue(element.panel);
            if (cache.Known.TryGetValue(cls, out properties))
            {
                return true;
            }
            Request(element, cls, cache);
            return false;
        }

        // Whether the properties of cls are already known, asking nothing.
        internal static bool TryGetKnown(VisualElement element, string cls, out StyleLonghandSet properties)
        {
            properties = StyleLonghandSet.Empty;
            if (IsEngineOwned(cls))
            {
                return true;
            }
            if (element.panel == null)
            {
                return false;
            }
            return s_caches.GetOrCreateValue(element.panel).Known.TryGetValue(cls, out properties);
        }

        // The classes UI Toolkit's own controls and Velvet's internal elements carry. A rule of theirs that a probe read
        // would let a payload take a built-in control's own styling off it.
        private static bool IsEngineOwned(string cls)
            => cls.StartsWith("unity-", StringComparison.Ordinal) || cls.StartsWith("velvet-", StringComparison.Ordinal);

        private static void Defer(VisualElement element, string cls)
        {
            if (!s_unattached.TryGetValue(element, out var classes))
            {
                classes = new List<string>();
                s_unattached.Add(element, classes);
                element.RegisterCallback(s_onAttach);
            }
            if (!classes.Contains(cls))
            {
                classes.Add(cls);
            }
        }

        private static void OnAttach(AttachToPanelEvent evt)
        {
            var element = (VisualElement)evt.currentTarget;
            element.UnregisterCallback(s_onAttach);
            if (!s_unattached.TryGetValue(element, out var classes))
            {
                return;
            }
            s_unattached.Remove(element);
            var model = StyleArbitraryValueResolver.TryGetProjection(element);
            foreach (var cls in classes)
            {
                var known = TryGet(element, cls, out var properties);
                if (known && model != null)
                {
                    model.Learn(element, cls, properties);
                }
            }
        }

        private static void Request(VisualElement element, string cls, PanelCache cache)
        {
            if (cache.Waiting.TryGetValue(cls, out var waiting))
            {
                if (!IsWaiting(waiting, element))
                {
                    waiting.Add(new WeakReference<VisualElement>(element));
                }
                return;
            }
            cache.Waiting[cls] = new List<WeakReference<VisualElement>> { new(element) };
            Host(element.parent ?? element, cls, cache);
        }

        private static bool IsWaiting(List<WeakReference<VisualElement>> waiting, VisualElement element)
        {
            var found = false;
            foreach (var reference in waiting)
            {
                reference.TryGetTarget(out var target);
                found |= ReferenceEquals(target, element);
            }
            return found;
        }

        private static void Host(VisualElement host, string cls, PanelCache cache)
        {
            var container = new VisualElement { pickingMode = PickingMode.Ignore };
            container.AddToClassList(MarkerClass);
            container.style.position = Position.Absolute;
            container.style.width = 1f;
            container.style.height = 1f;
            var blank = new VisualElement { pickingMode = PickingMode.Ignore };
            var probe = new VisualElement { pickingMode = PickingMode.Ignore };
            probe.AddToClassList(cls);
            container.Add(blank);
            container.Add(probe);
            container.RegisterCallback<GeometryChangedEvent>(_ => Finish(container, blank, probe, cls, cache));
            host.Add(container);
        }

        private static void Finish(VisualElement container, VisualElement blank, VisualElement probe, string cls,
            PanelCache cache)
        {
            if (container.parent == null)
            {
                return;
            }
            var properties = Differing(blank, probe);
            container.RemoveFromHierarchy();
            cache.Known[cls] = properties;
            if (!cache.Waiting.Remove(cls, out var waiting))
            {
                return;
            }
            foreach (var reference in waiting)
            {
                if (reference.TryGetTarget(out var element))
                {
                    StyleArbitraryValueResolver.TryGetProjection(element)?.Learn(element, cls, properties);
                }
            }
        }

        // The computed style's property for each longhand the table indexes, by the name UI Toolkit gives it.
        private static (StyleLonghand Longhand, PropertyInfo Property)[]? s_readers;

        private static (StyleLonghand Longhand, PropertyInfo Property)[] ReadersOf(Type computedStyle)
        {
            if (s_readers != null)
            {
                return s_readers;
            }
            var byName = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
            foreach (var property in computedStyle.GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                byName[property.Name] = property;
            }
            var readers = new List<(StyleLonghand, PropertyInfo)>();
            foreach (StyleLonghand longhand in Enum.GetValues(typeof(StyleLonghand)))
            {
                if (byName.TryGetValue(ComputedName(longhand), out var property))
                {
                    readers.Add((longhand, property));
                }
            }
            s_readers = readers.ToArray();
            return s_readers;
        }

        private static string ComputedName(StyleLonghand longhand)
        {
            if (longhand == StyleLonghand.UnityFontStyle)
            {
                return "unityFontStyleAndWeight";
            }
            var name = longhand.ToString();
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        private static StyleLonghandSet Differing(VisualElement blank, VisualElement probe)
        {
            var set = StyleLonghandSet.Empty;
            var before = StyleCascade.Computed(blank);
            var after = StyleCascade.Computed(probe);
            if (before == null || after == null)
            {
                return set;
            }
            foreach (var (longhand, property) in ReadersOf(before.GetType()))
            {
                if (!Same(property.GetValue(before), property.GetValue(after)))
                {
                    set = set.Union(StyleLonghandSet.Of(longhand));
                }
            }
            return set;
        }

        // A list-valued property (the transition lists) is a list of its own per style, so equal lists differ by
        // reference.
        private static bool Same(object? a, object? b)
        {
            var lists = a is IEnumerable && b is IEnumerable && a is not string;
            return lists ? SameSequence((IEnumerable)a!, (IEnumerable)b!) : Equals(a, b);
        }

        private static bool SameSequence(IEnumerable left, IEnumerable right)
        {
            var l = left.GetEnumerator();
            var r = right.GetEnumerator();
            while (true)
            {
                var more = l.MoveNext();
                if (more != r.MoveNext())
                {
                    return false;
                }
                if (!more)
                {
                    return true;
                }
                if (!Equals(l.Current, r.Current))
                {
                    return false;
                }
            }
        }
    }
}
