### Changed

- Reading which class families an element's classes belong to no longer copies a string of a class written behind a
  variant (`dark:shadow-lg`) or with an important marker (`!p-2`, `p-2!`, `hover:!p-2`). A patch takes that reading
  again wherever it is no longer cached for the class array.
