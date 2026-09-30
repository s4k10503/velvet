### Added

- Tailwind v4's `flex` shorthand utilities: `flex-<N>` (`flex: <N>`), `flex-<a>/<b>`
  (`flex: calc(<a>/<b> * 100%)`) and `flex-[…]`, which takes the CSS shorthand with `_` for each space,
  such as `flex-[2_1_120px]` or `flex-[none]`.
- `border-t-0`, `border-r-0`, `border-b-0`, `border-l-0`, `border-x-0` and `border-y-0`, which Tailwind
  ships and the bundled stylesheet did not.
