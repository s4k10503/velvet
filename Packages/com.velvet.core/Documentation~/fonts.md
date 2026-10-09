# Styling notes: Fonts (family, weight, italic)

Velvet's font utilities are Tailwind-inspired (`font-sans`, `font-bold`, `italic`, …) but they
run on Unity UI Toolkit's text stack (TextCore / SDF Font Assets). UI Toolkit exposes only two
font style properties:

| Property | Type | What it can express |
|---|---|---|
| `-unity-font-definition` | a Font Asset | which **family** (and, if you register them, which **weight/italic** asset) |
| `-unity-font-style` | `{ normal, bold, italic, bold-and-italic }` | a **binary** weight axis + italic, in one value |

That single binary `-unity-font-style` is why raw USS can express neither a 100–900 weight scale
nor a composed `font-bold italic`. Velvet closes the gap with `StyleFontResolver`, which resolves
family + weight + italic **together** and writes them as inline style, plus the `VelvetFonts`
registry that maps family names to Font Assets.

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

1. **Family asset found** → assign it via `-unity-font-definition`. The entry is chosen the way
   CSS's font matching chooses a face: by style first, so an italic request takes an entry that
   carries an italic face (and an upright request one that carries an upright face) at any weight
   before the requested weight in the other style; then by weight, in CSS's search order — an exact
   weight; for a request from 400 to 500, the lightest weight from the request up to 500, then the
   heaviest below the request, then the lightest above 500; under 400, the heaviest at or below the
   request, then the lightest above; over 500, the lightest at or above the request, then the
   heaviest below. An upright request over a family whose entries all hold only italic faces takes
   an italic face, as CSS does when no face of the requested style exists. Only the part the asset
   can't satisfy is synthesized through `-unity-font-style`: bold for a request of 600 or more on a
   face under 600, and italic when no italic face is available.
2. **No family / no asset** → the same synthesis over the default face: weight `>= 600` renders
   bold, below renders normal, combined with italic. This matches the USS fallback classes in
   `_typography.uss`, so output is sensible before any font is registered.

Inline style wins over the USS classes, so the resolver is the authoritative font layer.

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

