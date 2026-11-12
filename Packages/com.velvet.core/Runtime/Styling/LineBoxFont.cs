#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Velvet
{
    // A copy of a font asset whose ascent and descent are spread evenly about the font's own until they are one line
    // height apart, for text whose line height cannot be written as a <line-height> tag: a multiline field's input
    // text and a label or button with rich text off, none of which parses tags. Both text generators then lay each line
    // out one line height tall and one line height below the last (LineBoxFontTests pins both). The faces its weight
    // table and its fallback table name are copied the same way: under the standard generator a line drawn from an
    // uncopied fallback keeps that face's height (LineBoxFontTests' fallback case).
    //
    // A copy is an instance of its source, so it keeps the source's scale, sampling size, render mode, material settings
    // and style weights. One whose source can add glyphs gets an atlas and a material of its own: one atlas filled by two
    // assets would be packed twice over. One whose source cannot shares the source's atlas.
    //
    // A copy is held by the elements written in it and by the copies naming it. One nothing holds is spread to the next
    // line height asked of its source rather than destroyed and made again, so a source has no more copies than were
    // held at once. One whose source is gone is destroyed the next time any copy is asked for or given back.
    internal static class LineBoxFont
    {
        private sealed class Entry
        {
            public FontAsset Source = null!;
            public int Key;
            // A line height and font size the key was taken at, which a fallback table the source grows is copied at.
            public float LineHeight;
            public float FontSize;
            public FontAsset? Copy;
            public bool SharesAtlas;
            public readonly List<WeakReference<object>> Users = new();
            public int Parents;
            public readonly List<Entry> Children = new();
            // The source's fallback table as the copy's was last built from, which a font stack grows and releases.
            public FontAsset[] Fallbacks = Array.Empty<FontAsset>();
        }

        private static readonly Dictionary<(FontAsset Source, int Key), Entry> s_entries = new();
        private static readonly Dictionary<FontAsset, Entry> s_byCopy = new();
        private static readonly LinkedList<Entry> s_idle = new();

        // Keyed in 1/256 font units, a step under the line height, since a height a hair over a device pixel is laid
        // out a whole device pixel taller (LineBoxFontTests' Standard cases read 24.5 for a 24px line otherwise).
        private const float UnitsPerKey = 256f;

        private static int s_made;

        private static readonly FieldInfo? s_sourcePath = EngineMember.FontAssetSourceFontFilePath.ResolveField();
        private static readonly MethodInfo? s_uploadQueued = EngineMember.FontAssetUpdateAtlasTexturesInQueue.ResolveMethod();
        private static readonly FieldInfo? s_nativeAsset = EngineMember.FontAssetNativeFontAsset.ResolveField();
        private static readonly MethodInfo? s_updateFallbacks = EngineMember.FontAssetUpdateFallbacks.ResolveMethod();
        private static readonly MethodInfo? s_updateWeightFallbacks = EngineMember.FontAssetUpdateWeightFallbacks.ResolveMethod();

        static LineBoxFont()
        {
            VelvetFontStack.Grew += Refallback;
            VelvetFontStack.Shrank += Refallback;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += DestroyAll;
#endif
        }

        // source with its lines lineHeight pixels apart at fontSize, held by user until Release, or null where no copy
        // can be made.
        public static FontAsset? Acquire(object user, FontAsset source, float lineHeight, float fontSize)
        {
            SweepGone();
            var entry = EntryFor(source, lineHeight, fontSize);
            if (entry?.Copy == null)
            {
                return null;
            }
            foreach (var held in entry.Users)
            {
                if (held.TryGetTarget(out var target) && ReferenceEquals(target, user))
                {
                    return entry.Copy;
                }
            }
            entry.Users.Add(new WeakReference<object>(user));
            s_idle.Remove(entry);
            return entry.Copy;
        }

        public static void Release(object user, FontAsset copy)
        {
            if (!s_byCopy.TryGetValue(copy, out var entry))
            {
                return;
            }
            entry.Users.RemoveAll(held => !held.TryGetTarget(out var target) || ReferenceEquals(target, user));
            Idle(entry);
            SweepGone();
        }

        // The asset copy was made of, or null for an asset that is not a copy.
        public static FontAsset? SourceOf(FontAsset? copy)
        {
            Entry? entry = null;
            if (copy != null)
            {
                s_byCopy.TryGetValue(copy, out entry);
            }
            return entry?.Source;
        }

        // The number of copies that exist, held or kept.
        internal static int Count => s_byCopy.Count;

        private static Entry? EntryFor(FontAsset source, float lineHeight, float fontSize)
        {
            var face = source.faceInfo;
            if (!(fontSize > 0f) || !(lineHeight >= 0f) || !(face.pointSize > 0f) || !(face.scale > 0f))
            {
                return null;
            }
            var units = lineHeight / (fontSize / face.pointSize * face.scale);
            var key = (source, Mathf.FloorToInt(units * UnitsPerKey) - 1);
            if (s_entries.TryGetValue(key, out var entry))
            {
                if (entry.Copy != null && !SameFallbacks(entry))
                {
                    Link(entry, lineHeight, fontSize);
                }
                return entry;
            }
            // Entered before its faces are copied, so a table that names it again finds it and stops.
            entry = TakeIdle(source);
            if (entry != null)
            {
                s_entries.Remove((source, entry.Key));
                entry.Key = key.Item2;
                entry.LineHeight = lineHeight;
                entry.FontSize = fontSize;
                s_entries[key] = entry;
                Spread(entry.Copy!, source, key.Item2 / UnitsPerKey);
                Link(entry, lineHeight, fontSize);
                return entry;
            }
            entry = new Entry { Source = source, Key = key.Item2, LineHeight = lineHeight, FontSize = fontSize };
            s_entries[key] = entry;
            entry.Copy = Create(source, out entry.SharesAtlas);
            if (entry.Copy == null)
            {
                return entry;
            }
            Spread(entry.Copy, source, key.Item2 / UnitsPerKey);
            s_byCopy[entry.Copy] = entry;
            Link(entry, lineHeight, fontSize);
            return entry;
        }

        // A copy of source nothing holds, taken to be spread to another line height rather than another made.
        private static Entry? TakeIdle(FontAsset source)
        {
            for (var node = s_idle.First; node != null; node = node.Next)
            {
                if (node.Value.Source == source && node.Value.Copy != null)
                {
                    s_idle.Remove(node);
                    return node.Value;
                }
            }
            return null;
        }

        private static FontAsset? Create(FontAsset source, out bool sharesAtlas)
        {
            sharesAtlas = !AddsGlyphs(source);
            var copy = UnityEngine.Object.Instantiate(source);
            copy.hideFlags = HideFlags.DontSave;
            if (!sharesAtlas)
            {
                var format = (source.atlasRenderMode & (UnityEngine.TextCore.LowLevel.GlyphRenderMode)0x10000) != 0
                    ? TextureFormat.RGBA32
                    : TextureFormat.Alpha8;
                var atlas = new Texture2D(source.atlasWidth, source.atlasHeight, format, false) { hideFlags = HideFlags.DontSave };
                var material = new Material(source.material) { hideFlags = HideFlags.DontSave };
                material.mainTexture = atlas;
                copy.atlasTextures = new[] { atlas };
                copy.material = material;
                material.name = "Velvet line box material #" + ++s_made;
                copy.materialHashCode = material.name.GetHashCode();
                copy.ClearFontAssetData();
            }
            return copy;
        }

        // copy given the source's ascent and descent spread evenly until they are units apart.
        private static void Spread(FontAsset copy, FontAsset source, float units)
        {
            // Named apart from its source and from what it was named before, since the text system finds an asset by a
            // hash of its name (TextAsset.hashCode), which an instance carries over from its source.
            copy.name = source.name + " line box " + units.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " #" + ++s_made;
            copy.hashCode = copy.name.GetHashCode();
            var face = source.faceInfo;
            var spread = (units - (face.ascentLine - face.descentLine)) * 0.5f;
            face.ascentLine += spread;
            face.descentLine -= spread;
            face.lineHeight = face.ascentLine - face.descentLine;
            copy.faceInfo = face;
        }

        // Whether source rasterises glyphs it does not hold yet: from a font file, a font file's path, or the OS.
        private static bool AddsGlyphs(FontAsset source) => source.atlasPopulationMode switch
        {
            AtlasPopulationMode.DynamicOS => true,
            AtlasPopulationMode.Dynamic => source.sourceFontFile != null
                || !string.IsNullOrEmpty(s_sourcePath?.GetValue(source) as string),
            _ => false,
        };

        // The copy's weight and fallback tables written as copies of the source's, at the same line height.
        private static void Link(Entry entry, float lineHeight, float fontSize)
        {
            var copy = entry.Copy!;
            var previous = entry.Children.ToArray();
            entry.Children.Clear();
            // Taken before any face is copied, so a face whose tables name this source back finds its copy current and
            // takes it as it stands rather than copying its tables again.
            var fallbacks = entry.Source.fallbackFontAssetTable;
            entry.Fallbacks = fallbacks != null ? fallbacks.ToArray() : Array.Empty<FontAsset>();
            var weights = entry.Source.fontWeightTable;
            var copied = copy.fontWeightTable;
            for (var i = 0; weights != null && copied != null && i < weights.Length && i < copied.Length; i++)
            {
                copied[i].regularTypeface = Face(entry, weights[i].regularTypeface, lineHeight, fontSize);
                copied[i].italicTypeface = Face(entry, weights[i].italicTypeface, lineHeight, fontSize);
            }
            var table = new List<FontAsset>(entry.Fallbacks.Length);
            foreach (var fallback in entry.Fallbacks)
            {
                if (Face(entry, fallback, lineHeight, fontSize) is { } face)
                {
                    table.Add(face);
                }
            }
            copy.fallbackFontAssetTable = table;
            copy.ReadFontAssetDefinition();
            // The advanced generator reads the tables off the copy's native asset, which takes them in when it is
            // created and not when they are written; one created already is handed them here, before a face it named
            // can be released below.
            var native = s_nativeAsset?.GetValue(copy) as IntPtr?;
            if (native.HasValue && native.Value != IntPtr.Zero)
            {
                s_updateFallbacks?.Invoke(copy, null);
                s_updateWeightFallbacks?.Invoke(copy, null);
            }
            foreach (var child in previous)
            {
                child.Parents--;
                Idle(child);
            }
        }

        // face in the line height, held by entry: the copy itself for the source, and face itself where no copy can be
        // made or where face is being copied further up.
        private static FontAsset? Face(Entry entry, FontAsset? face, float lineHeight, float fontSize)
        {
            if (face == null)
            {
                return null;
            }
            if (face == entry.Source)
            {
                return entry.Copy;
            }
            var child = EntryFor(face, lineHeight, fontSize);
            if (child?.Copy == null)
            {
                return face;
            }
            child.Parents++;
            s_idle.Remove(child);
            entry.Children.Add(child);
            return child.Copy;
        }

        private static bool SameFallbacks(Entry entry)
        {
            var table = entry.Source.fallbackFontAssetTable;
            var same = (table?.Count ?? 0) == entry.Fallbacks.Length;
            for (var i = 0; same && i < entry.Fallbacks.Length; i++)
            {
                same = table![i] == entry.Fallbacks[i];
            }
            return same;
        }

        // A font stack grew or released source's fallback table, which each copy of it takes in now, before the text
        // drawn in one is generated again.
        private static void Refallback(FontAsset source)
        {
            foreach (var entry in new List<Entry>(s_entries.Values))
            {
                if (entry.Source == source && entry.Copy != null && !SameFallbacks(entry))
                {
                    Link(entry, entry.LineHeight, entry.FontSize);
                }
            }
        }

        private static bool Held(Entry entry)
        {
            entry.Users.RemoveAll(held => !held.TryGetTarget(out _));
            return entry.Users.Count > 0 || entry.Parents > 0;
        }

        private static void Idle(Entry entry)
        {
            if (entry.Copy == null || Held(entry) || s_idle.Contains(entry))
            {
                return;
            }
            s_idle.AddLast(entry);
        }

        // Destroys each copy nothing holds whose source is gone, which is not asked for again.
        private static void SweepGone()
        {
            for (var node = s_idle.First; node != null;)
            {
                var next = node.Next;
                if (node.Value.Source == null && !Held(node.Value))
                {
                    s_idle.Remove(node);
                    Destroy(node.Value);
                }
                node = next;
            }
        }

        private static void Destroy(Entry entry)
        {
            s_entries.Remove((entry.Source, entry.Key));
            var copy = entry.Copy;
            entry.Copy = null;
            if (copy != null)
            {
                s_byCopy.Remove(copy);
                if (entry.SharesAtlas)
                {
                    // The atlas and material are the source's, which destroying the copy would destroy with it.
                    copy.atlasTextures = Array.Empty<Texture2D>();
                    copy.material = null;
                }
                else
                {
                    // Atlases the text system has queued for upload, which may list this copy's.
                    s_uploadQueued?.Invoke(null, null);
                }
                UnityEngine.Object.DestroyImmediate(copy);
            }
            foreach (var child in entry.Children)
            {
                child.Parents--;
                Idle(child);
            }
            entry.Children.Clear();
        }

        private static void DestroyAll()
        {
            foreach (var entry in new List<Entry>(s_entries.Values))
            {
                entry.Children.Clear();
                Destroy(entry);
            }
            s_entries.Clear();
            s_byCopy.Clear();
            s_idle.Clear();
        }
    }

    // An element's inline font written as LineBoxFont's copy of the font it would otherwise take, and given back. The
    // font it would otherwise take is its inline font definition where somebody else wrote one, and else the one its
    // rules, its parent and an inline legacy Font give it beneath that (StyleCascade.UnderlyingFontAsset).
    internal sealed class LineBoxFontOwner
    {
        private FontAsset? _written;
        private StyleFontDefinition _underlying;

        // The owner that last wrote a copy on each element, which CopiesInherited asks whether the copy still shows.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, LineBoxFontOwner>
            s_shown = new();

        // Whether element shows a copy of the font its rules and parent give it, with no inline value of anybody's
        // beneath the copy, which a writer of an inline font may then write over as it would over none
        // (DefaultFamilyRoots).
        internal static bool CopiesInherited(VisualElement element)
        {
            if (!s_shown.TryGetValue(element, out var owner)) return false;

            return owner._written != null && owner._underlying.keyword != StyleKeyword.Undefined
                && element.style.unityFontDefinition.value.fontAsset == owner._written;
        }

        public void Settle(VisualElement element, float lineHeight)
        {
            var inline = element.style.unityFontDefinition;
            if (_written == null || inline.value.fontAsset != _written)
            {
                Drop();
                _underlying = inline;
            }
            FontAsset? source;
            if (_underlying.keyword == StyleKeyword.Undefined)
            {
                source = _underlying.value.fontAsset != null
                    ? _underlying.value.fontAsset
                    : StyleCascade.CachedFontAsset(element, _underlying.value.font);
            }
            else
            {
                var legacy = element.style.unityFont;
                source = StyleCascade.UnderlyingFontAsset(element,
                    legacy.keyword == StyleKeyword.Undefined ? legacy.value : null);
            }
            var variant = source != null && !float.IsNaN(lineHeight)
                ? LineBoxFont.Acquire(this, source, lineHeight, element.resolvedStyle.fontSize)
                : null;
            if (variant == null)
            {
                Release(element);
                return;
            }
            if (variant == _written)
            {
                return;
            }
            Drop();
            _written = variant;
            element.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromSDFFont(variant));
            s_shown.AddOrUpdate(element, this);
        }

        public void Release(VisualElement element)
        {
            if (_written == null)
            {
                return;
            }
            if (element.style.unityFontDefinition.value.fontAsset == _written)
            {
                element.style.unityFontDefinition = _underlying;
            }
            Drop();
            _underlying = default;
        }

        private void Drop()
        {
            if (_written != null)
            {
                LineBoxFont.Release(this, _written);
            }
            _written = null;
        }
    }
}
