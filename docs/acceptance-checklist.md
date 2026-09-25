# Manual acceptance testing

Excel interop is not covered by the automated tests — this is an accepted
limitation, not an oversight. This checklist is the counterweight: it is run
**before each tag**, on a machine with 64-bit Excel and a reachable SSAS
Multidimensional cube.

Record the tested version and the date at the bottom of the page.

## Preparation

- [ ] `npm ci && npm run build` in `src/PivotScope.Web`
- [ ] `dotnet build -c Release`
- [ ] Release the `.xll`. Excel is a **single process**: a single workbook
      still open, even one unrelated to PivotScope, is enough to lock
      `PivotScope64.xll` and the neighbouring DLLs, and the build fails with
      `UnauthorizedAccessException`.
      Two ways:
      - close **all** workbooks — check with
        `tasklist /FI "IMAGENAME eq EXCEL.EXE" /V` that no process remains;
      - or, without quitting Excel, **untick PivotScope** in File → Options
        → Add-ins → *Manage: Excel Add-ins* → Go. The file is
        released immediately. This is the preferred move during development.

      When in doubt, MSBuild names the lock itself:
      "The file is locked by: "Microsoft Excel (PID)"".
- [ ] Load `src/PivotScope.AddIn/bin/Release/net10.0-windows/PivotScope64.xll`
      via Excel → Options → Add-ins → *Manage: Excel Add-ins* →
      Go → Browse.
      **Do not** double-click the `.xll` or go through File → Open:
      Excel would take it for a workbook and display "the file format and
      extension don't match".

## Loading

- [ ] The **PivotScope** ribbon tab appears
- [ ] `%LOCALAPPDATA%\PivotScope\logs\pivotscope-<date>.log` contains
      "PivotScope loaded"
- [ ] No dialog box appeared at startup
- [ ] Excel has not put the add-in among its disabled items
      (Options → Add-ins → Manage: Disabled Items)

## Pane

- [ ] The "Volet PivotScope" button docks a pane on the right
- [ ] The pane displays the interface, not a blank page or a WebView2 error
- [ ] **Keyboard focus test** (phase 0 GO/NO-GO criterion): *Filtre*
      tab, type in the paste area — the characters are entered.
      ⚠️ This area appears **only** if an OLAP PivotTable is active: first do
      the "PivotTable context" section below, otherwise the pane only shows
      the degraded-mode message and there is nowhere to type.
- [ ] Ctrl+A, Ctrl+C and Ctrl+V work in this area
- [ ] The pane closes and reopens without error

## PivotTable context

- [ ] Cursor **outside** any PivotTable → "Placez le curseur dans un tableau croisé
      dynamique." No exception.
- [ ] Cursor in a **non-OLAP** PivotTable (Excel table source) → message
      explaining that only SSAS Multidimensional is supported
- [ ] Cursor in an OLAP PivotTable → correct server, catalog, cube and number
      of fields
- [ ] "Actualiser" after dropping a field reflects the change

## Generated MDX

- [ ] The PivotTable's MDX is displayed in the *Aperçu* tab
- [ ] "Copier" does put the query on the clipboard
- [ ] PivotTable **without any measure** → prompt message, no error
      (`PivotTable.MDX` throws in this case, it is documented)

## Metadata

- [ ] "Charger" fills the tree of dimensions and measures
- [ ] The text filter narrows the tree
- [ ] The levels of a hierarchy are listed with their unique name
- [ ] The first load opens the SSAS connection (visible in the log), the
      following ones are instantaneous

## Filter by list

- [ ] Select a field placed on rows, then a level
- [ ] **On a multi-level hierarchy**, filter on a level that
      is **not** the first one. This is the case that broke during acceptance
      testing on 2026-07-27: a CubeField exposes one PivotField per level, and
      targeting the wrong one answers "The item could not be found in the OLAP cube".
- [ ] Paste a **caption** (e.g. `Aurore`) whose key is different
      (e.g. `PRD014`) → resolved and applied
- [ ] Paste the **key** directly → same result
- [ ] Paste **3 valid values + 1 invalid** → the PivotTable is filtered on the 3
      valid ones, and the invalid one is listed in red
- [ ] Paste keys separated by commas, semicolons and
      tabs → all recognized
- [ ] Paste a list with duplicates → applied only once
- [ ] Paste **only** invalid keys → clear error message in the
      banner, PivotTable unchanged
- [ ] Paste a long list (> 100 keys) → transparent splitting into batches,
      complete result

## Free MDX query (phase 2)

- [ ] The editor shows MDX syntax highlighting
- [ ] Completion after `[Measures].` offers **only** measures, and
      insertion does not duplicate the prefix
- [ ] Completion after `[Dim].` offers the hierarchies of that dimension
- [ ] Completion after `[Dim].[Hier].` offers the members
- [ ] **F5** and **Ctrl+Enter** run the query
- [ ] Result in a new sheet: range written, address and duration displayed
- [ ] Result at the active cell, **cursor outside the PivotTable**: works (the
      connection is remembered, it does not require a PivotTable under the cursor)
