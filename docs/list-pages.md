# List pages: the recipe

The Containers page (`src/WslcAgent.UI/Pages/Containers.razor`) is the
settled layout for every list page (Images, Volumes, Networks, …). A new
list page copies its structure and changes only the data: the columns,
the card body and the actions.
Everything below is already implemented by shared components; a page never
sets grid look parameters, paddings or widths itself. The parameters of each
control are in `docs/ui-controls.md`.

## Page skeleton

```razor
@page "/images"
@inherits ListPageBase<ImageSummary>

<PageShell Title="Images">
    <TopbarSearch>
        <SearchBox @bind-Value="Search" />   @* centred in the title bar *@
    </TopbarSearch>
    <TopbarNav>
        <BulkBar Count="Selected.Count">
            <MudButton Size="Size.Small" Variant="Variant.Filled" Color="Color.Error" OnClick="@(() => BulkRemoveAsync("image", (i, ct) => Api.RemoveImageAsync(i.Reference, force: false, ct)))">Remove</MudButton>
        </BulkBar>
        <MudButton Size="Size.Small" Variant="Variant.Filled" Color="Color.Error" StartIcon="@WslcIcons.Prune" OnClick="PruneAsync" Class="ml-2" title="…">Prune</MudButton>
    </TopbarNav>
    <ChildContent>
        @* 1. header row *@
        @* 2. <MudAlert Severity="Severity.Error" Class="mb-2 flex-shrink-0">@Error</MudAlert> when Error is not null *@
        @* 3. ListGrid or CardsGrid, chosen by View *@
    </ChildContent>
</PageShell>

@code {
    protected override async Task<IReadOnlyList<ImageSummary>> LoadAsync() => (await Api.GetImagesAsync()).Images;
    protected override bool Matches(ImageSummary i, string search) => Has(i.Repository, search) || Has(i.Tag, search) || Has(i.Id, search);
    protected override string KeyOf(ImageSummary i) => i.Reference;
    protected override string NameOf(ImageSummary i) => i.Reference;
}
```

`ListPageBase<TItem>` (`Components/ListPageBase.cs`) owns everything a list
page does the same way: `Api`, `Snackbar`, `Dialogs`, `Items`, `Selected`,
`Search`, `Loading`, `Error`, `View`, `Visible`, the 5-second poll,
`RefreshAsync`, `RunAsync` / `ConfirmAndRunAsync` (page verbs), `BulkAsync` /
`BulkRemoveAsync` (selection verbs), `OpenAsync<TDialog>` (form dialogs) and
`Dash` / `Has`. A page overrides the four members above and nothing else.

`PageShell` hands `Title`, `Status`, `TopbarSearch`, `TopbarNav` and `SectionActions` to the
layout (`MainLayout` + `PageChrome`). The layout draws the navigation button, the top
bar and the section-actions row; the page never touches them.

## 1. Header row (stats left, tools right)

```razor
<MudStack Row="true" AlignItems="AlignItems.Center" Wrap="Wrap.NoWrap" Spacing="2" Class="wslc-toolbar mb-1 flex-shrink-0">
    <PageStats>
        <PageStat Label="CPU" Value="@CpuText" />
        <PageStat Label="Memory" Value="@MemoryText" />
    </PageStats>
    <MudCheckBox T="bool" Value="..." ValueChanged="..." Dense="true" Size="Size.Small" Label="Running" />
    <ViewModeToggle @bind-Value="View" />
    <MudIconButton Icon="@Icons.Material.Filled.Refresh" Size="Size.Small" OnClick="RefreshAsync" Disabled="Loading" title="Refresh" />
</MudStack>
```

The page's verbs (Run, Create, Pull, Build, Import, Load, Hub…) are not in
this row: they go in `PageShell`'s `Verbs` as `VerbFab`s, and the layout draws them as the menu over
the navigation button. The row keeps the stats, the filters, the view toggle and
Refresh.

Fixed by `.wslc-toolbar` in `wslc-agent-ui.css`, not by the page:

- one line, always (`Wrap.NoWrap`); the stats step their text down at 1150px
  and 950px of row width, the search box narrows at 700px; nothing is hidden;
- surface tone, line border, rounded corners, 4px 8px padding;
- every control 26px tall, body2 text, 18px icons, filled square-cornered
  buttons with sentence-case labels.

`PageStats` holds the stats (it takes the spare width and truncates); `PageStat` is the only way to write a "label value" pair; `SearchBox` sits first in `TopbarNav`, before the bulk bar and the page's verbs, on every width (no `Label` when
the value names itself, as "6 volumes"); `SearchBox` the
only search box. A verb that is not built yet stays a normal button that calls
`NotYet(feature, slice)` and says which slice brings it.

## 2. Table view: `ListGrid`

```razor
<ListGrid TItem="ImageSummary" Items="Visible" Loading="_loading" EmptyText="@EmptyText"
          MultiSelection="true" @bind-SelectedItems="_selected">
    <Columns>
        <SelectColumn T="ImageSummary" Size="Size.Small" StickyLeft="true" HeaderClass="@GridColumn.Select" CellClass="@GridColumn.Select" />
        @* status dot only when the resource has a state; title-less *@
        <TemplateColumn T="ImageSummary" Title="" Sortable="false" Resizable="false" StickyLeft="true"
                        HeaderClass="@GridColumn.Bullet" CellClass="@GridColumn.Bullet">
            <CellTemplate><StateDot State="@context.Item.State" /></CellTemplate>
        </TemplateColumn>
        @* identity column, pinned left *@
        <PropertyColumn T="ImageSummary" TProperty="string" Property="i => i.Repository" Title="Repository" StickyLeft="true" />
        @* the rest, in the page's declared order; empty values as a dash *@
        <PropertyColumn T="ImageSummary" TProperty="string" Property="i => i.Tag" Title="Tag" />
        <PropertyColumn T="ImageSummary" TProperty="string" Property="i => i.Id" Title="ID" CellClass="mud-text-secondary" />
        <PropertyColumn T="ImageSummary" TProperty="string" Property="i => i.Size" Title="Size">
            <CellTemplate>@Dash(context.Item.Size)</CellTemplate>
        </PropertyColumn>
    </Columns>
    <Actions>
        <TemplateColumn T="ImageSummary" Title="Actions" Sortable="false" Resizable="false" DragAndDropEnabled="false" StickyRight="true" CellClass="@GridColumn.Actions">
            <CellTemplate><ImageActions Image="context.Item" Changed="RefreshAsync" /></CellTemplate>
        </TemplateColumn>
    </Actions>
</ListGrid>
```

What `ListGrid` fixes (never repeat these on a page):