VelvetFonts.DefaultFamily = "sans"; // see What DefaultFamily reaches, below
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
`font-medium` with no `Medium` entry picks the registered weight CSS's search order reaches first
(see [How resolution works](#how-resolution-works)).

### What `DefaultFamily` reaches

`DefaultFamily` supplies the family to an element that has a weight or italic class (`font-normal`,
`font-bold`, `italic`, …), no `font-<name>`, and no ancestor naming a family (below), and Velvet writes the resolved asset inline on that
element. Its descendants inherit that asset like any other. **A label with no font class, and no
ancestor carrying one, gets no font from Velvet**: it keeps whatever font its stylesheets, ancestors
and the panel's text settings give it.

To have every label under the mounted root start from the registered family, put a font class on that
root. Either kind works:

```csharp
VelvetFonts.Register(families, defaultFamily: "sans");
V.Mount(root, V.Div("font-sans", app));    // names the family
V.Mount(root, V.Div("font-normal", app));  // follows DefaultFamily
```

A label under that root inherits the root's asset unless the label, or an element between them, is
assigned another one, or a stylesheet sets a font on the label itself. Content a portal renders
(`V.Portal`, `V.WorldSpace`, see [portals.md](portals.md)) attaches under its target rather than
under this root, so the target, layer root or world-space panel root needs the class too.
`DefaultFamilyScopePanelTests` pins the scope and both kinds of root on a panel with no theme
stylesheet.

A weight or italic class on an element with no `font-<name>` of its own keeps the family it inherits,
as in CSS: `font-bold` inside a `font-serif` container selects the registered `Bold` entry of `serif`.
The family is the one named by the nearest ancestor carrying a `font-<name>` class that Velvet rendered;
an ancestor that has only a weight class names none. `DefaultFamily` applies when no such ancestor
exists. A root whose `font-<name>` class Velvet did not render (a portal target, layer root or
world-space panel root, or the root of another `V.Mount`) is not seen, so under one register the same
family as the default as well, or `font-bold` below it selects the default family's `Bold` entry.
`InheritedFontFamilyPanelTests` pins the inheritance.

Glyphs the assigned asset lacks still go through TextCore's fallback chain, which
[Multilingual / CJK fallback](#multilingual--cjk-fallback) covers.

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
locale's families and bump the state that drives your tree. `VelvetFonts.FontsChanged` fires on
every registry mutation if you want to hook it to a store/`UseState` setter to force that
re-render. On re-render every element re-resolves its font intent against the new registry.

## Tailwind text-utility parity vs. UI Toolkit

Verified against the Unity 6000.3 USS *common properties* reference. Velvet ships every text
utility UI Toolkit can express; the rest are omitted because UI Toolkit has **no USS property** for
them.

**Supported (shipped):** `font-<family>` / `font-thin`…`font-black` / `italic` / `not-italic` /
`text-xs`…`text-9xl` (font-size) / `text-left|center|right|start|end` (`-unity-text-align`) /
`tracking-*` (`letter-spacing`) / `leading-*` (`line-height`) /
`whitespace-normal|nowrap|pre|pre-wrap|pre-line` /
`text-wrap` / `text-nowrap` / `text-balance` / `text-pretty` / `truncate` / `text-ellipsis` / `text-clip`
(`text-overflow: ellipsis | clip`) / text color / `uppercase` `lowercase` `capitalize` `normal-case`
(text-transform) / `underline` `line-through` `overline` `no-underline` (text-decoration).

**Text-transform / text-decoration are realised by mutating the displayed text** (UI Toolkit has no
property for either): the string is upper/lower/title-cased, and underline / line-through wrap it in
the `<u>` / `<s>` rich-text tags UITK renders (`enableRichText` is on by default). Both **inherit**
like CSS — put the class on an ancestor and the descendant text leaves pick it up
(`StyleTextEffectResolver` walks ancestors); see it for the one cascade-freshness caveat (an
ancestor's class toggled without that ancestor re-rendering). If an element turns `enableRichText`
off, the `<u>` / `<s>` markup shows up as literal text in the label instead (`leading-*`'s
`<line-height=X>` tag, below, has the identical caveat).

**`overline` is the one decoration value that cannot be a string rewrite:** UI Toolkit's rich text
has no overline tag (only `<u>` / `<s>`), so `overline` **paints** a solid rule above the text via
`generateVisualContent` on the leaf `TextElement` (`TextOverlinePainter` / `TextOverlineBinding`) and
the string passes through unchanged.

The stroke color tracks `resolvedStyle.color`, its thickness is
~1/16th of the font size (floored at 1px, matching a typical browser ratio), and it follows
`-unity-text-align`'s vertical component for where the FIRST line sits — top-aligned under an
`upper-*` anchor, vertically centered under a `middle-*` anchor (the default for
`text-left|center|right|start|end`, and UI Toolkit's own unstyled default), bottom-aligned under a
`lower-*` anchor — then nudges down a small, documented fraction of the font size from that line's
own top edge as an approximation of CSS's ascent-line placement (UI Toolkit exposes no public,
synchronously-reachable ascent metric to place it exactly).

`overline` joins the SAME decoration axis as `underline` / `line-through` / `no-underline`, so
cascade, inheritance, and the `no-underline` reset all apply to it. The axis stays single-valued,
which is a deviation: CSS lets `text-decoration-line` combine lines (`underline overline` shows
both), while Velvet's decoration axis resolves exactly one value, last-token-wins, so
`underline overline` on one element renders only the overline. **v1 scope:** one rule, positioned
above the FIRST line only and sized to the text's natural single-line-equivalent width (clamped to
the content width) — per-line metrics of wrapped text are not publicly reachable in a way usable
synchronously from `generateVisualContent`, so a multi-line label shows the rule above its top line
only; per-line placement is left as future work.

**`white-space`** has four native USS values, and `whitespace-normal`, `whitespace-nowrap`,
`whitespace-pre`, and `whitespace-pre-wrap` map directly onto them — no C# involved.
**`whitespace-pre-line`** is the exception: there is no matching engine value, so it is realised the
same way as text-transform / text-decoration — a display-string rewrite (collapse runs of
spaces/tabs to one space, keep newlines, and drop whitespace that sits right at a line edge) plus an
inline `white-space: pre-wrap` write on every text leaf whose resolved whitespace axis is pre-line,
so the preserved newlines still render as breaks.

The write is per-leaf rather than once on the class-bearing element: `Label`/`TextElement` carries
its own element-level `white-space` rule from the default theme/USS, and an element's own matching
rule always beats an INHERITED value in the cascade, so a write on an ancestor alone would never
reach a descendant Label. It inherits and cascades the same way text-transform / text-decoration
do: an explicit `whitespace-*` or `truncate` class always wins on the SAME element, and — exactly like how
`normal-case` / `no-underline` stop an inherited transform / decoration — it also blocks a farther
ancestor's `whitespace-pre-line` from reaching that subtree at all, rather than merely leaving the
collapse unapplied on that one element.

**`leading-*` (line-height)** has no USS property either — `-unity-paragraph-spacing` only affects
explicit `\n` breaks, not the per-line advance a real line-height changes — so it is realised
through UI Toolkit's rich-text `<line-height=X>` tag instead. Both text engines (the standard
generator and the Advanced Text Generator) implement the tag natively, feeding it into the
line-advance math at every line-break site; a following `</line-height>` restores the natural
metric.

The named presets (`leading-none` 1 · `leading-tight` 1.25 · `leading-snug` 1.375 ·
`leading-normal` 1.5 · `leading-relaxed` 1.625 · `leading-loose` 2) emit their multiplier verbatim
as `<line-height=1.625em>…</line-height>`. The bracket form takes CSS `line-height`'s values: a
unitless number (`leading-[1.5]`), an `em` length (`leading-[1.5em]`), a percentage
(`leading-[150%]`), and `px`, `rem` (1rem = 16px, as `w-[…]` takes it) or an absolute unit (`in`, `cm`, `mm`, `pt`, `pc`, `Q`), which emit an absolute
`<line-height=Npx>`. A math function is read where it comes to one of those pixel lengths or one percentage
([styling-arbitrary-lengths.md](styling-arbitrary-lengths.md)). A negative value, any other unit, or a
malformed value is ignored.

A preset and a unitless value are numbers, as in CSS: they emit an em tag, which the **text engine
itself** resolves against whichever font size is in effect at that point in the string, so every
text under the class multiplies its own size. An `em` or percentage value is a length CSS computes on
the element that declares it, from that element's computed font size, and every text under it gets
that length in pixels: `V.Div("text-[20px] leading-[150%]", V.Label("text-[40px]", …))` emits
`<line-height=30px>`, whether the declaring element's size is inline, inherited or a `text-*` scale
class. Velvet reads the size UI Toolkit resolves for the declaring element
(`LeadingLengthProbe`, which the bundled stylesheet's `velvet-leading-length` rule arms) and
re-resolves the text whenever that size changes. Until the panel has resolved it — a tree that is
not yet on a panel — the text carries the em tag. `tracking-*` is the contrast: USS `letter-spacing` has no
`em` unit (see `_typography.uss`), so its em scale had to be **baked to px at Tailwind's 16px root
font** — `tracking-wide`'s 0.4px is only 0.025em at exactly 16px and drifts off-ratio at any other
size. `leading-*` inherits and cascades exactly like text-transform / text-decoration (a nearer
ancestor's `leading-*` overrides a farther one's). It has no reset utility, unlike the other three
axes: Tailwind defines no `leading-auto` below `leading-none`, and every preset — `leading-none`'s
multiplier of 1 included — is already a real value, so there is nothing to reset back to.

**`text-balance` and `text-pretty`** realise CSS `text-wrap: balance` and `text-wrap: pretty` by writing
line breaks into the text a leaf *displays*, the way text-transform rewrites it. A box the cascade
sizes keeps the width its classes and its parent give it, so `w-full text-balance` balances, as do a
`max-w-*`, padding and a border. Velvet keeps the text it was given and rebuilds the displayed string from it, so a state value, a
prop or an `onChange` payload carries no newline Velvet added; the text read off the element itself is
the displayed string, as it is under `uppercase`.

Both inherit, as CSS's `text-wrap-style` does. Put the class on a container and every text leaf under it
breaks its lines; a nearer `text-wrap` or `text-nowrap` resets it, and the nearest of the four
classes decides. A leaf that cannot wrap (`text-nowrap`, `whitespace-nowrap`, `truncate`) is left
alone.

Where the lines break is Chromium's score line breaker (`score_line_breaker.cc`): the cheapest division
by the squared slack of every line, with a penalty on breaking before the last word and a penalty per
line, measured with `TextElement.MeasureTextSize`. Its limits carry over: nothing is changed below four
words, a division with another number of lines than the greedy one is dropped, `text-balance` acts on
paragraphs of up to six lines and `text-pretty` on up to four, and `text-pretty` acts when the
last line is one word narrower than a third of the line (Chromium's other trigger, consecutive
hyphenated lines, has no counterpart here). Text between newlines is a paragraph. A line may break at
white space (a tab or any white space but a no-break space; the white space at a break is dropped), between
CJK characters (ideographs, kana, hangul) with the basic kinsoku rule that no line begins with a closing
mark, a stop or comma, a percent sign, an ellipsis, the prolonged sound mark or an iteration mark and none
ends with an opening bracket, after a hyphen-minus that follows a letter or digit and does not precede a
digit, after the hyphens U+2010, U+2012 and U+2013, and after a zero width space, which is a break
opportunity and not a space. `TextBreakOpportunities` holds the table for every consumer of line
breaking.

A leaf is broken once it has a width, and again whenever the width its text is laid out in, its font
(family, size, style or weight, spacing) or its text changes. Before its first layout, and until a layout
pass has run since it was attached, it shows the text unbroken. What was measured of a text is kept
across widths, so resizing re-runs only the choice of breaks.

A leaf that gets breaks is written `white-space: pre-wrap` over a string whose white space Velvet has
collapsed itself, as `white-space: normal` would, because the breaks it writes are newlines; text that
inherits a preserving white-space keeps its spaces and newlines, and the style then balances each
newline-separated paragraph. A leaf that gets no break, a text that fits one line included, keeps its
white-space and its text as written.

Deviations from CSS:

- **A box UI Toolkit sizes from its text can be narrower.** A leaf with auto width in a row is measured
  from the lines it displays, so it can end up narrower than the unbalanced text would make it. Such a
  leaf is broken against the room its parent offers, as its text takes that room unbroken, rather than
  against its own width, which is only its last longest line; a `max-width` bounds that width. The leaf
  is re-broken when its parent's room has moved by more than two pixels from where it last was, so a
  widening in smaller steps adds up. Only the leaf's direct parent is watched: a widening that reaches
  the leaf through a parent sized by the leaf itself is not seen.
- **Only part of UAX #14 is read**, where Chromium reads all of it through ICU. Quotation marks, the
  numeric and symbol rules, emoji and Indic sequences, CJK symbols other than the marks named above and
  the combining behaviour of the CJK classes are not, so a break one of those rules would allow beyond
  the list above is not taken, and one it would prohibit can be. A tab's width is an estimate that the
  engine's own layout of each line checks.
- **`text-balance` stops at six lines and `text-pretty` at four**, the limits Chromium applies.

**Wrapping:** `text-wrap`, `text-nowrap`, `text-balance` and `text-pretty` are CSS's `text-wrap`
shorthand, which sets the wrap mode (`wrap`, or `nowrap` for `text-nowrap`) and leaves the collapse
alone. So text that inherits `whitespace-pre`, `whitespace-pre-wrap` or `whitespace-pre-line` keeps
its spaces or newlines: it wraps as `pre-wrap`, or under `text-nowrap` stays on its lines as `pre`,
while other text collapses as `normal` or `nowrap`. A white-space class (`whitespace-*` or
`truncate`, whose `white-space: nowrap` resets the collapse as the CSS shorthand does) on the same
element or on one nearer the text keeps its own value. Velvet writes this per text leaf as an inline
`white-space`, the way it writes `whitespace-pre-line`. When several of the four meet on one element,
the later class wins, as they set the same CSS properties; the later of them decides which line
breaking the text gets.

**Not expressible in UI Toolkit (no USS property — intentionally absent):**

| Tailwind | Why omitted |
|---|---|
| `antialiased` / `subpixel-antialiased` (font-smoothing) | Text renders as SDF alpha-blend only — no antialiasing-mode axis to switch |
| `font-stretch-*` | No variable-font / width-axis support |
| `tabular-nums` etc. (font-variant-numeric) | No OpenType feature-substitution slot in the runtime font pipeline |

None of the font-smoothing / font-stretch / font-variant-numeric rows above are reproducible at
runtime by any class or `refCallback` — there is no engine hook to flip. Where the *look* matters
(tabular figures in a price list, a condensed headline face, …), register a purpose-built Font Asset
for that face through the same `font-family` mechanism (`VelvetFonts.Register`) and select it with
`font-<name>`.

UI Toolkit *does* expose `word-spacing`, `-unity-paragraph-spacing`, and `-unity-text-outline-*`,
but none has a standard Tailwind utility, so Velvet leaves them to arbitrary inline styles via
`refCallback`.
