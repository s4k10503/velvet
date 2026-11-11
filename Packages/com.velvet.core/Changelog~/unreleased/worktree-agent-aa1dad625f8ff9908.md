### Fixed

- A USS file holding a custom property with one of these, each of which UI Toolkit's importer fails the file on, is
  imported, the value stored as the text it was written with, as one holding a `rem` is: a number with an exponent
  (`1e3px`, `1e3`), one a `-`, a `_` or a non-ASCII character directly follows (`1-2`, `1_a`, `1-a`), most
  delimiters, such as the `/` of Tailwind's `--aspect-video: 16 / 9` or a full stop no digit follows after a fraction
  (`.5.p`), a hex colour of other than three, four, six or eight digits (`#zzz`, `#12345`), an `rgb()` of other
  than three plain numbers or an `rgba()` of fewer than four, a percentage counting as none (`rgb(100%, 0%, 0%)`,
  `rgba(0, 0, 0)`), a function with nothing between its parentheses other than `none()`, `filter()` and the URL
  functions `url()`, `url-prefix()` and `domain()` (`blur()`, `var()`), and a `var()` whose arguments the importer
  refuses (`var(--a,)`, which Tailwind v4 writes, `var(--a, 1px, 2px)`, `var(a)`, `var(--a b)`). The importer used
  to leave the whole file empty.
- A custom property whose `!important` is written in another case, which UI Toolkit's importer fails the file on,
  has it written in lower case.
- A custom property in a USS file calling a function UI Toolkit's importer does not know, such as `mysteps(3)` or
  `rgb()` written in capitals, is stored as the text it was written with, as one calling `cubic-bezier()` is. The
  importer used to drop the function with a warning.
