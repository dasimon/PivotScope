# PivotScope

An Excel add-in for people who write MDX against **SQL Server Analysis Services
Multidimensional** cubes — and who spend their day inside a PivotTable rather
than inside a query editor.

PivotScope docks a task pane next to your PivotTable and keeps it in sync: the
MDX Excel actually sends to the server, the cube's metadata tree, and a filter
that takes a pasted list of business keys instead of a checkbox marathon.

It is the Excel counterpart of [CubeScope](https://github.com/dasimon/CubeScope),
and shares its engine.

> **Out of scope, permanently:** Tabular, Power BI, DAX, Power Pivot (the Excel
> Data Model). PivotScope targets SSAS Multidimensional and nothing else. That
> restriction is what keeps it small.

## Status

Early development. Phase 1 (pane, MDX view, metadata explorer, filter by list)
is the first usable milestone. See
[the design spec](docs/superpowers/specs/2026-07-26-pivotscope-design.md).

## Requirements

- Excel **64-bit**, 2016 or later
- **.NET Desktop Runtime 10 (x64)**
- WebView2 Evergreen runtime (installed by default on Windows 11)
- An SSAS Multidimensional instance, reachable with Windows integrated security

No installer, no admin rights, no COM registration.

To load it: **Excel → File → Options → Add-ins → Manage: _Excel Add-ins_ → Go →
Browse**, then pick `PivotScope64.xll`.

> Do **not** double-click the `.xll` or open it through File → Open. Excel then
> treats it as a workbook and warns that "the file format and extension don't
> match" — the add-in is fine, the loading path is wrong.

Only a 64-bit build is produced, on purpose: 32-bit Excel is out of scope, and
shipping both flavours only invites loading the wrong one.

## Building

```bash
git clone --recurse-submodules https://github.com/dasimon/PivotScope.git
cd PivotScope
dotnet build -c Release
```

`external/CubeScope` is a pinned git submodule providing the SSAS engine
(metadata, MDX execution, AI assistance). PivotScope consumes it behind its own
interfaces, so the sharing mechanism can change without touching feature code.

## Credits

PivotScope is **inspired by**
[OLAP PivotTable Extensions](https://olappivottableextensions.github.io/) by
Greg Galloway, licensed under Ms-PL. PivotScope is an independent rewrite under
the MIT license and contains **no code** from that project.

## License

MIT — see [LICENSE](LICENSE).
