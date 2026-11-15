### Fixed

- `UseImperativeHandle` now sets its handle up in hook order with the component's layout effects: a layout effect
  declared before it runs first and one declared after it runs after. Before, every handle was set up ahead of all of
  the component's layout effects.
- A replaced ref callback's cleanup now runs where its element's place in the commit is reached, after the cleanups of
  the components below the element, rather than ahead of every cleanup of the commit.
