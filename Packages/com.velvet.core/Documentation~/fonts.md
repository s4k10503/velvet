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
`tracking-*` (`letter-spacing`) / `leading-*` (`line-height`) /
`whitespace-normal|nowrap|pre|pre-wrap|pre-line` /
`text-wrap` / `text-nowrap` / `text-balance` / `truncate` / `text-ellipsis` / `text-clip`
(`text-overflow: ellipsis | clip`) / text color / `uppercase` `lowercase` `capitalize` `normal-case`
(text-transform) / `underline` `line-through` `overline` `no-underline` (text-decoration).

**Text-transform / text-decoration are realised by mutating the displayed text** (UI Toolkit has no property
for either): the string is upper/lower/title-cased, and underline / line-through wrap it in the `<u>` / `<s>`
rich-text tags UITK renders (`enableRichText` is on by default). Both **inherit** like CSS — put the class on
an ancestor and the descendant text leaves pick it up (`StyleTextEffectResolver` walks ancestors). See it for
the one cascade-freshness caveat (an ancestor's class toggled without that ancestor re-rendering). If an
element turns `enableRichText` off, the `<u>` / `<s>` markup is not interpreted — it shows up as literal text
in the label instead, like any other rich-text tag on that element (`leading-*`'s `<line-height=X>` tag,
below, has the identical caveat).

**`overline` is the one decoration value that cannot be a string rewrite:** UI Toolkit's rich text has no
overline tag (only `<u>` / `<s>`), so instead of wrapping the string, `overline` **paints** a solid rule above
the text via `generateVisualContent` on the leaf `TextElement` (`TextOverlinePainter` / `TextOverlineBinding`)
— the string itself passes through unchanged. The stroke color tracks `resolvedStyle.color`, its thickness is
~1/16th of the font size (floored at 1px, matching a typical browser ratio), and it follows `-unity-text-align`'s
vertical component for where the FIRST line sits — top-aligned under an `upper-*` anchor, vertically centered
under a `middle-*` anchor (the default for `text-left|center|right|start|end`, and UI Toolkit's own unstyled
default), bottom-aligned under a `lower-*` anchor — then nudges down a small, documented fraction of the font
size from that line's own top edge as an approximation of CSS's ascent-line placement (UI Toolkit exposes no
public, synchronously-reachable ascent metric to place it exactly). It still joins the
SAME decoration axis as `underline` / `line-through` / `no-underline` — cascade, inheritance, and the
`no-underline` reset all apply to it for free — but the axis stays single-valued by this subsystem's
pre-existing design: CSS lets `text-decoration-line` combine multiple lines (`underline overline` shows
both), Velvet's decoration axis resolves exactly one value, last-token-wins, so `underline overline` on one
element renders only the overline, not both. **v1 scope:** one rule, positioned above the FIRST line only and
sized to the text's natural single-line-equivalent width (clamped to the content width) — per-line metrics of
wrapped text are not publicly reachable in a way usable synchronously from `generateVisualContent`, so a
multi-line label shows the rule above its top line only; a documented limitation, with per-line placement
left as future work.

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

**`leading-*` (line-height)** has no USS property either — `-unity-paragraph-spacing` only affects explicit
`\n` breaks, not the per-line advance a real line-height changes — so it is realised through UI Toolkit's
rich-text `<line-height=X>` tag instead. Both text engines (the standard generator and the Advanced Text
Generator) implement the tag natively, feeding it into the line-advance math at every line-break site; a
following `</line-height>` restores the natural metric. The named presets (`leading-none` 1 · `leading-tight`
1.25 · `leading-snug` 1.375 · `leading-normal` 1.5 · `leading-relaxed` 1.625 · `leading-loose` 2) emit their
multiplier verbatim as `<line-height=1.625em>…</line-height>`; `leading-[Npx]` emits an absolute
`<line-height=Npx>` — only the `px` unit is accepted inside the bracket for now, any other unit (or a
malformed value) is silently ignored, like a malformed value on any other bracketed utility.
Unlike `tracking-*`, whose em scale had to be **baked to px at Tailwind's 16px root font** (USS
`letter-spacing` has no `em` unit — see `_typography.uss` — so `tracking-wide`'s 0.4px is only really 0.025em
at exactly 16px, drifting off-ratio at any other size), `leading-*`'s em form is resolved by the **text engine
itself** against whichever font size is actually in effect at that point in the string. It composes correctly
with any `text-*` size — or one inherited from further up the tree — at any value, with no lookup table to
keep in sync. `leading-*` inherits and cascades exactly like text-transform / text-decoration (a nearer
ancestor's `leading-*` overrides a farther one's). It has no reset utility, unlike the other three axes:
Tailwind defines no `leading-auto` below `leading-none`, and every preset — `leading-none`'s multiplier of 1
included — is already a real value, so there is nothing for a descendant to reset back to the way
`normal-case` / `no-underline` / an explicit `whitespace-*` class do.

