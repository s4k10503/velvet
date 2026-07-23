# Styling notes: Fonts (family, weight, italic)

Velvet's font utilities are Tailwind-inspired (`font-sans`, `font-bold`, `italic`, …) but they
run on Unity UI Toolkit's text stack (TextCore / SDF Font Assets). UI Toolkit exposes only two
font style properties:

| Property | Type | What it can express |
|---|---|---|
| `-unity-font-definition` | a Font Asset | which **family** (and, if you register them, which **weight/italic** asset) |
| `-unity-font-style` | `{ normal, bold, italic, bold-and-italic }` | a **binary** weight axis + italic, in one value |

That single binary `-unity-font-style` is why raw USS can express neither a 100–900 weight scale
nor a composed `font-bold italic`. Velvet closes the gap with a small font layer — the
`StyleFontResolver`, which resolves family + weight + italic **together** and writes them as inline
style, plus the `VelvetFonts` registry that maps family names to Font Assets.

## The utility classes

| Class | Meaning |
|---|---|
| `font-sans`, `font-serif`, `font-mono`, `font-<name>` | Selects the family named `<name>` from the registry |
| `font-[Inter]` | Arbitrary family by registered name |
| `font-[addr:<key>]` | Arbitrary family loaded from Addressables (mirrors `bg-[addr:<key>]`) |
| `font-thin` … `font-black` | Weight scale (100…900) |
| `font-[550]` / `font-[weight:550]` | Arbitrary weight |
| `italic` / `not-italic` | Italic axis |
| `font-bold italic` | **Composes** to bold-and-italic |
| `bold-italic` | Single-class bold + italic (back-compat) |

Facets follow CSS cascade order — a later class of the same facet wins (`font-bold font-light` →
light), and the three facets coexist on one element.

## How resolution works

For each element with a font class, `StyleFontResolver` computes one `(family, weight, italic)`
intent and asks `VelvetFonts.Resolve`:

1. **Family asset found** (a weight-specific Font Asset is registered) → assign it via
   `-unity-font-definition`. Only the part the asset can't satisfy is emulated through
   `-unity-font-style` (e.g. faux bold when you asked for `font-black` but only a regular asset
   is registered, or faux italic when no italic asset exists).
2. **No family / no asset** → fall back to the binary threshold: weight `>= 600` renders bold,
   below renders normal, combined with italic. This is also what the USS fallback classes in
   `_typography.uss` do, so output is sensible even before you register any fonts.

Inline style wins over the USS classes, so the resolver is always the authoritative font layer.

## Registering fonts (production / master-data)

`VelvetFonts` is **independent of how the master data is stored**. It only consumes
`VelvetFontFamily` values through `VelvetFonts.Register`; turning a CSV row, MasterMemory entity, or
any other source into those values is an adapter you own — the package ships no ScriptableObject (or
any other) representation. A family holds one entry per weight, and each entry can reference the Font
Asset **directly** or by an **Addressables key** (loaded and cached on first use).

**From code** — the canonical, representation-agnostic path:

```csharp
VelvetFonts.Register(new VelvetFontFamily("sans",
    new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, upright = interRegular, italic = interItalic },
    new VelvetFontWeightEntry { weight = VelvetFontWeight.Bold,   uprightAddress = "Fonts/Inter-Bold" }));

VelvetFonts.DefaultFamily = "sans"; // applied to elements that set a weight/style but no family
```

**From CSV / MasterMemory / a ScriptableObject / any other source** — map your rows to
`VelvetFontFamily` and call the batch entry point (raises `FontsChanged` once):

```csharp
IEnumerable<VelvetFontFamily> families = myMasterData.Select(row => new VelvetFontFamily(
    row.Name,
    new VelvetFontWeightEntry { weight = row.Weight, uprightAddress = row.Address }));

VelvetFonts.Register(families, defaultFamily: "sans");
```

With the `Bold` entry registered, `font-bold` renders a **true** bold asset rather than faux bold;
`font-medium` with no `Medium` entry picks the closest registered weight.

## Multilingual / CJK fallback

Velvet selects *which* family/weight asset to assign — it does **not** implement per-glyph
fallback. Configure that the standard TextCore way:

- **Local fallback**: add a fallback table on the Font Asset itself.
- **Global fallback**: assign a UITK **Text Settings** asset to the Panel Settings and list the
  fallback fonts there (local fallback takes priority over global).

A Latin family with a Japanese fallback asset therefore renders mixed text correctly while still
being selected by `font-sans`.

## Swapping fonts at runtime (e.g. per-locale)

