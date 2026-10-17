#nullable enable
using System;
using System.Collections.Generic;

namespace Velvet
{
    // Tailwind emits a shorthand ahead of the longhands it contains, so pt-2 beats p-13 whichever is written
    // first: padding, margin, inset and size/width/height sort that way in property-order.ts. Two utilities
    // of one family at one priority are therefore ordered here by that property order, in the place a border
    // side colour already is (StyleBorderSideColor.CompareCascade), instead of by which arrived last. The radius
    // families take part too, ranked by the order StyleRuleOrder gives their tokens.
    //
    // A stylesheet class cannot take part: an inline value outranks every class on the element it sits on, so
    // `p-13 pt-2` would paint 52px at the top with pt-2 a class. Normalize rewrites such a class into an inline
    // form (pt-2 becomes pt-[8px]) when an inline utility of an overlapping family stands beside it, so both
    // are inline layers and the order above decides.
    internal static class StyleSpacingPrecedence
    {
        private static readonly int[] s_rank = BuildRanks();

        private static int[] BuildRanks()
        {
            var ranks = new int[Enum.GetValues(typeof(ArbitraryProperty)).Length];
            Array.Fill(ranks, -1);
            Set(ranks, ArbitraryProperty.Padding, "padding");
            Set(ranks, ArbitraryProperty.PaddingX, "padding-inline");
            Set(ranks, ArbitraryProperty.PaddingY, "padding-block");
            Set(ranks, ArbitraryProperty.PaddingTop, "padding-top");
            Set(ranks, ArbitraryProperty.PaddingRight, "padding-right");
            Set(ranks, ArbitraryProperty.PaddingBottom, "padding-bottom");
            Set(ranks, ArbitraryProperty.PaddingLeft, "padding-left");
            Set(ranks, ArbitraryProperty.Margin, "margin");
            Set(ranks, ArbitraryProperty.MarginX, "margin-inline");
            Set(ranks, ArbitraryProperty.MarginY, "margin-block");
            Set(ranks, ArbitraryProperty.MarginTop, "margin-top");
            Set(ranks, ArbitraryProperty.MarginRight, "margin-right");
            Set(ranks, ArbitraryProperty.MarginBottom, "margin-bottom");
            Set(ranks, ArbitraryProperty.MarginLeft, "margin-left");
            Set(ranks, ArbitraryProperty.Inset, "inset");
            Set(ranks, ArbitraryProperty.InsetX, "inset-inline");
            Set(ranks, ArbitraryProperty.InsetY, "inset-block");
            Set(ranks, ArbitraryProperty.Top, "top");
            Set(ranks, ArbitraryProperty.Right, "right");
            Set(ranks, ArbitraryProperty.Bottom, "bottom");
            Set(ranks, ArbitraryProperty.Left, "left");
            Set(ranks, ArbitraryProperty.Height, "height");
            Set(ranks, ArbitraryProperty.Width, "width");
            // size-* declares width and height, and property-order.ts sorts height first, so it ahead of both w-*
            // and h-*: the first property it differs from w-* on is height, and from h-* the extra width.
            ranks[(int)ArbitraryProperty.Size] = ranks[(int)ArbitraryProperty.Height] - 1;
            SetRadiusRanks(ranks);
            return ranks;
        }