- [ ] Active cell **inside** a PivotTable: refused with an explicit message
- [ ] Without headers: the first row contains data
- [ ] Invalid MDX: error banner carrying the SSAS message, nothing written
- [ ] **Stop** on a long query: effective stop, "Requête arrêtée."
      in grey and not in a red banner

## Calculations (phase 2)

- [ ] Create a simple **calculated measure** (`1`) → appears in the PivotTable
- [ ] Create a measure with a **display folder** → placed in that folder
- [ ] Create a **calculated member** with a parent hierarchy and the format
      `#,##0.00` → **formatted**, which no Excel interface allows
- [ ] Try a format on a *measure* → refused before reaching Excel
- [ ] Try a folder on a *member* → refused likewise
- [ ] Invalid MDX → clear message, no calculation left behind
- [ ] Recreate a calculation with the same name → replaces instead of failing
- [ ] Delete a calculation → disappears from the PivotTable and from the list

## Library (phase 2)

- [ ] Save a calculation → appears in the library
- [ ] Save the same name again for the same cube → **updated**, not duplicated
- [ ] "Charger" fills the form with all the fields, format included
- [ ] Close and reopen Excel → the library is still there
- [ ] Delete an entry → disappears

## Building (phase 2)

- [ ] "Charger" lists the cube's fields with their state
- [ ] Untick a field **not placed** → it disappears from Excel's field list
- [ ] Untick a field **placed on the PivotTable** → refused with an explanation
- [ ] "Tout réafficher" restores, the counter goes back to zero
- [ ] Turn off refreshing → the ribbon button is **released**
- [ ] Turn it back on → the button is **pressed** again, a single server round trip

## Context menu (phase 3)

- [ ] Right-click in a PivotTable → **three** PivotScope entries, no more
- [ ] "D'où vient ce chiffre ?" opens the pane on the right tab
- [ ] Unload the add-in → the entries disappear from the menu

## Where does this figure come from (phase 3)

- [ ] On a **value cell** → full tuple displayed, report filters
      included
- [ ] On a **header** or a **total** → clear message, no exception
- [ ] With a **multi-select report filter** → message explaining
      that it must be reduced to a single item
- [ ] On a **physical measure** → "physical measure" note, not an error
- [ ] On a **calculated measure** → expression, line number in the script,
      dependency tree, and the "used by" list
- [ ] "Expliquer avec l'IA" switches to the AI tab with the expression filled in

## MDX assistant (phase 3)

- [ ] **Without** `ANTHROPIC_API_KEY` → clear message, buttons disabled, no
      network call
- [ ] With the key → the four actions respond
- [ ] The answer takes the **table context** into account (it mentions the
      fields actually placed, not only the query)
- [ ] "Reprendre la requête du tableau" fills the editor
- [ ] **Stop** during an answer → clean interruption, no red banner
- [ ] An answer containing `<script>` or HTML is displayed **as text**, never
      interpreted

## Standalone deliverable (phase 5)

The test that decides whether PivotScope can be distributed. It has a history: on
CubeScope, the first published exe was broken once moved, and this was only
noticed on download.

- [ ] `pwsh build\pack.ps1` produces `artifacts\PivotScope.zip`
- [ ] The folder contains **4 useful files**: the `.xll` and three natives
      under `runtimes\win-x64\native\`
- [ ] Extract the zip into an **isolated** folder, outside the repository — typically
      `%USERPROFILE%\Downloads\PivotScope`
- [ ] Load `PivotScope64.xll` **from this folder**
- [ ] The pane opens, the SPA displays (managed assemblies properly merged)
- [ ] **Save a calculation in the library**: this is the action that
      exercises SQLite, and therefore `e_sqlite3.dll`. If it fails, the native is not
      resolved from this location.
- [ ] Close Excel, reopen, reload: the library has survived

## Automatic tracking and navigation (ergonomic redesign)

- [ ] **The pane follows the PivotTable without being asked**: move the cursor
      from one PivotTable to another → the header changes on its own, without clicking "Actualiser"
- [ ] Drop a field on the PivotTable → the displayed MDX updates on its own
- [ ] Move quickly across many cells → **a single** reload, not
      one per cell (notifications are grouped)
- [ ] Switch workbooks → the header follows
- [ ] **The header stays visible from all five tabs**
- [ ] The five tabs fit in the pane's width, without truncation
- [ ] Open *Tableau* → the fields load on their own
- [ ] Open *Calculs* → calculations and library load on their own
- [ ] Open *Ce chiffre* on a value cell → the analysis runs on its own
- [ ] Open *Requête* → the metadata explorer is there, collapsed
- [ ] Outside a PivotTable, switching tabs triggers **no** server call

## Bilingual interface

- [ ] The **FR / EN** selector is in the header, visible from all tabs
- [ ] Switching to EN **immediately** translates tabs, buttons and messages —
      without reloading the pane
- [ ] Close and reopen Excel: the chosen language is **kept**
- [ ] Error messages coming from Excel or SSAS **stay in the server's
      language** — out of our control, this is expected
- [ ] In EN, an answer from the AI assistant arrives **in English**
- [ ] The ribbon and the context menu follow **Excel**'s language, not the
      pane's: this is the accepted trade-off, the ribbon is built only once

## Code review fixes (unreleased)

Behaviours changed after the full code review of 2026-09-24. Each one
depends on Excel and could only be reasoned about, not run: check them here.

### Free query → sheet

- [ ] A query returning numbers writes **numbers**: `=SUM()` over the result
      is not 0, and a French-formatted value is not re-read as another number
- [ ] A key such as `0012` keeps its zeros; a caption starting with `=` stays
      text (the leading apostrophe does not show in the cell)
- [ ] A cell in error is written as **#VALUE!**, not as the text `#ERREUR`
- [ ] Known trade-off: numbers arrive **unformatted** (a percentage shows as
      0.125) — the raw value is the price of a sheet you can calculate on
