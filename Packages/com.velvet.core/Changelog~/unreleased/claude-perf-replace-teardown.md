### Fixed

- Adding the rows of a list at ascending positions places each from the one added before it, where it used to walk the container's children from the first one for every row it added. A component whose children grow from none, as a row of three children does when it is created, no longer pays that walk, and the cost of adding the rows of a long list no longer grows with the square of its length.
- Re-rendering an element whose class list changes no longer runs the checks for variant, custom-property, arbitrary font and line-height tokens on a plain class (`flex`, `p-4`) it adds or removes, as creating an element already skipped them.
- Removing a row that is a plain container no longer unregisters the event bindings of the elements inside it, which leave with the row, as the click gates and text-item callbacks already did. A binding on the row itself, or on an element a pool keeps, is unregistered as before.
- Removing an element no longer takes the class and callbacks that watch a background image's size off an element that declared no percentage slice.