        // The radius properties ranked by where Tailwind emits their utilities, from border-radius's own index
        // up. The order is read off StyleRuleOrder through one token per property, not restated from
        // property-order.ts. A local, because a static field would still be null when s_rank is built.
        private static void SetRadiusRanks(int[] ranks)
        {
            var ordered = new (ArbitraryProperty Property, string Token)[]
            {
                (ArbitraryProperty.BorderRadius, "rounded-[1px]"),
                (ArbitraryProperty.BorderTopRadius, "rounded-t-[1px]"),
                (ArbitraryProperty.BorderRightRadius, "rounded-r-[1px]"),
                (ArbitraryProperty.BorderBottomRadius, "rounded-b-[1px]"),
                (ArbitraryProperty.BorderLeftRadius, "rounded-l-[1px]"),
                (ArbitraryProperty.BorderTopLeftRadius, "rounded-tl-[1px]"),
                (ArbitraryProperty.BorderTopRightRadius, "rounded-tr-[1px]"),
                (ArbitraryProperty.BorderBottomRightRadius, "rounded-br-[1px]"),
                (ArbitraryProperty.BorderBottomLeftRadius, "rounded-bl-[1px]"),
                (ArbitraryProperty.BorderStartRadius, "rounded-s-[1px]"),
                (ArbitraryProperty.BorderEndRadius, "rounded-e-[1px]"),
                (ArbitraryProperty.BorderStartStartRadius, "rounded-ss-[1px]"),
                (ArbitraryProperty.BorderStartEndRadius, "rounded-se-[1px]"),
                (ArbitraryProperty.BorderEndStartRadius, "rounded-es-[1px]"),
                (ArbitraryProperty.BorderEndEndRadius, "rounded-ee-[1px]"),
            };
            Array.Sort(ordered, (a, z) => StyleRuleOrder.CompareUtilities(a.Token, z.Token));
            var first = 2 * StyleRuleOrder.TailwindIndexOf("border-radius");
            for (var i = 0; i < ordered.Length; i++)
            {
                ranks[(int)ordered[i].Property] = first + i;
            }
        }

        // Twice the property's index in property-order.ts, which leaves the odd rank before it free for size.
        private static void Set(int[] ranks, ArbitraryProperty property, string tailwindProperty)
            => ranks[(int)property] = 2 * StyleRuleOrder.TailwindIndexOf(tailwindProperty);

        // The place of property's utility in Tailwind's order, or -1 for one outside these families.
        internal static int RankOf(ArbitraryProperty property) => s_rank[(int)property];

        // Two writers of one slot at one priority, ordered as Tailwind emits them; the border colour families
        // where either is one, and 0 where either is neither, which leaves them to arrival.
        internal static int CompareCascade(ArbitraryProperty a, ArbitraryProperty z)
        {
            var aRank = s_rank[(int)a];
            var zRank = s_rank[(int)z];
            // MUTANT_SURVIVES(equivalent, boundary): a property outside these families ranks -1 and every ranked one at least 4, inset's index in the Tailwind order being 2, so a bound of 0 separates the same pairs.
            return aRank < 0 || zRank < 0 ? StyleBorderSideColor.CompareCascade(a, z) : aRank.CompareTo(zRank);
        }

        // tokens without a composite family the browser discards (StyleCompositeRejection), as NormalizeSpacing leaves
        // them, with the tokens of each family a C# resolver folds from the whole list put in Tailwind's order
        // (StyleFamilyOrder); tokens itself when nothing moves. What was written is remembered against the array it
        // returns, the one a caller holds for a pose to be read over (StyleCompositeRejection.Layered).
        internal static string[] Normalize(string[] tokens)
        {
            var composed = StyleCompositeRejection.WithoutRejectedComposites(tokens);
            var normalized = composed.Length < 2 ? composed : StyleFamilyOrder.Sorted(NormalizeSpacing(composed));
            StyleCompositeRejection.Remember(normalized, tokens, composed);
            return normalized;
        }