| Setting | Value | Where |
| --- | --- | --- |
| Dense / Striped / Bordered / Hover / FixedHeader | off / on / on / off / on | `ListGrid.razor` |
| ColumnResizeMode | `Container` (a column grows into the width the last column is holding; `Column` mode cannot move content-width columns); double-click on a handle drops the dragged width and the column returns to the one the application gives it | `ListGrid.razor` + `wslc-agent-ui.js` |
| DragDropColumnReordering | on, drag icon hidden (the header is the drop zone) | `ListGrid.razor` + CSS |
| ShowColumnOptions | off | `ListGrid.razor` |
| Breakpoint | `None` (always a table; never MudBlazor's stacked phone layout) | `ListGrid.razor` |
| SortMode / Filterable | `Single` / off | `ListGrid.razor` |
| The last data column | takes the width left over (`GridColumn.Stretch(n)` / `StretchNumber(n)`), so no empty strip stands between it and the actions; its declared width is its floor | `GridColumn` + CSS |
| Pager | last line, page sizes 10/25/50/100, 50 per page, 28px, body2 text | `ListGrid.razor` + CSS |
| Column width | every column declares one; only the last data column is `auto` (`table-layout: fixed`) | CSS |
| Every data column | a fixed width in characters, `GridColumn.Width(n)` (or `Number(n)`) on the header and the cells, sized for the widest real value and no wider: a column is never as wide as what happens to be in it, so a value that comes and goes (CPU, memory, disk while a container starts or stops) cannot move the columns after it — the actions at the end above all, which the pointer is on. A cell that does not fit keeps one line and ends in an ellipsis. The spare width goes to the last column. The steps that exist are listed in `wslc-agent-ui.css`; a new one is a line there | `GridColumn` + CSS |
| A column of numbers (Ports, CPU, Memory, Mem%, Disk, an image's Size) | right-aligned, **title and values alike**, so the figures are read down their last digit; text columns stay at the left. `GridColumn.Numeric`, or `GridColumn.Number(width)` with a fixed width. A value carrying a unit (2.84%, 899.5MiB / 30.98GiB, 3000→8080) is a number | `GridColumn` + CSS |
| A numeric header | its column menu moves to the left end of the cell (the header row is reversed), so the title's last letter sits over the values' last digit | CSS |
| Air between columns | every column carries `--wslc-col-air` characters beyond its widest value (one number, set in `wslc-agent-ui.css`); the alignment puts them where the text is not | CSS |
| Sorting by state | the dot column sorts running first, then stopped (`SortBy`) | `Containers.razor` |
| Selection column | 32px, small checkbox | `GridColumn.Select` + CSS |
| Status-dot column | 20px, no title | `GridColumn.Bullet` + CSS |
| Pinned columns | selection, dot, identity left (offsets 32px, 52px); actions right; they keep the row tone | CSS |
| Rows | one line per cell, 3px vertical padding, 0.75rem text; header 0.72rem bold | CSS + theme |
| Row identity | `ListRow<TItem>`, equal by the page's `KeyOf`: a poll updates cells in place instead of redrawing rows, so ticks and open menus survive it | `ListRow` + `ListPageBase.Rows` |
| A stopped WSLC session | the page goes dead with it: `PageShell RequiresSession="true"` says so above the screen and greys it out, pointer and keyboard alike (`inert` plus `.wslc-session-off`), in its content and in the title-bar and section-action slots the layout draws for it; it stops loading and polling and drops what it held. Starting the session wakes every screen at once, with no reload. System and Settings never carry the flag: Compact VHDX is what a stopped session is for | `SessionState` + `PageShell` + `MainLayout` + `ListPageBase` |
| Header and pager tone | primary mixed 18% into the surface | CSS |

Column types: `PropertyColumn` with explicit `T` and `TProperty` (RenderFragment
generic inference needs them), `TemplateColumn` for composed cells. A
`TemplateColumn` that is a data column (Ports) needs `Resizable="true"
DragAndDropEnabled="true"`: MudBlazor defaults both to false on template
columns, so it would be the one column that cannot be resized or moved.
Empty values render as a dash through a page-local `Dash(string)` helper.
`SelectColumn` needs `Size="Size.Small"`.

Columns, their order, their titles and which are pinned are part of each
page's contract: they are declared once in the page and changed on purpose,
never invented per screen.

### A paged grid, a detail row, a strip and a rail (Logs)

The Logs page (`Pages/AgentLogsPage.razor`) is two rows in the action body's
main column. The first is the same `ListGrid`, pager included: every entry
the API hands over (its cap, 5000) is in the list and the pager at the
bottom walks through them 50 at a time, the page kept in the URL (`?page=`)
as on every list, so Back returns to it and a filter change sends it back
to the first; the reader sorts and pages the log as they like. The second
row is **the Activity list** (`CliActivityList`, the CLI Activity
list: each `wslc` command in its own box, its header one row of
fixed columns — +/−, start 9ch, type 11ch, title with session stretching,
status chip 9ch, time taken 7ch right, exit 8ch right (`.wslc-cli-row`, a
CSS grid in characters like the grid's columns) — and opened,
`CliCommandDetail` — the command line, Output and Errors pretty-printed
when JSON, "No output captured."), the latest at the bottom
as in a chat, the running ones last; it takes the type toggles and no other
filter, keeps its open rows by command id across reloads, and it is what
Autorefresh follows. It draws **the last 200 commands** (CLI Activity's
capacity) and a panel's content only once opened: every command loaded was
five thousand panels formatting their JSON per render, and the page froze. `CliCommandDetail` is the same block the grid's child
row shows under a command's log row, so the two never drift apart. The two
rows sit in **MudBlazor's `MudSplitPanel`** (`Horizontal`, transparent, 6px
gap, 60px minimum per pane, double click resets to where it opened): its
panes are plain blocks with a height in pixels, so each gets
`.wslc-split-pane` (a flex column) for the grid and the list to fill them,
and `.wslc-split-divider` draws a line through the gap. The panel opens
where it was left (`FirstPanelInitialSize` from the remembered grid height);
the first time on a device it would open at half and half, so it stays
`visibility: hidden` (`.wslc-split-unplaced`) until the page has read the
divider's place, worked out the total and put the divider where the list
gets a tenth — no size shown and then changed. **The page is remembered on
this device** (`LogsPreference`, `wslcAgent.logs`, the pattern of
`ViewPreference`): the hidden types, the level, Autorefresh and the
divider's place (the grid's height, read after every pointer or key
release in the two rows, since the panel reports no drag). Arriving with
no filter in the address (the sidebar's link) applies the remembered types
and level to the address, with `replace`; an address that carries one
wins. **The grid's list is anchored**: the first full load takes the last
5000 entries and keeps the first one's id; every reload asks
`GET /logs?from=<id>`, so the list only grows at the end and the page being
read never moves (the last 5000 alone is a window that slides by a row with
every entry the agents write, and page 1 with it). A delete drops the
anchor and starts over. Virtualisation was tried for
the grid and given up: it drew slowly and read
backwards from the pager's point of view; the settled answer is the paged
grid for reading the log and the strip for watching it. The filtered lists
are built once per load or filter change, not per render (`Visible`,
`Strip`). The grid's rows open a detail under themselves through the grid's
`ChildRowContent`, whose `context.Item` is the row. The +/− that opens it
is a `TemplateColumn` of the page (`GridColumn.Toggle` on header and cells),
declared **after the select column** — the tick is always the first column —
and drawing a button **only in a row that has something to open**; the
other rows get an empty cell, never a disabled button. MudBlazor's own
`HierarchyColumn` would put itself first, before the tick, so it is there
only hidden, which is what lets the grid open detail rows at all; the
button calls `ToggleAsync(item)` on the grid, `ExpandAsync(rows)` /
`CollapseAll()` serve Expand all, and the page keeps its own set of what
is open, keyed by the row's `Key`. `RowClassFunc` gives each row a class,
which is how a log colours time, level, source and message by level. The
search box sits in the title bar (`?q=`, replacing the history entry as it
is typed).

**The grid has no rail and no Autorefresh: the log is a log**, searched
with the search box and the level select, reloaded by hand — Copy and
Refresh sit in its header row — so what is being read never moves; watching the log as it grows is the
Activity list's job. **The
Activity list has its own header row** (`CliActivityList`: `PageStat`
"Activity · N commands" at the left, Expand all and Collapse all at the
right) and **the one rail** at its right (`.wslc-action-body` >
`.wslc-action-main` + `.wslc-action-rail`, never with `wslc-fill` on the
body, which would stack the rail under the content), at the bottom of the
page whatever the divider does. The rail holds the list's filters as one
group — All, then one toggle per type: Containers, Images, Networks,
Volumes, General (`CliKinds.Groups`, with the sidebar's icons); pressed,
that type is listed; the hidden ones go in the URL as `?hide=a,b`; All
lists or hides every type; the counts in the tooltips are of commands —
then a gap (`.wslc-rail-gap`) and the list's **Autorefresh**, drawn as a ring (`MudProgressCircular`, `.wslc-rail-spinner`, the 30px
of a square rail button) with a round button inside
(`.wslc-rail-spinner-dot`) and no box (`.wslc-rail-ring`, a text-variant
`MudIconButton` with the rail's shadow, fill and padding removed). On, the
ring goes round
and the list follows its end as a chat does — turning it on jumps to the
last line once; each reload calls `scrollToEnd(id)` and goes to the end
(the helper's `follow` mode, which leaves a box the reader scrolled up in
alone, is the pull console's; here one turn of the wheel switched the
following off, so the hold below is the one thing that stops it). **While
the reader holds the list** — pointer over it, finger on it, or a command
open (`CliActivityList.Held`, told through `HeldChanged`) — the ring stands
still and no scroll happens, without switching off; both resume when they
let go. That switch alone runs the 2 s poll, and the poll feeds the list
only: the grid keeps the read it was given until Refresh. The switch is
remembered (`followCli`). Every toggle
takes its state from `RailToggle`, the one helper every rail uses: green
standing proud while off, blue and pressed in while on. The counts per type
are in the toggles' tooltips.

Logs holds what CLI Activity shows: a `wslc`
command's entry carries the command (`AgentLogEntry.Command`), and its
detail is CLI Activity's disclosure — type, title, status chip, time taken,
session, start, exit; Command and output; Output; Errors — with the level's
own layout classes (`.wslc-cli-detail`, `.wslc-cli-line`, `.wslc-cli-label`,
`.wslc-cli-block`). A command still running comes from the agent's memory as
a `RUNNING` row (`EntryId` null: it cannot be ticked for deletion) under the
same key its finished entry will have, so across a refresh the page sees one
row change state, and reopens it if it was open. Everything else — the
select column, sorting, the widths, the tone — is the list contract
unchanged.

Two rules every grid keeps:
**the selection checkbox is the first column**, whatever else the grid
has; and **a row is ticked by its checkbox and by nothing else** —
`ListGrid` sets `SelectOnRowClick="false"`, so clicking a row never marks
or unmarks it.

## 3. Cards view: `CardsGrid` + `EntityCard`

```razor
<CardsGrid TItem="ImageSummary" Items="Visible" Loading="_loading" EmptyText="@EmptyText">
    <ItemTemplate Context="i">
        <EntityCard Title="@i.Repository" Subtitle="@i.Tag" AvatarColor="@StateColors.For(i.State)">
            <HeaderActions><StateChip State="@i.State" /></HeaderActions>
            <ChildContent>
                <MudText Typo="Typo.body2"><b>Id</b> @i.Id</MudText>
                <MudText Typo="Typo.body2"><b>Size</b> @i.Size</MudText>
            </ChildContent>
            <Actions><ImageActions Image="i" Changed="RefreshAsync" /></Actions>
        </EntityCard>
    </ItemTemplate>
</CardsGrid>
```

A card's body may put dials beside its texts (`.wslc-card-detail`: the
texts at the left in `.wslc-card-texts`, the dials at the right in
`.wslc-card-dials`, one `UsageDial` each). Containers show CPU and memory,
the measure's four colours (the row carries the container's disk and
network totals too, `diskIoBytes` / `netIoBytes`, but the card does not
draw them: that card keeps CPU and memory only). Networks show **Received**
and **Sent**, Volumes **Read** and **Written**: what the containers on the
resource have moved together (each container's whole `stats` figure, so a
container on two networks counts on both), the ring at its share of what
every container has moved, primary and secondary as Home's two series. A
total keeps one colour, `Tone`, since it has no danger level. The figures
come with the list (per row and, for the shares, for the whole list); a
resource nothing is on shows the dash.

`CardsGrid` fills the height, scrolls inside, pages with the same sizes as the
table and ends with a pager of the same shape ("Cards per page", range,
first/prev/next/last). The cards themselves stand in one fluid grid
(`.wslc-cards-grid`, `repeat(auto-fill, minmax(min(21rem, 100%), 1fr))`): each
card is at least as wide as its action row needs and as many as fit share the
room, one per row on a phone. Not a count per breakpoint — a count divides the
room instead of respecting the card, and a card narrower than its verbs pushes
the last of them past its own edge, which is what a page zoom used to do. A
card's verbs never wrap to a second line: they step down a size instead
(`@container wslc-card`) where even the width they asked for cannot be given. `EntityCard` puts the data in the body and the actions
in the footer row, right-aligned; header and footer share the grid's tone.
While a verb runs on the row, the card draws a thin busy line over the seam
between body and footer: the footer keeps its height and the buttons do not
move. Work in progress is per row (`BusyRows`, a scoped service keyed by the
row's `KeyOf`): the actions component marks its key while a verb runs, the
table's `StateDot` pulses in place and the card shows its line; pages pass
`Key` to both. The grid's own loading bar shows only for the first load.

## 4. Actions

One `<Resource>Actions` component per resource, all deriving from
`EntityActionsBase` (`Components/EntityActionsBase.cs`), which owns `Api`,
`Busy`, `RunAsync` (verb + snackbar + `Changed`), `RemoveAsync` (confirm,
then remove) and `OpenAsync<TDialog>`. The same row of small icon buttons
serves the table's Actions column and the card footer, in this
order: containers [start or stop] [⋮] [remove]; images [＋] [▶] [⋮] [remove];
volumes [files] [⋮] [remove]; networks [connect] [disconnect] [view] [remove].
Menu entries and buttons use `Icons.Material.Filled`, one icon per action
across the application (the table in `docs/ui-controls.md`, Icons). No entry is disabled by
the row's state (the CLI answers). An entry a
later slice brings stays in place and says so through `NotYet`. The
containers menu, complete: View details · View & edit · View image packages
and CVEs (`JsonDialog` over `/images/inspect`) · Copy run command · Open in
terminal (terminal slice) · Use wslc Debug (not built yet: a toast
says so) · View files (files slice) · Restart · Open with browser
(`OpenPortDialog`, every published port as a link; the `PortsLink` opens the
preferred one) · View browser sessions (browser slice) · Logs · Stats
(`ContainerStatsDialog`, also the details tab) · Exec (terminal slice) ·
Export JSON (`/containers/{id}/inspect.json` download) · Backup
(`BackupDialog`, persistent, the job's steps and Save as…) · Kill.

## 5. Data and behaviour

- Load through `WslcAgentApi` only; catch `AgentApiException` and
  `HttpRequestException`, show them in a `MudAlert` (load) or a snackbar
  (actions).
- Poll every 5 seconds with a `PeriodicTimer`, cancelled in `Dispose`. A tick
  is skipped while a row menu is open (`OpenPopups`, fed by the menus'
  `OpenChanged` through `EntityActionsBase.MenuOpenChanged`): the refresh
  rebuilds the rows and would close the menu by itself.
- `Visible` = items filtered by the search text (case-insensitive over the
  identity and a few key fields) and the page's checkboxes.
- Bulk actions live in `TopbarNav` inside `BulkBar`, which shows "N
  selected" plus the verbs and stays invisible while nothing is selected so
  the bar keeps its width. Page-wide verbs (Prune, Map) follow it in
  the same slot, next to Log out;
  `SectionActions` stays free for pages that have a second row.
- Forms (create, pull, tag, connect…) are dialogs built on `FormDialog`, one
  component per form under `Components/Dialogs/`, opened with
  `OpenAsync<TDialog>(title, parameters, large)` from a page or an actions
  row. A creator exists once: `CreateVolumeDialog`, `CreateNetworkDialog` and
  `PullImageDialog` are the dialogs the pickers embed.
- Pickers exist once, in `Components/Pickers.cs`: `ImageAsync`, `VolumeAsync`,
  `NetworkAsync` (one `NamedPickerDialog` fed a loader and the resource's
  create dialog, which selects what it created) and `HostFolderAsync`
  (`HostFolderPickerDialog`, the agent machine's folders).
- The container launch form is one component, `Launch/ContainerForm`, in
  three uses: Run (Fill bar, always detached), Create (Start
  toggle) and View & edit (pre-filled from `GET /containers/{id}/details`,
  saving removes and recreates). `LaunchForm` is its state and converts to
  and from `ContainerLaunchRequest`; `MountRows` and `NetworkRows` are its
  repeatable rows. `ContainerFormDialog` hosts it as a dialog; the details
  page embeds it in the Inspect tab.
- What the page is showing lives in the query, so Back is real navigation:
  `?q=` the searched text, `?view=cards` or `?view=table` (always written, so a
  step Back never depends on what was remembered), `?page=` the grid page (`ListPageBase`
  reads them in `OnParametersSet` and writes them with
  `GetUriWithQueryParameters`). A deliberate change — the view, the page — adds
  a history entry; typing in the search replaces the current one so a filter is
  not a trail of steps. Sorting and column widths are not kept yet.
- Table or cards is remembered per list and per client (`ViewPreference`,
  keyed by the row type), cards until the user picks the table: containers can
  be cards while images stay rows, and
  coming back to a list opens it the way it was left. It lives in this device's
  storage (`wslcAgent.views`), not in the agent: each client keeps its own, and
  closing the app and opening it again finds every list as it was left. The
  browser client reads it before the first paint (`Program.cs`) and the native
  ones on the first list they draw, so no list ever shows rows for an instant
  and corrects itself. An address that carries `?view=` — Back, a shared link —
  wins and becomes that list’s choice.
- A list draws itself before it reads: waiting for the agent first left the
  previous screen on show while the CLI answered, and the new one then
  appeared and changed under the user. Measured: the list is on screen, in its
  remembered view, 2 ms after the navigation entry is clicked.
- A details page (`/containers/{id}?tab=`) has a header
  (state, name, id, image, ports, the same actions row) and tabs; `LogsView`
  is the logs tab and the ⋮ menu's Logs.

- The section remembers the details it is showing (`OpenDetails`): while a
  container's page is open the sidebar's Containers entry points at it, so
  leaving for Networks and coming back returns to that container instead of
  the list. Arriving at the list, by the header's ← Containers or any other
  route, is what forgets it. It is remembered in memory, not in session
  storage, so a full reload of the page starts at the list.
## 6. What a slice ships together

Service (`WslcAgent.Server/<Resource>/`, usage through `ContainerUsageScanner`,
arguments through `WslcArgs`), endpoints (`Endpoints/`), contract
records (`WslcAgent.ApiClient/Contracts/`), client methods (`WslcAgentApi`),
MCP tools (`WslcAgent.Mcp/Tools/`) over the same service interface, the page,
the actions component, a fixture and quick tests, and a row in
`docs/api-v1.md`.

## 7. Every screen, not only lists

The sizes fixed here are the product's sizes everywhere: text fields,
checkboxes, buttons, titles and captions on forms, dialogs and detail views
use the same theme steps (`Layout/WslcTheme.cs`) and the same 26px control
height. A new screen never introduces its own font sizes, paddings or
control heights.

## 8. Navigation and icons

Icons are `Icons.Material.Filled` almost everywhere: the navigation, the row
actions and their menus (one component draws the table row and the card), the
action rail and the shell's own controls (search, refresh, view toggle, pager,
theme). `WslcIcons` — the project's own glyph set, in colour — keeps the session
block, the pages' own verbs (Run, Create, Pull, Prune…) and the Files browser. A new page gets its entry in
the navigation button's row (`Layout/PageVerbsFab.razor`).

## 9. The dashboard

Home is not a list page but it keeps the same contract for what it shows:
designing it is a step of its own in the address, so Back takes the
dashboard out of design, and the view chosen and the page shown are in the
address too (`Pages/DashboardPage.razor`).
