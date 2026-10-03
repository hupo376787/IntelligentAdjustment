# v1.0 requirements baseline

This file captures the implementation decisions already agreed before UI coding begins.

## Product

- New replacement product; no legacy `.adl` compatibility requirement.
- User project is a single `.iap` SQLite file.
- Project settings are stored per project; new projects use application defaults.
- Internal numeric calculations/storage use full `double` precision. Decimal places are presentation/export settings only.

## Default settings

- tolerance mode: distance
- distance tolerance coefficient: 4 mm sqrt(km)
- station tolerance coefficient: 0.3 mm sqrt(stations)
- adjustment method: classical
- auto transition-point merge/recognition: off
- raw-observation change -> high-difference update: on
- distance unit m, 4 decimals
- height unit m, 5 decimals
- point name horizontal alignment centered
- point name vertical alignment top
- open last project at startup off

## Known heights

- duplicate point name input is rejected rather than overwritten
- a known height may exist before that point appears in the observation network
- deleting a known height does not erase old results; recalculation validates the current network again
- known points are protected from automatic transition-point classification, including purely numeric names

## Import

The user explicitly chooses the importer; no automatic vendor detector is required.

- level-difference OUT
- Leica DNA MDT/GSI
- Leica Sprinter TXT
- GeoMax/ZDL MDT
- Trimble DiNi DAT
- Sokkia SDL CSV/CS1/CS2
- Topcon DL DAT

Multiple selected instrument files default to one observation line per file.

## OUT format

Header:

`From To Hei_Diff Distance Station`

Then level differences separated by whitespace. A line containing `Height` starts the known-height section, where each row is:

`PointName Height`

## Transition points

UI can explicitly set AdjustmentPoint / TransitionPoint. User choice wins over automation.

Core operations:

- manually set/toggle selected roles
- auto-recognize transition candidates
- compress already-marked transition chains
- merge selected chain and delete intermediate transition points

Compression always preserves the sum of height difference, distance and station count.

## Results freshness

Changes to calculation inputs increment the project input revision. Existing results remain viewable but are stale when `ResultRevision != InputRevision`.

UI text:

`当前结果基于旧数据，工程数据已发生修改，是否更新？`

## WPF UI (next stage)

- traditional Menu + Toolbar
- modern HandyControl left navigation
- center Tab workspace
- professional editable DataGrids with Enter/Tab, row copy/paste, multi-delete, undo/redo, context menu, batch point-role changes and validation errors
- raw-observation edits prompt after row commit whether to recalculate affected level difference
- network sketch: drag nodes, marquee select, pan, wheel zoom around cursor, fit-to-view, delete coordinate, double-click reposition, optional snapping
- network graph derived from sketch coordinates + network topology
- graph/table two-way selection highlighting
- export graph as PNG and SVG

## Reporting (later stage)

- DOCX and XLSX, no Office Interop
- report layout follows the supplied old report's professional chapter/table organization, not its old template engine
- project standards are editable rather than hard-coded
- do not auto-claim acceptance/inspection statements that the software cannot verify
