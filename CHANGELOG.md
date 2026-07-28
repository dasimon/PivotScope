# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning: [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Excel add-in shell** — Excel-DNA on .NET 10, ribbon tab, file log in
  `%LOCALAPPDATA%\PivotScope\logs`. Startup registers the ribbon and nothing
  else; SSAS, SQLite and WebView2 initialise lazily on first pane open.
- **Task pane** — WebView2 hosted in an Office `CustomTaskPane`, serving an
  embedded Vue 3 SPA over the virtual origin `https://pivotscope.local/`. No
  files are extracted to disk.
- **Typed bridge** — `postMessage` envelope `{id, method, params}` →
  `{id, ok, result, error}`. The router never throws: every failure comes back
  as `ok:false`, so no promise is ever left pending on the SPA side.
- **PivotTable context** — server, catalog, cube, generated MDX
  (`PivotTable.MDX`) and field layout, refreshed on demand. Degrades to an
  explanatory message outside a PivotTable or on a non-OLAP one.
- **Metadata explorer** — filterable tree of dimensions, hierarchies, levels
  and measure folders, read through `CubeScope.Core`.
- **Filter by list** — paste values and filter a PivotTable field on a chosen
  level. Each value is resolved in three steps: a full unique name is taken
  as-is, then a member **key** via MDX `StrToMember` in batches of 100, then a
  **caption** via a one-off enumeration of the level (cached per cube and
  level). `$SYSTEM.MDSCHEMA_MEMBERS` is never used — it has no `IN` support and
  scans the whole dimension. A caption borne by several members is reported as
  *ambiguous* rather than picked at random: filtering the wrong member would
  produce a silently wrong figure. Unresolved values are reported, never
  swallowed.
- Solution conventions: central package management, shared build properties,
  `TreatWarningsAsErrors`, `.editorconfig`, `.gitattributes`.
- `external/CubeScope` pinned as a git submodule, isolated from PivotScope's
  build conventions by a stopper `Directory.Build.props`.
- CI on `windows-latest`: build, unit tests, NuGet and npm vulnerability audits.
- Manual acceptance checklist in [`docs/recette.md`](docs/recette.md), covering
  what automated tests cannot: Excel interop.

### Added — phase 2

- **MDX editor** — Monaco with the MDX grammar, folding and function catalogue
  reused from CubeScope. Completion is context-aware: `[Measures].` offers
  measures only, `[Dim].` offers that dimension's hierarchies, `[Dim].[Hier].`
  lazily loads members. F5 and Ctrl+Enter run the query.
- **Free MDX → Excel range** — write results to a new sheet or from the active
  cell, with or without headers. The grid is written in a single `Range.Value2`
  assignment; writing cell by cell through COM is orders of magnitude slower.
  Writing over a PivotTable is refused — it would corrupt it.
- **Stop button** — cancels through to the server via `AdomdCommand.Cancel()`,
  not merely abandoning the wait. A cancellation reports as such, in grey, not
  as an error.
- **Calculations** — calculated measures, members and named sets through
  `AddCalculatedMember`, with display folders for measures and **number formats
  for members** — a setting Excel exposes to no user interface, only to macros.
- **Calculation library** — SQLite, versioned schema, upsert by name and cube,
  so a calculation written once can be replayed in another workbook.
- **Build panel** — choose which cube fields stay visible in the PivotTable
  field list (`CubeField.ShowInFieldList`), and toggle automatic refresh with a
  ribbon indicator that keeps its state visible. Hiding a field currently laid
  out on the PivotTable is refused rather than silently removing it from view.

### Added — phase 3

- **"Where does this figure come from?"** — right-click any value cell and get
  its full MDX coordinates (report filters included, via `PivotCell.MDX`), the
  expression that produces it, the line it sits on in the cube's MDX script, and
  its recursive dependency tree. A physical measure or an unreadable script are
  reported as *notes*, not errors: showing the tuple alone beats showing nothing.
- **MDX assistant** — explain, optimise, spot anti-patterns, reformat. On top of
  the query it sends the **state of the PivotTable** — fields on rows, columns,
  filters, and the measures displayed — which is the context CubeScope cannot
  provide and what makes the answer worth reading. Degrades cleanly without
  `ANTHROPIC_API_KEY`; cancellable.
- **PivotTable context menu** — three entries, deliberately: open the pane,
  filter by a list, where does this figure come from. The original add-in
  injected eight and made the menu unreadable. Entries are removed on unload,
  otherwise Excel keeps dead ones.

Markdown is rendered by a small hand-written renderer rather than another
dependency: a model's output is untrusted input, and everything is escaped
before rendering.

### Verified

Phases 0 and 1 were validated end to end on a real SSAS Multidimensional cube
on 2026-07-27 — see [`docs/recette.md`](docs/recette.md). The keyboard-focus
question that gated the whole architecture (WebView2 inside an Office task
pane) is answered: it works, and the fallback to a modeless window is not
needed.

### Notes

- `Microsoft.Identity.Client` is pinned to 4.86.1 to override the vulnerable
  4.56.0 that ADOMD pulls transitively. Re-check on every ADOMD upgrade.
- The WPF flavour of the WebView2 control is removed from the reference set
  before `ResolveAssemblyReferences`; only the WinForms control is used, and
  its presence caused an unresolvable `WindowsBase` conflict (MSB3277).
