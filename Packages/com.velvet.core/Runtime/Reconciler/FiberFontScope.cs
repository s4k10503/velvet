#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Velvet
{
    // Lets a weight or italic class with no family class of its own resolve against the family CSS gives the element:
    // the nearest declaration of a font-family, the element itself counting, since a weight class selects a face within
    // a family and never picks the family. A declaration is a font-<name> class on an ancestor, a font an author's rule
    // (a stylesheet other than a default one) or inline style gives an element, or the legacy -unity-font property so
    // given. Where an ancestor's class is nearest, its family is named; where a font is, the family is the one that
    // holds that font, and a font no registered family holds is kept with the weight or italic emulated on it.
    // An inline font on the element itself is the author's where it is not the one written here (Inheritor.Written);
    // the author's font is remembered (Inheritor.Own) since the write replaces it.
    //
    // Resolution order is the constraint. A parent's font layer runs after its children are created, and a
    // created element is parented only afterwards, so a lookup made while an element resolves can come back
    // empty or stale. Each such element is therefore queued, and Drain re-resolves it at the top-level pass
    // boundary, where an element created in the pass has its final parent (the same point RingOverlay.DrainPendingPlacements
    // runs). A change to an ancestor's family outside creation queues the inheritors under it the same way.
    //
    // Where nothing is declared, an inheritor takes the family that holds the font its parent resolves to. That font is
    // read from the parent's resolvedStyle, so it is read again at every update of the panel (Tick) for as long as an
    // inheritor exists. So is what an author can change beneath an inheritor without an event of its own: Tick compares
    // a stamp of the elements from the inheritor up to its named ancestor (classes, name, stylesheets, inline fonts, and
    // the key the engine holds the matched rules of the element a rule gave the font to), which allocates nothing.
    // Author rules are asked of the engine's matcher (BorderPreflight.AuthorRuleSets) only where a stylesheet other than a
    // default one is attached above the element; where the matcher cannot be built only inline fonts are read. A rule
    // that matches only in a pseudo-state and sets the font of an element between an inheritor and its named ancestor is
    // not seen until another change moves the stamp.
    //
    // A change to the registry resolves every element the tables hold again: one naming a family, or inheriting a
    // named one, at once; one inheriting its parent's font once that font has been read again.
    //
    // Both tables are keyed by element and torn down with it, so they are enrolled in
    // ReconcilerContext's pure side-tables.
    internal sealed class FiberFontScope
    {
        // What an inheritor was last resolved against. Named is the family a font-<name> ancestor names; Seen the font its
        // parent resolved to then, which only an element with no named family reads. Wait is the number of reads left
        // after a registry change before it is resolved again whether or not anything moved. Chain is the stamp of what
        // was read and Rule the element whose rules gave the font it was resolved from, null where none did. Own is the
        // font the author gave the element inline, which the inline write replaces, and Written the asset that write
        // put there. Family, Foreign and Keep are what was applied.
        internal readonly struct Inheritor
        {
            public readonly FontIntent Intent;
            public readonly string? Named;
            public readonly FontAsset? Seen;
            public readonly int Wait;
            public readonly int? Chain;
            public readonly VisualElement? Rule;
            public readonly UnityEngine.Object? Own;
            public readonly FontAsset? Written;
            public readonly string? Family;
            public readonly bool Foreign;
            public readonly bool Keep;

            public Inheritor(FontIntent intent, string? named, FontAsset? seen, int wait, int? chain = null,
                VisualElement? rule = null, UnityEngine.Object? own = null, FontAsset? written = null,
                string? family = null, bool foreign = false, bool keep = false)
            {
                Intent = intent;
                Named = named;
                Seen = seen;
                Wait = wait;
                Chain = chain;
                Rule = rule;
                Own = own;
                Written = written;
                Family = family;
                Foreign = foreign;
                Keep = keep;
            }

            public Inheritor WithWait(int wait)
                => new(Intent, Named, Seen, wait, Chain, Rule, Own, Written, Family, Foreign, Keep);
        }

        // Where a font was found, and the font itself.
        private readonly struct Located
        {
            public readonly string? Named;
            public readonly FontAsset? Source;
            public readonly VisualElement? Rule;
            public readonly UnityEngine.Object? Own;

            public Located(string? named, FontAsset? source, VisualElement? rule, UnityEngine.Object? own)
            {
                Named = named;
                Source = source;
                Rule = rule;
                Own = own;
            }
        }

        // Elements whose class list names a family, with the request and that family.
        internal Dictionary<VisualElement, (FontIntent Intent, string Family)> Families { get; } = new();

        // Elements with a font class and no family.
        internal Dictionary<VisualElement, Inheritor> Inheritors { get; } = new();

        private const int ChangeWait = 2;

        private static readonly string[] s_fontProperties = { "-unity-font-definition", "-unity-font" };

        // Weak, so a scope nobody disposed is not kept alive by the registry's event.
        private static readonly List<WeakReference<FiberFontScope>> s_scopes = new();

        private readonly HashSet<VisualElement> _pending = new();
        private readonly Predicate<VisualElement> _settle;
        private readonly List<VisualElement> _buffer = new();

        // What one read of an element's own state hashed to, so elements that share ancestors read each only once in a Tick.
        // Kept only inside a Tick, since an element can change between ticks without a record of it here.
        private readonly Dictionary<VisualElement, int> _linkStamps = new();
        private bool _inTick;
        private VisualElement? _mountRoot;
        private VisualElement? _anchor;
        private IVisualElementScheduledItem? _follow;
        private bool _wantsFollow;

        static FiberFontScope()
        {
            VelvetFonts.FontsChanged += RegistryChanged;
        }

        public FiberFontScope()
        {
            _settle = Settle;
            s_scopes.RemoveAll(static entry => !entry.TryGetTarget(out _));
            s_scopes.Add(new WeakReference<FiberFontScope>(this));
        }

        private static void RegistryChanged()
        {
            foreach (var entry in s_scopes.ToArray())
            {
                if (entry.TryGetTarget(out var scope))
                {
                    scope.Refresh();
                }
            }
        }

        private void Refresh()
        {
            foreach (var element in new List<VisualElement>(Families.Keys))
            {
                // MUTANT_SURVIVES(equivalent, literal): Families holds only intents that name a family (Apply stores one under
                // intent.HasFamily), and ApplyResolved tests foreign only in a conjunction with !intent.HasFamily, which such
                // an intent makes false.
                StyleFontResolver.ApplyResolved(element, Families[element].Intent, null, foreign: false);
            }

            foreach (var element in new List<VisualElement>(Inheritors.Keys))
            {
                var entry = Inheritors[element];
                if (entry.Named != null || InheritedFamily(element) != null)
                {
                    Resolve(element, entry.Intent, force: true);
                }
                else
                {
                    Inheritors[element] = entry.WithWait(ChangeWait);
                    _wantsFollow = true;
                }
            }

            Follow();
        }

        // The family the nearest ancestor carrying a font-<name> class names, or null where there is none or where a font
        // is declared nearer (Locate).
        internal string? InheritedFamily(VisualElement element)
        {
            if (Families.Count == 0)
            {
                return null;
            }

            return Locate(element, Inheritors.TryGetValue(element, out var prior) ? prior : default).Named;
        }

        // The legacy font definition or font an author gave element inline, or null. An inline font Velvet wrote is told
        // apart by the caller.
        private static UnityEngine.Object? InlineFont(VisualElement element)
        {
            var definition = element.style.unityFontDefinition;
            if (definition.keyword != StyleKeyword.Null)
            {
                var value = definition.value;
                return (UnityEngine.Object?)value.fontAsset ?? value.font;
            }

            var legacy = element.style.unityFont;
            return legacy.keyword != StyleKeyword.Null ? legacy.value : null;
        }

        // The font the author gave element inline: what is there unless it is the asset the last write put there, in which
        // case the font that write replaced.
        private static UnityEngine.Object? AuthorInline(VisualElement element, in Inheritor prior)
        {
            var inline = InlineFont(element);
            if (inline is FontAsset shown && prior.Written != null
                && (shown == prior.Written || LineBoxFont.SourceOf(shown) == prior.Written))
            {
                return prior.Own;
            }

            return inline;
        }

        // Finds the nearest declaration of a font for element: its own, else an ancestor's class, else the font of an
        // element between that ancestor and it, else none.
        private Located Locate(VisualElement element, in Inheritor prior)
        {
            var gate = BorderPreflight.HasAuthorSheet(element) == true;
            var own = AuthorInline(element, prior);
            var ruled = own == null && gate && BorderPreflight.AuthorRuleSets(element, s_fontProperties) == true;
            if (own != null || ruled)
            {
                return new Located(null, DeclaredAsset(element, own), ruled ? element : null, own);
            }

            for (var ancestor = ChildListWrites.ParentOf(element); ancestor != null; ancestor = ChildListWrites.ParentOf(ancestor))
            {
                if (Families.TryGetValue(ancestor, out var named))
                {
                    return new Located(named.Family, null, null, null);
                }

                var inline = Inheritors.TryGetValue(ancestor, out var inheritor) ? inheritor.Own : InlineFont(ancestor);
                var rule = inline == null && gate && BorderPreflight.AuthorRuleSets(ancestor, s_fontProperties) == true;
                if (inline != null || rule)
                {
                    return new Located(null, DeclaredAsset(ancestor, inline), rule ? ancestor : null, null);
                }
            }

            return new Located(null, ParentFont(element), null, null);
        }

        // The font asset UI Toolkit draws a declared font in: the inline one, else the one the rules give, which a Font
        // reads through the text settings; null before the panel has resolved the rules.
        private static FontAsset? DeclaredAsset(VisualElement declarer, UnityEngine.Object? inline)
        {
            if (inline is FontAsset asset)
            {
                return LineBoxFont.SourceOf(asset) ?? asset;
            }

            return inline is UnityEngine.Font font
                ? StyleCascade.CachedFontAsset(declarer, font)
                : StyleCascade.UnderlyingFontAsset(declarer, null);
        }

        // The font the element's parent resolves to; null before the panel has resolved one.
        private static FontAsset? ParentFont(VisualElement element)
        {
            var asset = ChildListWrites.ParentOf(element)?.resolvedStyle.unityFontDefinition.fontAsset;
            return LineBoxFont.SourceOf(asset) ?? asset;
        }

        private int LinkStamp(VisualElement link)
        {
            if (_inTick && _linkStamps.TryGetValue(link, out var known))
            {
                return known;
            }

            var style = link.style;
            var definition = style.unityFontDefinition;
            var stamp = HashCode.Combine(link, link.name, (int)definition.keyword, definition.value.fontAsset,
                definition.value.font, (int)style.unityFont.keyword, style.unityFont.value);
            stamp = VelvetStyleUtilities.ClassStamp(stamp, link);
            for (var i = 0; i < link.styleSheets.count; i++)
            {
                stamp = VelvetStyleUtilities.SheetStamp(stamp, link.styleSheets[i]);
            }

            if (_inTick)
            {
                _linkStamps[link] = stamp;
            }

            return stamp;
        }

        // What decides element's font apart from the parent's resolved one, from element up to the ancestor naming a
        // family: each element's inline font, name, classes and stylesheets, and the key the engine holds rule's
        // matched rules under, which also moves with the inherited styles of rule's parent.
        private int ChainStamp(VisualElement element, VisualElement? rule)
        {
            var stamp = 0;
            for (var link = element; link != null && (link == element || !Families.ContainsKey(link)); link = ChildListWrites.ParentOf(link))
            {
                stamp = HashCode.Combine(stamp, LinkStamp(link));
            }

            return rule == null ? stamp : HashCode.Combine(stamp, StyleCascade.RulesHash(rule));
        }

        private bool Moved(VisualElement element, in Inheritor entry)
            => (entry.Named == null && ParentFont(element) != entry.Seen)
                || (entry.Chain is { } chain && ChainStamp(element, entry.Rule) != chain);

        private void Resolve(VisualElement element, in FontIntent intent, bool force)
        {
            var hadPrior = Inheritors.TryGetValue(element, out var prior);
            var found = Locate(element, prior);
            var family = found.Named ?? VelvetFonts.FamilyOf(found.Source);
            var foreign = found.Named == null && found.Source != null && family == null;
            var keep = foreign && found.Own != null;
            var written = prior.Written;
            if (force || !hadPrior || family != prior.Family || foreign != prior.Foreign || keep != prior.Keep)
            {
                StyleFontResolver.ApplyResolved(element, intent, family, foreign, keep);
                var inline = element.style.unityFontDefinition;
                written = inline.keyword != StyleKeyword.Null && !keep ? inline.value.fontAsset : null;
                _linkStamps.Remove(element);
            }

            Inheritors[element] = new Inheritor(intent, found.Named, found.Named == null ? ParentFont(element) : null, 0,
                ChainStamp(element, found.Rule), found.Rule, found.Own, written, family, foreign, keep);
            _wantsFollow = true;
        }

        // Starts the per-update read once an inheritor exists and an anchor to schedule it on is known.
        private void Follow()
        {
            if (_wantsFollow && _follow == null && _anchor != null)
            {
                _follow = _anchor.schedule.Execute(Tick).Every(0);
                _wantsFollow = false;
            }
        }

        // Resolves the element's font request and records it. created is true on the create path, where
        // everything under the element is queued already, so the subtree walk is skipped.
        internal void Apply(VisualElement element, in FontIntent intent, bool created)
        {
            if (intent.HasFamily)
            {
                // MUTANT_SURVIVES(equivalent, literal): this branch runs only for an intent that names a family, and ApplyResolved tests foreign only in a conjunction with !intent.HasFamily, which such an intent makes false.
                StyleFontResolver.ApplyResolved(element, intent, null, foreign: false);
                Inheritors.Remove(element);
                var changed = !Families.TryGetValue(element, out var previous) || previous.Family != intent.Family;
                Families[element] = (intent, intent.Family!);
                if (changed && !created)
                {
                    QueueInheritorsUnder(element);
                }

                return;
            }

            var hadFamily = Families.Remove(element);
            Resolve(element, intent, force: true);
            _pending.Add(element);
            if (hadFamily && !created)
            {
                QueueInheritorsUnder(element);
            }
        }

        // The element no longer carries a font class.
        internal void Untrack(VisualElement element)
        {
            Inheritors.Remove(element);
            if (Families.Remove(element))
            {
                QueueInheritorsUnder(element);
            }
        }

        private void QueueInheritorsUnder(VisualElement ancestor)
        {
            foreach (var inheritor in Inheritors.Keys)
            {
                if (ancestor.Contains(inheritor))
                {
                    _pending.Add(inheritor);
                }
            }
        }

        // Re-resolves each queued inheritor against what it now inherits. An inheritor stays queued
        // until its parent chain ends at the mount root (the scheduler's anchor, which the tree hangs from
        // whether or not a panel holds it) or at an element a panel holds: a parked pass reaches this
        // boundary with subtrees it has created and not yet placed, and a chain ending at an unparented
        // ancestor says nothing about the family that ancestor will be given.
        internal void Drain(VisualElement? mountRoot)
        {
            _anchor ??= mountRoot;
            if (_pending.Count > 0)
            {
                _mountRoot = mountRoot;
                _pending.RemoveWhere(_settle);
                _mountRoot = null;
            }

            Follow();
        }

        // Reads again, for each inheritor, what its font was resolved from, and stops once none is left, which is also
        // where a disposed context's emptied tables leave it.
        private void Tick()
        {
            _buffer.Clear();
            _linkStamps.Clear();
            _inTick = true;
            try
            {
                TickOnce();
            }
            finally
            {
                _inTick = false;
                _linkStamps.Clear();
            }
        }

        private void TickOnce()
        {
            var any = false;
            foreach (var pair in Inheritors)
            {
                any = true;
                var entry = pair.Value;
                if (entry.Wait != 0 || Moved(pair.Key, entry))
                {
                    _buffer.Add(pair.Key);
                }
            }

            if (!any)
            {
                _follow?.Pause();
                _follow = null;
                return;
            }

            foreach (var element in _buffer)
            {
                var entry = Inheritors[element];
                if (entry.Wait == 1 || Moved(element, entry))
                {
                    ResolveDown(element, entry.Intent, entry.Wait == 1);
                }
                else if (entry.Wait != 0)
                {
                    Inheritors[element] = entry.WithWait(entry.Wait - 1);
                }
            }
        }

        // A child reads its font off its parent, so each unnamed inheritor directly under one resolved here is read again
        // straight after it, in this Tick rather than the next.
        private void ResolveDown(VisualElement element, in FontIntent intent, bool force)
        {
            Resolve(element, intent, force);
            var count = element.hierarchy.childCount;
            for (var i = 0; i < count; i++)
            {
                var child = element.hierarchy[i];
                if (Inheritors.TryGetValue(child, out var entry) && entry.Named == null && ParentFont(child) != entry.Seen)
                {
                    ResolveDown(child, entry.Intent, false);
                }
            }
        }

        private bool Settle(VisualElement element)
        {
            if (!Inheritors.TryGetValue(element, out var entry))
            {
                return true;
            }

            Resolve(element, entry.Intent, force: false);
            return IsPlaced(element, _mountRoot);
        }

        private static bool IsPlaced(VisualElement element, VisualElement? mountRoot)
        {
            var top = element;
            for (var up = ChildListWrites.ParentOf(top); up != null; up = ChildListWrites.ParentOf(top))
            {
                top = up;
            }

            return top.panel != null || ReferenceEquals(top, mountRoot);
        }
    }
}
