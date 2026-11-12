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
    // a family an element between the two names reaches what is under it.
    //
    // The preflight's rule loses to an author's, so nothing is written on one of them that a rule of a stylesheet other
    // than a default one, or an inline font, gives a font, on itself or on an element above it: that font is inherited
    // instead. The rules are asked of the engine's matcher through BorderPreflight.AuthorRuleSets, and where the
    // matcher cannot be built only inline fonts are read. An inline font somebody else wrote on one of them is theirs
    // and stays; the value written here is taken back only while it is still the one written here.
    //
    // What decides all of this is read again at each hold, each release and each change to the registry, and, while a
    // default family resolves, at each update of the element's panel after the stamp of its chain moves.
    internal static class DefaultFamilyRoots
    {
        private sealed class Root
        {
            // Weak, so a tree nobody disposed does not keep the element it was mounted on alive through this.
            public readonly WeakReference<VisualElement> Element;
            public int Holds;

            // What the last write here put on the element, or null for a write taking it back.
            public FontAsset? Written;

            // Scheduled only while a default family resolves and the element is held. Weak for the reason Element is,
            // since the item holds the element.
            public WeakReference<IVisualElementScheduledItem>? Follow;
            public int Stamp;
            public readonly Action Tick;

            public Root(VisualElement element)
            {
                Element = new WeakReference<VisualElement>(element);
                Tick = () =>
                {
                    if (!Element.TryGetTarget(out var held)) return;

                    if (ChainStamp(held) != Stamp)
                    {
                        Decide(held, this, VelvetFonts.Resolve(null, VelvetFontWeight.Normal, italic: false).Asset);
                    }
                };
            }
        }

        private static readonly ConditionalWeakTable<VisualElement, Root> s_roots = new();
        private static readonly List<Root> s_held = new();
        private static readonly Predicate<Root> s_collected = static root => !root.Element.TryGetTarget(out _);

        private static readonly string[] s_fontProperties = { "-unity-font-definition", "-unity-font" };

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
                    Decide(_element, _root, null);
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
                    Decide(element, root, asset);
                }
            }
        }

        private static void Decide(VisualElement element, Root root, FontAsset? asset)
        {
            Write(element, root, asset == null || InsideAnother(element) || AuthorFont(element) ? null : asset);
            if (asset == null)
            {
                if (root.Follow != null)
                {
                    if (root.Follow.TryGetTarget(out var item)) item.Pause();
                    root.Follow = null;
                }
                return;
            }

            // MUTANT_SURVIVES(equivalent, line removed): with no stamp kept, each update decides again and writes what
            // the last decision wrote; keeping it is what lets an update that moved nothing allocate nothing.
            root.Stamp = ChainStamp(element);
            root.Follow ??=
                new WeakReference<IVisualElementScheduledItem>(element.schedule.Execute(root.Tick).Every(0));
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

        // Whether an author gives element, or an element above it, a font: an inline one on an ancestor (the element's
        // own is Write's to keep), or one a rule of a stylesheet other than a default one sets.
        private static bool AuthorFont(VisualElement element)
        {
            for (var link = element; link != null; link = link.hierarchy.parent)
            {
                if (link != element && InlineFont(link)) return true;
                if (BorderPreflight.AuthorRuleSets(link, s_fontProperties) == true) return true;
            }

            return false;
        }

        private static bool InlineFont(VisualElement element)
            => element.style.unityFontDefinition.keyword != StyleKeyword.Null
               || element.style.unityFont.keyword != StyleKeyword.Null;

        // What the decision reads off the chain from element up: each element, its name, classes and stylesheets, and
        // its inline font. Read on every update of the panel, so nothing here allocates once each element of the chain
        // and each of its stylesheets has been read before.
        private static int ChainStamp(VisualElement element)
        {
            var stamp = 0;
            for (var link = element; link != null; link = link.hierarchy.parent)
            {
                var style = link.style;
                var definition = style.unityFontDefinition;
                stamp = HashCode.Combine(stamp, link, link.name, (int)definition.keyword, definition.value.fontAsset,
                    definition.value.font, (int)style.unityFont.keyword, style.unityFont.value);
                stamp = VelvetStyleUtilities.ClassStamp(stamp, link);
                for (var i = 0; i < link.styleSheets.count; i++)
                {
                    stamp = VelvetStyleUtilities.SheetStamp(stamp, link.styleSheets[i]);
                }
            }

            return stamp;
        }
    }
}
