# PivotScope

Excel add-in for SSAS **Multidimensional** developers: write, run and
understand MDX where the work actually happens — in the PivotTable right in
front of you. Sibling of [CubeScope](https://github.com/dasimon/CubeScope),
whose engine it reuses.

**Permanently out of scope: Tabular, Power BI, DAX, Power Pivot.** Never
introduce a multi-engine abstraction "just in case".

Inspired by OLAP PivotTable Extensions (Greg Galloway, Ms-PL), **without any
code reuse**: PivotScope is MIT-licensed.

## Architecture decisions (settled — do not reopen without a strong reason)

- **Excel-DNA**, target `net10.0-windows`, **x64 only**. VSTO is ruled out
  (Microsoft: "VSTO Add-Ins can't be created with .NET"), and so is Office.js
  ("PivotTables created with OLAP are not currently supported").
- **Office task pane (CustomTaskPane) hosting WebView2**, which displays a
  Vue 3 + Monaco SPA. Validated on a real machine: keyboard focus works.
- **SPA embedded as resources**, served on the virtual origin
  `https://pivotscope.local/` by intercepting `WebResourceRequested`.
  No file is extracted to disk.
- **`postMessage` bridge** (`{id, method, params}` → `{id, ok, result|error}`),
  not `AddHostObjectToScript`. The router **never throws**: an exception
  would leave a pending promise on the SPA side.
- **`PivotScope.Core` never references `Microsoft.Office.Interop.Excel`.**
  That is what makes the logic testable without Excel.
- **CubeScope.Core as a pinned git submodule**, behind `ICubeMetadataReader`
  / `IMdxExecutor` / `ILevelMemberReader` — so the sharing mechanism stays
  replaceable.
- **No `MessageBox`**: a banner in the pane, plus a file log in
  `%LOCALAPPDATA%\PivotScope\logs`.

## Known pitfalls (paid for once, do not rediscover them)

### Excel-DNA and COM

- **A `CustomTaskPane` instantiates its control through COM.** Without
  `[ComVisible(true)]` + `[ComDefaultInterface(typeof(I…))]` on an interface
  (even an empty one), `CreateCTP` fails with `COMException 0x80004005` "Unable
  to create specified ActiveX control". Corollary: every public member of the
  control is a candidate for COM exposure, and a generic event
  (`EventHandler<string>`) cannot be represented there → keep the public
  surface empty, everything else `internal`.
- **By default Excel-DNA produces a 32-bit `.xll` AND a 64-bit one.** Loading
  the 32-bit one into a 64-bit Excel gives "the file format and extension
  don't match", which makes you think the file is corrupted.
  `ExcelDnaCreate32BitAddIn=false`: a single deliverable.
- **Excel locks the `.xll` and its DLLs** as long as the process lives — a
  single open workbook, even an unrelated one, is enough. To build without
  quitting Excel: untick PivotScope in Options → Add-ins → Go. MSBuild names
  the lock: "The file is locked by: "Microsoft Excel (PID)"".
- **Threading**: Excel is STA on its main thread, WebView2 messages arrive
  on the UI thread. Every COM call goes through `ExcelThread` — otherwise
  intermittent `RPC_E_SERVERCALL_RETRYLATER`.
- **Culture**: on a French Excel, COM APIs that take formula strings expect
  English. The switch is confined to `InvariantFormattingScope`, applied at
  the COM boundary only.

### PivotTable object

- **A hierarchy `CubeField` exposes ONE `PivotField` PER LEVEL.** Writing
  `VisibleItemsList` on the wrong level answers "The item could not be found
  in the OLAP cube". The naming of these `PivotField`s is not documented:
  `PivotFilterApplier` tries the level's unique name, its last segment and
  the caption, then **logs all the candidates**.
- **`CubeField.IncludeNewItemsInFilter` must be `False`** before writing
  `VisibleItemsList`, otherwise the assignment **silently has no effect**.
  And `ClearManualFilter` is called on the `CubeField`, not the `PivotField`.
- **Excel only materializes the `CubeField` of a session measure after a
  refresh.** Creating a calculated measure then looking for its cube field
  fails: `RefreshTable()` must come first. (That is what the "Refresh
  data by default" of the original add-in was for.)
- **`CalculatedMember.IsValid` returns `True` on a disconnected PivotTable**:
  call `PivotCache.MakeConnection()` before relying on it.
- **`PivotCache.MissingItemsLimit` only works on NON-OLAP PivotTables.** It
  is not the mechanism behind "Clear Cache"; the original recreates the
  workbook's connection.
- `NumberFormat` is only valid for a calculated **member**, `DisplayFolder`
  only for a **measure**. Outside these cases, the setting is accepted then
  ignored.
- `PivotTable.MDX` throws if the PivotTable has no data item.
- `PivotCell.MDX` throws outside the values area and on a multi-select report
  filter.
- `CubeFields.GetMeasure` **is not** for displaying a calculated measure: it
  only concerns the implicit measures of an attribute hierarchy, and only
  for Count/Sum/Average/Max/Min. Use `AddDataField(cubeField, …)`.

### SSAS and MDX

- **NEVER go through `$SYSTEM.MDSCHEMA_MEMBERS`**: no support for `IN`,
  and a filter on `MEMBER_UNIQUE_NAME` scans the entire dimension. Everything
  goes through MDX.
- **`StrToMember` on a non-existent member does not throw**, it returns `null`
  (observed on a real cube).
- The user pastes **captions**, not keys ("Aurore" when the key is
  "PRD014"). `MemberResolver` tries: full unique name → key → caption, and
  enumerating a level only costs ~79 ms for 3,157 members.
- A caption carried by **several** members is reported as ambiguous, never
  resolved at random: filtering on the wrong member would produce a wrong figure.

### Front end

- **Do not wrap Monaco in a `<label>`**: a label intercepts clicks
  and redirects focus to its first control, so Monaco can no longer take
  it. Use `.field` / `.field-label`.
- **Monaco auto-closes brackets**: typing `[` writes `[]`. A completion that
  inserts its own `]` produces `]]` — the replaced range must swallow the
  trailing bracket.
- **`%(RecursiveDir)` yields a BACKSLASH** on Windows: without normalization,
  the Monaco worker is embedded as `spa/assets\x.js` while the URL asks for
  slashes → silent 404 and a dead editor. Check with
  `GetManifestResourceNames()`, not by eye.
- The three MSBuild embedding pitfalls inherited from CubeScope: hook
  `PrepareForBuild` (at `CoreCompile` the list is frozen, 0 resources); go
  through a qualified intermediate item for the `LogicalName` (otherwise
  `%(Filename)` evaluates to empty → `CS1508`); glob `**\*.*` and not `**\*`.
- **`System.Text.Json` serializes enums as numbers** by default: the SPA
  would compare `2` to `"Measure"`. `JsonStringEnumConverter` is set on the
  bridge, and a test locks it in.
- **`monaco-editor` 0.56 pulls in a vulnerable `dompurify`.** `npm audit fix
  --force` offers to downgrade Monaco to 0.53, which would break the exports
  map used by `monaco-core`: force the transitive dependency through
  `overrides` instead.
- `monaco-core.ts` is the import list of the slimmed-down Monaco, taken from
  CubeScope: **resynchronize it on every monaco version bump**.

## Working conventions

- Each phase ends with a binary usable day to day.
- Interface messages bilingual (French by default, English), code and symbols
  in English — see *Bilingual interface* below.
- Excel interop cannot be tested automatically: the counterpart is
  [`docs/acceptance-checklist.md`](docs/acceptance-checklist.md), run before each tag.
- **When the interop resists, log the actual inventory** (the `CubeFields`,
  the `PivotFields`, their names and types) before throwing. This reflex solved
  three bugs that the documentation alone could not settle.

## Status

**Phases 0 and 1 (2026-07-27)** — pane following the active PivotTable,
generated MDX, metadata explorer, filter by list of keys or captions.

**Phase 2 (2026-07-27)** — Monaco MDX editor with contextual completion,
free query → Excel range (cancellable all the way to the server), MDX
calculations (measures, members, sets) with number format, SQLite library,
PivotTable building conveniences with a ribbon indicator.

**Phase 3 (2026-07-27)** — "where does this figure come from" (full tuple,
expression, dependency graph), MDX assistant enriched with the PivotTable
context, context menu limited to three entries.

Phases 0 to 2 validated end to end on `SSAS01` / `Analytics` /
`Ventes`; phase 3 awaiting acceptance testing.

Known limitation of the assistant: `AiService.RunAsync` builds its cube context
from `cubes[0]` of the catalog, not from the current cube. On a multi-cube
catalog the injected context is **impoverished**, not wrong. If the quality of
the answers suffers, add a `cube` parameter to `RunAsync` in the
submodule — it would also be a fix for CubeScope.

**Packaging (2026-07-27)** — `build\pack.ps1` produces a folder of 4 files
(the packed `.xll` + three natives) and its zip, instead of the 76 files of
the build folder. `release.yml` workflow on `v*` tags.

**Remaining debt**: the repository has **no remote** and nothing is pushed.

### Packaging, in practice

- `ExcelDnaPack` only runs on **`dotnet publish`**, not on `build`: for an
  SDK project, `ExcelDnaPublishPath` stays empty and packing targets the
  publish folder. The packed `.xll` comes out in `bin\<conf>\<tfm>\publish\`.
- `ExcelDnaPackNativeLibraryDependencies=true` is set in the `.csproj` but
  **has no observable effect** with ExcelDna.AddIn 1.9: the `.xll` has the
  same size and contains neither `e_sqlite3` nor `WebView2Loader`. Hence the
  natives shipped alongside, in `runtimes\win-x64\native\` — the location where
  .NET resolves them. Retry on the next Excel-DNA upgrade.
- **A `.ps1` containing accented characters must have a UTF-8 BOM**: otherwise
  Windows PowerShell 5.1 reads the file as ANSI and fails to parse it. `pwsh`
  does not have this flaw, but we do not choose the interpreter of whoever runs it.
- The only test that counts: extract the zip into an **isolated** folder and
  load the `.xll` from there. Saving a calculation in the library exercises
  SQLite, and therefore native resolution.

**Phase 4: suspended.** It was meant to measure whether Excel queries the server
during an operation on the PivotTable. The question was settled without code:
copying the query displayed by PivotScope and replaying it in CubeScope is
enough — it is slow on its own, the cost is **in the cube**. A second profiler
here would duplicate CubeScope's. The two tools compose: one
shows the query, the other dissects it.

**Clear PivotTable Cache** remains out of scope: the only feature on the
roadmap that modifies the connection of the user's workbook.

### Bilingual interface

- `en.ts` is typed **`typeof fr`**: a forgotten key becomes a compilation
  error, not an empty text discovered in production.
- **Never write `|` in a vue-i18n message**: it is the plural
  separator, the text would be silently cut.
- The ribbon and the context menu **cannot** go through vue-i18n: they
  live in Excel. They follow **Excel**'s language (`LanguageSettings`),
  fixed at load time — a ribbon that changed language mid-session
  would need to be rebuilt entirely, for about fifteen words.
- The ribbon XML is **assembled and escaped** (`SecurityElement.Escape`): an
  unescaped apostrophe makes the ribbon invalid, and Excel silently ignores it.
- Out of our control, these stay in the server's language: SSAS errors
  and Excel's messages.

### On PivotTable slowness, what you need to know

- A **cold** OLAP PivotTable pays the full price of its query; after that
  everything is cached. Measured on a real cube: 5 minutes, then ~130 ms.
- **`PivotTable.MDX` does not reflect level visibility.** Comparing this
  query before/after an operation proves nothing — instrument abandoned after
  wrongly believing it conclusive. Only the measured time is informative.
- The right diagnostic reflex is not in PivotScope: **copy the query
  and replay it in CubeScope**.
