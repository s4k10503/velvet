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

- `var(--name)` and `var(--name, fallback)` anywhere in a utility's bracket: `w-[var(--w)]`,
  `from-[var(--brand)]`, `shadow-[0_2px_var(--blur)_#000]`, `w-[calc(var(--w)*2)]`. The fallback may hold
  further `var()`s.
- Tailwind's shorthand: `bg-(--brand)` is `bg-[var(--brand)]`, and `text-(length:--size)` is
  `text-[length:var(--size)]`.
- An arbitrary property, `[property:value]`, for the properties a utility writes: `width`, `height`,
  `min-width`, `min-height`, `max-width`, `max-height`, `flex-basis`, `flex-grow`, `flex-shrink`, `top`,
  `right`, `bottom`, `left`, `inset`, `padding` and `margin` with their four sides, `border-radius`,
  `border-width`, `border-color`, `background-color`, `color`, `font-size`, `letter-spacing`, `opacity`,
  `rotate`, `aspect-ratio` and `transition-duration`. The value is read as the matching utility's bracket, so
  `[width:var(--w)]` is `w-[var(--w)]`.

What a property resolves to on an element is its own declaration, else the nearest ancestor's. A `var()` inside
a declaration is resolved on the element declaring it. `initial` as a declaration's value, and a cycle of
declarations reading each other, leave the property undeclared, so a reader takes its fallback; `inherit` and
`unset` take the parent's value.

### Which property a utility writes

Tailwind decides which property a utility writes from the value as written, and declines to test a value that
opens with `var()`. A utility that writes either a colour or something else — `text-*`, `bg-*`, `border-*`,
`ring-*`, `ring-offset-*`, `outline-*`, `from-*`, `via-*`, `to-*` — then writes the colour, so
`text-[var(--size)]` sets the text colour whatever `--size` holds. A type hint picks the other property:
`text-[length:var(--size)]`, or `text-(length:--size)`.

### A value that does not resolve

When a `var()` with no fallback reads an undeclared property, or the value it resolves to is not one the
property takes, the browser computes the property as `unset`. For a property that does not inherit that is its
initial value. For one that does — text colour, font size, letter spacing — it is the parent's value, which
Velvet leaves to UI Toolkit by writing no inline value, so a rule of a stylesheet other than Velvet's that
matches the element itself, such as a control's theme, still applies. The rule still outranks the element's lower-ranked rules for that
property, so `w-[20px] hover:w-[var(--missing)]` leaves the width `auto` while hovered rather than 20px.

Where Velvet paints the utility itself rather than writing a style property, it takes what the browser draws: a
gradient stop's colour is transparent and its position the stop's default (0%, 50% or 100%), `shadow-[…]` draws no shadow, `gap-[…]` leaves no gap and a filter function is
left out of the filter. Any other painted utility reading an unresolvable `var()`, such as a ring colour, a font
family or a line height, is dropped, so a lower-ranked rule of its own still applies.

## When a reader is resolved again

- A declaration in a class changing, on a render or as a variant turns on or off: every reader in the element's
  subtree, in the same pass.
- The element attaching to a panel, which is when it first has the ancestors its declarations come from.
- A stylesheet's declaration changing, which UI Toolkit applies at its style pass: on the panel's next scheduler
  tick after that pass.

## Not supported

- An arbitrary property not named above writes nothing.
- An arbitrary property holding a value its utility does not read, such as the multi-value shorthand
  `[padding:1px_2px]`, writes nothing; one reading a `var()` computes to `unset` instead.
- An underscore inside a custom property's value is read as a space where the value is substituted.
- `var()` in a modifier, `bg-red-500/(--alpha)`.
- `var()` in a `z-*` bracket.
- A stylesheet's custom property holding an asset (`url()`, `resource()`) or a function other than `var()` is
  read as undeclared.