- [ ] "Active cell" unticked, start a long query, then click elsewhere: the
      result lands at the cell that was active **at launch**
- [ ] Destination not empty: nothing is written, the pane offers
      **Overwrite / New sheet / Discard**, and each choice does what it says
- [ ] A result that would overlap a PivotTable, overflow the sheet, or land
      on a protected sheet is refused with a readable message
- [ ] The pane shows the **server and catalog** the query will run on
- [ ] Leave the PivotTable to pick the destination cell: the editor, F5 and
      completion **stay available**
- [ ] During a long query, completion and the explorer still answer (second
      SSAS connection)
- [ ] Start a query, then "Explain" in the AI tab: each **Stop** button stops
      its own operation only

### Filter by list

- [ ] A multi-line paste containing `Actions, Europe` filters on that one
      caption; a single typed line `EUR, USD` still gives two values
- [ ] Start a filter, click into **another** PivotTable before it ends: the
      filter lands on the table it was launched from
- [ ] A rejected list (value from another level): the **previous filter is
      still there** afterwards, not an unfiltered table
- [ ] On a report-filter field, a list of several members is applied (multiple
      selection switched on)
- [ ] Changing the field resets the level

### Calculations

- [ ] Number format of a calculated member: **Default / Number / Percent**
      (Excel's `XlCalcMemNumberFormatType`) — Percent really shows a percentage
- [ ] A library entry saved by 0.5.0 with a format such as `0.00%` loads as
      Percent
- [ ] Replace a working measure with a broken expression: the error shows,
      and the **previous version is still there**, at its place in the values area
- [ ] A named set is created under `[Name]` and appears in the field list
- [ ] Remove a calculation, remove a library entry, replace a calculation:
      each asks for a second click
- [ ] Two members named `Total` under two hierarchies are two library entries

### Several windows, several tables

- [ ] Two workbooks open: the pane opens in the window it was asked from
- [ ] Close the workbook that holds the pane, open the pane again from the
      other: it opens (no restart needed)
- [ ] Two sheets each with `PivotTable1`, on different cubes: moving from
      one to the other (click, or Ctrl+PgDn) refreshes header, fields and
      calculations — nothing from the first table is left
- [ ] Right-click → "Where does this figure come from?" on a **closed** pane:
      it opens on that tab
- [ ] Context menu entries still work after a long session (they no longer
      depend on the garbage collector)

### Building

- [ ] "Defer layout" toggled from the pane: the ribbon button follows
- [ ] **To confirm**: toggle "Defer layout", then check in a second action
      that `ManualUpdate` is still True (Microsoft documents that it resets
      when the macro ends)
- [ ] "Apply and refresh" on a table whose refresh is disabled in the
      workbook: refused with a message, the setting is **not** changed

### Hardening

- [ ] Release build: F12 / devtools unavailable, F5 in the pane does not
      reload it (F5 in the editor still runs the query)
- [ ] A PivotTable on the workbook Data Model (Power Pivot) is reported as
      out of scope
- [ ] Restart the SSAS service while Excel stays open: the next action
      reconnects instead of failing until Excel restarts
- [ ] Protected sheet / cell in edit mode: a readable message, not an HRESULT

## Unloading

- [ ] Close Excel: "PivotScope unloaded" in the log
- [ ] No leftover `EXCEL.EXE` process

---

| Tested version | Date | Tester | Result |
|---|---|---|---|
| 0.1.0 (phases 0-1) | 2026-07-27 | David | ✅ loading, pane, keyboard focus, PivotTable context, MDX, metadata, filter by caption — validated on `SSAS01` / `Analytics` / `Ventes` |
| 0.2.0 (phase 2) | 2026-07-27 | David | ✅ Monaco editor and contextual completion, free query → range (new sheet and active cell, with and without headers), creation of a calculated measure displayed in the PivotTable |
| 0.3.0 (phase 3) | 2026-07-28 | David | ✅ provenance of a cell, MDX assistant, context menu |
| 0.3.0 (deliverable) | 2026-07-28 | David | ✅ **zip extracted into an isolated folder, loaded from there: works** — the managed assemblies are properly merged and the natives resolved |
| 0.4.0 | 2026-07-28 | David | ✅ level picker, automatic PivotTable tracking, pane redesign into five tabs, permanent header, ribbon icon |
| 0.5.0 | 2026-07-29 | David | ✅ bilingual FR/EN interface — immediate switch, choice kept, AI answers in the current language |