**`text-balance`** approximates CSS `text-wrap: balance` even though UI Toolkit's text engine exposes no
line-break hook at all (there is no way to influence *where* a line wraps). `StyleTextBalanceManipulator`
sidesteps that by not touching line breaks: it binary-searches the public
`TextElement.MeasureTextSize(text, width, widthMode, height, heightMode)` — the same method the engine's
own autosize pass already calls — for the **narrowest inline `maxWidth`** that keeps the measured height
at or under the height a normal (unbalanced) layout would take at the element's available width. Since
font metrics are constant across candidates, comparing heights stands in for comparing line counts: a
width narrow enough to add a line always measures taller and is rejected. The result reads more evenly
than a near-empty last line, the same visual goal as the real CSS feature.

Two deviations from CSS, both documented on the manipulator itself and worth knowing before reaching for
this class:

- **The box can shrink.** Real `text-wrap: balance` never resizes the element — only where its lines
  break. This approximation instead narrows the box via `maxWidth`, so anything sized from that box (a
  background, alignment relative to it) reads against a smaller box than an unbalanced sibling would
  have. Applied only when the text actually wraps to 2+ lines (see below), so a single-line label's box
  is never touched.
- **`maxWidth` is measured against the PARENT's content width**, not the element's own — reading the
  element's own already-narrowed width back would ratchet it narrower every pass instead of converging.
  This is exact when the element is its parent's sole / stretch-to-fill child (the common
  paragraph/heading usage) and an over-estimate when siblings share the row or the element carries its
  own narrower `max-w-*`. An over-estimate only makes balancing less aggressive (it never widens the box
  past what normal layout already gives it), so the deviation is a conservative under-balance, never a
  wrong one. `text-balance` otherwise owns the element's inline `maxWidth` outright while its class is
  present — a co-present `max-w-*` utility on the same element is overwritten, the same ownership rule
  `grid-cols-*` applies to a column's width — and that ownership is enforced every patch (not just once at
  attach), so a `max-w-*` value that changes in the same render as a still-present `text-balance` is
  re-overwritten before the render ends. Removing the `text-balance` class instead RESTORES a co-present
  `max-w-*` utility's own value rather than leaving `maxWidth` cleared.

**Prerequisite — needs a wrapping white-space too:** Velvet's `Label` ships with no bundled base
white-space rule, so its engine default is `nowrap`. `text-balance` alone is therefore a **silent no-op**
on a default `Label` — pair it with `text-wrap` / `whitespace-normal` (or another wrapping white-space)
for it to have any effect.

**Single-line gate:** CSS balance is a no-op on one line, and since this approximation instead shrinks the
box, applying it to single-line text would shrink that box for no CSS-parity benefit. The manipulator
only writes a narrower `maxWidth` when the natural height at the available width exceeds the text's
unconstrained single-line height (2+ lines); otherwise any previously-applied `maxWidth` is cleared. An
element with `white-space: nowrap` reaches the same "single line" outcome through this same comparison
(`MeasureTextSize` already measures against the element's own resolved white-space), so no separate check
is needed for it — this is also why the prerequisite above is silent rather than a thrown error: nowrap
text always measures as "single line" and the gate above clears (or never writes) a `maxWidth` for it.

**Staleness:** the manipulator re-derives on attach; on its own `GeometryChangedEvent`; on a listener on
the PARENT's `GeometryChangedEvent` (needed because the manipulator's own `maxWidth` write pins the
element's resolved size, so an ancestor WIDENING never changes the element's own rect and would otherwise
never re-fire the search — listening on the parent directly, the same element the available width is read
from, closes that gap); and on the `ChangeEvent<string>` UI Toolkit raises whenever `.text` is reassigned
on a live element (covers a text swap that happens to keep the same wrapped box size, and therefore raises
no geometry event, from going stale). What's left uncovered: a resize confined entirely to a `ScrollView`
parent's own content viewport with no accompanying change to the `ScrollView`'s own outer rect (e.g. a
scrollbar toggling) — a narrower edge case than the ancestor-resize gap the parent listener closes.

**Not expressible in UI Toolkit (no USS property — intentionally absent):**

| Tailwind | Why omitted |
|---|---|
| `antialiased` / `subpixel-antialiased` (font-smoothing) | Text renders as SDF alpha-blend only — no antialiasing-mode axis to switch |
| `font-stretch-*` | No variable-font / width-axis support |
| `tabular-nums` etc. (font-variant-numeric) | No OpenType feature-substitution slot in the runtime font pipeline |
| `text-pretty` | Also a browser line-breaking heuristic, and a distinct one from `balance` (avoids orphans without redistributing every line) — not aliased to `text-balance` since the two have different goals; unsupported for now |

None of the font-smoothing / font-stretch / font-variant-numeric rows above are reproducible at runtime by
any class or `refCallback` — there is no engine hook to flip. Where the *look* matters (tabular figures in a
price list, a condensed headline face, …), register a purpose-built Font Asset for that face through the
same `font-family` mechanism (`VelvetFonts.Register`) and select it with `font-<name>`; that reaches the
visual result directly instead of trying to toggle a feature the text stack does not expose.

UI Toolkit *does* expose `word-spacing`, `-unity-paragraph-spacing`, and `-unity-text-outline-*`,
but none has a standard Tailwind utility, so Velvet leaves them to arbitrary inline styles via
`refCallback`.
