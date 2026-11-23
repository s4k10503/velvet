#nullable enable
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Velvet
{
    // What Velvet last wrote as an element's inline font definition, so that an inline font that is not that write is the
    // author's, and the font an author had there, which the write replaced, can be given back. Every Velvet writer of the
    // inline definition records here (StyleFontResolver, DefaultFamilyRoots); a copy LineBoxFontOwner makes of a font is
    // read back to the font it copies (LineBoxFont.SourceOf). An element's entry is dropped when it is reset for reuse.
    internal static class InlineFontLedger
    {
        private sealed class Entry
        {
            public FontAsset? Written;
            public UnityEngine.Object? Replaced;
        }

        private static readonly ConditionalWeakTable<VisualElement, Entry> s_entries = new();

        // A write of written (null for one taking the font off) that replaced the author's font replaced, if any.
        internal static void Wrote(VisualElement element, FontAsset? written, UnityEngine.Object? replaced)
        {
            var entry = s_entries.GetValue(element, static _ => new Entry());
            entry.Written = written;
            entry.Replaced = replaced;
        }

        // A write that leaves what an earlier one replaced as it was.
        internal static void Wrote(VisualElement element, FontAsset? written)
        {
            s_entries.GetValue(element, static _ => new Entry()).Written = written;
        }

        internal static void Forget(VisualElement element) => s_entries.Remove(element);

        // The asset of Velvet's last write, while the inline definition still shows it; null otherwise.
        internal static FontAsset? Written(VisualElement element)
        {
            if (!s_entries.TryGetValue(element, out var entry) || entry.Written == null)
            {
                return null;
            }

            var shown = element.style.unityFontDefinition.value.fontAsset;
            if (shown == null)
            {
                return null;
            }

            return shown == entry.Written || LineBoxFont.SourceOf(shown) == entry.Written ? entry.Written : null;
        }

        // The font the author gave element as an inline definition: what is there unless Velvet wrote it, in which case the
        // font that write replaced. A Font Asset is read back through a line-box copy to the font it copies.
        internal static UnityEngine.Object? AuthorDefinition(VisualElement element)
        {
            var definition = element.style.unityFontDefinition;
            if (definition.keyword == StyleKeyword.Null)
            {
                return null;
            }

            var value = definition.value;
            if (value.fontAsset == null)
            {
                return value.font;
            }

            if (Written(element) != null)
            {
                return s_entries.TryGetValue(element, out var entry) ? entry.Replaced : null;
            }

            if (LineBoxFontOwner.CopiesInherited(element))
            {
                return null;
            }

            return LineBoxFont.SourceOf(value.fontAsset) ?? value.fontAsset;
        }

        // AuthorDefinition, else the legacy font the author gave element inline, which Velvet never writes.
        internal static UnityEngine.Object? AuthorFont(VisualElement element)
        {
            var definition = AuthorDefinition(element);
            if (definition != null)
            {
                return definition;
            }

            var legacy = element.style.unityFont;
            return legacy.keyword != StyleKeyword.Null ? legacy.value : null;
        }

        internal static StyleFontDefinition DefinitionOf(UnityEngine.Object font)
            => font is FontAsset asset
                ? new StyleFontDefinition(FontDefinition.FromSDFFont(asset))
                : new StyleFontDefinition(FontDefinition.FromFont((UnityEngine.Font)font));

        // Takes Velvet's write off element: the author's font it replaced goes back, or the definition is cleared where
        // there was none. An inline definition that is not Velvet's write is left as it is.
        internal static void TakeOff(VisualElement element)
        {
            if (Written(element) != null)
            {
                var replaced = s_entries.TryGetValue(element, out var entry) ? entry.Replaced : null;
                element.style.unityFontDefinition = replaced == null ? new StyleFontDefinition(StyleKeyword.Null) : DefinitionOf(replaced);
            }

            Forget(element);
        }
    }
}
