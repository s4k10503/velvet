using System;
using System.Collections.Generic;

namespace Velvet
{
    // Parses Velvet's [&>*]:<utility> child-combinator variant. [&>*] is CSS's "every direct child"
    // combinator, so [&>*]:mt-2 means "apply mt-2 to each direct child of THIS element." UI Toolkit has no
    // `> *` selector and a USS class name cannot contain the bracket / combinator syntax, so the token never
    // enters a class list: the reconciler routes it to StyleChildVariantManipulator, which walks the
    // container's direct children and applies the wrapped payload to each via StyleVariantPayload.
    //
    // The wrapped payload is an ordinary utility (mt-2, text-red-500, w-[8px]) or a STATE variant
    // (hover:bg-red-500, even dark:hover:bg-red-500): a state-variant payload composes through the same
    // StyleVariantPayload -> ReconcilerContext.GateStackedVariant machinery the has- manipulator uses, so
    // each child gets its own per-child stacked manipulator gated open by the child-variant walk. A payload
    // with NO gating owner reachable through that path — a structural (first:/nth-child), has-, attribute-,
    // supports-, or (self-)child-variant — is rejected at parse time (it would only ever be a dead token),
    // mirroring the has- extractor's reject-list precisely (which, unlike the structural config's stricter
    // list, keeps state variants because they DO reach a gating owner here). The self-nesting reject matters
    // because a child is never itself the CONTAINER a nested [&>*]: would need to walk — [&>*]:[&>*]:mt-2 has
    // nothing to apply the inner wrap to and must not silently degrade into applying mt-2 directly.
    //
    // This recognizes only the literal [&>*]: direct-child scope, not a general child-combinator family.
    internal static class StyleChildVariantClass
    {
        private const string Prefix = "[&>*]:";

        // True when cls is a [&>*]: token — the cheap routing gate the class-list sites check before their
        // inline-resolved branch, so the combinator token (which starts with '[' and would otherwise look
        // inline-resolvable) never enters the CONTAINER's own class list.
        public static bool IsChildVariant(string? cls)
            => !string.IsNullOrEmpty(cls) && cls.StartsWith(Prefix, StringComparison.Ordinal);

        // Cheap early-out gate: true when ANY class is a [&>*]: token. Skips the full TryExtract scan on the
        // ~99% of elements that carry no child-combinator variant.
        public static bool HasChildVariantClass(string[] classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            foreach (var cls in classNames)
            {
                if (IsChildVariant(cls))
                {
                    return true;
                }
            }
            return false;
        }

        // Splits a [&>*]:<payload> token into its wrapped payload. Returns false for a non-token, an empty
        // payload, or a payload with no gating owner reachable through StyleVariantPayload.Apply — a
        // structural / has- / attribute- / supports- / (self-nested) child-variant — which would only become
        // a dead token. A state variant (hover:, dark:hover:, group-hover:) IS accepted: it composes through
        // GateStackedVariant.
        public static bool TryParse(string? cls, out string payload)
        {
            payload = string.Empty;
            if (!IsChildVariant(cls))
            {
                return false;
            }
            var inner = cls!.Substring(Prefix.Length);
            if (inner.Length == 0
                || StyleStructuralVariantClass.IsStructural(inner)
                || StyleHasVariantClass.IsHas(inner)
                || StyleAttributeVariantClass.IsAttribute(inner)
                || StyleSupportsVariantClass.IsSupports(inner)
                || IsChildVariant(inner))
            {
                return false;
            }
            payload = inner;
            return true;
        }

        // Collects the wrapped payload of EVERY [&>*]: token, unlike gap / divide's last-value-wins scalar:
        // [&>*]:mt-2 [&>*]:text-red-500 are two independent utilities that must both compose, not overwrite
        // each other. Returns false when no valid token is present.
        public static bool TryExtract(string[] classNames, out string[] payloads)
        {
            payloads = Array.Empty<string>();
            if (classNames == null)
            {
                return false;
            }

            List<string>? collected = null;
            foreach (var cls in classNames)
            {
                if (TryParse(cls, out var payload))
                {
                    (collected ??= new List<string>()).Add(payload);
                }
            }

            if (collected == null)
            {
                return false;
            }
            payloads = collected.ToArray();
            return true;
        }
    }
}
