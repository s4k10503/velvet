### Changed

- A skewed `V.Div`, `V.Button` or label is picked by its sheared shape instead of the bounding rectangle of the sheared
  box: a pointer over a corner the shear leaves bare reaches what is behind the element. The elements Velvet builds for those three are now `VisualElement`, `Button` and `Label` subclasses, so
  code that compares `GetType()` against `typeof(VisualElement)`, `typeof(Button)` or `typeof(Label)` must test with
  `is` instead.
- A radius that does not fit its box is scaled on the elements Velvet builds for element and motion nodes, whatever
  its source: a rule of your own stylesheet and a length your code writes to `style` are fitted when the element is
  laid out and each time its size changes, as a `rounded-*` class already was. An element that relied on overlapping
  radii shows them reduced.
- A class of your own is ranked by the properties its rule sets, read off a probe element once per class and panel:
  `my-card dark:my-card-dark` takes `my-card` off the element while `dark:` holds, when `my-card-dark` sets every
  property `my-card` does, where the two used to tie on the stylesheet's declaration order. A class starting `unity-` or `velvet-` is never probed.
