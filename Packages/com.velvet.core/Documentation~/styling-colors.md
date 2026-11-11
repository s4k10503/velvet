# Styling notes: Colours

Every utility that takes a colour in brackets reads it with one grammar: `bg-[…]`, `text-[…]` and
`border-[…]` and their opacity modifier (`bg-[…]/50`), `ring-[…]` and `outline-[…]`, `divide-[…]`,
`shadow-[…]` and `drop-shadow-[…]`, the gradient stops (`from-[…]`, `via-[…]`, `to-[…]` and the colours of
a stop list), a text input's `caret-[…]`, `selection:bg-[…]` and `selection:text-[…]` and their opacity
modifier, and the colour argument of a custom filter (`filter-[name:…]`). An underscore stands for a
space: `bg-[rgb(255_0_0_/_50%)]` is `rgb(255 0 0 / 50%)`.

## Accepted forms

| Form | Example |
|---|---|
| `#rgb`, `#rgba`, `#rrggbb`, `#rrggbbaa` | `bg-[#0f172a]` |
| a colour name Unity's `ColorUtility` knows (`red`, `blue`, `white`, `black`, …) | `text-[red]` |
| `transparent` | `bg-[transparent]` |
| `rgb()` / `rgba()`, comma syntax | `bg-[rgb(15,23,42)]`, `bg-[rgba(0,_0,_0,_0.5)]` |
| `rgb()` / `rgba()`, space syntax | `bg-[rgb(15_23_42_/_50%)]` |
| `hsl()` / `hsla()`, comma syntax | `text-[hsl(210,40%,50%)]` |
| `hsl()` / `hsla()`, space syntax | `text-[hsl(210_40%_50%_/_0.8)]` |
| `hwb()` | `border-[hwb(150_20%_10%)]` |

`lab()`, `lch()`, `oklab()` and `oklch()` are not read, whatever their components: the class declines.

The functions follow CSS Color 4:

- **The space syntax** separates the three components with spaces and takes an optional alpha after a `/`,
  with or without spaces around it. A component may be a number, a percentage or `none`, mixed freely;
  `none` reads as zero.
- **The comma syntax** exists for `rgb()` / `rgba()` and `hsl()` / `hsla()` only. It takes no `none`, its
  `rgb()` channels are all numbers or all percentages, and its `hsl()` saturation and lightness are
  percentages. `rgb()` and `rgba()` are the same function, as are `hsl()` and `hsla()`: either takes three
  arguments or four.
- **Percentages**: an `rgb()` channel's 100% is 255; saturation, lightness, whiteness and blackness read a
  number as the percentage it names (`hsl(210_40_50)` is `hsl(210_40%_50%)`); an alpha's 100% is 1.
- **A hue** is a number of degrees or an angle in `deg`, `grad`, `rad` or `turn`, and wraps around the circle
  (`-120` is `240`).
- **Out-of-range values are clamped as CSS clamps them when it parses**: `rgb()` channels to 0–255, alpha to
  0–1, and a negative saturation to 0, and in the space syntax a negative lightness too (`hsl(0_150_-50)` is
  black). A whiteness and blackness summing to 100% or more give the gray `whiteness / (whiteness + blackness)`.
- **A saturation above 100%** clamps to 100% in the comma syntax. In the space syntax it clamps only as
  Chromium's fast path does, which reads a *plain* `hsl()` on a colour longhand. Plain means:
  - the function is named exactly `hsl` or `hsla`, in lower case;
  - the hue is a decimal (`210`, `.5`, `-0.25`) with an optional `deg`, `grad`, `rad` or `turn`;
  - the saturation and the lightness are percentages whose decimal has a digit before any point, so `.5%` is not
    plain;
  - an alpha, if any, is a decimal with nothing between it and the closing bracket;
  - no component has an exponent, a `+` sign or `none`, and the saturation is a percentage rather than a number.

  `text-[hsl(0_150%_75%)]` is then `text-[hsl(0_100%_75%)]`. Any other spelling (`hsl(0_150_75)`,
  `hSLa(0_150%_75%)`, `hsl(0_150%_75%_/_50%)`, `hsl(0_150%_75%_/_0.5_)`) keeps the saturation above 100%, and the
  channels clip, so `hsl(0_150_75)` is `rgb(100% 37.5% 37.5%)`.
