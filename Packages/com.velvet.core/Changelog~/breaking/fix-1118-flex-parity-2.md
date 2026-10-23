### Changed

- A `gap-*` adds to a child's own margin on the edge it spaces, as CSS adds a flex gap to an item's margin,
  where it used to replace it: `ml-2` on the second child of a `gap-x-4` row now puts `24px` before it rather
  than `16px`, a `[&>*]:ml-[2px]` on the row gives every child after the first `18px`, and a child's own margin
  on that edge wins over a `space-*` margin there with the gap added to it. `ml-auto` stays `auto`. Under
  `flex-wrap` the half-margins add to every margin a child declares, and the container's own margin keeps its
  value with the negated halves added, so `m-4 flex flex-wrap gap-4` writes `8px` on the container where it
  wrote `-8px`. A grid still writes its children's margins over their own.

- A direction or wrap utility on a composite widget — `V.ScrollView`, `V.Custom<Foldout>` and any other
  element that puts its children in an inner box — lays out that inner box, as a CSS flex utility lays out
  the element's own children. `V.ScrollView("flex gap-2", …)` now lays its content out in a row, since `flex`
  is a row, where the content stayed a column; write `flex flex-col` for a column. `flex-wrap` on the widget
  wraps its content and takes the gap's wrap path there. The class still applies to the widget's own box
  too.

- On a panel, a gap reads its container's direction and wrap from the resolved style once a layout pass has
  run, so a stylesheet rule of your own that outranks a direction class, or a `flex-direction` written from
  code, decides which edge the gap takes; the direction class used to win until the next reconcile. Off a
  panel, a horizontally scrolling `ScrollView` and a horizontal `TwoPaneSplitView` space their content as
  the row their stylesheet lays it out in, where the first application spaced it as a column.
