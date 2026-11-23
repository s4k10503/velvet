#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Velvet
{
    // Lets a weight or italic class with no family class of its own resolve against the family CSS gives the element:
    // the nearest declaration of a font-family, the element itself counting, since a weight class selects a face within
    // a family and never picks the family. The element's own declaration is an inline font the author gave it (told from
    // Velvet's own write by InlineFontLedger) or a rule of a stylesheet other than a default one setting its font.
    // Below that, the font its parent resolves to is what the engine's cascade gives it, wherever the rule or inline
    // style that set it sits, and the family is the one that holds that font; a font no registered family holds is
    // kept with the weight or italic emulated on it. Where a font-<name> ancestor exists, the family it names is kept
    // for as long as the parent's font belongs to that family (VelvetFonts.NamedHolds), which is what keeps a font
    // list's fallbacks; a parent's font that does not belong is an author's, and decides instead.
    //
    // Resolution order is the constraint. A parent's font layer runs after its children are created, and a
    // created element is parented only afterwards, so a lookup made while an element resolves can come back
    // empty or stale. Each such element is therefore queued, and Drain re-resolves it at the top-level pass
    // boundary, where an element created in the pass has its final parent (the same point RingOverlay.DrainPendingPlacements
    // runs). A change to an ancestor's family outside creation queues the inheritors under it the same way. A parent's
    // font read while an ancestor holds a write of Velvet's that the panel has not resolved yet is not read: the named
    // family stands until the next update of the panel.
    //
    // The parent's resolved font is read again at every update of the panel (Tick) for as long as an inheritor exists,
    // there being no event of its own for a stylesheet, a class or a pseudo-state changing a font above an element. What
    // an author can change on the element itself without one is read the same way: its inline font and the key the
    // engine holds its matched rules under, which also moves with the inherited styles of its parent and then asks the
    // engine's rule matcher again (BorderPreflight.AuthorRuleSets, only where a stylesheet other than a default one is
    // attached above the element; where the matcher cannot be built, only inline fonts are read).
    //
    // A change to the registry resolves every element the tables hold again: one naming a family, or inheriting a
    // named one, at once; one inheriting its parent's font once that font has been read again.
    //
    // Both tables are keyed by element and torn down with it, so they are enrolled in
    // ReconcilerContext's pure side-tables.
    internal sealed class FiberFontScope
    {
        // What an inheritor was last resolved against. Named is the family a font-<name> ancestor names and kept; Seen the
        // font its parent resolved to then. Wait is the number of reads left after a registry change before it is resolved
        // again whether or not anything moved. Rules is the key the engine held the element's matched rules under and Own
        // the font the author gave the element inline. Family and Foreign are what was applied.
        internal readonly struct Inheritor
        {
            public readonly FontIntent Intent;
            public readonly string? Named;
            public readonly UnityEngine.Object? Seen;
            public readonly int Wait;
            public readonly long? Rules;
            public readonly UnityEngine.Object? Own;
            public readonly string? Family;
            public readonly bool Foreign;

            public Inheritor(FontIntent intent, string? named, UnityEngine.Object? seen, int wait, long? rules = null,
                UnityEngine.Object? own = null, string? family = null, bool foreign = false)
            {
                Intent = intent;
                Named = named;
                Seen = seen;
                Wait = wait;
                Rules = rules;
                Own = own;
                Family = family;
                Foreign = foreign;
            }

            public Inheritor WithWait(int wait) => new(Intent, Named, Seen, wait, Rules, Own, Family, Foreign);
        }

        // Where a font was found: the family a font-<name> ancestor names, or the asset of the font that decides.
        private readonly struct Located
        {
            public readonly string? Named;
            public readonly FontAsset? Source;

            public Located(string? named, FontAsset? source)
            {
                Named = named;
                Source = source;
            }
        }

        // Elements whose class list names a family, with the request and that family.
        internal Dictionary<VisualElement, (FontIntent Intent, string Family)> Families { get; } = new();

        // Elements with a font class and no family.
        internal Dictionary<VisualElement, Inheritor> Inheritors { get; } = new();

        private const int ChangeWait = 2;

        private static readonly string[] s_fontProperties = { "-unity-font-definition", "-unity-font" };

        // Weak, so a scope nobody disposed is not kept alive by the registry's event.
        private static readonly List<System.WeakReference<FiberFontScope>> s_scopes = new();

        private readonly HashSet<VisualElement> _pending = new();
        private readonly System.Predicate<VisualElement> _settle;
        private readonly List<VisualElement> _buffer = new();
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
            s_scopes.Add(new System.WeakReference<FiberFontScope>(this));
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

        // The family a font-<name> ancestor names and the element keeps, or null where there is none or where a font
        // declared nearer decides.
        internal string? InheritedFamily(VisualElement element) => Locate(element).Named;

        // The nearest ancestor carrying a font-<name> class.
        private VisualElement? NamedAncestor(VisualElement element)
        {
            if (Families.Count == 0)
            {
                return null;
            }

            var up = ChildListWrites.ParentOf(element);
            while (up != null && !Families.ContainsKey(up))
            {
                up = ChildListWrites.ParentOf(up);
            }

            return up;
        }

        // The font the element's parent resolves to, as the object the engine holds: null before the panel has resolved
        // one. A legacy font counts only where the definition names none.
        private static UnityEngine.Object? ParentPeek(VisualElement element)
        {
            var parent = ChildListWrites.ParentOf(element);
            if (parent == null)
            {
                return null;
            }

            var resolved = parent.resolvedStyle;
            var definition = resolved.unityFontDefinition;
            return (UnityEngine.Object?)definition.fontAsset ?? (UnityEngine.Object?)definition.font ?? resolved.unityFont;
        }

        private static FontAsset? AssetOfPeek(VisualElement element, UnityEngine.Object? peek)
        {
            if (peek is FontAsset asset)
            {
                return LineBoxFont.SourceOf(asset) ?? asset;
            }

            var parent = ChildListWrites.ParentOf(element);
            var font = peek as UnityEngine.Font;
            if (font == null || parent == null)
            {
                return null;
            }

            return StyleCascade.CachedFontAsset(parent, font);
        }

        // Whether an ancestor between element and the one naming its family holds a write of Velvet's that the panel has not
        // resolved yet, so that the parent's resolved font says nothing about what is written above it.
        private static bool Unresolved(VisualElement element, VisualElement named)
        {
            for (var ancestor = ChildListWrites.ParentOf(element); ancestor != null; ancestor = ChildListWrites.ParentOf(ancestor))
            {
                var written = InlineFontLedger.Written(ancestor);
                if (written != null)
                {
                    var shown = ancestor.resolvedStyle.unityFontDefinition.fontAsset;
                    return (LineBoxFont.SourceOf(shown) ?? shown) != written;
                }

                if (ancestor == named)
                {
                    return false;
                }
            }

            return false;
        }

        // Finds what decides element's family: its own declared font, else the family a font-<name> ancestor names while
        // the parent's font belongs to it, else the parent's font.
        private Located Locate(VisualElement element)
        {
            var own = InlineFontLedger.AuthorFont(element);
            var ruled = own == null && BorderPreflight.HasAuthorSheet(element) == true
                && BorderPreflight.AuthorRuleSets(element, s_fontProperties) == true;
            if (own != null || ruled)
            {
                return new Located(null, DeclaredAsset(element, own));
            }

            var peek = ParentPeek(element);
            var named = NamedAncestor(element);
            if (named != null)
            {
                var family = Families[named].Family;
                var parentAsset = AssetOfPeek(element, peek);
                var kept = parentAsset == null || VelvetFonts.NamedHolds(family, parentAsset)
                    || Unresolved(element, named) || !VelvetFonts.Resolve(family, VelvetFontWeight.Normal, false).HasAsset;
                if (kept)
                {
                    return new Located(family, null);
                }

                return new Located(null, parentAsset);
            }

            return new Located(null, AssetOfPeek(element, peek));
        }

        // The font asset UI Toolkit draws element's declared font in: the inline one, else the one the rules give, which a
        // Font reads through the text settings; null before the panel has resolved the rules.
        private static FontAsset? DeclaredAsset(VisualElement element, UnityEngine.Object? inline)
        {
            if (inline is FontAsset asset)
            {
                return LineBoxFont.SourceOf(asset) ?? asset;
            }

            return inline is UnityEngine.Font font
                ? StyleCascade.CachedFontAsset(element, font)
                : StyleCascade.UnderlyingFontAsset(element, null);
        }

        private bool Moved(VisualElement element, in Inheritor entry)
        {
            if (ParentPeek(element) != entry.Seen || InlineFontLedger.AuthorFont(element) != entry.Own)
            {
                return true;
            }

            var rules = entry.Rules;
            if (rules != null)
            {
                return StyleCascade.RulesHash(element) != rules;
            }

            return false;
        }

        private void Resolve(VisualElement element, in FontIntent intent, bool force)
        {
            var hadPrior = Inheritors.TryGetValue(element, out var prior);
            var found = Locate(element);
            var family = found.Named ?? VelvetFonts.FamilyOf(found.Source);
            var foreign = found.Named == null && found.Source != null && family == null;
            if (force || !hadPrior || family != prior.Family || foreign != prior.Foreign)
            {
                StyleFontResolver.ApplyResolved(element, intent, family, foreign);
            }

            Inheritors[element] = new Inheritor(intent, found.Named, ParentPeek(element), 0, StyleCascade.RulesHash(element),
                InlineFontLedger.AuthorFont(element), family, foreign);
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

        // A child reads its font off its parent, so each inheritor directly under one resolved here is read again
        // straight after it, in this Tick rather than the next.
        private void ResolveDown(VisualElement element, in FontIntent intent, bool force)
        {
            Resolve(element, intent, force);
            var count = element.hierarchy.childCount;
            for (var i = 0; i < count; i++)
            {
                var child = element.hierarchy[i];
                if (Inheritors.TryGetValue(child, out var entry) && ParentPeek(child) != entry.Seen)
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
