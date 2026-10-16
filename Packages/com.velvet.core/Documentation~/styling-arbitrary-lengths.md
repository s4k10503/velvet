# Bracketed lengths and bare spacing steps

A bracketed value such as `w-[120px]`, `top-[1in]` or `translate-x-[25%]` is resolved by Velvet rather
than by a USS rule, because no stylesheet can declare every value a bracket can hold. This guide owns which
lengths a bracket reads, which utilities read which of them, and what each is measured against. The
[last section](#bare-spacing-steps) owns the bare number a spacing utility takes without a bracket, and the order of
a shorthand and a longhand of one spacing family ([below](#a-shorthand-and-its-longhand)).

## What every length bracket reads

| Written | Read as |
|---------|---------|
| `120px`, or a bare `120` | pixels |
| `1.5rem` | pixels, at 1rem = 16px: UI Toolkit has no root font size to take a rem from |
| `1in`, `2.54cm`, `25.4mm`, `72pt`, `6pc`, `101.6Q` | pixels, at CSS's fixed ratio of 96px to the inch |
| `25%` | a percentage, where the utility takes one; a border width takes none, as in CSS |
| `auto` | the keyword, where the utility takes one: margin (a negated one, `-mt-[auto]`, is declined), `w-*`, `h-*`, `size-*`, `min-w-*`, `min-h-*`, `basis-*` and the insets; padding and the maximum sizes take none |
| `calc()`, `min()`, `max()`, `clamp()` whose terms all come to pixels, or all to percentages | that pixel length or that percentage |

Unit names match case-sensitively, so `Q` is upper case and the rest lower.

Swapping one bracketed value for another that writes the same property (`w-[300px]` for `w-[10px]`, with or
without `!`) leaves the old inline value in place until the new one is written, so a `transition-*` or
`duration-*` on the element runs between the two.

A math function follows CSS's grammar with Tailwind's spelling. `_` stands for a space. CSS wants a space
on both sides of a binary `+` or `-`; Tailwind writes `calc(2rem-4px)` and adds the spaces itself, so a
space on neither side reads as one on both, and `calc(2rem-4px)` and `calc(2rem_-_4px)` are the same
length. A space on one side only is declined: `calc(1px_-2px)` is `1px` followed by a second value `-2px`.
A sign belongs to a number (`-2px`, `+.5rem`, `-1e3px`), never to a parenthesis or a function, so `-(1px)`
and `-calc(1px)` are declined, and so is a space before the function. Functions nest and `(` groups; `*`
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
of `w-*` and `h-*`. It covers padding, margin, the insets and `size-*` / `w-*` / `h-*`, at one priority: a
variant or `!` outranks the plain utility as it does anywhere, and a bracket value takes part the same way
(`p-[52px] pt-2`). Variants are compared as a set, so `hover:focus:` and `focus:hover:` are one group.

An inline value outranks every class on its element, so a stylesheet class that is the narrower of such a pair
is written inline too when an inline utility of an overlapping family stands beside it: `pt-2` becomes
`pt-[8px]`, `mx-auto` becomes `mx-[auto]` (the `auto` bracket in the [table above](#what-every-length-bracket-reads)) and `w-full`
becomes `w-[100%]`. Two things follow. The rewritten class is no longer in the element's class list, so a
selector on it (`.pt-2 > .x`) and `ClassListContains("pt-2")` stop seeing it, though the unrewritten classes of
the list are unchanged. And the rewrite writes the pixel value of the step as Velvet's scale gives it, so a
stylesheet that redefines `--space-2` moves `pt-2` and leaves `pt-[8px]` where it was, the same limit as
the logical utilities have ([react-migration.md](react-migration.md)).

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
- Border radii are not ordered this way: `rounded-r-*` and `rounded-t-*` on one corner are left to whichever is
  written last.

`z-<n>` takes any whole number an int holds the same way (`z-15`, `-z-15`); see [styling-z-index.md](styling-z-index.md).