- **Which utilities clamp**: the colour longhands only: `text-[…]`, `bg-[…]`, `border-t-[…]` / `-r-` / `-b-` / `-l-`
  / `-s-` / `-e-` / `-bs-` / `-be-`, `outline-[…]`, `caret-[…]` and `selection:bg-[…]` / `selection:text-[…]`. A
  shorthand (`border-[…]`, `border-x-[…]`, `border-y-[…]`), a colour with an opacity modifier (`bg-[…]/50`),
  `ring-[…]`, `shadow-[…]`, `drop-shadow-[…]`, `divide-[…]`, a gradient stop and a custom filter's colour
  argument read through Chromium's general parser, which leaves the saturation above 100%.
- **A number** is a CSS `<number>`: `.5`, `+1e2` and `-0` read, `1.` and `1e` do not, and neither do `infinity` or
  `nan`. One past single precision's range (`1e39`, `1e999`) reads as the largest value of its sign, about
  `3.4e38`, as Chromium reads it, so `rgb(1e999_0_0)` is red and `rgb(-1e999_0_0)` black. A hue is then the hue of
  plus or minus that value: 0deg in degrees or turns, 216deg in gradians and 240deg in radians, and 144deg and
  120deg for the negative gradians and radians.
- **A math function** ([styling-arbitrary-lengths.md](styling-arbitrary-lengths.md#math-functions)) stands for any
  component that comes to a number or a percentage, and for a hue that comes to an angle or a number:
  `rgb(calc(255/2)_0_0)`, `hsl(calc(60deg*2)_100%_50%)`, `rgb(255_0_0_/_calc(50%))`. It is clamped as a written
  component is, and a NaN reads as 0. One coming to a length is no component (`rgb(calc(1px)_0_0)` is inert).
- **Function names, `none` and angle units** are read in any case.
- **The opacity modifier** (`bg-[…]/50`, `bg-red-500/50`, `text-…/[0.3]`, `text-…/[30%]`) multiplies the colour's alpha, as
  Tailwind v4's `color-mix(in oklab, <colour> 50%, transparent)` does: `bg-[#ff000080]/50` has an alpha of a
  quarter. The modifier is written the forms Tailwind generates: a number in steps of 0.25 as a number prints it (`/50`,
  `/2.25`, not `/05`, `/.5` or `/50.0`), a bracketed number, which is a fraction (`/[0.33]`), or a bracketed percentage
  (`/[33.3%]`). One outside 0–100% on a colour Tailwind writes through a variable (a palette name, a bracketed `var()`) leaves the
  `color-mix()` invalid once computed, so the property takes its unset value: `bg-red-500/150` is transparent,
  `text-red-500/150` the inherited text colour and `border-red-500/150` `currentcolor` (`outline-red-500/150` is the text
  colour, and a ring, `shadow-*` or inset colour discards the element's box-shadow). On any other colour
  (`bg-[#f00]/150`, `bg-transparent/150`) Tailwind writes no variable and the browser drops the declaration, so an
  earlier class stands; on `current` (`bg-current/150`) the property is plain `currentcolor`.
- **`current` and `inherit`** (`text-current`, `bg-inherit`, `border-current`, `ring-current`, `ring-inherit`,
  `outline-inherit`, …) read their value off a live source: `current` is the element's own text colour (the inherited one
  on `text-current` itself), `current/50` scales its alpha, and `inherit` is the parent's value of the same property, which
  an `inherit` there passes on. Both follow their source as it changes, through a transition included: an element reading one through `text-`, `bg-` or `border-`, and its parent, carry the `velvet-color-reference` class while it does, whose rule in the bundled stylesheet is what makes UI Toolkit report their restyles. `inherit` takes no modifier, which `color-mix()`
  cannot hold.
- **A border width draws in the text colour** unless a rule says otherwise, as Tailwind v4's preflight
  `border-color: currentcolor` does. The element carries a border-width utility (`border`, `border-2`, `border-t-4`,
  `border-s-2`, `border-[3px]`, under any variant), is given one by a child-scoped variant (`*:border`, or `border` on a
  field, which goes to its input box), is a child of a `divide-*` parent (a dashed divider's dashes included), is given
  one by a rule of a stylesheet of your own (not a control theme's) with a width other than zero that selects it by a
  class, its name, its type or `*`, or has a width your code wrote inline. An element is asked whether such a rule names
  it when it attaches and when its classes change, and listens for it from then on, following the rule as pseudo-states
  and ancestors change. A stylesheet added to an ancestor of an element that has attached is noticed within a frame and
  the elements under that ancestor are asked again. A width
  your code writes inline is seen the next time an element that is already listening is restyled, as the write raises no
  restyle of its own, and gives no preflight colour to any other.
  The border
  follows the text colour. A rule of a stylesheet that is not a control theme's that sets a side's border colour takes
  that side from it, whatever its selector or state: `border-red-500`, `border-transparent`, a variant such as
  `hover:border-blue-500`, and your own USS class, id, type, descendant or `:hover` rule. So does a colour your code
  writes inline (`element.style.borderTopColor`), whatever its value: the text colour included, which the border then
  keeps when the text colour changes. A control theme's rule does not take a side, as preflight outranks a browser's
  default styles. The colour is held in the element's inline rule (the slot a UXML `style` attribute fills), not in an
  inline value, so no inline value of yours shares its slot.

## Theme tokens

A custom property a stylesheet declares in one of Tailwind v4's theme namespaces
([the namespace table](https://tailwindcss.com/docs/theme#theme-variable-namespaces)) generates the utilities of that
namespace, as a variable in an `@theme` block does. The declaration is a USS rule; its selector may be `:root`, a
theme class or any other:

```css
:root { --color-brand: #ff0066; --radius-pill: 999px; }
.dark { --color-brand: #ff6699; }
```

`bg-brand`, `text-brand`, `border-brand`, `border-b-brand` (and the other per-side `border-t-` … `border-be-`),
`ring-brand`, `ring-offset-brand`, `outline-brand`, `divide-brand`, `shadow-brand`, `drop-shadow-brand`,
`from-brand` / `via-brand` / `to-brand` and `caret-brand` then resolve, with the opacity modifier (`bg-brand/50`) and
every variant. A class is the bracketed utility reading the variable (`bg-[color:var(--color-brand)]`), so the value
is the one the element computes: the `.dark` rule above changes what `bg-brand` draws, and the bundled semantic
colours (`primary`, `surface`, `text-subtle`, …), which are declared the same way in the bundled sheet, take the
families the palette names do (`border-b-primary`, `ring-primary`, `bg-primary/50`). Utilities rank among the
element's classes as the bracketed form does ([styling-variants.md](styling-variants.md)).

A project token with the name of a built-in overrides it in the forms the palette resolves from its own table: with
`--color-red-500` redeclared, `border-b-red-500`, `ring-red-500`, `bg-red-500/50` and the shadow and gradient forms
read the variable.

The other namespaces read are `--radius-*` (`rounded-pill`, and the per-corner and logical forms), `--text-*`
(`text-display`, its `--text-display--line-height`, written as a number (a multiple of the font size) or a length, and `text-display/7` with a line height), `--font-*`, `--font-weight-*`, `--tracking-*`, `--leading-*`,
`--shadow-*`, `--inset-shadow-*`, `--drop-shadow-*`, `--blur-*`, `--aspect-*`, `--spacing-*` and `--spacing`
([styling-arbitrary-lengths.md](styling-arbitrary-lengths.md#bare-spacing-steps)), and `--breakpoint-*`,
`--container-*` and `--animate-*` ([styling-variants.md](styling-variants.md#theme-breakpoints-container-sizes-and-animations)). `--ease-*` makes `ease-<name>` (`--ease-snappy: cubic-bezier(0.2, 0.8, 0.2, 1)` makes `ease-snappy`). The value is a
`cubic-bezier()` with both x coordinates in 0 to 1, `steps()` (with `jump-start`, `jump-end`, `jump-none` or
`jump-both`, or `start` or `end`; `step-start` and `step-end` too), or one of CSS's easing keywords, and
`ease-[cubic-bezier(…)]` and `ease-[steps(…)]` take it in place. `ease-in`, `ease-out` and `ease-in-out`, behind a
variant or not, run along Tailwind v4's curves (`cubic-bezier(0.4, 0, 1, 1)`, `(0, 0, 0.2, 1)` and `(0.4, 0, 0.2, 1)`),
as does the `ease-in-out` each `transition-*` utility but `transition-none` bundles
(`--default-transition-timing-function`); a project's `--ease-in`, `--ease-out` or `--ease-in-out` replaces its
curve. UI Toolkit's `transition-timing-function` takes an
`EasingMode` keyword and no cubic-bezier, so the element carries the keyword nearest the function, and Velvet has the
transition the engine runs ease along the function once the engine reports it running. The engine keeps the duration,
delay and reversal, per property, a transition running keeps the function it started on, and these transitions Velvet
runs itself ease by the same function: `transition-filter`'s tween, a `backdrop-*` change, a dashed or dotted divider's
colour, and a background or border colour repainted in the engine's place under a `shadow-*`, a `border-dashed` or
`border-dotted`, or a layoutId Motion's per-axis corners. A timing function written inline, as a Motion's is, is the
keyword it names. A name is a token of its namespace only where a sheet declares it.

A namespace is set to initial as Tailwind sets it, `--color-*: initial;` (`--*: initial;` for every one). The bundled
utilities of that namespace (`bg-red-500`, `bg-primary`, `text-white`, `bg-red-500/50`, `border-b-primary`,
`shadow-md`, `font-bold`, `sm:`, `@md:`, `animate-spin`, …) then do nothing, and the tokens the project declares itself
still generate theirs.

A length in a token's value is read as Tailwind v4 reads it: `rem` is 16px (`--spacing: 0.25rem`,
`--breakpoint-3xl: 120rem`), and `calc()`, `min()`, `max()` and `clamp()` are evaluated. UI Toolkit's own importer fails
a whole file on a `rem` it meets in a custom property and drops a math function, `cubic-bezier()` or `steps()`, so `Velvet.Editor` imports such a file
itself, storing those custom properties as the text they were written with. A new or edited file is imported first by
the stock importer, which logs its error, and then again by Velvet's once the selection is written to the file's meta
file; commit that meta file. The two settings of the stock importer's inspector, validation and unsupported selectors,
start from their defaults on the file's new importer. The selection is not made for a file in a package the Package Manager installs, which
cannot be written to.

Each mount reads the sheets attached to its mount target or one of its ancestors, and the sheets they `@import`, at
the start of every render pass of the mount and every tenth of a second. A sheet attached, edited or removed moves
what the mount's classes resolve to: the elements whose class lists name a theme class are configured again. A reader
outside a pass finds the scope of the mount its element sits in.

## Where Velvet differs from CSS

- **Named colours** are the ones `ColorUtility.TryParseHtmlString` reads, not the full CSS list.
- **The palette** (`bg-red-500`, a palette name in a gradient stop) is Tailwind v4's in sRGB: each OKLCH colour
  is converted as CSS Color 4 converts it and a channel outside 0–1 is clipped, so `bg-blue-500` is `#2b7fff`.
- **A colour outside sRGB**, which an `hsl()` or `hwb()` whose components lie outside their ranges can
  describe, has each channel clipped to 0–1 where CSS Color 4 maps it into the gamut.
- **`currentColor` and relative colour syntax** are not read. A `var()` is replaced by the custom
  property it reads before the colour is read ([styling-custom-properties.md](styling-custom-properties.md)).