        // tokens without the inline token a later stylesheet class or inline token of its property writes in full, and
        // with a border width cut down to the sides such tokens leave (StyleInlineSheetOrder), and with each stylesheet-backed spacing class that an inline utility of an overlapping family stands
        // beside rewritten to its inline form (pt-2 as pt-[8px], mx-auto as mx-[auto], w-full as w-[100%]), and
        // each logical utility dropped for the physical one of its own side (ps-13 beside pl-2: Tailwind sorts
        // padding-inline-start ahead of padding-left, so the physical one holds the side whichever is written
        // first), of the physical classes on the side, the one the stylesheet cascade lets win is written inline when all
        // are stylesheet classes, so that it is not left to a rule the logical token's removal no longer has anything to outrank; tokens itself when neither applies. Tokens are grouped by importance and by the set of their
        // variants, so `sm:p-13 sm:pt-2` is judged apart from `p-13 pt-2`, and `hover:focus:` is `focus:hover:`. A class
        // written inline goes when another token of its slot is emitted after it (DropOutrankedRewrites).
        private static string[] NormalizeSpacing(string[] tokens)
        {
            // Before the readings, so a token this drops is not one a class is rewritten inline to outrank.
            tokens = StyleInlineSheetOrder.WithoutOutrankedInline(tokens);
            if (!Array.Exists(tokens, IsStylesheetClassOrLogical))
            {
                return tokens;
            }
            var readings = ReadAll(tokens);
            string[]? rewritten = null;
            List<int>? dropped = null;
            foreach (var reading in readings)
            {
                // Behind a variant the rule keys already order a logical utility ahead of the physical one of its side
                // (StyleRuleOrder), so only the base layer, whose keys tie, needs this.
                if (!reading.Logical || reading.Variants.Length != 0)
                {
                    continue;
                }
                var physical = readings.FindAll(other => !other.Logical && SameSlot(reading, other));
                if (physical.Count == 0)
                {
                    continue;
                }
                (dropped ??= new List<int>()).Add(reading.Index);
                // The physical class stays, and is written inline like the pair it would otherwise be one of: a
                // sheet rule is the one layer the logical token's removal would leave unable to hold the side. Of
                // several classes on the side only the one the stylesheet cascade lets win is written inline, so the
                // others still lose to it as they did before; an inline one on the side already outranks them all.
                if (!physical.Exists(match => !match.Backed))
                {
                    var winner = CascadeWinner(physical);
                    rewritten ??= (string[])tokens.Clone();
                    rewritten[winner.Index] = Inlined(winner);
                }
            }
            foreach (var candidate in readings)
            {
                if (candidate.Backed && IsNarrowerThanAnInlineNeighbour(candidate, readings, dropped))
                {
                    rewritten ??= (string[])tokens.Clone();
                    rewritten[candidate.Index] = Inlined(candidate);
                }
            }
            DropOutrankedRewrites(tokens, readings, rewritten, ref dropped);
            if (dropped == null)
            {
                return rewritten ?? tokens;
            }
            var source = rewritten ?? tokens;
            var kept = new List<string>(tokens.Length);
            for (var i = 0; i < source.Length; i++)
            {
                if (!dropped.Contains(i))
                {
                    kept.Add(source[i]);
                }
            }
            return kept.ToArray();
        }

        // Each class written inline above, dropped when another token of its slot is emitted after it. Two inline
        // tokens of one slot share a layer slot, so the one applied last would hold it. Tailwind's order decides, read
        // off the class as written (w-4) rather than off its inline form (w-[16px]): w-[10px] follows w-4 and
        // precedes w-[16px].
        private static void DropOutrankedRewrites(string[] tokens, List<Reading> readings, string[]? rewritten,
            ref List<int>? dropped)
        {
            if (rewritten == null)
            {
                return;
            }
            foreach (var reading in readings)
            {
                if (!ReferenceEquals(rewritten[reading.Index], tokens[reading.Index])
                    && IsOutrankedInSlot(tokens, readings, reading, dropped))
                {
                    (dropped ??= new List<int>()).Add(reading.Index);
                }
            }
        }

        private static bool IsOutrankedInSlot(string[] tokens, List<Reading> readings, Reading reading,
            List<int>? dropped)
            => readings.Exists(other => SameSlot(reading, other)
                && (dropped == null || !dropped.Contains(other.Index))
                && StyleRuleOrder.CompareUtilities(tokens[other.Index], tokens[reading.Index]) > 0);

