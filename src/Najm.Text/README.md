# Najm.Text

`Najm.Text` owns Najm's platform-independent typesetting realization. The
current foundation embeds the pinned Latin Modern defaults and shapes explicit
in-memory font bytes through a privately owned HarfBuzz pipeline. Public text
layout contracts will arrive with their complete Core rendering slice; this
assembly intentionally exposes no provisional layout API.

Font provenance and hashes are recorded in `Fonts/fonts.manifest.json`. The
verbatim upstream licenses, readmes, and manifests are kept beside the font
assets and included in packages. Repository-wide attribution is in
`THIRD-PARTY-NOTICES.md`.
