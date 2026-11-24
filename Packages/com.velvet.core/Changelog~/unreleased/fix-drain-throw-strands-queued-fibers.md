### Fixed

- In the Editor with `FiberStrictMode.Enabled`, a render whose output held a value whose `ToString` throws, such as a
  component's props, threw, because the StrictMode diagnostic read that value outside the guard around its second
  render. Where an update pass was rendering it, the components queued behind it in that pass were left unrendered, and
  a later update of the same priority to one of them did not queue it again. The diagnostic now reads such an output as
  unreadable instead of throwing, and reports it as impure only when the other of its two renders produced a readable
  output. Separately, an exception that leaves an update pass as it renders a component no longer strands the
  components queued behind it: they go back on the queue, and a later pass renders them with the updates they received
  since, as React keeps a root's updates pending until a render that takes them commits. Updates queued in a transition
  are kept the same way.
