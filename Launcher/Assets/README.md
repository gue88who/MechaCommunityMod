# Launcher artwork

This small subset of Mechabellum's UI export supplies the launcher's native blue
and amber button states, disabled button, panel frame, checkmark, and loading art.
`provenance.json` preserves original sprite names, asset files, path IDs, slice
borders and SHA-256 hashes from Steam build **25924694** (2.0.0.4.2331).

`RobotoCondensed-Regular.ttf` and `Xolonium-Bold.ttf` are the game's UI fonts,
extracted from `globalgamemanagers.assets`. Fonts are registered privately for
the launcher process and are not installed into Windows.
Their original copyright notices and Apache 2.0 / SIL OFL 1.1 license texts
are in `Font-NOTICES.txt`, `Roboto-LICENSE.txt`, and `Xolonium-LICENSE.txt`.

Artwork and the supplied font files are third-party game assets and are excluded
from this repository's MIT source-code license. Their original rights remain
with their respective owners. The launcher is a community project.

These files are embedded into the launcher executable's managed assembly; the
installer does not need an external asset pack or Python on the user's device.
