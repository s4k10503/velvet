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
| a math function ([below](#math-functions)) whose terms all come to pixels, or all to percentages | that pixel length or that percentage |

A bracketed border width (`border-[…]`, `border-t-[…]`, `divide-x-[…]`, …) is snapped as the browser snaps one,
down to a whole number of the panel's device pixels and up to one where it is less: `border-[2.75px]` is 2.5px at
two device pixels a point. The bundled classes' widths are whole pixels, which a whole-number scale leaves as
they are.

A unit is read in any case, as CSS reads it: `w-[10PX]` is `w-[10px]`. Where Tailwind reads a bracket as
either a length or a colour — `border-*`, `text-*`, `ring-*`, `outline-*`, `ring-offset-*` and `from-*`,
`via-*` and `to-*` — its type tests decide, and what they do not take for a length is written as a colour,
which the browser rejects unless it is one ([a rejected value](#a-rejected-value)):

- its length test takes a number with a unit in its own case, `Q` upper and the rest lower, or a math function
  named in lower case whatever case its units are in, so `border-[2PX]`, `text-[16PX]` and
  `border-[cALC(2px)]` are colours there, and `border-[calc(2PX)]` is a length;
- `border-*` also takes a bare number, which the browser keeps only as `0` (`border-[2]` is inert), and `thin`,
  `medium` and `thick`, at 1px, 3px and 5px;
- `text-*` also takes a percentage and the font-size keywords: `xx-small` 9px, `x-small` 10px, `small` 13px,
  `medium` 16px, `large` 18px, `x-large` 24px, `xx-large` 32px, `xxx-large` 48px, and `larger` and `smaller`,
  1.2 times the parent's size and the parent's divided by 1.2. A bare number, `text-[16]`, is a colour;
- `outline-*` also takes a number or a percentage, which it writes as `outline-width` beside a solid style: the
  browser keeps such a width only as `0`, and otherwise draws the outline at the width the other classes give
  it, 3px where none does, so `outline-[2]` is a 3px outline and `outline outline-[2]` a 1px one;
- `ring-*`, `ring-offset-*` and `from-*` / `via-*` / `to-*` take the length alone, so `ring-[2]` is a colour.

A type hint, a lower-case word and a colon at the start of a bracket (`[length:…]`, `[color:…]`), is read by the
utilities this page covers, by `ring-*`, `outline-*`, `gap-*`, `divide-*`, `shadow-*`, `skew-*`, `z-*`, the leading a
`text-*` takes after its slash, gradient stops and the border colours. A utility that writes its bracket through drops it, so
`w-[length:10px]` is `w-[10px]` and `rotate-[angle:45deg]` is `rotate-[45deg]`; an empty hint (`w-[:10px]`) is none.
One of those above takes the branch the hint names in place of its tests — `length` or `line-width` for `border-*`;
`size`, `length`, `percentage`, `absolute-size` or `relative-size` for `text-*`; `length`, `number` or `percentage`
for `outline-*`; `length` for `ring-*` and `ring-offset-*`; `length` or `percentage` for `from-*` / `via-*` /
`to-*` — and any other hint the colour, so `text-[length:16PX]` and `text-[size:16px]` are 16px,
`bg-[color:#f00]/50` a half-transparent red, and `border-[color:2px]` inert. `drop-shadow-*` and `font-[…]` read
their bracket as written and take no hint. Two colon forms are Velvet's own and are no hint: `filter-[name:args]`,
the custom-filter call ([styling-filters.md](styling-filters.md)), and `bg-[addr:key]`, the addressable background
image.

Swapping one bracketed value for another that writes the same property (`w-[300px]` for `w-[10px]`, with or
without `!`) leaves the old inline value in place until the new one is written, so a `transition-*` or
`duration-*` on the element runs between the two.

An angle (`rotate-[…]`, `skew-[…]`, `skew-x-[…]`, `hue-rotate-[…]`, a gradient's) is read in `deg`, `grad`, `rad` or `turn`,
and a time (`duration-[…]`, `animate-spin-[…]` and the other loops) in `ms` or `s`, each in any case. A bare number
is read only where the CSS property Tailwind writes takes one: `0` in `skew-[…]`, `skew-x-[…]`, `hue-rotate-[…]` and
`bg-linear-[…]`, and none in `rotate-[…]`, whose `rotate` property takes no bare `0`, or in a time.

### Math functions

A math function follows CSS's grammar with Tailwind's spelling. `_` stands for a space. CSS wants a space
on both sides of a binary `+` or `-`. Tailwind adds the spaces itself around an operator inside a function
it names in lower case, so `calc(2rem-4px)`, `calc(2rem_-_4px)` and `calc(2rem_-4px)` are the same length.
A function name is read in any case, but Tailwind leaves one in another case as written, where CSS's rule
holds: `cALC(1px_+_2px)` is 3px and `cALC(1px+2px)` is declined.
A sign belongs to a number (`-2px`, `+.5rem`, `-1e3px`), never to a parenthesis or a function, so `-(1px)`
and `-calc(1px)` are declined. A space at either end of a length is trimmed, as the browser trims it from the
declaration Tailwind writes (`top-[_calc(1px)]`, `w-[1px_]`), except where Tailwind reads the bracket as either a
length or a colour: outside a math function its length test takes no space, so `border-[2px_]` is inert. Functions nest and `(` groups; `*`
multiplies and `/` divides any two terms, and the result carries the powers of their units, as CSS Values 4
types them: `calc(100px/2px*1px)` is 50px and `calc(1s/1ms*1px)` 1000px. Up to 63 nested functions, and a run
of up to 63 terms, factors or arguments, are read; 64 is declined. The two count against one bound, so a run
inside nested functions is declined sooner.

The functions are CSS Values 4's: `calc()`, `min()`, `max()`, `clamp()`, `round()`, `mod()`, `rem()`, `abs()`,
`sign()`, `hypot()`, `sin()`, `cos()`, `tan()`, `asin()`, `acos()`, `atan()`, `atan2()`, `pow()`, `sqrt()`,
`log()` and `exp()`, with the constants `e`, `pi`, `infinity`, `-infinity` and `NaN`, each in any case.
`clamp(low, value, high)` is `max(low, min(value, high))`, so a `low` above `high` wins. `round()` takes an
optional `nearest`, `up`, `down` or `to-zero` first, and leaves out its step only for a number, where the step
is 1; a tie rounds up. `mod()` takes the sign of its divisor and `rem()` of its dividend. A trigonometric
function takes an angle (`deg`, `grad`, `rad`, `turn`) or a number of radians, and is exact on a multiple of a
quarter turn, so `tan(90deg)` is infinite. A time (`s`, `ms`) takes part in the arithmetic as an angle does.

Inside a calculation a division by zero, an overflow and an undefined result follow IEEE arithmetic, so
`min(1px/0,5px)` is 5px. At the top a NaN is 0, and an infinity, or a value past a float's range, is the
largest float of its sign, the range a UI Toolkit length holds: `w-[calc(1px/0)]` is 3.4028235e38px. A result
below what its property takes is raised to it, as CSS clamps a calculation: `w-[calc(-5px)]`, `p-[…]`,
`rounded-[…]`, `border-[…]`, `text-[…]`, `basis-[…]`, `blur-[…]`, `duration-[…]`, `grow-[…]` and the filter
amounts stop at 0, and `opacity-[…]` at 0 and 1.

The same functions are read where a utility takes an angle (`rotate-[calc(45deg*2)]`, `skew-x-[…]`,
`hue-rotate-[…]`, a gradient's angle), a number (`grow-[…]`), a number or a percentage (`scale-[…]`,
`opacity-[…]`, the filter amounts, each of which takes a percentage literal too: `scale-[150%]` is 1.5) or a time
(`duration-[…]`), coming to that type. What CSS rejects is declined and leaves the class inert:
adding a number to a length (`calc(1px+2)`), a result of the wrong type (`calc(4)` or `calc(1px*2px)` for a
length, `rotate-[calc(45)]`), a function given a type it does not take (`sin(1px)`, `mod(10px,3)`), `round()`
leaving out a length's step, `clamp()` without three arguments, and any function other than these
(`env()`, …). A `var()` is replaced by the custom property it reads before the bracket is read
([styling-custom-properties.md](styling-custom-properties.md)).

So `top-[calc(1rem+4px)]`, `gap-[min(2rem,24px)]`, `blur-[calc(2px*3)]`,
`shadow-[calc(1rem+4px)_4px_8px_#101820]` and `w-[calc(100%/3)]` resolve as the plain length would.

### A rejected value

A value Tailwind writes and the browser rejects does what it does in the browser. A plain declaration is dropped,
so the class is inert: `border-[2PX]`, `text-[16]`, `w-[120]`, and a negative literal where the property takes
none, as `w-[-5px]`, `p-[-5px]`, `rounded-[-2px]`, `border-[-2px]`, `text-[-4px]` and `basis-[-4px]` are (a margin,
an inset and `tracking-[…]` take one). A literal past a float's range is the largest float of its sign, as the
browser reads it (`w-[1e39px]`, `rotate-[1e39deg]`), and `opacity-[…]` takes any number, clamped to 0..1. One Tailwind composes into the element's box-shadow
discards the whole box-shadow, the ring and every `shadow-*`, `inset-shadow-*` and `inset-ring-*` with it, while a `drop-shadow-*`, a filter, stays
(`ring ring-[5PX] drop-shadow-lg` keeps its drop shadow): a ring or inset ring colour that is no colour
(`ring ring-[5PX]`, `ring-2 ring-[2]`, `inset-ring inset-ring-[2]`), a ring width that is no length
(`ring-[length:2]`, and `ring-[length:0]` and `ring-[length:50%]`, which Tailwind adds to the offset width in
`calc()`), an inset ring width that is no length (`inset-ring-[length:2]`; `inset-ring-[length:0]` is one), a ring
offset colour that is no colour beside an offset width (`ring-offset-2 ring-offset-[2]`), a `shadow-[…]` or
`inset-shadow-[…]` that is no box-shadow (`shadow-[0_4_8_#000]`, `shadow-[4px]`,
`inset-shadow-[inset_0_2px_4px_red]`, which Tailwind writes behind a second `inset`), and a shadow colour that is
no colour where the shadow reads it (`shadow-md shadow-[#ggg]`, `inset-shadow-sm inset-shadow-[#ggg]`; beside
`shadow-none`, or no shadow, it discards nothing). A `shadow-[…]` Tailwind types as a colour
(`shadow-[#f00]`, `shadow-[color:red]`) writes the shadow colour alone, so it does not stand in for a rejected
`shadow-[…]` before it. Each of these is the value of the class Tailwind emits last among those writing it, in
whatever order they are written, and a variant's value holds while its variant applies
(`ring ring-red-500 hover:ring-[5PX]` draws no ring while hovered): `ring-2 ring-red-500 ring-[5PX]` draws a red
ring, Tailwind emitting `ring-[5PX]` first, while `ring-[length:2] ring-4` discards, Tailwind emitting
`ring-[length:2]` last. A colour keyword (`ring-current`, `ring-inherit`, `ring-transparent`) is a colour. A
`from-` / `via-` / `to-` colour that is no colour (`from-[1IN]`) leaves its stop transparent, and a position that
is no length or percentage (`from-[length:red]`) returns the stop to its initial position: 0%, 50% or 100%.

Only what the browser rejects counts. A function other than `var()`, `currentColor` or a colour name Velvet does
not read (`ring-[oklch(62.3%_0.214_259.815)]`, `ring-[rebeccapurple]`), and a length in a unit a stop cannot place
(`via-[length:2em]`), discard nothing, and Velvet leaves such a class unread. A `var()` is resolved first, and the
value it resolves to is the one judged ([styling-custom-properties.md](styling-custom-properties.md)).

Tailwind composes four more families through one declaration each: `translate-*`, `translate-x-*` and
`translate-y-*`; `scale-x-*`, `scale-y-*` and the uniform `scale-<n>` (a bracketed `scale-[…]` is written as
`scale` alone); `skew-<n>`, `skew-x-*` and `skew-y-*`; and the filters, `blur-*` to `sepia-*`, `hue-rotate-*` and
`drop-shadow-*`. Each utility writes custom properties of the element, and whenever the value holding one of them
is one the browser rejects, the family's declaration is dropped and the family draws nothing:
`translate-x-4 translate-y-[10]` translates nowhere, and `blur-sm brightness-[abc]`, `drop-shadow-lg blur-[2]` and
`blur-sm drop-shadow-[foo]` filter nothing. A math function coming to a type the property does not take is
such a value: `translate-y-[calc(45deg)]`, `skew-y-[calc(10px)]`, `blur-[calc(50%)]`. Velvet answers which value holds as it does for any value of the
property, in the state the element is in: an important one first
(`!translate-y-[10] hover:translate-y-2 translate-x-4` translates nowhere, hovered or not), then the one whose
variants rank higher, by the selector they add and then by the order Tailwind registers them, with every
breakpoint at or below the viewport's applying, then the one Tailwind emits last, in whatever order they are
written. So `blur-[2] blur-md` blurs as `blur-md`, while `translate-y-[10] translate-y-2 translate-x-4` translates
nowhere, Tailwind emitting `translate-y-[10]` after `translate-y-2`; `translate-x-4 hover:translate-y-[10]`
translates along x until hovered and not at all while hovered; and
`translate-x-4 hover:translate-y-[10] focus:translate-y-2` translates along both axes while hovered and focused,
the `focus:` value holding. A `drop-shadow-*` colour and size are separate values, as Tailwind writes them, and a colour the browser
rejects (`drop-shadow-[#ggg]`, `drop-shadow-[#ggg]/50`) counts only where the drawn shadow reads it: `blur-sm drop-shadow-lg drop-shadow-[#ggg]`
filters nothing, while `blur-sm drop-shadow-[#ggg]` blurs. A
utility writing both axes (`translate-4`, `translate-[10px]`, `translate-full`, `translate-px`, `translate-1/2`,
and their negations) holds each axis as an axis utility does, and an axis utility of the same rank holds over it,
Tailwind emitting it later: `translate-x-4 translate-[10]` translates nowhere, its y holding the rejected value. A
uniform `scale-<n>` or `-scale-<n>` writes both scale axes the same way and ranks against `scale-x-*` and `scale-y-*` by its
variants: `scale-50 hover:scale-x-[2]` keeps its y at 0.5 while hovered. A variant's value of one axis leaves the
other axis to the value holding it: `translate-8 hover:translate-x-2` translates by 8 and 32 while hovered. A
`scale-[…]` declares `scale` itself rather than either axis, so where its rank is at or above every axis utility's
it is drawn alone, whatever the axes hold (`scale-x-[2px] scale-[1.5]` scales by 1.5, Tailwind emitting
`scale-[1.5]` later), and below one it supplies neither axis (`scale-[1.5] hover:scale-x-[2]` scales by 2 and 1
while hovered). A `rotate-[…]` and a `scale-[…]` the browser rejects take nothing with them.

## Lengths only an element can measure

| Written | Measured against |
|---------|------------------|
| `2em` | the element's computed font size; on `text-[…]`, its parent's |
| `2ex` | the x-height of the font UI Toolkit draws the element's text in, at that size; 0.5em where the font carries none |
| `2cap` | that font's cap height; its ascent where it carries none, and the em where it carries neither |
| `2ch`, `2ic` | the advance of `0` and of `水` in that font, or in its first fallback font holding the glyph; 0.5em and 1em where none does |
| `2lh` | the line height Velvet gives the element's text: the nearest `leading-*` or `text-<size>` line height on it or an ancestor ([fonts.md](fonts.md)), else the font's own |
| `2rex`, `2rch`, `2rcap`, `2ric`, `2rlh` | the same, for the panel root's font at 16px, the size a rem is taken at |
| `50vw`, `50vh`, `50vmin`, `50vmax` | the panel — in an editor window, the window's content: its width, its height, the smaller and the larger of the two |
| `50svw`, `50lvh`, `50dvmin`, … | the same as the unprefixed unit: a panel has no browser chrome to show or hide, which is where CSS makes the small, large and dynamic viewports equal |
| a math function mixing a percentage with a length, or reading an `em` or a viewport unit | each term against its own basis, as CSS evaluates it |

Every utility that takes a length reads them. Those that write a length-percentage inline are these:

- sizing: `w-`, `h-`, `min-w-`, `min-h-`, `max-w-`, `max-h-`, `size-`, `basis-`;
- position: `inset-`, `inset-x-`, `inset-y-`, `top-`, `right-`, `bottom-`, `left-`, `start-`, `end-`;
- spacing: the `p*-` and `m*-` utilities, the logical `ps-` / `pe-` / `ms-` / `me-` among them, and their
  negated forms (`-mt-[1em]`);
- type: `text-[…]` as a font size, `tracking-[…]` and the `tracking-*` presets, which are em of the element's own
  font size; `leading-[…]` is measured on the element too, a percentage being of its font size
  ([fonts.md](fonts.md));
- filters: `blur-[…]` and the offsets and deviation of a `drop-shadow-[…]`, an em being the element's own font
  size and a percentage declined, as `blur()` and `drop-shadow()` take none. A math function coming below zero gives a
  blur or a deviation of zero.

A percentage inside one of them is taken where CSS takes it for the longhand written: `w-`, `min-w-`,
`max-w-`, `left-` and `right-` of the parent's width, and so is every padding and margin edge, `pt-` and
`mt-` included; `h-`, `min-h-`, `max-h-`, `top-` and `bottom-` of the parent's height; `basis-` of the
parent's size along its flex direction; `text-[…]` of the parent's font size; and `tracking-[…]` of the
element's own font size. The parent's size is its content box, and its padding box for an `absolute`
element, as CSS and UI Toolkit both take it. On an element with a `clip-path-*`, every longhand — padding included — is measured against the parent the clip wrapper
sits in, not the wrapper.

The others measure them on the element they style, and its percentages as their property takes them: a border
width, `rounded-*`, `translate-*` and `origin-[…]` (a percentage of the element's own box), `shadow-[…]`,
`ring-*`, `ring-offset-*`, `outline-*` and `outline-offset-*`, `clip-path-[…]`, a gradient's stops, radii and
centre, and `leading-[…]` (a percentage of the font size of the element declaring it). A `gap-*` is measured on
the container, and a `space-*` margin and a `divide-*` width on each child, whose margin or border it is. A border
width, a ring, a gap, a space margin, a divide and a shadow take no percentage, as in CSS.

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
keeps what the cascade gives it, and the filter leaves out the blur or shadow reading it. It is measured again on a 16 ms tick of the panel's scheduler after any of
these moves:

- whether the element has been laid out, its own font-relative units (its font size, its font and its line
  height), and its `position`;
- the parent's font-relative units, size, border widths, padding and flex direction;
- the panel root's font;
- whether the parent's width and height are definite (above);
- the panel's size.

The utilities that measure on their own element, above, are measured again on the same tick, or, for a
ring or an outline, on the frame's sample of its element. Unlike CSS, where the value is part of layout, the
new value lands a tick after the change. A re-measure is written with the transitions of the element and of its
clip wrapper held at zero duration and delay, and a filter's with no transition, so it lands at once rather than
animating.

### Where they are not interpolated

A spring or bezier `V.Motion` does not interpolate a length only an element can measure: the class still
applies, uninterpolated, as `w-auto` does.

## Bare spacing steps

A spacing utility takes a bare number as Tailwind v4 does: any non-negative multiple of 0.25 written in
canonical decimal form is that many 4px steps, so `p-13` is 52px, `gap-4.5` 18px and `w-0.25` 1px. `.5`,
`4.50`, `05` and `4.1` are not steps and leave the class unresolved. The utilities are padding, margin
(`m-*`, `mx-*`, `ps-*`, …), `gap-*`, `space-x-*` / `space-y-*`, `w-*`, `h-*`, `size-*`, `min-*` / `max-*`
width and height, `basis-*`, `top-*` … `inset-*` and the logical insets, `translate-*` / `translate-x-*` / `translate-y-*`,
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

`z-<n>` takes a whole number the same way (`z-15`, `-z-15`); see [styling-z-index.md](styling-z-index.md).

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
token and a class gated on a state are ranked by their own layer. Two inline tokens of one property are ordered in
the next section.

### Two inline utilities of one property

Two base utilities that both resolve inline and write the same property are ordered as Tailwind emits them, whichever
is written first: `w-[1px] w-[2px]` and `w-[2px] w-[1px]` both give 2px, `w-1/2 w-[10px]` gives 10px,
`bg-red-500/50 bg-[#ff0000]` gives the half-transparent red, `border-b-red-500 border-b-[#0000ff]` leaves the bottom
border red, and `scale-[1.5] scale-[2]`, `translate-x-[4px] translate-x-[8px]`, `opacity-[.3] opacity-[.7]` and
`blur-[2px] blur-[4px]` give the second value. A `drop-shadow-*` size is ordered against another size the same way
(`drop-shadow-sm drop-shadow-lg` gives the small one), and a `shadow-*` beside it stays as written, since box-shadow
and filter are separate properties and both render. The important layer is ordered the same way (`!w-[1px] !w-[2px]`
gives 2px), and a custom filter is told apart by its registered name, so two different names are both kept. A token of
another property and a token in another layer are left as written (`!w-[1px] w-[2px]`), and a variant token is ranked
by the position of its own rule ([styling-variants.md](styling-variants.md)).

The earlier token is left out of the class list, so it writes no style; taking the later one away brings it back, and
taking the earlier one away changes nothing. A border width bracket that later inline tokens write in part is cut down
to the sides they leave, as it is for a class above: `border-[3px] border-t-[4px]` and `border-t-[4px] border-[3px]`
give the top 4px and the other three 3px.

A stylesheet class that is written inline beside a narrower inline utility (`pt-2` beside `p-[3px]`, `w-4` beside
`size-[10px]`) is ordered against the other writers of its property by the name it was written with, not by the
inline form it becomes: `size-[10px] w-[10px] w-4` gives a 10px width, since Tailwind emits `w-[10px]` after `w-4`,
where the inline form `w-[16px]` would sort after it. The same holds behind a variant and under `!`.

A slash text size (`text-[14px]/6`) is not left out for a later plain font size (`text-[16px]`), because its line
height is read off the token. Tailwind emits the slash form first, since it declares font size and line height, so
the plain size holds the font size and the slash token the line height in either order:
`text-[16px] text-[14px]/6` and `text-[14px]/6 text-[16px]` give 16px with the 6-step line height, and
`text-sm/6 text-lg/7` gives the `text-sm` size with the `/6` line height.

### Utilities a C# resolver folds from the class list

A few families have no inline layer to settle a pair: a resolver reads the whole class list and ends on the last
token of each slot. Their base tokens are put in the order Tailwind emits them, in the positions they hold, so the
token the fold ends on is the one Tailwind emits last whichever is written first. Tailwind sorts two utilities by
the first property they differ on, then by the one with more declarations, then by the candidate name, digits compared
as numbers and a bracket ahead of a letter. Most utilities of one slot declare the same properties, so the name decides;
the exceptions are named in the list:

- the font facets: `font-light font-bold` and `font-bold font-light` both give light, `not-italic italic` gives not
  italic, `font-[700] font-[600]` gives 700, `font-light font-[700]` gives light, `font-sans font-mono` gives sans;
  `bold-italic` sorts as its name, which Tailwind has no utility for;
- the text effects: `uppercase lowercase` gives upper case, `underline overline` gives underline, `text-pretty
  text-balance` gives pretty, `leading-6 leading-4` gives the 6 step, with `whitespace-*` and the named and slash
  sizes in the same list;
- `z-20 z-10` gives 20, `grid-cols-3 grid-cols-2` three columns, `gap-x-4 gap-2` a 4 horizontal gap with a 2
  vertical one, `space-x-*` and `space-y-*`, and `divide-*`;
- `shadow-*`, so `shadow-sm shadow-lg` gives the small shadow; `ring-*` and `outline-*`, outline being emitted after
  the ring (Tailwind v4's `property-order.ts` lists the ring's box-shadow properties before the outline's), so
  `ring-0 outline-2` gives a 2px outline;
  `skew-*`, `pointer-events-none` and `pointer-events-auto`, `border-solid` and the other line styles, and
  `animate-*`;
- the gradient shape, where a keyword or integer `bg-linear-*` counts three declarations to the two of the others
  and is emitted first ([styling-gradients.md](styling-gradients.md)), beside its `from-` / `via-` / `to-` stops;
- `clip-path-none` and `clip-path-[…]`, `bg-[addr:…]`, `animate-gradient`, `animate-shimmer` and `animate-hue`, which
  Tailwind has no utility for, so the candidate name orders them.

Tokens of the two importances are ordered apart, as the resolvers fold them. A variant payload is appended after the
base tokens by the variant gate and keeps its place there. The nine-slice insets (`slice-*`) and `bold-italic` also have no Tailwind utility; the insets write overlapping
longhands, so they follow the sort key instead: more longhands first, then the
candidate name, which puts `slice-[…]` ahead of `slice-y-[…]` ahead of `slice-t-[…]` whichever is written first.

This is a change from the last token written: `not-italic italic`, `underline overline`, `text-pretty text-balance`
and `uppercase lowercase` used to end on their second class.
