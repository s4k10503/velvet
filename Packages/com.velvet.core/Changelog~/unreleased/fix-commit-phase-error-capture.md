### Fixed

- An error thrown by a layout effect, a passive effect or a ref setup, or by the cleanup of one an update commit
  replaces, now reaches its error boundary in a follow-up commit, as React's does, rather than inside the commit
  that threw. The other layout effects, ref setups and passive effects of that commit still run, and the boundary
  then shows its fallback and calls `OnCaughtError` from the commit that follows. Where two boundaries are nested
  and both capture an error in one commit, only the outer one renders its fallback and reports, since the inner
  one is replaced before it renders. A cleanup that throws while a component unmounts, while a Suspense hides its
  content, or while a removed ref detaches is still caught at once, inside the pass that removes it.