Velvet is React-style, so the idiomatic switch is to change state and re-render: register the new
locale's families (via any source — `VelvetFonts.Register`, `config.Register()`, …) and bump the
state that drives your tree. `VelvetFonts.FontsChanged` fires on every registry mutation if you want
to hook it to a store/`UseState` setter to force that re-render. On re-render every element
re-resolves its font intent against the new registry.

## Tailwind text-utility parity vs. UI Toolkit

Verified against the Unity 6000.3 USS *common properties* reference. Velvet ships every text
utility UI Toolkit can express; the rest are omitted because UI Toolkit has **no USS property** for
them (not a Velvet decision).

**Supported (shipped):** `font-<family>` / `font-thin`…`font-black` / `italic` / `not-italic` /
`text-xs`…`text-4xl` (font-size) / `text-left|center|right|start|end` (`-unity-text-align`) /
`tracking-*` (`letter-spacing`) / `whitespace-normal|nowrap|pre|pre-wrap|pre-line` /
`text-wrap` / `text-nowrap` / `truncate` / `text-ellipsis` / `text-clip` (`text-overflow: ellipsis | clip`) /
text color / `uppercase` `lowercase` `capitalize` `normal-case` (text-transform) /
`underline` `line-through` `no-underline` (text-decoration).

**Text-transform / text-decoration are realised by mutating the displayed text** (UI Toolkit has no property
for either): the string is upper/lower/title-cased, and underline / line-through wrap it in the `<u>` / `<s>`
rich-text tags UITK renders (`enableRichText` is on by default). Both **inherit** like CSS — put the class on
an ancestor and the descendant text leaves pick it up (`StyleTextEffectResolver` walks ancestors). See it for
the one cascade-freshness caveat (an ancestor's class toggled without that ancestor re-rendering).

**`white-space`** has four native USS values, and `whitespace-normal`, `whitespace-nowrap`,
`whitespace-pre`, and `whitespace-pre-wrap` map directly onto them — no C# involved.
**`whitespace-pre-line`** is the exception:
there is no matching engine value, so it is realised the same way as text-transform / text-decoration — a
display-string rewrite (collapse runs of spaces/tabs to one space, keep newlines, and drop whitespace that
sits right at a line edge) plus an inline `white-space: pre-wrap` write on every text leaf whose resolved
whitespace axis is pre-line, so the preserved newlines still render as breaks and the text still wraps. The
write is per-leaf rather than written once on the class-bearing element: `Label`/`TextElement` carries its
own element-level `white-space` rule from the default theme/USS, and an element's own matching rule always
beats an INHERITED value in the cascade, so a write on an ancestor alone would never reach a descendant
Label. It inherits and cascades the same way text-transform / text-decoration do: an explicit
`whitespace-*` class always wins on the SAME element (a single-purpose, literal utility taking precedence
over a text-mutating one is the less surprising outcome), and — exactly like how `normal-case` /
`no-underline` stop an inherited transform / decoration — it also blocks a farther ancestor's
`whitespace-pre-line` from reaching that subtree at all, rather than merely leaving the collapse unapplied
on that one element.

**Not expressible in UI Toolkit (no USS property — intentionally absent):**

| Tailwind | Why omitted |
|---|---|
| `leading-*` (line-height) | USS has no `line-height` (`-unity-paragraph-spacing` is paragraph gap, not line-height) |
| `overline` (text-decoration) | UI Toolkit rich text has no overline tag (`<u>` / `<s>` only) |
| `antialiased` / `subpixel-antialiased` (font-smoothing) | Text renders as SDF alpha-blend only — no antialiasing-mode axis to switch |
| `font-stretch-*` | No variable-font / width-axis support |
| `tabular-nums` etc. (font-variant-numeric) | No OpenType feature-substitution slot in the runtime font pipeline |
| `text-balance` / `text-pretty` | Browser line-breaking heuristics (best-fit paragraph shaping); UI Toolkit's text generator has no such heuristic to opt into |

None of the font-smoothing / font-stretch / font-variant-numeric rows above are reproducible at runtime by
any class or `refCallback` — there is no engine hook to flip. Where the *look* matters (tabular figures in a
price list, a condensed headline face, …), register a purpose-built Font Asset for that face through the
same `font-family` mechanism (`VelvetFonts.Register`) and select it with `font-<name>`; that reaches the
visual result directly instead of trying to toggle a feature the text stack does not expose.

UI Toolkit *does* expose `word-spacing`, `-unity-paragraph-spacing`, and `-unity-text-outline-*`,
but none has a standard Tailwind utility, so Velvet leaves them to arbitrary inline styles via
`refCallback`.
