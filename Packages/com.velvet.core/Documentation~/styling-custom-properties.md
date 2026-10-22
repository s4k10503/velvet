# Custom properties

Tailwind v4 writes a CSS custom property as a class, and reads one with `var()`. Velvet resolves both on the
element, the way a browser computes them, so a value set on a container reaches every descendant reading it.

```csharp
V.Div(className: "[--accent:#2563eb] [--gap:12px] hover:[--accent:#1d4ed8]", children: new VNode?[]
{
    V.Div(className: "bg-[var(--accent)] p-(--gap) w-[calc(var(--gap)*10)]"),
});
```

## Declaring a property

`[--name:value]` declares `--name` on the element, as Tailwind's arbitrary property does: `_` in the value is a
space (`\_` an underscore), except in a `var()` name and inside `url()`, and the value is trimmed. A declaration
takes variants and the `!` modifier like any other class (`dark:[--accent:#93c5fd]`, `hover:[--w:40px]`) and
ranks against the element's other declarations of the same name the way two payloads rank
([styling-variants.md](styling-variants.md)).

A custom property a stylesheet declares (`--accent: #2563eb;` in a USS rule) is read too. On one element a class
declaration outranks a stylesheet's.

## Reading a property

- `var(--name)` and `var(--name, fallback)` anywhere in a utility's bracket, or in its modifier's: `w-[var(--w)]`,
  `from-[var(--brand)]`, `shadow-[0_2px_var(--blur)_#000]`, `w-[calc(var(--w)*2)]`, `bg-red-500/[var(--alpha)]`.
  The fallback may hold further `var()`s.
- Tailwind's shorthand: `bg-(--brand)` is `bg-[var(--brand)]`, `text-(length:--size)` is
  `text-[length:var(--size)]`, `w-(--w,calc(1rem*2))` is `w-[var(--w,calc(1rem*2))]`, and `bg-red-500/(--alpha)`
  is `bg-red-500/[var(--alpha)]`. A modifier's shorthand takes no type hint, so `bg-red-500/(number:--alpha)` is
  no utility at all, as in Tailwind.
- An arbitrary property, `[property:value]`, for the properties a utility writes: `width`, `height`,
  `min-width`, `min-height`, `max-width`, `max-height`, `flex-basis`, `flex-grow`, `flex-shrink`, `top`,
  `right`, `bottom`, `left`, `inset`, `padding` and `margin` with their four sides, `gap`, `row-gap`,
  `column-gap`, `border-radius`, `border-width`, `border-color`, `background-color`, `color`, `font-size`,
  `font-weight`, `font-family`, `line-height`, `letter-spacing`, `outline-color`, `opacity`, `rotate`,
  `aspect-ratio` and `transition-duration`. The value is read as the matching utility's bracket, so
  `[width:var(--w)]` is `w-[var(--w)]`.

What a property resolves to on an element is its own declaration, else the nearest ancestor's. A `var()` inside
a declaration is resolved on the element declaring it. `initial` as a declaration's value, and a cycle of
declarations reading each other, leave the property undeclared, so a reader takes its fallback; `inherit` and
`unset` take the parent's value. A value is put into the reader's bracket the way Tailwind reads one back: a
space as `_` and an underscore as `\_`.

### Which property a utility writes

Tailwind decides which property a utility writes from the value as written, and declines to test a value that
opens with `var()`. A utility that writes either a colour or something else — `text-*`, `bg-*`, `border-*` and
its sides (`border-t-*`, `border-x-*`, …), `ring-*`, `ring-offset-*`, `outline-*`, `from-*`, `via-*`, `to-*` — then
writes the colour, so
`text-[var(--size)]` sets the text colour whatever `--size` holds, and `font-[var(--f)]` writes the font
weight. A type hint picks the other property: `text-[length:var(--size)]`, `text-(length:--size)`,
`font-(family-name:--f)`.

A `z-*` reading a `var()` stacks the element at the integer it resolves to, and moves it within or between its
parent's layers when that changes ([styling-z-index.md](styling-z-index.md)).

A modifier read from a `var()` is the percentage Tailwind hands `color-mix()`: `--alpha: 50%` is half alpha,
and a bare number is not a percentage, so `--alpha: 0.5` does not resolve.

### Shorthands holding several values

A shorthand's bracket may hold one value per side, as the CSS property does, written into it or resolved from a
`var()`: `p-[1px_2px]`, `[margin:0_auto]`, `p-(--pad)` with `--pad: 4px 8px 12px`. `p-*`, `m-*`, `inset-*` and
`rounded-*` take one to four values, top, right, bottom and left (from the top-left corner for a radius), a
`border-[length:…]` or `border-[color:…]` the same, and `gap-*` a row and a column gap.

### Rule order

A utility reading a custom property and writing a style property ranks among the element's other utilities by
the order Tailwind emits them, as any utility does, so `pt-[9px] [padding:1px_2px]` keeps the 9px top whichever way the class list writes them,
and of `w-[var(--a)] w-[var(--b)]` the second wins.

### A value that does not resolve

When a `var()` with no fallback reads an undeclared property, or the value it resolves to is not one the
property takes, the browser computes the property as `unset`: the initial value for a property that does not
inherit, and the parent's value for one that does — text colour, font size, letter spacing. Where a rule of the
element's own sets the inheriting property, such as a control's theme, the parent's value is written over it from
the panel's next scheduler tick. The rule still outranks the element's lower-ranked rules
for that property, so `w-[20px] hover:w-[var(--missing)]` leaves the width `auto` while hovered rather than 20px.

Where Velvet paints the utility itself rather than writing a style property, it takes what the browser draws: a
gradient stop's colour is transparent and its position the stop's default (0%, 50% or 100%), `shadow-[…]` draws
no shadow, a ring's colour is the text colour and its width none, an outline's colour is the text colour and its
width 3px, `gap-[…]` leaves no gap, a filter function is left out of the filter, a `drop-shadow-[color:…]` leaves
each drop shadow its own colour, a `skew-x-[…]` or `skew-y-[…]` leaves that axis unsheared and a `skew-[…]` both,
and a font weight, a font family and a line height are the parent's.

## When a reader is resolved again

- A declaration in a class changing, on a render or as a variant turns on or off: every reader in the element's
  subtree, in the same pass.
- The element attaching to a panel, which is when it first has the ancestors its declarations come from.
- A stylesheet's declaration changing in value: in the style pass that applies it, for a reader below the declaring
  element whose value is a style property; a reader on the declaring element itself, and one Velvet paints itself,
  at the panel's next scheduler tick. A declaration that stops applying because its rule no longer matches, as when
  a theme class is removed, is read again by the panel's next scheduler tick after the style pass.

## Not supported

- An arbitrary property not named above writes nothing.
- A `z-*` reading a `var()` on an element a `clip-path-*` wrapper holds stacks at 0 whatever the property holds.
- A stylesheet's custom property holding an asset reference is read as undeclared, and one holding a `resource()`
  or a function other than `var()` has no utility that reads it: no Velvet bracket takes an image.
