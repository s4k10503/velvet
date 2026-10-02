### Added

- The analyzer VEL600 warns about each `V.*` call a `Select` selector returns without a `key:` where
  `.ToArray()` hands the mapped array straight to a factory's children, as React warns about a list child
  with no key. A key given as `null` counts as none, and a factory that takes no `key:`, such as `V.Text`, is
  not reported.