        // baseClasses without the inline spacing tokens a pose of the same Motion overrides: a pose is Framer
        // Motion's animate, which overrides the node's own style, so a base pt-[8px] must not outrank a pose's
        // p-[52px] on the top edge as it would in one class list. A base token goes when the pose token's property
        // contains everything it writes, which includes the same property. A base radius a pose overlaps without
        // containing is cut down to the corners the pose leaves (rounded-l-[4px] under a rounded-t-[16px] pose
        // becomes rounded-bl-[4px]), since Tailwind may rank the base ahead of the pose on a shared corner;
        // baseClasses itself when no token changes.
        internal static string[] WithoutOverriddenBase(string[] baseClasses, string[] pose, string[]? morePose = null)
        {
            if (!MayHoldSpacing(pose) && (morePose == null || !MayHoldSpacing(morePose)) || !MayHoldSpacing(baseClasses))
            {
                return baseClasses;
            }
            var poseReadings = ReadAll(pose);
            if (morePose != null)
            {
                poseReadings.AddRange(ReadAll(morePose));
            }
            if (poseReadings.Count == 0)
            {
                return baseClasses;
            }
            var overridden = new HashSet<int>();
            var cut = new Dictionary<int, string[]>();
            foreach (var own in ReadAll(baseClasses))
            {
                if (own.Backed)
                {
                    continue;
                }
                if (poseReadings.Exists(posed => Overrides(posed, own)))
                {
                    overridden.Add(own.Index);
                    continue;
                }
                var left = CornersLeftBy(baseClasses[own.Index], own, poseReadings);
                if (left != null)
                {
                    cut[own.Index] = left;
                }
            }
            if (overridden.Count == 0 && cut.Count == 0)
            {
                return baseClasses;
            }
            var kept = new List<string>(baseClasses.Length);
            for (var i = 0; i < baseClasses.Length; i++)
            {
                if (overridden.Contains(i))
                {
                    continue;
                }
                if (cut.TryGetValue(i, out var corners))
                {
                    kept.AddRange(corners);
                    continue;
                }
                kept.Add(baseClasses[i]);
            }
            return kept.ToArray();
        }

        private static readonly (StyleLonghand Longhand, string Prefix)[] s_cornerTokens =
        {
            (StyleLonghand.BorderTopLeftRadius, "rounded-tl-"),
            (StyleLonghand.BorderTopRightRadius, "rounded-tr-"),
            (StyleLonghand.BorderBottomRightRadius, "rounded-br-"),
            (StyleLonghand.BorderBottomLeftRadius, "rounded-bl-"),
        };

        // The corners of the base radius token that no pose of its variants and importance writes, each as a token
        // of its own with the base's value; null for a token that is no radius or that no pose touches.
        private static string[]? CornersLeftBy(string token, Reading own, List<Reading> poseReadings)
        {
            if (!StyleArbitraryValueResolver.IsRadiusProperty(own.Property))
            {
                return null;
            }
            var core = StyleArbitraryValueResolver.StripImportant(token, out _);
            if (!StyleArbitraryValueResolver.TryParseLayer(core.Substring(VariantsLength(core)), out var style))
            {
                return null;
            }
            var written = StyleArbitraryLonghands.Of(own.Property);
            var left = new List<string>();
            var touched = false;
            foreach (var corner in s_cornerTokens)
            {
                if (!written.Contains(corner.Longhand))
                {
                    continue;
                }
                if (poseReadings.Exists(posed => SameLayer(posed, own)
                    && StyleArbitraryLonghands.Of(posed.Property).Contains(corner.Longhand)))
                {
                    touched = true;
                    continue;
                }
                var unit = style.Unit == UnityEngine.UIElements.LengthUnit.Percent ? "%" : "px";
                var value = style.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                left.Add(Spelled(own, corner.Prefix + "[" + value + unit + "]"));
            }
            return touched ? left.ToArray() : null;
        }

        private static bool SameLayer(Reading a, Reading z) => a.Important == z.Important && a.VariantSet == z.VariantSet;

        // Whether any token can be a spacing or radius utility: the Motion paths ask it of every pose on every render, and
        // build the readings only where it holds.
        private static bool MayHoldSpacing(string[] tokens)
        {
            // MUTANT_SURVIVES(equivalent, literal): a list holding no spacing token reads as no readings, so each caller returns its input on this path as it does on the early return; the gate only spares the readings' allocation.
            var any = false;
            foreach (var token in tokens)
            {
                any = any || MayBeSpacing(token);
            }
            return any;
        }

        private static bool MayBeSpacing(string token)
        {
            var core = StyleArbitraryValueResolver.StripImportant(token, out _);
            var variantsLength = VariantsLength(core);
            var payload = variantsLength == 0 ? core : core.Substring(variantsLength);
            return StyleArbitraryValueResolver.StartsWithSpacingPrefix(payload)
                || StyleArbitraryValueResolver.StartsWithRadiusPrefix(payload)
                || StyleLogicalUtilities.SortPropertiesOf(payload) != null;
        }

