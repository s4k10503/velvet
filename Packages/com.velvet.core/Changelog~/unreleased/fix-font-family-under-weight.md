### Fixed

- A weight, italic or width class now keeps the `font-family` CSS gives the element. The element's own font decides
  first, whether an author's inline font or a rule of its own stylesheet, and the class writes that font's family's
  face rather than the parent's; removing the font classes gives an inline font the class replaced back. Otherwise the
  font the parent resolves to decides, so an author font set on an element between the element and its `font-<name>`
  ancestor, by a rule, a class, a pseudo-state or inline style, wins over that ancestor's class. A font given as a font
  file, or by the legacy `-unity-font` on the element or an ancestor, decides too. Where no registered family holds
  it, the element's definition is that font, written over a definition inherited from above, with the weight or italic
  emulated on it. A change reaches the element within the next updates of its panel.
