# Fonts

Bitmap fonts shipped with CanvasManagement. Used on LED matrices via `CanvasManagement.BdfFontManager`.

| Files | Origin | License |
|-------|--------|---------|
| `4x6` … `10x20`, `clR6x12`, `tom-thumb` | X11 misc-fixed / Markus Kuhn UCS fonts | Public domain (see `AUTHORS`) |
| `helvR12.bdf` | X Consortium BDF (Adobe / DEC copyright notice in the file) | Redistributable with the bundled copyright comment |
| `texgyre-27.bdf` | TeX Gyre Adventor converted to BDF | [GUST Font License](https://www.gust.org.pl/projects/e-foundry/licenses/GUST-FONT-LICENSE.txt) |

These are **not** included:

- `Webcomicwhore-*.bdf` — All Rights Reserved (Teabeer Studios)
- `WaltDisneyScriptv4.1-*.bdf` — name/letterforms collide with the Disney trademark
- `q3arena.bdf` — Quake III Arena game font (not covered by the id Tech 3 GPL)

Keep any private copies on the machine only; `.gitignore` already excludes those names.

Optional Home Assistant icons: drop `materialdesignicons-webfont.ttf` + `meta.json` (Apache-2.0, [Pictogrammers](https://pictogrammers.com/library/mdi/)) into this folder before `deploy.ps1`. They are not vendored here.
