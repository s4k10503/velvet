### Changed

- Velvet keeps a table of which utility families (shadow, gap, z-index and a dozen more) each class array holds, and a
  patch reads an array into it wherever the table does not hold that array. That read no longer copies a string of a
  class written behind a variant (`dark:shadow-lg`) or with an important marker (`!p-2`, `p-2!`, `hover:!p-2`).
