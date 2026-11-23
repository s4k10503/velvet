#nullable enable
using System.Collections.Generic;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Velvet
{
    // Lets a weight or italic class with no family class of its own resolve against the family it inherits,
    // the way CSS keeps an inherited font-family under a weight change. The family an element inherits is the
    // one named by the nearest ancestor carrying a font-<name> class, and only that class names it: an
    // ancestor that merely has a weight class contributes nothing, so a lookup skips it. A font an author declares
    // (a rule of a stylesheet other than a default one, or inline) on the element itself or on an element between
    // it and that ancestor comes first instead, as the nearest declaration does in CSS: the family is then the one
    // that holds that font, read the way a parent's font is below.
    //
    // Resolution order is the constraint. A parent's font layer runs after its children are created, and a
    // created element is parented only afterwards, so a lookup made while an element resolves can come back
    // empty or stale. Each such element is therefore queued, and Drain re-resolves it at the top-level pass
    // boundary, where an element created in the pass has its final parent (the same point RingOverlay.DrainPendingPlacements
    // runs). A change to an ancestor's family outside creation queues the inheritors under it the same way.
    //
    // With no named family, an inheritor takes the family that holds the font an author's rule gives the element itself,
    // else the one its parent resolves to, and that font alone where no family holds it: a font an author's stylesheet or
    // inline style gives an element above it is what a weight or italic class keeps. The parent's font is read from its
    // resolvedStyle, so it is read again at every update of the panel (Tick) for as long as an inheritor with no named
    // family exists, there being no event of its own for a stylesheet giving an ancestor one; the element's own rule font
    // is read again when the key its matched rules are held under moves.
    //
    // A change to the registry resolves every element the tables hold again: one naming a family, or inheriting a
    // named one, at once; one inheriting its parent's font once that font has been read again.
    //
    // Both tables are keyed by element and torn down with it, so they are enrolled in
    // ReconcilerContext's pure side-tables.
    internal sealed class FiberFontScope
    {
        // What an inheritor was last resolved against: the family a font-<name> ancestor names, or else the font its
        // parent resolved to then. Wait is the number of reads left after a registry change before the inheritor is
        // resolved again whether or not the parent's font has moved. Rules is the key the engine held the element's
        // matched rules under then, null where it was not read; a font an author's rule gives the element itself is
        // read again when it moves.
        internal readonly struct Inheritor
        {
            public readonly FontIntent Intent;
            public readonly string? Named;
            public readonly FontAsset? Seen;
            public readonly int Wait;
            public readonly long? Rules;

            public Inheritor(FontIntent intent, string? named, FontAsset? seen, int wait, long? rules = null)
            {
                Intent = intent;
                Named = named;
                Seen = seen;
                Wait = wait;
                Rules = rules;
            }
        }

        // Elements whose class list names a family, with the request and that family.
        internal Dictionary<VisualElement, (FontIntent Intent, string Family)> Families { get; } = new();

        // Elements with a font class and no family.
        internal Dictionary<VisualElement, Inheritor> Inheritors { get; } = new();

        private const int ChangeWait = 2;

        private static readonly string[] s_definition = { "-unity-font-definition" };

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
                    Resolve(element, entry.Intent);
                }
                else
                {
                    Inheritors[element] = new Inheritor(entry.Intent, null, entry.Seen, ChangeWait, entry.Rules);
                    _wantsFollow = true;
                }
            }

            Follow();
        }

        // The family the nearest ancestor carrying a font-<name> class names, or null where there is none or where a font
        // an author declares comes first: on the element itself, or on an element between it and that ancestor. Such a
        // font is what the element inherits, and its family is read from the font itself.
        internal string? InheritedFamily(VisualElement element)
        {
            if (Families.Count == 0 || DeclaresFont(element, own: true))
            {
                return null;
            }

            for (var ancestor = ChildListWrites.ParentOf(element); ancestor != null; ancestor = ChildListWrites.ParentOf(ancestor))
            {
                if (Families.TryGetValue(ancestor, out var named))
                {
                    return named.Family;
                }

                if (DeclaresFont(ancestor, own: false))
                {
                    return null;
                }
            }

            return null;
        }

        // Whether a font is declared on element by a rule of a stylesheet other than a default one, or inline. The
        // inline font of the element a font is being resolved for, of an inheritor and of a root the default family is
        // written on is Velvet's own write, not a declaration.
        private bool DeclaresFont(VisualElement element, bool own)
        {
            if (BorderPreflight.AuthorRuleSets(element, s_definition) == true)
            {
                return true;
            }

            return !own && !Inheritors.ContainsKey(element) && !DefaultFamilyRoots.IsRoot(element)
                && element.style.unityFontDefinition.keyword != StyleKeyword.Null;
        }

        // The font an author's rule gives element itself, as the engine holds it once the panel has resolved the
        // element's styles; null where none does or before then.
        private static FontAsset? OwnRuleFont(VisualElement element)
        {
            if (element.panel == null || BorderPreflight.AuthorRuleSets(element, s_definition) != true)
            {
                return null;
            }

            var asset = StyleCascade.RulesFontDefinition(element)?.fontAsset;
            return LineBoxFont.SourceOf(asset) ?? asset;
        }

        private static bool Moved(VisualElement element, in Inheritor entry)
            => ParentFont(element) != entry.Seen
                || (entry.Rules is { } rules && StyleCascade.RulesHash(element) != rules);

        // The font the element's parent resolves to; null before the panel has resolved one.
        private static FontAsset? ParentFont(VisualElement element)
        {
            var asset = ChildListWrites.ParentOf(element)?.resolvedStyle.unityFontDefinition.fontAsset;
            return LineBoxFont.SourceOf(asset) ?? asset;
        }

        private void Resolve(VisualElement element, in FontIntent intent)
        {
            var named = InheritedFamily(element);
            var seen = named == null ? ParentFont(element) : null;
            var source = named == null ? OwnRuleFont(element) ?? seen : null;
            var family = named ?? VelvetFonts.FamilyOf(source);
            var foreign = named == null && source != null && family == null;
            StyleFontResolver.ApplyResolved(element, intent, family, foreign);
            Inheritors[element] = new Inheritor(intent, named, seen, 0, named == null ? StyleCascade.RulesHash(element) : null);
            _wantsFollow |= named == null;
        }

        // Starts the per-update read once an inheritor with no named family exists and an anchor to schedule it on is known.
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
            Resolve(element, intent);
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

        // Re-resolves each queued inheritor against the family it now inherits. An inheritor stays queued
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

        // Reads again the font each inheritor with no named family takes from its parent, and stops once none is left,
        // which is also where a disposed context's emptied tables leave it.
        private void Tick()
        {
            _buffer.Clear();
            var unnamed = false;
            foreach (var pair in Inheritors)
            {
                var entry = pair.Value;
                if (entry.Named != null)
                {
                    continue;
                }

                unnamed = true;
                if (entry.Wait != 0 || Moved(pair.Key, entry))
                {
                    _buffer.Add(pair.Key);
                }
            }

            if (!unnamed)
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
                    ResolveDown(element, entry.Intent);
                }
                else if (entry.Wait != 0)
                {
                    Inheritors[element] = new Inheritor(entry.Intent, null, entry.Seen, entry.Wait - 1, entry.Rules);
                }
            }
        }

        // A child reads its font off its parent, so each unnamed inheritor directly under one resolved here is read again
        // straight after it, in this Tick rather than the next.
        private void ResolveDown(VisualElement element, in FontIntent intent)
        {
            Resolve(element, intent);
            var count = element.hierarchy.childCount;
            for (var i = 0; i < count; i++)
            {
                var child = element.hierarchy[i];
                if (Inheritors.TryGetValue(child, out var entry) && entry.Named == null && ParentFont(child) != entry.Seen)
                {
                    ResolveDown(child, entry.Intent);
                }
            }
        }

        private bool Settle(VisualElement element)
        {
            if (!Inheritors.TryGetValue(element, out var entry))
            {
                return true;
            }

            if (InheritedFamily(element) != entry.Named)
            {
                Resolve(element, entry.Intent);
            }

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
