#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The scale, rotate and transform origin a layoutId Motion's classes give it after a patch has changed its class list
    // and before a style pass has matched its rules again, read off the class list: StyleCascade and resolvedStyle give
    // what the last style pass matched, and a pass run inside the render writes before the next one
    // (Given_AHolderScaledByAStylesheetClass_When_ItTakesTheLeadBackInTheRenderThatRescalesIt_Then_ItsMoveIsDrawnAtTheNewScale).
    // A property is read only where the bundled sheet is attached above the element and every rule that could give the
    // property to the element is a bundled single-class utility whose value is read here, so that the reading is what the
    // style pass will match. Null for any other property, which keeps what the style pass gives.
    internal static class BundledClassTransform
    {
        private const int ScaleBit = 1, RotateBit = 2, OriginBit = 4;

        private struct Declared
        {
            // The properties read here: a bundled class declares each with a value read here, or none could give it.
            public int Read;
            public Vector3 Scale;
            public float Rotate;
            // No class declares a rotate, which leaves it none rather than at zero degrees.
            public bool RotateNone;
            public Vector2 Origin;

            public bool Same(Declared other) => Read == other.Read && Scale == other.Scale && Rotate == other.Rotate
                && RotateNone == other.RotateNone && Origin == other.Origin;
        }

        private sealed class State
        {
            public int Signature;
            public Declared Seen;
            // Set from a patch that changed the class list until the computed style's rules hash moves.
            public bool Pending;
            public long? StaleHash;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, State> s_states = new();

        // Called as a patch of a layoutId Motion has written its class list, its first one included. Returns whether the
        // scale, rotate or origin read off the classes changed.
        public static bool Observe(VisualElement element)
        {
            var signature = Signature(element);
            if (!s_states.TryGetValue(element, out var state))
            {
                s_states.Add(element, new State { Signature = signature, Seen = Read(element) });
                return false;
            }
            if (signature == state.Signature) return false;
            var seen = Read(element);
            var changed = !seen.Same(state.Seen);
            if (!IsStale(element, state)) (state.Pending, state.StaleHash) = (true, StyleCascade.RulesHash(element));
            (state.Signature, state.Seen) = (signature, seen);
            return changed;
        }

        public static void Forget(VisualElement element) => s_states.Remove(element);

        public static Vector3? DeclaredScale(VisualElement element) =>
            TryPending(element, ScaleBit, out var seen) ? seen.Scale : (Vector3?)null;

        public static Rotate? DeclaredRotate(VisualElement element) =>
            !TryPending(element, RotateBit, out var seen) ? (Rotate?)null
            : seen.RotateNone ? Rotate.None() : new Rotate(new Angle(seen.Rotate, AngleUnit.Degree));

        // As fractions of the element's size.
        public static Vector2? DeclaredOrigin(VisualElement element) =>
            TryPending(element, OriginBit, out var seen) ? seen.Origin : (Vector2?)null;

        private static bool TryPending(VisualElement element, int bit, out Declared seen)
        {
            seen = default;
            if (!s_states.TryGetValue(element, out var state) || !IsStale(element, state) || (state.Seen.Read & bit) == 0) return false;
            seen = state.Seen;
            return true;
        }

        private static bool IsStale(VisualElement element, State state)
        {
            if (!state.Pending) return false;
            if (StyleCascade.RulesHash(element) == state.StaleHash) return true;
            state.Pending = false;
            return false;
        }

        // Order-free, as the cascade is; the name too, which an id selector reads.
        private static int Signature(VisualElement element)
        {
            var (signature, count) = (element.name?.GetHashCode() ?? 0, 0);
            foreach (var cls in element.GetClasses()) (signature, count) = (signature + cls.GetHashCode(), count + 1);
            return signature * 31 + count;
        }

        private static Declared Read(VisualElement element)
        {
            var declared = new Declared { Scale = Vector3.one, Origin = new Vector2(0.5f, 0.5f) };
            var unread = SheetRules.Unread(element);
            int scaleAt = -1, rotateAt = -1, originAt = -1;
            // Of two bundled classes on the list declaring a property, the one later in the cascade holds it.
            foreach (var cls in element.GetClasses())
            {
                if (!StyleUtilityProperties.TryGet(cls, out var rule) || rule.Gate != StyleUtilityGate.None) continue;
                var at = rule.CascadePosition;
                if (rule.Properties.Contains(StyleLonghand.Scale) && at > scaleAt)
                {
                    scaleAt = at;
                    if (TryScaleOf(cls, out var scale)) declared.Scale = new Vector3(scale, scale, 1f);
                    else unread |= ScaleBit;
                }
                if (rule.Properties.Contains(StyleLonghand.Rotate) && at > rotateAt)
                {
                    rotateAt = at;
                    if (!TryRotateOf(cls, out declared.Rotate)) unread |= RotateBit;
                }
                if (rule.Properties.Contains(StyleLonghand.TransformOrigin) && at > originAt)
                {
                    originAt = at;
                    if (!TryOriginOf(cls, out declared.Origin)) unread |= OriginBit;
                }
            }
            declared.RotateNone = rotateAt < 0;
            declared.Read = (ScaleBit | RotateBit | OriginBit) & ~unread;
            return declared;
        }

        // The uniform scale-N, through the table the per-axis presets mirror the sheet's scale classes by.
        internal static bool TryScaleOf(string cls, out float scale)
        {
            scale = 1f;
            return cls.StartsWith("scale-", System.StringComparison.Ordinal)
                && StyleArbitraryValueResolver.TryGetAxisScale(cls.Substring("scale-".Length), out scale);
        }

        // rotate-nN is the negative one.
        internal static bool TryRotateOf(string cls, out float degrees)
        {
            degrees = 0f;
            if (!cls.StartsWith("rotate-", System.StringComparison.Ordinal)) return false;
            var suffix = cls.Substring("rotate-".Length);
            var negated = suffix.StartsWith("n", System.StringComparison.Ordinal);
            if (!StyleArbitraryValueResolver.TryGetRotateScale(negated ? suffix.Substring(1) : suffix, out degrees)) return false;
            if (negated) degrees = -degrees;
            return true;
        }

        // origin-<side>[-<side>], each side a keyword of one axis and center the middle of either; an axis no keyword
        // names is centred, as `origin-top` is `center top`.
        internal static bool TryOriginOf(string cls, out Vector2 origin)
        {
            origin = new Vector2(0.5f, 0.5f);
            if (!cls.StartsWith("origin-", System.StringComparison.Ordinal)) return false;
            foreach (var side in cls.Substring("origin-".Length).Split('-'))
            {
                switch (side)
                {
                    case "left": origin.x = 0f; break;
                    case "right": origin.x = 1f; break;
                    case "top": origin.y = 0f; break;
                    case "bottom": origin.y = 1f; break;
                    case "center": break;
                    default: return false;
                }
            }
            return true;
        }

        // The rules of the sheets attached at or above an element that declare a scale, a rotate or a transform origin.
        private static class SheetRules
        {
            private static readonly System.Reflection.PropertyInfo? s_rules = EngineMember.StyleSheetRules.ResolveProperty();
            private static readonly System.Reflection.PropertyInfo? s_properties = EngineMember.StyleRuleProperties.ResolveProperty();
            private static readonly System.Reflection.PropertyInfo? s_complex = EngineMember.StyleRuleComplexSelectors.ResolveProperty();
            private static readonly System.Reflection.PropertyInfo? s_name = EngineMember.StylePropertyName.ResolveProperty();
            private static readonly System.Reflection.PropertyInfo? s_selectors = EngineMember.StyleComplexSelectorSelectors.ResolveProperty();
            private static readonly System.Reflection.PropertyInfo? s_parts = EngineMember.StyleSelectorParts.ResolveProperty();
            private static readonly System.Reflection.PropertyInfo? s_value = EngineMember.StyleSelectorPartValue.ResolveProperty();
            private static readonly System.Reflection.PropertyInfo? s_type = EngineMember.StyleSelectorPartType.ResolveProperty();

            // One selector of a rule declaring the properties in Bits: the classes, names and types its last compound
            // selector requires of the element it matches. Its ancestors, pseudo-classes and predicates are taken to hold.
            private sealed class Entry
            {
                public int Bits;
                // A bundled rule whose selector is one class alone, which StyleUtilityProperties ranks.
                public bool Utility;
                public readonly System.Collections.Generic.List<string> Classes = new();
                public readonly System.Collections.Generic.List<string> Names = new();
                public readonly System.Collections.Generic.List<string> Types = new();

                public bool CouldMatch(VisualElement element)
                {
                    foreach (var cls in Classes) if (!element.ClassListContains(cls)) return false;
                    foreach (var name in Names) if (name != element.name) return false;
                    foreach (var type in Types) if (type != element.GetType().Name) return false;
                    return true;
                }
            }

            private sealed class Scan
            {
                public int ContentHash;
                // Whether the sheet was reached through the bundled sheet's imports, the key the scan is cached under.
                public bool ReachedBundled;
                public bool Bundled;
                // Null where the sheet's rules could not be read.
                public System.Collections.Generic.List<Entry>? Entries;
            }

            private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StyleSheet, Scan> s_scans = new();

            // The properties of the three that a rule other than a bundled utility could give the element, all three where
            // the bundled sheet is not attached at or above it or a sheet cannot be read.
            public static int Unread(VisualElement element)
            {
                var unread = 0;
                var attached = false;
                for (var e = element; e != null; e = e.hierarchy.parent)
                {
                    for (var i = 0; i < e.styleSheets.count; i++)
                    {
                        unread |= Unread(element, e.styleSheets[i], false, ref attached);
                    }
                }
                return attached ? unread : ScaleBit | RotateBit | OriginBit;
            }

            private static int Unread(VisualElement element, StyleSheet sheet, bool bundled, ref bool attached)
            {
                var scan = ScanOf(sheet, bundled);
                attached |= scan.Bundled;
                if (scan.Entries == null) return ScaleBit | RotateBit | OriginBit;
                var unread = 0;
                foreach (var entry in scan.Entries)
                {
                    if (!entry.Utility && entry.CouldMatch(element)) unread |= entry.Bits;
                }
                foreach (var imported in VelvetStyleUtilities.ImportsOf(sheet))
                {
                    unread |= Unread(element, imported, scan.Bundled, ref attached);
                }
                return unread;
            }

            private static Scan ScanOf(StyleSheet sheet, bool bundled)
            {
                if (s_scans.TryGetValue(sheet, out var scan) && scan.ContentHash == sheet.contentHash && scan.ReachedBundled == bundled)
                {
                    return scan;
                }
                s_scans.Remove(sheet);
                scan = new Scan
                {
                    ContentHash = sheet.contentHash, ReachedBundled = bundled, Bundled = bundled || VelvetStyleUtilities.IsUtilities(sheet),
                };
                scan.Entries = ReadEntries(sheet, scan.Bundled);
                s_scans.Add(sheet, scan);
                return scan;
            }

            private static System.Collections.Generic.List<Entry>? ReadEntries(StyleSheet sheet, bool bundled)
            {
                if (s_rules == null || s_properties == null || s_complex == null || s_name == null || s_selectors == null
                    || s_parts == null || s_value == null || s_type == null)
                {
                    return null;
                }
                var entries = new System.Collections.Generic.List<Entry>();
                foreach (var rule in (System.Array?)s_rules.GetValue(sheet) ?? System.Array.Empty<object>())
                {
                    var bits = Bits((System.Array?)s_properties.GetValue(rule));
                    if (bits == 0) continue;
                    foreach (var complex in (System.Array?)s_complex.GetValue(rule) ?? System.Array.Empty<object>())
                    {
                        entries.Add(EntryOf((System.Array?)s_selectors.GetValue(complex), bits, bundled));
                    }
                }
                return entries;
            }

            private static int Bits(System.Array? properties)
            {
                var bits = 0;
                foreach (var property in properties ?? System.Array.Empty<object>())
                {
                    bits |= (s_name!.GetValue(property) as string) switch
                    {
                        "scale" => ScaleBit,
                        "rotate" => RotateBit,
                        "transform-origin" => OriginBit,
                        _ => 0,
                    };
                }
                return bits;
            }

            private static Entry EntryOf(System.Array? selectors, int bits, bool bundled)
            {
                var entry = new Entry { Bits = bits };
                if (selectors == null || selectors.Length == 0) return entry;
                var parts = (System.Array?)s_parts!.GetValue(selectors.GetValue(selectors.Length - 1)) ?? System.Array.Empty<object>();
                foreach (var part in parts)
                {
                    var value = s_value!.GetValue(part) as string ?? "";
                    switch (s_type!.GetValue(part)?.ToString())
                    {
                        case "Class": entry.Classes.Add(value); break;
                        case "ID": entry.Names.Add(value); break;
                        case "Type": entry.Types.Add(value); break;
                    }
                }
                entry.Utility = bundled && selectors.Length == 1 && parts.Length == 1 && entry.Classes.Count == 1
                    && StyleUtilityProperties.TryGet(entry.Classes[0], out var utility) && utility.Gate == StyleUtilityGate.None;
                return entry;
            }
        }
    }
}
