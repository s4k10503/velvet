# Bracketed lengths and bare spacing steps

A bracketed value such as `w-[120px]`, `top-[1in]` or `translate-x-[25%]` is resolved by Velvet rather
than by a USS rule, because no stylesheet can declare every value a bracket can hold. This guide owns which
lengths a bracket reads, which utilities read which of them, and what each is measured against. The
[last section](#bare-spacing-steps) owns the bare number a spacing utility takes without a bracket, and the order of
a shorthand and a longhand of one spacing family ([below](#a-shorthand-and-its-longhand)).

## What every length bracket reads

| Written | Read as |
|---------|---------|
| `120px`, or a bare `0` | pixels; a bare number other than `0` is declined, as the browser declines the `width: 120` Tailwind writes for `w-[120]` |
| `1.5rem` | pixels, at 1rem = 16px: UI Toolkit has no root font size to take a rem from |
| `1in`, `2.54cm`, `25.4mm`, `72pt`, `6pc`, `101.6Q` | pixels, at CSS's fixed ratio of 96px to the inch |
| `25%` | a percentage, where the utility takes one; a border width takes none, as in CSS |
| `auto` | the keyword, where the utility takes one: margin (a negated one, `-mt-[auto]`, is declined), `w-*`, `h-*`, `size-*`, `min-w-*`, `min-h-*`, `basis-*` and the insets; padding and the maximum sizes take none |
| `calc()`, `min()`, `max()`, `clamp()` whose terms all come to pixels, or all to percentages | that pixel length or that percentage |

A unit is read in any case, as CSS reads it: `w-[10PX]` is `w-[10px]`. Where Tailwind reads a bracket as
either a length or a colour — `border-*`, `text-*`, `ring-*`, `outline-*`, `ring-offset-*` and `from-*`,
`via-*` and `to-*` — its type tests decide, and what they do not take for a length is written as a colour,
which the browser rejects unless it is one ([a rejected value](#a-rejected-value)):

- its length test takes a number with a unit in its own case, `Q` upper and the rest lower, or a math function
  named in lower case whatever case its units are in, so `border-[2PX]`, `text-[16PX]` and
  `border-[CALC(2px)]` are colours there, and `border-[calc(2PX)]` is a length;
- `border-*` also takes a bare number, which the browser keeps only as `0` (`border-[2]` is inert), and `thin`,
  `medium` and `thick`, at 1px, 3px and 5px;
- `text-*` also takes a percentage and the font-size keywords: `xx-small` 9px, `x-small` 10px, `small` 13px,
  `medium` 16px, `large` 18px, `x-large` 24px, `xx-large` 32px, `xxx-large` 48px, and `larger` and `smaller`,
  1.2 times the parent's size and the parent's divided by 1.2. A bare number, `text-[16]`, is a colour;
- `outline-*` also takes a number or a percentage, which it writes as `outline-width` beside a solid style: the
  browser keeps such a width only as `0`, and otherwise draws the outline at the width the other classes give
  it, 3px where none does, so `outline-[2]` is a 3px outline and `outline outline-[2]` a 1px one;
- `ring-*`, `ring-offset-*` and `from-*` / `via-*` / `to-*` take the length alone, so `ring-[2]` is a colour.

A type hint, a lower-case word and a colon at the start of a bracket (`[length:…]`, `[color:…]`), is read as
Tailwind reads it. A utility that writes its bracket through drops it, so `w-[length:10px]` is `w-[10px]` and
`rotate-[angle:45deg]` is `rotate-[45deg]`. One of those above takes the branch the hint names in place of its
tests — `length` or `line-width` for `border-*`; `length`, `percentage`, `absolute-size` or `relative-size` for
`text-*`; `length`, `number` or `percentage` for `outline-*`; `length` for `ring-*` and `ring-offset-*`;
`length` or `percentage` for `from-*` / `via-*` / `to-*` — and any other hint the colour, so
`text-[length:16PX]` is 16px, `bg-[color:#f00]/50` a half-transparent red, and `border-[color:2px]` inert.
`filter-[name:args]` is Velvet's custom-filter call, not a hint ([styling-filters.md](styling-filters.md)).

An angle (`rotate-[…]`, `skew-x-[…]`, `hue-rotate-[…]`, a gradient's) is read in `deg`, `grad`, `rad` or `turn`,
and a time (`duration-[…]`, `animate-spin-[…]` and the other loops) in `ms` or `s`, each in any case. A bare number
is read only where the CSS property Tailwind writes takes one: `0` in `skew-x-[…]`, `hue-rotate-[…]` and
`bg-linear-[…]`, and none in `rotate-[…]`, whose `rotate` property takes no bare `0`, or in a time.

A math function follows CSS's grammar with Tailwind's spelling. `_` stands for a space. CSS wants a space
on both sides of a binary `+` or `-`. Tailwind adds the spaces itself around an operator inside a function
it names in lower case, so `calc(2rem-4px)`, `calc(2rem_-_4px)` and `calc(2rem_-4px)` are the same length.
A function name is read in any case, but Tailwind leaves one in another case as written, where CSS's rule
holds: `CALC(1px_+_2px)` is 3px and `CALC(1px+2px)` is declined.
A sign belongs to a number (`-2px`, `+.5rem`, `-1e3px`), never to a parenthesis or a function, so `-(1px)`
and `-calc(1px)` are declined. A space at either end of a length is trimmed, as the browser trims it from the
declaration Tailwind writes (`top-[_calc(1px)]`, `w-[1px_]`), except where Tailwind reads the bracket as either a
length or a colour: outside a math function its length test takes no space, so `border-[2px_]` is inert. Functions nest and `(` groups; `*`
takes a plain number on one side, and `/` a plain number on its right. Up to 63 nested functions, and a run of
up to 63 terms, factors or arguments, are read; 64 is declined. The two count against one bound, so a run
inside nested functions is declined sooner. `clamp(low, value, high)` is `max(low, min(value, high))`, so a
`low` above `high` wins. What CSS rejects is declined and leaves the class inert: adding a number to a length
(`calc(1px+2)`), multiplying two lengths, dividing by a length, a result that is a plain number (`calc(4)`)
and `clamp()` without three arguments. Valid CSS that is not read here is declined too: any function other
than these four (`var()`, `env()`, `round()`, …), and, as a deliberate deviation, a division by zero and a
length too large for a float, both of which CSS reads by clamping an infinite result to the range of lengths
it supports.

So `top-[calc(1rem+4px)]`, `gap-[min(2rem,24px)]`, `blur-[calc(2px*3)]`,
`shadow-[calc(1rem+4px)_4px_8px_#101820]` and `w-[calc(100%/3)]` resolve as the plain length would.

### A rejected value

A value Tailwind writes and the browser rejects does what it does in the browser. A plain declaration is
dropped, so the class is inert: `border-[2PX]`, `text-[16]`, `w-[120]`. One Tailwind composes into the
element's box-shadow discards the whole box-shadow, the ring and every `shadow-*` with it, while a
`drop-shadow-*`, a filter, stays: a ring colour that is no colour (`ring ring-[5PX]`, `ring-2 ring-[2]`), a ring
width that is no length (`ring-[length:2]`), a ring offset colour that is no colour beside an offset width
(`ring-offset-2 ring-offset-[2]`), and a `shadow-[…]` holding a bare non-zero number (`shadow-[0_4_8_#000]`).
The ring colour and width are each the one Tailwind emits last, as the ring reads them. A `from-` / `via-` / `to-` colour
that is no colour (`from-[1IN]`) leaves its stop transparent, and a position that is no length
(`from-[length:red]`) returns the stop to its initial position: 0%, 50% or 100%.

Tailwind composes four more families through one declaration each: `translate-*`; `scale-x-*`, `scale-y-*` and
the uniform `scale-<n>` (a bracketed `scale-[…]` is written as `scale` alone); `skew-*`; and the filters, `blur-*`
to `sepia-*`, `hue-rotate-*` and `drop-shadow-*`. Each utility sets a custom property the shared declaration
reads, and a value the browser rejects leaves the family at its identity — no translation, a scale of 1, no
skew, no filter — wherever it is the value its property takes, as the browser discards the shared declaration
there: `translate-x-4 translate-y-[10]` translates nowhere, `blur-sm brightness-[abc]` filters nothing, and
`drop-shadow-lg blur-[2]` loses its drop shadow. A property takes the value of the class that wins it: an
important one over a plain one, then one behind a variant while the variant holds, then the later in
Tailwind's order. So a rejected value another class outranks takes nothing: `skew-x-[8] !skew-x-6` skews by
6°, `blur-[-2px] blur-sm` blurs, and `translate-y-[10] hover:translate-y-4` translates on hover alone, while
`translate-y-[10] hover:translate-x-4` never translates and `translate-x-4 hover:translate-y-[10]` translates
until hovered. A `translate-z-*` or `scale-z-*` class adds z to the declaration, so its rejected value takes
the family only where its declaration is the one that wins: `translate-x-4 translate-z-[10]` translates
nowhere, `!translate-x-4 translate-z-[10]` along x.
A `V.Motion` pose is read with the element's own `className` as one list, where a pose class outranks an own
class of the same importance and variants, as Framer Motion's `animate` overrides the element's style: over
`translate-y-[10]`, a `translate-y-4` pose translates and a `translate-x-4` pose does not, and a pose that
rejects a value holds the element's own family at its identity while the element rests at that pose. A family
the own `className` discards without a variant stays discarded under the stylesheet classes `whileHoverClass`
and the other class channels add.
A `rotate-[…]` and a `scale-[…]` the browser rejects take nothing with them.

## Lengths only an element can measure

| Written | Measured against |
|---------|------------------|
| `2em` | the element's computed font size; on `text-[…]`, its parent's |
| `50vw`, `50vh`, `50vmin`, `50vmax` | the panel — in an editor window, the window's content: its width, its height, the smaller and the larger of the two |
| `50svw`, `50lvh`, `50dvmin`, … | the same as the unprefixed unit: a panel has no browser chrome to show or hide, which is where CSS makes the small, large and dynamic viewports equal |
| a math function mixing a percentage with a length, or reading an `em` or a viewport unit | each term against its own basis, as CSS evaluates it |

These are read by the utilities that write a length-percentage inline:

- sizing: `w-`, `h-`, `min-w-`, `min-h-`, `max-w-`, `max-h-`, `size-`, `basis-`;
- position: `inset-`, `inset-x-`, `inset-y-`, `top-`, `right-`, `bottom-`, `left-`, `start-`, `end-`;
- spacing: the `p*-` and `m*-` utilities, the logical `ps-` / `pe-` / `ms-` / `me-` among them, and their
  negated forms (`-mt-[1em]`);
- type: `text-[…]` as a font size, and `tracking-[…]`.

A percentage inside one of them is taken where CSS takes it for the longhand written: `w-`, `min-w-`,
`max-w-`, `left-` and `right-` of the parent's width, and so is every padding and margin edge, `pt-` and
`mt-` included; `h-`, `min-h-`, `max-h-`, `top-` and `bottom-` of the parent's height; `basis-` of the
parent's size along its flex direction; `text-[…]` of the parent's font size; and `tracking-[…]` of the
element's own font size. The parent's size is its content box, and its padding box for an `absolute`
element, as CSS and UI Toolkit both take it. On an element with a `clip-path-*`, every longhand — padding included — is measured against the parent the clip wrapper
sits in, not the wrapper.

A percentage of a parent size the parent takes from its content is not measured, since the parent's size
would then follow the length measured from it. The parent's size on an axis is definite when the parent
declares a length for it, or a percentage of a size that is itself definite (`h-full` under a parent of
declared height); when, `absolute`, it declares a percentage, or is pinned by an inset on both sides
(`inset-0`); or when, in flow, it is stretched across a parent's cross axis, or grown along a parent's main
axis, while that parent's size is definite. A parent that wraps (`flex-wrap`) still counts as stretched: UI
Toolkit takes its own percentage of the stretched size there, where CSS would take none. `auto` (`h-auto`) declares no size, and
only inline values and the bundled utility classes count as declarations: a size another stylesheet gives the
parent reads as none. A variant (`hover:h-auto`) counts while it is on.

Against an indefinite size, `w-`, `h-`, `top-`, `basis-` and the other position and size longhands are written
as `auto`, and `max-w-` / `max-h-` as `none`. A minimum, a padding and a margin take the percentage as zero
and keep the rest of the length, so `pt-[calc(5%+8px)]` is 8px. On a height this is CSS's own rule. On the
width axis CSS goes on to take the percentage of the parent's final width once that is known, which Velvet
does not. An `absolute` element's percentages are always taken.

### When they change

The length is written in pixels once the element is on a panel and has been laid out; until then the longhand
keeps what the cascade gives it. It is measured again on a 16 ms tick of the panel's scheduler after any of
these moves:

- whether the element has been laid out, its own font size, and its `position`;
- the parent's font size, size, border widths, padding and flex direction;
- whether the parent's width and height are definite (above);
- the panel's size.

Unlike CSS, where the value is part of layout, the new value lands a tick after the change. A re-measure is
written with the transitions of the element and of its clip wrapper held at zero duration and delay, so it
lands at once rather than animating.

### Where they are not read

- Font-relative units other than `em` (`ex`, `ch`, `lh`, `rlh`, `cap`, `ic`) are declined everywhere.
- Elsewhere a length only an element can measure is declined and leaves the class inert: border widths,
  `rounded-*`, `translate-*`, `origin-[…]`, `blur-*`, `shadow-*`, `gap-*` / `space-*` / `divide-*` /
  `ring-*`, `clip-path-[…]` and gradient positions among them. A math function those read has to come to
  pixels or to a percentage. `leading-[…]` reads `em` and percentages itself ([fonts.md](fonts.md)), but no
  viewport unit, and no math function reading `em` or mixing a percentage with a length.
- A gradient stop list, a shadow and a `clip-path-[…]` split their bracket into words on `_`, so a math
  function inside one is written without spaces: `bg-linear-[to_right,#000000_0px,#ffffff_calc(1rem+4px)]`.
- A spring or bezier `V.Motion` does not interpolate a length only an element can measure: the class still
  applies, uninterpolated, as `w-auto` does.

## Bare spacing steps

A spacing utility takes a bare number as Tailwind v4 does: any non-negative multiple of 0.25 written in
canonical decimal form is that many 4px steps, so `p-13` is 52px, `gap-4.5` 18px and `w-0.25` 1px. `.5`,
`4.50`, `05` and `4.1` are not steps and leave the class unresolved. The utilities are padding, margin
(`m-*`, `mx-*`, `ps-*`, …), `gap-*`, `space-x-*` / `space-y-*`, `w-*`, `h-*`, `size-*`, `min-*` / `max-*`
width and height, `basis-*`, `top-*` … `inset-*` and the logical insets, `translate-x-*` / `translate-y-*`,
and `leading-*` (an absolute line height; a named leading such as `leading-tight` is read first). `auto` is not a step: it is
read as the table above says. A minus sign negates the margin, inset, translate and `space-*` utilities (`-mt-13`, `-top-4.5`), as in Tailwind;
padding, sizing, `basis-*` and `gap-*` take none.

What a step becomes depends on the utility. Padding, margin, sizing, `basis-*`, the insets and `translate-*`
write an inline style, as a bracket value does, so they take part in variants, the `!` modifier and the
cascade as `p-[52px]` does, and never enter the class list. `gap-*` and `space-*` are applied by the gap
manipulator as inter-child margins ([styling-flexbox-and-gap.md](styling-flexbox-and-gap.md)), and
`leading-*` becomes a line-height tag in the text ([fonts.md](fonts.md)).

The steps the bundled stylesheets declare — `0`, `px`, `0-5` … `3-5`, `4` … `12` and the larger ones up to
`96`, written with `-` for `.` — stay USS classes in the box utilities above. `p-1.5` is therefore the inline
twin of the `p-1-5` class, both 6px. A stylesheet of your own that declares a class named like a step it does
not (`.w-100`, `.p-30`) is shadowed by the inline value.

### A shorthand and its longhand

Where a shorthand and a longhand of one family meet in one `className`, the longhand holds its side whichever
comes first, as in Tailwind, which emits `padding` ahead of `padding-top`: `p-13 pt-2` and `pt-2 p-13` both
leave the top at 8px and the other sides at 52px. The order is Tailwind's property order: the all-sides
shorthand, then the axis (`px-*` / `py-*`, `inset-x-*` / `inset-y-*`), then a single side, and `size-*` ahead
of `w-*` and `h-*`. It covers padding, margin, the insets, `size-*` / `w-*` / `h-*` and the border radii, at one
priority: a variant or `!` outranks the plain utility as it does anywhere, and a bracket value takes part the same
way (`p-[52px] pt-2`). Variants are compared as a set, so `hover:focus:` and `focus:hover:` are one group.

The radii run `rounded-*`, then the logical sides and corners (`rounded-s-*`, `rounded-ss-*`, `rounded-e-*`,
`rounded-se-*`, `rounded-ee-*`, `rounded-es-*`), then the physical ones: `rounded-t-*`, `rounded-l-*`,
`rounded-tl-*`, `rounded-r-*`, `rounded-tr-*`, `rounded-b-*`, `rounded-br-*`, `rounded-bl-*`. Of two that write
different properties and share a corner the later holds it whichever comes first, so `rounded-t-[2px] rounded-r-[8px]` and
`rounded-r-[8px] rounded-t-[2px]` both give the top-right corner 8px, `rounded-l-sm rounded-b-lg` and
`rounded-b-lg rounded-l-sm` both give the bottom-left 8px, and a logical radius gives way to the physical one over
its corner. A stylesheet radius class that Tailwind emits after an inline one it overlaps is written inline, as
below: `rounded-r-lg rounded-se-[2px]` leaves the top-right at 8px. Two utilities of one family
(`rounded-md rounded-lg`) write one property, and the stylesheet declares them in Tailwind's order: both
spellings give `rounded-md`. In a `V.Motion` pose, a base radius
the pose partly overlaps keeps only the corners the pose leaves: `rounded-l-[4px]` under a `rounded-t-[16px]` pose
is `rounded-bl-[4px]`.

An inline value outranks every class on its element, so a stylesheet class that is the narrower of such a pair
is written inline too when an inline utility of an overlapping family stands beside it: `pt-2` becomes
`pt-[8px]`, `mx-auto` becomes `mx-[auto]` (the `auto` bracket in the [table above](#what-every-length-bracket-reads)), `w-full`
becomes `w-[100%]` and `rounded-t-lg` becomes `rounded-t-[8px]` (`rounded-t-full` becomes `rounded-t-[9999px]`). Two things follow. The rewritten class is no longer in the element's class list, so a
selector on it (`.pt-2 > .x`) and `ClassListContains("pt-2")` stop seeing it, though the unrewritten classes of
the list are unchanged. And the rewrite writes the pixel value of the step as Velvet's scale gives it, so a
stylesheet that redefines `--space-2` moves `pt-2` and leaves `pt-[8px]` where it was (`--radius-lg` and
`rounded-t-[8px]` likewise), the same limit as the logical utilities have ([react-migration.md](react-migration.md)).

The rule is made where a shorthand and a longhand meet in one `className` string; these differ:
- A logical utility (`ps-*`, `ms-*`, `start-*`, the block forms) beside the physical utility of its own side
  gives way to it, since Tailwind sorts `padding-inline-start` ahead of `padding-left`: `ps-13 pl-2` and
  `pl-2 ps-13` leave the left at 8px, and the logical one is dropped from the list and the physical class
  written inline; of several physical classes on that side, the one the stylesheet lets win is the one written
  inline. Behind a variant the rule keys already give the physical one the side.
- A `V.Motion` pose overrides the node's own spacing, as Framer Motion's `animate` overrides `style`: a base
  `pt-[8px]` beside a pose `p-[52px]` leaves the top at 52px, the base token being left out while that pose
  applies. A base class beside an inline pose needs no such step, the inline value outranking it already; a pose class that is the narrower of a pair with an inline base token (`p-13` base, `pt-2` pose) is written inline like any other, so it holds its side and the base keeps the rest.
- A class list assembled outside a `className` string, or beside a class list of another element, is not ordered this way.

`z-<n>` takes any whole number an int holds the same way (`z-15`, `-z-15`); see [styling-z-index.md](styling-z-index.md).

### An inline value and a stylesheet class

Of two base utilities that write one longhand, one resolved inline (a bracket value, a fraction, `auto`, a negated
step, a colour with an opacity, a logical side) and one a stylesheet class, the one Tailwind emits later holds it,
whichever is written first. Tailwind sorts the candidate names by character code with digits compared as numbers, and
a rule with more declarations ahead of one with fewer, so `w-px w-[10px]` and `w-[10px] w-px` both give 1px,
`w-1/2 w-4` gives 16px, and `mt-[10px] mt-auto`, `-mt-2 mt-1`, `bg-[#ff0000] bg-red-500`, `border-s-2 border-l-4` and
`text-lg/7 text-sm` leave the class the property. Where the inline token is the later one it keeps the property:
`w-4 w-[10px]` gives 10px, and `scale-100 -scale-[0.5]` gives the inline scale, since `scale-100` declares three
variables beside `scale` and so sorts ahead.

An inline token that the classes Tailwind emits after it write in full is left out of the class list, so it writes no
style; taking the class away brings it back. One they write only in part keeps the rest: for the spacing, size, inset
and radius families the class is written inline (above), and a border width is cut down to the sides the classes leave,
so `border-[3px] border-l-4` and `border-l-4 border-[3px]` give the left side 4px and the other three 3px. A few
bundled classes carry Velvet's own name for a Tailwind utility and sort by that utility: `rotate-n6` as `-rotate-6`,
and `duration-fast`, `duration-normal` and `duration-slow` as `duration-100`, `duration-150` and `duration-200`. The sheets declare them there too, so `rotate-6 rotate-n6` gives 6 degrees.

This orders one inline token against the classes beside it. Only plain base tokens take part: a variant, an important
token and a class gated on a state are ranked by their own layer. Where a class is written inline beside a second
inline token of the same property (`size-[10px] w-[20px] w-4`), the two inline writers of that property go to the last
one written, which this section does not change.