        // pose with each stylesheet-backed spacing class that is the narrower of a pair with an inline base token
        // written inline (pt-2 as pt-[8px]), because a pose class would otherwise lose its side to the base's
        // inline shorthand; pose itself when none is.
        internal static string[] InlineNarrowerPose(string[] baseClasses, string[] pose)
        {
            if (baseClasses.Length == 0 || pose.Length == 0 || !MayHoldSpacing(pose) || !MayHoldSpacing(baseClasses))
            {
                return pose;
            }
            var posed = ReadAll(pose);
            if (!posed.Exists(reading => reading.Backed))
            {
                return pose;
            }
            var own = ReadAll(baseClasses);
            string[]? rewritten = null;
            foreach (var reading in posed)
            {
                if (reading.Backed && own.Exists(inlineBase => !inlineBase.Backed && Narrows(reading, inlineBase)))
                {
                    rewritten ??= (string[])pose.Clone();
                    rewritten[reading.Index] = Inlined(reading);
                }
            }
            return rewritten ?? pose;
        }

        private static bool Overrides(Reading posed, Reading own)
        {
            if (posed.Important != own.Important || posed.VariantSet != own.VariantSet)
            {
                return false;
            }
            var posedWritten = StyleArbitraryLonghands.Of(posed.Property);
            return posedWritten.Union(StyleArbitraryLonghands.Of(own.Property)) == posedWritten;
        }

        private static List<Reading> ReadAll(string[] tokens)
        {
            var readings = new List<Reading>();
            for (var i = 0; i < tokens.Length; i++)
            {
                if (TryRead(tokens[i], i, out var reading))
                {
                    readings.Add(reading);
                }
            }
            return readings;
        }

        // The first of the highest-positioned stylesheet classes in matches, which is non-empty.
        private static Reading CascadeWinner(List<Reading> matches)
        {
            var winner = matches[0];
            foreach (var match in matches)
            {
                // MUTANT_SURVIVES(equivalent, boundary): a class holds one position of its own among the declared ones, so a tie is a repeated class or its two importance spellings, and writing either inline sets the same value.
                if (CascadePositionOf(match.Class) > CascadePositionOf(winner.Class))
                {
                    winner = match;
                }
            }
            return winner;
        }

        private static int CascadePositionOf(string cls)
            => StyleUtilityProperties.TryGet(cls, out var rule) ? rule.CascadePosition : -1;

        private static bool IsStylesheetClassOrLogical(string token)
        {
            var core = StyleArbitraryValueResolver.StripImportant(token, out _);
            var variantsLength = VariantsLength(core);
            var payload = variantsLength == 0 ? core : core.Substring(variantsLength);
            return StyleArbitraryValueResolver.TryReadStylesheetClass(payload, out _, out _)
                || StyleLogicalUtilities.SortPropertiesOf(payload) != null;
        }

        private readonly struct Reading
        {
            public int Index { get; init; }
            public string Variants { get; init; }
            // The variants as a sorted set, so stacking order does not tell two tokens apart.
            public string VariantSet { get; init; }
            public bool Important { get; init; }
            public bool TrailingBang { get; init; }
            public ArbitraryProperty Property { get; init; }
            // The inline token a stylesheet-backed class resolves to when it is rewritten.
            public string InlineForm { get; init; }
            public bool Backed { get; init; }
            // The stylesheet class a Backed reading is, without its variants or importance.
            public string Class { get; init; }
            public bool Logical { get; init; }
        }

