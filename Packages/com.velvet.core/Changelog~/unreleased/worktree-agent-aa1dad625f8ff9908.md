### Fixed

- A USS file holding a custom property with one of these, each of which UI Toolkit's importer fails the file on, is
  imported, the value stored as the text it was written with, as one holding a `rem` is: a number with an exponent
  (`1e3px`, `1e3`), one a `-`, a `_` or a non-ASCII character directly follows (`1-2`, `1_a`, `1-a`), most
  delimiters, such as the `/` of Tailwind's `--aspect-video: 16 / 9` or a full stop no digit follows after a fraction
  (`.5.p`), a hex colour of other than three, four, six or eight digits (`#zzz`, `#12345`), an `rgb()` of other
  than three plain numbers or an `rgba()` of fewer than four, a percentage or a range such as `u+1` counting as none
  (`rgb(100%, 0%, 0%)`, `rgba(0, 0, 0)`, `rgb(u+1 2 3)`), a function with nothing between its parentheses other than
  `none()`, `filter()` and the URL functions `url()`, `url-prefix()` and `domain()` (`blur()`, `var()`), a `var()`
  whose arguments the importer refuses (`var(--a,)`, which Tailwind v4 writes, `var(--a, 1px, 2px)`, `var(a)`,
  `var(--a b)`), a comment among the arguments of a function such as `blur()`, `var()` or `linear-gradient()`
  (`blur(/* x */ 4px)`, `var(--a /* c */)`), a range (`u+1`, `U+12`), and a URL function whose path the importer's
  lexer cannot read (`url(a b)`, `url(a /* c */)`, `url("a" /* c */)`, stored as `url("a"  )`). The importer used to
  leave the whole file empty.
- A custom property stored as the text it was written with keeps a `/*` inside an unquoted URL path, which CSS reads
  as part of the path (`url(a/*c*/) 1rem`), where it used to be taken out as a comment. A `/*` or a quote in an
  unquoted URL path, in any declaration, used to leave the custom properties after it as written up to the next `*/`
  or quote of its kind, a `rem` among them failing the file (`url(icons/*.svg)` or `url(images/don't-panic.png)`,
  then `--gap: 1rem`), and with no `*/` after it the custom property holding a `/*` as well
  (`url(icons/*.svg) 1rem`). Such a path now ends at its first `)` no backslash escapes, as CSS ends it.
- A custom property stored as text whose value holds a form feed no longer fails the file. The form feed used to be
  written inside the quotes as it was, which UI Toolkit's importer refuses.
- `Hooks.UseQueries` and `Hooks.UseSuspenseQueries` run `combine` again once a commit changes only a query's
  `Select`, as TanStack Query v5 does. The value from before the change used to stay until another change ran it
  again.
- A custom property whose `!important` is written in another case, which UI Toolkit's importer fails the file on,
  has it written in lower case.
- A custom property in a USS file calling a function UI Toolkit's importer does not know, such as `mysteps(3)` or
  `rgb()` written in capitals, is stored as the text it was written with, as one calling `cubic-bezier()` is. The
  importer used to drop the function with a warning.
