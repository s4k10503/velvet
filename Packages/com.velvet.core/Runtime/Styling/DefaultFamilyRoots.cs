#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Velvet
{
    // VelvetFonts.DefaultFamily written inline on the elements Velvet renders into, for what is under them to inherit,
    // as what is in a page inherits the font-family Tailwind's preflight gives html: the element a tree is mounted on,
    // a portal's container, and a layer or world-space panel's root. One of them inside another is left to inherit, so
    // a family an element between the two names reaches what is under it; which is inside which is read again at each
    // hold, each release and each change to the registry.
    //
    // An inline font somebody else wrote on one of them is theirs and stays, as an inline style on html outranks the
    // preflight's rule; the value written here is taken back only while it is still the one written here.
    internal static class DefaultFamilyRoots
    {
        private sealed class Root
        {
            // Weak, so a tree nobody disposed does not keep the element it was mounted on alive through this.
            public readonly WeakReference<VisualElement> Element;
            public int Holds;

            // What the last write here put on the element, or null for a write taking it back.
            public FontAsset? Written;

            public Root(VisualElement element) => Element = new WeakReference<VisualElement>(element);
        }

        private static readonly ConditionalWeakTable<VisualElement, Root> s_roots = new();
        private static readonly List<Root> s_held = new();
        private static readonly Predicate<Root> s_collected = static root => !root.Element.TryGetTarget(out _);

        static DefaultFamilyRoots() => VelvetFonts.FontsChanged += Apply;

        internal static IDisposable Hold(VisualElement element)
        {
            if (!s_roots.TryGetValue(element, out var root))
            {
                root = new Root(element);
                s_roots.Add(element, root);
                s_held.Add(root);
            }

            root.Holds++;
            Apply();
            return new Release(element, root);
        }

        // No guard against a second Dispose: a MountedTree disposes this on its first Dispose alone, and a portal's
        // bridge and a panel host's record each release theirs once and drop it.
        private sealed class Release : IDisposable
        {
            private readonly VisualElement _element;
            private readonly Root _root;

            public Release(VisualElement element, Root root)
            {
                _element = element;
                _root = root;
            }

            public void Dispose()
            {
                if (--_root.Holds == 0)
                {
                    s_roots.Remove(_element);
                    s_held.Remove(_root);
                    Write(_element, _root, null);
                }

                Apply();
            }
        }

        private static void Apply()
        {
            var asset = VelvetFonts.Resolve(null, VelvetFontWeight.Normal, italic: false).Asset;
            // MUTANT_SURVIVES(equivalent, line removed): the loop below skips a collected element's entry whether or
            // not it is taken out here; taking it out keeps entries of trees nobody disposed from piling up.
            s_held.RemoveAll(s_collected);
            foreach (var root in s_held)
            {
                if (root.Element.TryGetTarget(out var element))
                {
                    Write(element, root, InsideAnother(element) ? null : asset);
                }
            }
        }

        private static void Write(VisualElement element, Root root, FontAsset? asset)
        {
            var inline = element.style.unityFontDefinition;
            var ours = root.Written != null && inline.value.fontAsset == root.Written;
            if (!ours && inline.keyword != StyleKeyword.Null) return;

            root.Written = asset;
            element.style.unityFontDefinition = asset == null
                ? new StyleFontDefinition(StyleKeyword.Null)
                : new StyleFontDefinition(FontDefinition.FromSDFFont(asset));
        }

        private static bool InsideAnother(VisualElement element)
        {
            for (var ancestor = element.hierarchy.parent; ancestor != null; ancestor = ancestor.hierarchy.parent)
            {
                if (s_roots.TryGetValue(ancestor, out _)) return true;
            }

            return false;
        }
    }
}