        private static bool TryRead(string token, int index, out Reading reading)
        {
            reading = default;
            var core = StyleArbitraryValueResolver.StripImportant(token, out var important);
            if (core.Length < 3)
            {
                return false;
            }
            var variantsLength = VariantsLength(core);
            var variants = core.Substring(0, variantsLength);
            var payload = variantsLength == 0 ? core : core.Substring(variantsLength);
            var trailingBang = important && token[token.Length - 1] == '!';
            if (StyleArbitraryValueResolver.TryReadStylesheetClass(payload, out var property, out var form))
            {
                reading = new Reading
                {
                    Index = index, Variants = variants, VariantSet = VariantSetOf(variants), Important = important,
                    TrailingBang = trailingBang, Property = property, InlineForm = form, Backed = true, Class = payload,
                };
                return true;
            }
            if (StyleArbitraryValueResolver.IsInlineResolved(payload)
                && StyleArbitraryValueResolver.TryParseLayer(payload, out var style)
                && StyleArbitraryValueResolver.IsSpacingOrRadiusProperty(style.Property))
            {
                reading = new Reading
                {
                    Index = index, Variants = variants, VariantSet = VariantSetOf(variants), Important = important,
                    TrailingBang = trailingBang, Property = style.Property, InlineForm = string.Empty, Class = string.Empty,
                    Logical = StyleLogicalUtilities.SortPropertiesOf(payload) != null,
                };
                return true;
            }
            return false;
        }

        // The length of the variants in front of a payload: up to and including the last ':' outside brackets.
        internal static int VariantsLength(string core)
        {
            var depth = 0;
            var length = 0;
            for (var i = 0; i < core.Length; i++)
            {
                if (core[i] == '[')
                {
                    depth++;
                }
                else if (core[i] == ']')
                {
                    depth--;
                }
                else if (core[i] == ':' && depth == 0)
                {
                    length = i + 1;
                }
            }
            return length;
        }

        // `hover:focus:` and `focus:hover:` name one rule's variants, so plain ones compare as a set. An arbitrary
        // selector variant (`[&>*]:`) is order-sensitive, as Tailwind reads `[&>*]:hover:` and `hover:[&>*]:` as
        // different selectors, so those keep their order.
        private static string VariantSetOf(string variants)
        {
            if (variants.Length == 0)
            {
                return variants;
            }
            var parts = new List<string>();
            var ordered = new List<string>();
            var depth = 0;
            var start = 0;
            for (var i = 0; i < variants.Length; i++)
            {
                if (variants[i] == '[')
                {
                    depth++;
                }
                else if (variants[i] == ']')
                {
                    depth--;
                }
                else if (variants[i] == ':' && depth == 0)
                {
                    var part = variants.Substring(start, i - start);
                    (part.StartsWith("[", StringComparison.Ordinal) ? ordered : parts).Add(part);
                    start = i + 1;
                }
            }
            parts.Sort(StringComparer.Ordinal);
            return string.Join(":", parts) + "|" + string.Join(":", ordered);
        }

        private static bool SameSlot(Reading a, Reading z)
            => a.Property == z.Property && a.Important == z.Important && a.VariantSet == z.VariantSet;

        // Whether an inline reading in the same variants and importance writes some of what candidate does and
        // Tailwind emits candidate after it, which an inline value beating a class would reverse. Either
        // candidate does not contain all of what the inline reading writes, so it is the narrower utility; or it
        // does and is emitted later all the same, as a physical radius side or corner is after the logical one
        // over the corners it contains.
        private static bool IsNarrowerThanAnInlineNeighbour(Reading candidate, List<Reading> readings, List<int>? dropped)
            => readings.Exists(other => !other.Backed && Narrows(candidate, other)
                && (dropped == null || !dropped.Contains(other.Index)));

        private static bool Narrows(Reading candidate, Reading other)
        {
            if (other.Property == candidate.Property || other.Important != candidate.Important
                || other.VariantSet != candidate.VariantSet)
            {
                return false;
            }
            var written = StyleArbitraryLonghands.Of(candidate.Property);
            var otherWritten = StyleArbitraryLonghands.Of(other.Property);
            // MUTANT_SURVIVES(equivalent, boundary): two properties that share a longhand are both ranked, and no two ranked properties share a rank, so the comparison is never zero here.
            return written.Overlaps(otherWritten) && (written.Union(otherWritten) != written || CompareCascade(candidate.Property, other.Property) > 0);
        }

        private static string Inlined(Reading candidate) => Spelled(candidate, candidate.InlineForm);

        private static string Spelled(Reading candidate, string form)
        {
            var core = candidate.Variants + form;
            if (!candidate.Important)
            {
                return core;
            }
            return candidate.TrailingBang ? core + "!" : "!" + core;
        }
    }
}
