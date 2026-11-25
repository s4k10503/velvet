#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
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
    // engine holds its matched rules under. That key also moves with the inherited styles of its parent, so a move of it
    // resolves the element again only where the fonts its rules and parent give it differ (RuleKey, two reads of the
    // engine's cached style), read only where a stylesheet other than a default one is attached above the element. A
    // resolve asks the engine's rule matcher (BorderPreflight.AuthorRuleFonts) for the element's own rules and, where
    // the parent resolves a definition and a legacy font, once for each ancestor up to the one naming a family, an
    // inheritor or an author's definition (AncestorLegacy); where the matcher cannot be built, only inline fonts are read.
    //
    // The same reads are also taken in each style pass that restyles an inheritor (Restyled), which it hears through
    // MarkerClass and the sheet it carries (MarkerSheet) by ColorReferenceWatch's mechanism. That pass is what brings a
    // font a class, a pseudo-state or a stylesheet gives an ancestor to the inheritor in the update that shows it on the
    // ancestor
    // (Given_ABoldLabelUnderAClasslessElement_When_AnAncestorGainsAClassWhoseRuleGivesItAFont_Then_TheLabelFollowsInThatStylePass),
    // on a panel without the bundled stylesheet too. Where an engine member this reads does not resolve, no inheritor
    // carries either, and only the update reads them.
    //
    // A resolve reads the font of the nearest ancestor that declares one rather than the parent's where the parent only
    // inherits its own (InheritedPeek), and an inheritor resolved in a Tick whose family changed reads again those below
    // it through elements that only inherit (ResolveBelow).
    //
    // A change to the registry resolves every element the tables hold again: one naming a family, or inheriting a
    // named one, at once; one inheriting its parent's font once that font has been read again.
    //
    // Both tables are keyed by element and torn down with it: Families as one of ReconcilerContext's pure side-tables,
    // Inheritors through Forget and ForgetAll, since an entry holds the element's MarkerClass, MarkerSheet and restyle
    // callback.
    internal sealed class FiberFontScope
    {
        // What an inheritor was last resolved against. Named is the family a font-<name> ancestor names and kept; Seen and
        // SeenLegacy the font and the legacy font its parent resolved to then, SeenFar and SeenFarLegacy those of the ancestor
        // that declares one (InheritSource). Wait is the number of reads left after a
        // registry change before it is resolved again whether or not anything moved. Rules is the key the engine held
        // the element's matched rules under, Own the font the author gave the element inline and RuleKey the fonts its
        // rules and parent gave it, read only where a stylesheet other than a default one is attached above it. Family,
        // Foreign and Legacy are what was applied.
        internal struct Inheritor
        {
            public FontIntent Intent;
            public string? Named;
            public UnityEngine.Object? Seen;
            public int Wait;
            public long? Rules;
            public UnityEngine.Object? Own;
            public string? Family;
            public bool Foreign;
            public int? RuleKey;
            public UnityEngine.Object? SeenLegacy;
            public UnityEngine.Object? SeenFar;
            public UnityEngine.Object? SeenFarLegacy;
            public UnityEngine.Font? Legacy;

            public Inheritor(FontIntent intent, string? named, UnityEngine.Object? seen, int wait, long? rules = null,
                UnityEngine.Object? own = null, string? family = null, bool foreign = false, int? ruleKey = null,
                UnityEngine.Object? seenLegacy = null, UnityEngine.Font? legacy = null, UnityEngine.Object? seenFar = null,
                UnityEngine.Object? seenFarLegacy = null)
            {
                Intent = intent;
                Named = named;
                Seen = seen;
                Wait = wait;
                Rules = rules;
                Own = own;
                Family = family;
                Foreign = foreign;
                RuleKey = ruleKey;
                SeenLegacy = seenLegacy;
                SeenFar = seenFar;
                SeenFarLegacy = seenFarLegacy;
                Legacy = legacy;
            }

            public Inheritor WithWait(int wait)
            {
                var copy = this;
                copy.Wait = wait;
                return copy;
            }

            public Inheritor WithRules(long? rules)
            {
                var copy = this;
                copy.Rules = rules;
                return copy;
            }
        }

        // Where a font was found: the family a font-<name> ancestor names, or the asset of the font that decides and, where
        // it is a legacy font, that font, which the engine draws only if it is written as a definition.
        private readonly struct Located
        {
            public readonly string? Named;
            public readonly FontAsset? Source;
            public readonly UnityEngine.Font? Legacy;

            public Located(string? named, FontAsset? source, UnityEngine.Font? legacy = null)
            {
                Named = named;
                Source = source;
                Legacy = legacy;
            }
        }

        // Elements whose class list names a family, with the request and that family.
        internal Dictionary<VisualElement, (FontIntent Intent, string Family)> Families { get; } = new();

        // Elements with a font class and no family.
        internal Dictionary<VisualElement, Inheritor> Inheritors { get; } = new();

        private const int ChangeWait = 2;

        // Not ColorReferenceWatch's marker, whose rule is the bundled stylesheet's: MarkerSheet holds this one's, so a
        // panel without that stylesheet reports an inheritor's restyle as well.
        internal const string MarkerClass = "velvet-font-inherit";

        private const string MarkerProperty = "--velvet-font-inherit";

        private static readonly EventCallback<CustomStyleResolvedEvent> s_restyled = Restyled;

        private static readonly bool s_watches = MarkerSheets.Builds && InheritedStyleHash.Resolves;

        private static StyleSheet? s_markerSheet;

        private static readonly string[] s_definition = { "-unity-font-definition" };
        private static readonly string[] s_legacy = { "-unity-font" };

        // Weak, so a scope nobody disposed is not kept alive by the registry's event.
        private static readonly List<System.WeakReference<FiberFontScope>> s_scopes = new();

        private readonly HashSet<VisualElement> _pending = new();
        private readonly System.Predicate<VisualElement> _settle;
        private readonly List<VisualElement> _buffer = new();
        private readonly List<(int Depth, VisualElement Element)> _order = new();
        private readonly List<(int Depth, VisualElement Element)> _refreshOrder = new();
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

            _refreshOrder.Clear();
            foreach (var key in Inheritors.Keys)
            {
                Enqueue(_refreshOrder, key);
            }

            _refreshOrder.Sort(s_byDepth);
            for (var i = 0; i < _refreshOrder.Count; i++)
            {
                var element = _refreshOrder[i].Element;
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

        // The font an element resolves to, as the object the engine holds: null before the panel has resolved one. A legacy
        // font counts only where the definition names none.
        private static UnityEngine.Object? Peek(VisualElement source)
        {
            var resolved = source.resolvedStyle;
            var definition = resolved.unityFontDefinition;
            return (UnityEngine.Object?)definition.fontAsset ?? (UnityEngine.Object?)definition.font ?? resolved.unityFont;
        }

        // Whether the element takes its font from its parent: it declares none inline and transitions none, an element that
        // transitions its font being read as it shows it
        // (Given_AClasslessElementTransitioningItsFontBetweenABoldContainerAndABoldLabel_When_TheFontTheContainerInheritsChanges_Then_TheLabelKeepsTheFontTheElementShows).
        private static bool Inherits(VisualElement element)
            => element.style.unityFontDefinition.keyword == StyleKeyword.Null
                && element.style.unityFont.keyword == StyleKeyword.Null
                && !StyleFilterTransitionDriver.TryFindTransition(element, "-unity-font-definition", "-unity-font",
                    out _, out _, out _);

        // The nearest ancestor of the element that declares a font inline or runs a transition of it, which is where a font
        // written inline above the element is current whatever sits between; the top ancestor where none does.
        private static VisualElement? InheritSource(VisualElement element)
        {
            var source = ChildListWrites.ParentOf(element);
            while (source != null && Inherits(source))
            {
                var up = ChildListWrites.ParentOf(source);
                if (up == null)
                {
                    break;
                }

                source = up;
            }

            return source;
        }

        private static UnityEngine.Object? FarPeek(VisualElement element)
        {
            var source = InheritSource(element);
            return source == null ? null : Peek(source);
        }

        private static UnityEngine.Object? FarLegacy(VisualElement element)
            => InheritSource(element)?.resolvedStyle.unityFont;

        // The font the element's parent gives it as of now. A font written inline above an element that inherits its own
        // reaches that element's resolved style only at the style pass of its panel, so it is read off the declaring
        // ancestor (InheritSource) instead, unless a rule of any stylesheet gives an element in between a font, which is read
        // where it is given. Where the two reads agree, or the parent has none resolved yet, nothing is asked of the
        // matcher; where it cannot be built, only inline fonts are read. Only a resolve asks it: the poll compares the two
        // reads against the ones recorded (Moved).
        // Given_AClasslessElementBetweenABoldElementAndABoldLabel_When_TheContainersFamilyChanges_Then_TheLabelFollowsAtOnce
        // fails when the read stops at the parent.
        private static UnityEngine.Object? InheritedPeek(VisualElement element)
        {
            var parent = ChildListWrites.ParentOf(element);
            var source = InheritSource(element);
            if (parent == null || source == null)
            {
                return null;
            }

            var far = Peek(source);
            var near = Peek(parent);
            if (far == near || near == null)
            {
                return far;
            }

            for (var ancestor = parent; ancestor != source; ancestor = ChildListWrites.ParentOf(ancestor)!)
            {
                if ((BorderPreflight.RuleFonts(ancestor) ?? 0) != 0)
                {
                    return Peek(ancestor);
                }
            }

            return far;
        }

        private static UnityEngine.Object? ParentPeek(VisualElement element)
        {
            var parent = ChildListWrites.ParentOf(element);
            return parent == null ? null : Peek(parent);
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
        // the parent's font belongs to it, else the parent's font. A legacy font an author's rule gives the element or an
        // ancestor decides over the definition inherited from above, which the engine would otherwise draw ahead of it.
        private Located Locate(VisualElement element)
        {
            var own = InlineFontLedger.AuthorFont(element);
            var sheeted = BorderPreflight.HasAuthorSheet(element) == true;
            var rules = own == null && sheeted ? BorderPreflight.AuthorRuleFonts(element) ?? 0 : 0;
            if (own != null || (rules & 1) != 0)
            {
                var inlineLegacy = own as UnityEngine.Font;
                if (inlineLegacy != null && InlineFontLedger.AuthorDefinition(element) != null)
                {
                    inlineLegacy = null;
                }

                return new Located(null, DeclaredAsset(element, own), inlineLegacy);
            }

            if ((rules & 2) != 0)
            {
                return Declared(element, StyleCascade.RulesLegacyFont(element));
            }

            var peek = InheritedPeek(element);
            var named = NamedAncestor(element);
            var ancestorLegacy = AncestorLegacy(element, named, sheeted);
            if (ancestorLegacy != null)
            {
                return Declared(element, ancestorLegacy);
            }

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

        private static Located Declared(VisualElement element, UnityEngine.Font? font)
            => new(null, font == null ? null : StyleCascade.CachedFontAsset(element, font), font);

        // The legacy font an author gives the nearest ancestor that sets one, inline or by a rule, below the one naming a
        // family. The walk stops at an ancestor that is a family member or an inheritor, or that has a definition of the
        // author's, since that definition decides instead. It is made only where the parent, or the ancestor that declares a font above it, resolves a definition, which
        // the engine draws ahead of a legacy font, and a legacy font. An ancestor's rules are asked of the matcher once,
        // and only where a stylesheet other than a default one is attached.
        private static bool ResolvesDefinitionAndLegacy(VisualElement element)
        {
            var resolved = element.resolvedStyle;
            var definition = resolved.unityFontDefinition;
            return resolved.unityFont != null && (definition.fontAsset != null || definition.font != null);
        }

        private UnityEngine.Font? AncestorLegacy(VisualElement element, VisualElement? named, bool sheeted)
        {
            var parent = ChildListWrites.ParentOf(element);
            if (parent == null)
            {
                return null;
            }

            if (!ResolvesDefinitionAndLegacy(parent) && !ResolvesDefinitionAndLegacy(InheritSource(element)!))
            {
                return null;
            }

            for (var ancestor = parent; ancestor != null && ancestor != named; ancestor = ChildListWrites.ParentOf(ancestor))
            {
                if (Families.ContainsKey(ancestor) || Inheritors.ContainsKey(ancestor)
                    || InlineFontLedger.AuthorDefinition(ancestor) != null)
                {
                    return null;
                }

                var rules = sheeted ? BorderPreflight.AuthorRuleFonts(ancestor) ?? 0 : 0;
                if ((rules & 1) != 0)
                {
                    return null;
                }

                var inline = ancestor.style.unityFont;
                if (inline.keyword != StyleKeyword.Null)
                {
                    return inline.value;
                }

                if ((rules & 2) != 0)
                {
                    return StyleCascade.RulesLegacyFont(ancestor);
                }
            }

            return null;
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

        // Whether what the element's font was resolved from has moved, short of its matched rules: the parent's font and
        // the author's inline font on the element.
        private static bool Moved(VisualElement element, in Inheritor entry)
            => PeekMoved(element, entry) || InlineFontLedger.AuthorFont(element) != entry.Own
                || ParentLegacy(element) != entry.SeenLegacy || FarLegacy(element) != entry.SeenFarLegacy;

        // Whether either read InheritedPeek compares has moved, without the matcher it asks.
        private static bool PeekMoved(VisualElement element, in Inheritor entry)
            => ParentPeek(element) != entry.Seen || FarPeek(element) != entry.SeenFar;

        private static UnityEngine.Object? ParentLegacy(VisualElement element)
            => ChildListWrites.ParentOf(element)?.resolvedStyle.unityFont;

        // The key of the element's matched rules moves with them and with the inherited styles of its parent, so it
        // says only that the font the rules give might have.
        private static bool RulesMoved(VisualElement element, in Inheritor entry)
        {
            var rules = entry.Rules;
            if (rules == null)
            {
                return false;
            }

            return StyleCascade.RulesHash(element) != rules;
        }

        // The fonts the element's rules and parent give it, as the engine's cache holds them beneath the inline font.
        private static int RuleKeyOf(VisualElement element)
        {
            var definition = StyleCascade.RulesFontDefinition(element);
            var identity = definition == null ? null : (UnityEngine.Object?)definition.Value.fontAsset ?? definition.Value.font;
            return HashCode.Combine(identity, StyleCascade.RulesLegacyFont(element));
        }

        // Whether the fonts the rules give the element differ from the ones last read, which only an author's sheet
        // above the element can make a difference to.
        private static bool RuleFontMoved(VisualElement element, in Inheritor entry)
        {
            if (BorderPreflight.HasAuthorSheet(element) != true)
            {
                return entry.RuleKey != null;
            }

            return RuleKeyOf(element) != entry.RuleKey;
        }

        // Whether this resolve gave the element another family, or another font no family holds, than its last one.
        private bool Resolve(VisualElement element, in FontIntent intent, bool force)
        {
            var hadPrior = Inheritors.TryGetValue(element, out var prior);
            var found = Locate(element);
            var family = found.Named ?? VelvetFonts.FamilyOf(found.Source);
            var foreign = found.Named == null && found.Source != null && family == null;
            var legacy = foreign ? found.Legacy : null;
            var moved = !hadPrior || family != prior.Family || foreign != prior.Foreign || legacy != prior.Legacy;
            if (force || moved)
            {
                StyleFontResolver.ApplyResolved(element, intent, family, foreign, legacy);
            }

            int? ruleKey = BorderPreflight.HasAuthorSheet(element) == true ? RuleKeyOf(element) : null;
            Inheritors[element] = new Inheritor(intent, found.Named, ParentPeek(element), 0, StyleCascade.RulesHash(element),
                InlineFontLedger.AuthorFont(element), family, foreign, ruleKey, ParentLegacy(element), legacy, FarPeek(element),
                FarLegacy(element));
            if (!hadPrior)
            {
                Watch(element);
            }

            _wantsFollow = true;
            return hadPrior && moved;
        }

        private static void Watch(VisualElement element)
        {
            var sheet = MarkerSheet();
            if (sheet == null)
            {
                return;
            }

            element.RegisterCallback(s_restyled, TrickleDown.TrickleDown);
            element.AddToClassList(MarkerClass);
            element.styleSheets.Add(sheet);
        }

        private static void Unwatch(VisualElement element)
        {
            element.UnregisterCallback(s_restyled, TrickleDown.TrickleDown);
            element.RemoveFromClassList(MarkerClass);
            if (s_markerSheet != null)
            {
                element.styleSheets.Remove(s_markerSheet);
            }
        }

        // The element leaves the table: it no longer carries a font class of this kind, or it is torn down
        // (FiberElementCleaner).
        internal void Forget(VisualElement element)
        {
            if (Inheritors.Remove(element))
            {
                Unwatch(element);
            }
        }

        // The tree is disposed, which can leave its elements mounted (Reconciler.Dispose).
        internal void ForgetAll()
        {
            foreach (var element in new List<VisualElement>(Inheritors.Keys))
            {
                Forget(element);
            }
        }

        private static StyleSheet? MarkerSheet()
            => s_watches ? s_markerSheet ??= MarkerSheets.Build(MarkerClass, MarkerProperty) : null;

        private static bool Stale(VisualElement element, in Inheritor entry)
            => Moved(element, entry) || (RulesMoved(element, entry) && RuleFontMoved(element, entry));

        private static void Restyled(CustomStyleResolvedEvent evt)
        {
            var element = (VisualElement)evt.currentTarget;
            foreach (var reference in s_scopes)
            {
                if (reference.TryGetTarget(out var scope))
                {
                    scope.Restyled(element);
                }
            }
        }

        private void Restyled(VisualElement element)
        {
            if (!Inheritors.TryGetValue(element, out var entry))
            {
                return;
            }

            if (Stale(element, entry))
            {
                Resolve(element, entry.Intent, entry.Wait == 1);
                InheritedStyleHash.Refresh(element);
            }
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
                Forget(element);
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
            Forget(element);
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
                SettleInTreeOrder();
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
                if (entry.Wait != 0 || Moved(pair.Key, entry) || RulesMoved(pair.Key, entry))
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
                if (entry.Wait == 1 || Stale(element, entry))
                {
                    ResolveDown(element, entry.Intent, entry.Wait == 1);
                }
                else if (entry.Wait != 0)
                {
                    Inheritors[element] = entry.WithWait(entry.Wait - 1);
                }
                else
                {
                    Inheritors[element] = entry.WithRules(StyleCascade.RulesHash(element));
                }
            }
        }

        // A child reads its font off its parent, so each inheritor under one resolved here is read again straight after
        // it, in this Tick rather than the next: where the font it reads has moved, or the family it resolved to has.
        private void ResolveDown(VisualElement element, in FontIntent intent, bool force)
        {
            ResolveBelow(element, Resolve(element, intent, force));
        }

        private void ResolveBelow(VisualElement parent, bool moved)
        {
            var count = parent.hierarchy.childCount;
            for (var i = 0; i < count; i++)
            {
                var child = parent.hierarchy[i];
                if (Inheritors.TryGetValue(child, out var entry))
                {
                    if (moved || PeekMoved(child, entry))
                    {
                        ResolveDown(child, entry.Intent, false);
                    }
                }
                else if (moved && !Families.ContainsKey(child) && Inherits(child))
                {
                    ResolveBelow(child, true);
                }
            }
        }

        // An element reads the font its parent has just been given, so each ancestor goes before what is under it,
        // whatever order the collection holds them in. The buffers hold a copy, which Refresh needs since it writes
        // to the table it was filled from. Each caller has its own: neither can run inside the other, since a resolve
        // raises no registry event.
        private static void Enqueue(List<(int Depth, VisualElement Element)> buffer, VisualElement element)
        {
            var depth = 0;
            for (var up = ChildListWrites.ParentOf(element); up != null; up = ChildListWrites.ParentOf(up))
            {
                depth++;
            }

            buffer.Add((depth, element));
        }

        private static readonly Comparison<(int Depth, VisualElement Element)> s_byDepth =
            static (left, right) => left.Depth.CompareTo(right.Depth);

        private void SettleInTreeOrder()
        {
            _order.Clear();
            foreach (var element in _pending)
            {
                Enqueue(_order, element);
            }

            _order.Sort(s_byDepth);
            for (var i = 0; i < _order.Count; i++)
            {
                if (_settle(_order[i].Element))
                {
                    _pending.Remove(_order[i].Element);
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
