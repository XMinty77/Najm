# Third-Party Notices

Najm includes or consumes the following third-party components. This notice is
informational; the complete font license texts and upstream manifests are kept
beside the corresponding assets under `src/Najm.Text/Fonts/` and are configured
to accompany future packages.

## HarfBuzzSharp

- Packages: `HarfBuzzSharp` and `HarfBuzzSharp.NativeAssets.Linux` 8.3.1.5
- Project: https://github.com/mono/SkiaSharp
- Copyright: Microsoft Corporation and contributors
- License: MIT

The packages contain the managed HarfBuzz binding and the Linux native
HarfBuzz library used for deterministic in-memory font shaping.

## Latin Modern Roman

- Version: 2.005 (21 March 2021)
- Source: https://mirrors.ctan.org/fonts/lm.zip
- Designers/authors: Donald E. Knuth; Bogusław Jackowski and Janusz M. Nowacki
- Copyright: 2003-2021 B. Jackowski and J.M. Nowacki, on behalf of TeX Users Groups
- License: GUST Font License 1.0, based on LPPL 1.3c or later
- Included license: `src/Najm.Text/Fonts/LatinModernRoman-2.005/GUST-FONT-LICENSE.TXT`

Najm redistributes unchanged OpenType files. See the adjacent upstream README
and manifest for the complete authorship and file history.

## Latin Modern Math

- Version: 1.959 (5 September 2014)
- Source: https://mirrors.ctan.org/fonts/lm-math.zip
- Authors: Bogusław Jackowski, Piotr Strzelczyk and Piotr Pianowski
- Copyright: 2012-2014, on behalf of TeX Users Groups
- License: GUST Font License, an instance of LPPL as described upstream
- Included license: `src/Najm.Text/Fonts/LatinModernMath-1.959/GUST-FONT-LICENSE.txt`

Najm redistributes the unchanged OpenType math font. See the adjacent upstream
README and manifest for the complete attribution.

Exact source-archive, asset, documentation, and embedded-resource hashes are
recorded in `src/Najm.Text/Fonts/fonts.manifest.json`.
