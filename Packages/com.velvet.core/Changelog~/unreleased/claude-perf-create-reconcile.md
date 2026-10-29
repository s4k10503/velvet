### Fixed

- Mounting a component inline no longer builds a node factory, patcher, element cleaner and child reconciler for it up front. They are built when the component first reconciles rows of its own, which a component whose render its parent's pass reconciles does not, so creating a list of components allocates fewer objects per row.
- A Button's click gate, which keeps a click from reaching a Button the React tree would not carry it to, registers its six callbacks once a portal's event bridge exists rather than for every Button with an `onClick`: with no portal mounted the callbacks have nothing to decide. A Button made before the first portal attaches its bridge is registered then.
- Creating an element looks for the marks of a variant, custom-property, arbitrary font or line-height token once per plain class (`flex`, `p-4`) instead of running a check for each kind, and remembers the class tokens that resolve to no inline style, so the static-scale scan does not repeat for a token it has already ruled out.
- Tearing an element down skips the side tables that hold nothing, and creating one with no style overrides no longer diffs them.
- Creating an element that declares no children, as a Label or a Button without children does, no longer runs a child reconcile into it, which had nothing to place.
- A component mounting or updating mid-play no longer replaces the context's paint leases, which dropped the holds of a running Motion play and let the class pass paint over it.
