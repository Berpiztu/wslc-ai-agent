# UI controls: the parameters

The exact parameters every control takes in this UI, so any new screen looks
like the Containers page without deciding anything. Sizes, tones and type
steps are fixed once, in `src/WslcAgent.UI/Layout/WslcTheme.cs` (theme) and
`src/WslcAgent.UI/wwwroot/wslc-agent-ui.css` (layout utilities); a page or
component uses the controls below with these parameters and never adds
`Style`, font sizes, paddings or colours of its own.

`docs/list-pages.md` describes how the controls compose into a list page.

## Fixed once

| What | Value | Where |
| --- | --- | --- |
| Type steps | default/body1 0.82rem · subtitle1 (inputs) 0.82rem · body2 (tables, buttons, captions in bars) 0.75rem · subtitle2 (table header) 0.72rem bold · caption 0.72rem · overline 0.68rem · h6 (page and sidebar titles) 1.06rem | theme |
| Font | Inter, Segoe UI, Roboto, Helvetica, Arial | theme |
| Buttons | sentence case (no uppercase) | theme |
| Palette | dark colours: background #0f172a, surface #1e293b, lines #334155, primary #4f46e5, success #10b981, warning #f59e0b, error #ef4444; light palette alongside | theme |
| Layout | app bar 48px (36px dense), drawer 200px, mini rail 56px, radius 8px | theme |
| Header tone | primary mixed 18% into the surface: table header, pagers, card header and footer, section-actions row, pinned header cells | CSS |
| Page | 8px padding, viewport-high column, never scrolls itself | CSS |

## Text field (search, forms)

```razor
<SearchBox @bind-Value="Search" Placeholder="Search volumes" />
<MudTextField T="string" @bind-Value="_name" Label="Name" Placeholder="data" Immediate="true"
              Required="true" RequiredError="A volume name is required"
              Variant="Variant.Text" Margin="Margin.Dense" />
```

- Always `Variant="Variant.Text" Margin="Margin.Dense"`.
- Search boxes: only through `SearchBox`, in the title bar's centre (`TopbarSearch`)
  before the page's verbs; it fixes `Immediate`, `DebounceInterval="150"`,
  `Clearable`, the magnifier and the width (14rem, 7rem on phones).
- Form fields: `Label` and a `Placeholder` showing an example value;
  required ones `Required="true" RequiredError="…" Immediate="true"`; a
  prefilled read-only value `ReadOnly="true"`; fields after the first take
  `Class="mt-2"`.
- In the title bar the field is 26px tall; in forms it keeps MudBlazor's
  dense height.

## Dialogs (forms)

```razor
<FormDialog SubmitText="Create" OnSubmit="CreateAsync">
    ...fields...
</FormDialog>
```

- Every form is one component under `Components/Dialogs/` whose root is
  `FormDialog`: it renders the `MudForm`, Cancel, the one filled submit
  button (`SubmitText`, `BusyText` while running), validates required fields,
  shows an `AgentApiException` inside the dialog and closes as submitted only
  on success.
- **Every dialog carries its verbs in its title row and nothing at its foot.**
  The row is `DialogTitle` (title at the left, what says where it stands next
  to it, the verbs at the right): Close alone for a window that only shows
  something, Cancel and the submit for a form. No `DialogActions`, and no cross
  in the corner — `DialogFlow` switches MudBlazor's off.
- **No dialog is dragged sideways**: in its
  title row the title gives way first — it shrinks and is cut with an
  ellipsis — and on a phone the verbs take the theme's small type and a
  tighter padding, so four of them fit the width.
- **A dialog is sized when it opens and keeps that size**: it never grows or shrinks with what it holds. What can
  change — a list, a field that appears — sits in a box of fixed height that
  scrolls (`MudPaper Outlined` with `wslc-picker-list`), as the pickers do.
- **Close leaves the window; Cancel cancels what was typed.** They are two
  things, and a window that has both (the launch form, whose Cancel is in its
  action rail) keeps both: Close is in the title row and closes the dialog,
  asking first when something was typed and not saved; Cancel puts the fields
  back to what the form was opened with, after asking, and stays where it is.
  The rail's two verbs ask too — Save (create, or recreate, left stopped)
  and Run (create and start; in View & edit, recreate and start) — nothing
  in that rail acts without a question. Neither closes the window before
  the agent has answered: see "The launch form" below.
- MudBlazor renders a dialog's title row in its own tree: a state change that
  enables or disables a button there (a list loaded, an item selected, busy
  on/off) must call `Dialog.StateHasChanged()` on the `IMudDialogInstance`, or
  the buttons repaint only when the user clicks somewhere.
- Opened with `OpenAsync<TDialog>(title, parameters)` from a page
  (`ListPageBase`) or an actions row (`EntityActionsBase`); both refresh when
  the dialog was submitted. Parameters: `new DialogParameters<TDialog> { { d => d.Prop, value } }`.
- Size and behaviour are fixed in `DialogFlow` (small, full width, no cross,
  no backdrop close; `large: true` for the launch form, the pickers and the
  windows that show a view). Yes/cancel questions use `DialogFlow.ConfirmAsync` /
  `ConfirmRemoveAsync`; a picker returns its name through
  `DialogFlow.PickAsync`. A window over the whole screen is
  `DialogFlow.ShowFullScreenAsync` with `ContentClass="wslc-dialog-full"`:
  without a title row when it draws its own bar (the host browser pane), with
  one and Close in it (`titled: true`) when it only shows something (an
  Archify diagram from `wwwroot/archify/`, an iframe, opened by
  `ArchitectureVerb`, a verb of the round button on every screen, above
  the page's own and apart from them, so a stopped session leaves it
  working: the control plane everywhere, the terminal diagram on Terminal, the network one on
  Networks, the skill and MCP one on Settings; one list,
  `ArchitectureDiagrams`, and `docs/architecture.md` describes them). The
  window's title row holds a `MudToggleGroup` with a key per diagram, in the
  style of the table/cards keys, the current one pressed, so a reader switches
  without leaving it.
- Form fields carry a `Label`, `Placeholder` (an example value)
  and `HelperText` ("Example: …"), `Immediate="true"`, `Variant.Text`,
  `Margin.Dense`; multi-line values use `Lines`.
- Repeatable rows (Volumes, Networks) open with `RowsHeader` (the label at
  the left, the filled "Add" button at the right); each row ends with
  `<MudIconButton Icon="@Icons.Material.Filled.Close" Size="Size.Small" Color="Color.Error" Variant="Variant.Outlined" Class="wslc-row-remove" title="Remove row">`
  (a 22px square).
- Tables never set `Dense`: `ListGrid` leaves MudBlazor's default and the
  cell height comes from one CSS rule (3px 8px padding, line-height 1.25)
  that `MudSimpleTable Class="wslc-fill-scroll"` shares.
- Tabs on a details page are one bordered card on the surface tone (tab bar
  and panel); tabs are body2, weight 500, muted, the active one in the text
  colour on a faint accent tint (CSS on `.wslc-fill.mud-tabs`).
- A details header (`.wslc-details-header`) holds the state dot,
  name (`.wslc-details-name`) and the outlined capitalised state badge
  (`.wslc-details-state`); the ID / Image / Ports meta line; at the right
  `ContainerLifecycle` (start or stop, restart, remove) and an outlined
  refresh icon button. Ports are always `PortsLink` (green mono link,
  `host→container`, a muted dash when empty), in tables, cards and headers.
- An action view (the launch form, logs, and the exec / files / stats views
  to come) is `.wslc-action-body`: the main column and `.wslc-action-rail`
  at the right, panel-high, its buttons at the top, each
  `MudIconButton Size="Size.Small" Variant="Variant.Filled"` with a Material
  icon in its verb's colour (green starts, blue does or does again, light
  blue takes a copy out, amber takes back, red destroys or wipes). A toggle
  says its state the way a switch does: on it is green (`Color.Success`, what
  everything chosen wears) and drawn pressed into the rail (`wslc-rail-on`),
  off it is neutral and stands proud of it like every other button there. The
  exec window's rail holds every verb it has — Connect, Reconnect, Copy,
  Clear — because its pane takes the rest: a command is typed in the shell
  itself, not in a Command field over it. The search bar (`.wslc-action-search`)
  shows only while its rail toggle is on. Log lines are `.wslc-log-line`
  with `log-level-<level>` from `LogLines.DetectLevel`. `ContainerForm`
  takes `Above` / `Below` fragments for what scrolls with its fields and
  `MainClass="wslc-fill-scroll"` when it fills a panel.
- **Autorefresh of a live window is one control, `FollowRing`** (the Logs
  page's ring: a round button inside a ring, no box; blue and turning while
  on, green and still while off), in the Logs page's two windows, a
  container's logs and the pull console. Its rule travels with it: while the
  reader holds the window — pointer over it (`@onpointerenter` / `leave`),
  finger on it (`@ontouchstart` / `end` / `cancel`) — the ring stands still,
  the text is not reloaded and the box does not scroll; it catches up on the
  first tick after they leave. A box scrolls to its end only when something
  new arrived. `FollowRing.TitleFor(what, follow, held)` words the tooltip.

## The launch form (Run, Create, View & edit)

One component, `ContainerForm`, in three uses that differ only by the Fill
bar (Run) and by what the verbs do (View & edit removes and recreates).

- **Field order, the same in the three, one position each in the two-column
  grid** (a row is "left | right"; a dot joins two short values sharing one
  column through `wslc-form-pair`): Image | Tag / Name | Publish / Volumes
  (rows, whole line) / Env (whole line) / Networks (rows, whole line) /
  Entrypoint (whole line) / Command (whole line) / Workdir | User / Restart
  policy · Stop timeout | Memory · CPUs / Disable healthcheck / Health cmd
  (whole line) / Health interval · Health timeout | Health retries · Health
  start period. The two commands, Env and Health cmd take the whole line: a
  flag hidden past the edge of a half-width Command is how a wrong `--port`
  went unseen.
- **Two verbs in the rail, after Load JSON, Full variables and Export:** Save
  (`Save`; creates, or recreates, and leaves the container stopped) and Run
  (`PlayArrow`; creates and starts, pulling first when the image is not
  local). In View & edit both remove the container first and both wear
  `Color.Error`; otherwise Save is `Color.Primary` and Run `Color.Success`.
  Then Cancel (`Close`, amber). There is no Start switch: the verb says it.
- **What a verb does, in order:** the fields validated; the public names
  made possible (the Reverse proxy question); the agent's check
  (`POST /containers/launch-check`) under the veil — its errors go into the
  fields and stop, its warnings are listed above the fields and asked about
  ("Worth a look", Run anyway / Save anyway); the verb's own question; then
  the launch under the veil.
- **The veil (`MudOverlay Absolute`, over `.wslc-action-body`):** a large
  `MudProgressCircular`, a line saying what is being done ("Checking the
  settings…", "Creating x…", "Recreating x: the new settings are rehearsed
  first…", "Pulling image…" with the pull's own bar, "Starting x…"). A run is
  the agent's job: the form follows it every second and closes only when the
  agent says the container is up; the veil says the window may be closed
  meanwhile. A create or recreate cannot be left: the window's Close answers
  "wait for it to finish" while it works.
- **Where a message goes:** what is about one field, in that field, in place
  of its example (`Error` / `ErrorText`; `MultiValueField`, `PortsField`,
  `MountRows` and `NetworkRows` take an `Error` for it). What is about no
  field — the check's warnings, a failure the agent could not place, the note
  of what a recreate did with the container — above the fields in a dense
  `MudAlert`, never below them, where a short screen does not show it. The
  agent names the fields (`LaunchFields`; `fields` in the problem details, or
  in the failed launch job). The form never closes on a failure.

## Select

```razor
<MudSelect T="string" Value="_selected" ValueChanged="SelectAsync" Dense="true" Margin="Margin.Dense"
           Variant="Variant.Outlined" Placeholder="Session" FullWidth="true">
    <MudSelectItem T="string" Value="@name">@name</MudSelectItem>
</MudSelect>
```

- `Dense="true" Margin="Margin.Dense" Variant="Variant.Text"`; explicit `T`.
- Pager selects only: `Variant="Variant.Text"` with class
  `mud-table-pagination-select` (see `CardsGrid`).

## Checkbox and switch

```razor
<SquareCheckBox Value="_runningOnly" ValueChanged="OnRunningOnlyChanged" Label="Running" title="Show running containers only" />
```

- Always `SquareCheckBox`: MudBlazor's dense small checkbox with the Sharp
  glyphs (square corners, like every button). Label as `Label`, tooltip as `title`.
- A box that stands for several others (an All box) takes `Partly`: unticked
  it then draws the dash rather than the empty square, so "some" is not read
  as "none", and ticking it ticks them all.
- A filter that is not built yet: `Disabled="true"` with a `title` saying
  which slice brings it; never hidden.
- Grid row selection: `SelectColumn` with `Size="Size.Small"`.
- A page's filters in its header row (Running, Temporary) are switches, the
  label first: `<MudSwitch T="bool" Value="…" ValueChanged="…" Label="Running"
  LabelPlacement="Placement.Start" Color="Color.Warning" Size="Size.Small" title="…" />`.

## Buttons

```razor
<MudButton Size="Size.Small" Variant="Variant.Filled" StartIcon="@Icons.Material.Filled.Add" OnClick="…">Create</MudButton>
<MudButton Size="Size.Small" Variant="Variant.Filled" Color="Color.Error" OnClick="…">Remove</MudButton>
```

- `Size="Size.Small" Variant="Variant.Filled"` (MudBlazor's filled look), square
  corners in the header row and the title bar; text is a verb in sentence
  case; `Color.Error` for destructive verbs, `Color.Primary` for the main
  additive verb (Create, Pull), `Color.Success` for the verb that starts
  something running (Run), default otherwise.
- A dialog's primary action is `Color.Primary`; Cancel is the default colour.
- Not built yet: a normal button whose `OnClick` calls `NotYet(feature, slice)`
  (base classes), so it looks like a finished one and says which slice
  brings it; menu entries not built yet stay `Disabled`.

## Icon buttons

```razor
<MudIconButton Icon="@Icons.Material.Filled.PlayArrow" Size="Size.Small" Color="Color.Success" title="Start" Disabled="Busy" OnClick="…" />
<MudIconButton Icon="@Icons.Material.Filled.Refresh" Size="Size.Small" OnClick="RefreshAsync" Disabled="_loading" title="Refresh" />
```

- `Size="Size.Small"` always; `title` always (it is the only label).
- **Row actions and their menus: `Icons.Material.Filled`** — the same set as
  the rail and the shell's own controls. The colours are fixed
  (start green `Color.Success`, stop `Color.Primary`, remove `Color.Error`,
  more uncoloured), and the row keeps its horizontal shape. The rows and the
  cards are drawn by the same component, so they share MudBlazor's set.
- Shell controls (search, refresh, view toggle, pager, theme, menu):
  `Icons.Material.Filled`.
- `WslcIcons` (the project's own glyph set) keeps the session block, the page
  verbs of the toolbars and the Files browser. The navigation uses Material,
  like the row actions.

### Icon size rule

One glyph size everywhere: the icon box (`.mud-icon-root`) is **1.125rem
(18px)** in every icon button, whatever its context: title bar, page header
row, table rows, cards, details header, dialogs, action rail. It is
MudBlazor's own size for `Size.Small`, so a context needs no rule of its
own; a context that sets a button size (the 26px bar controls, the 1.75rem
rail squares, the 22px row-remove square) sets the button, never the glyph.
The only smaller glyphs are the status dot (0.42rem), the responsive
steps of the bars (0.9rem, then 0.75rem when the row is tight) and the
published mark after the ports (0.9rem).

Why the rule is needed: a `WslcIcons` glyph is SVG text 16 units high in a
24-unit box, so it renders at two thirds of the icon box (12px at 18px);
a Material icon fills the box. Halving the box size (a `0.85rem` box for a
rail, say) halves an already small glyph.

## Menus

```razor
<MudMenu Icon="@Icons.Material.Filled.MoreVert" Size="Size.Small" Dense="true" title="More actions" Disabled="Busy" OpenChanged="MenuOpenChanged">
    <MudMenuItem Icon="@Icons.Material.Filled.RestartAlt" OnClick="…">Restart</MudMenuItem>
</MudMenu>
```

- `Size="Size.Small" Dense="true"`; every item has a `WslcIcons` glyph and a
  sentence-case verb, in the order `docs/list-pages.md` lists;
  `OpenChanged="MenuOpenChanged"` so the page holds its refresh while the menu
  is open.
- No item is disabled by the row's state: the CLI answers and the snackbar
  reports.

## Toggle group (table / cards and any other mode switch)

```razor
<MudToggleGroup T="ViewMode" Value="Value" ValueChanged="ValueChanged" Size="Size.Small" Color="Color.Primary" Outlined="true" Delimiters="false">
    <MudToggleItem Value="ViewMode.Table" title="Table"><MudIcon Icon="@Icons.Material.Filled.TableRows" Size="Size.Small" /></MudToggleItem>
</MudToggleGroup>
```

- Items carry a `MudIcon` child (`MudToggleItem` has no `Icon` parameter) and a
  `title`. Use `ViewModeToggle` for table/cards; do not build a second one.
- Keys: the mode in use is the key held down (sunk into the surface, no
  fill, muted glyph), the other mode is the key you can press (primary fill,
  raised, with a drop shadow). The stylesheet does this; the component keeps
  its parameters.

## Tabs (details pages)

```razor
<MudTabs Elevation="0" Rounded="false" MinimumTabWidth="0" TabPanelsClass="wslc-tab-panels" Class="wslc-fill" ActivePanelIndex="_tab" ActivePanelIndexChanged="OnTabChanged">
    <MudTabPanel Text="Logs">…</MudTabPanel>
</MudTabs>
```

- `MinimumTabWidth="0"` removes MudBlazor's inline 160px minimum, so the
  tabs sit left at their titles' width and never scroll; sentence-case
  titles, 36px tall, the active one in white (CSS on `.wslc-fill.mud-tabs`).
  The active tab comes from the `tab` query parameter.

## Tables

Only through `ListGrid` (see `docs/list-pages.md`). A page passes columns and
actions; the shell owns striped/bordered/fixed header, resize (Container
mode, double-click puts a column back to its declared width), reorder, no column menu, no phone layout,
pager, widths, pinned columns.

Column parameters a page does write:

| Column | Parameters |
| --- | --- |
| Selection | `<SelectColumn T="…" Size="Size.Small" StickyLeft="true" HeaderClass="@GridColumn.Select" CellClass="@GridColumn.Select" />` |
| Status dot | `<TemplateColumn T="…" Title="" Sortable="false" Resizable="false" StickyLeft="true" HeaderClass="@GridColumn.Bullet" CellClass="@GridColumn.Bullet">` with `<StateDot Active="…" Title="in use" />` |
| Identity | `<PropertyColumn T="…" TProperty="string" Property="x => x.Name" Title="Name" StickyLeft="true" />` |
| Data (property) | `<PropertyColumn T="…" TProperty="string" Property="…" Title="…">` with a `CellTemplate` calling `Dash(...)` when the value can be empty |
| Data (composed) | `<TemplateColumn T="…" Title="…" Sortable="false" Resizable="true" DragAndDropEnabled="true">` |
| Secondary text (ids) | add `CellClass="mud-text-secondary"` |
| Actions | `<TemplateColumn T="…" Title="Actions" Sortable="false" Resizable="false" DragAndDropEnabled="false" StickyRight="true" CellClass="@GridColumn.Actions">` |

## Cards

Only through `CardsGrid` and `EntityCard`:

```razor
<EntityCard Title="@c.Name" Subtitle="@c.Image" AvatarColor="@StateColors.For(c.State)">
    <HeaderActions><StateChip State="@c.State" /></HeaderActions>
    <ChildContent><MudText Typo="Typo.body2"><b>Id</b> @c.Id</MudText></ChildContent>
    <Actions><ContainerActions Container="c" Changed="RefreshAsync" /></Actions>
</EntityCard>
```

Body lines are `Typo.body2` with the label in bold; secondary lines
`Typo.caption` with `mud-text-secondary`.

## State

- Table: `<StateDot Active="…" Title="running" />` (green dot while active,
  nothing otherwise; `Title` says what active means: "running", "in use").
- Cards and details: `StateChip` = `MudChip T="string" Size="Size.Small" Variant="Variant.Filled" Color="@StateColors.For(state)"`;
  for resources without a state word, `<StateChip State="in use" />` when in use.
- Colours only through `StateColors.For(state)`.

## Text

| Use | Component |
| --- | --- |
| Page title, sidebar brand | given to `PageShell Title`; the layout renders `Typo.h6` |
| Card title | `Typo.subtitle1` (inside `EntityCard`) |
| Data, table cells, card lines, bar captions | `Typo.body2` |
| Secondary line | `Typo.caption` + `Class="mud-text-secondary"` |
| Section label in the sidebar | `Typo.overline` |
| Header-row stat | `<PageStat Label="CPU" Value="…" />`, never raw `MudText`; `Large="true"` puts label and value in body1, the row's button size — the Activity header only |
| Bulk bar | `<BulkBar Count="Selected.Count">…verbs…</BulkBar>` in `TopbarNav` |

## Feedback

- Load errors: `<MudAlert Severity="Severity.Error" Class="mb-2 flex-shrink-0">@_error</MudAlert>` above the grid.
- Toasts appear top right, over the title bar: its height (36px), the application's body type, sliding in from the edge (position set once in `AddWslcAgentUi`, the rest in `wslc-agent-ui.css`). Held in the hand they come down under the title bar, across the top, the same toast (`Toasts`; the bottom edge is the status bar).
- Action results: `Snackbar.Add($"{name}: {verb} ok", Severity.Success)` and
  `Snackbar.Add($"{name}: {verb} failed. {ex.Message}", Severity.Error)`.
- Confirmations: `DialogFlow.ConfirmAsync(Dialogs, "Remove container", "Remove {name} ({id})? This cannot be undone.", "Remove", destructive: true)` (`ConfirmRemoveAsync` for removals); the yes text is the verb, drawn in `Color.Error` when destructive and `Color.Primary` otherwise; never MudBlazor's message box.
- Picker dialogs (`NamedPickerDialog`, `HostFolderPickerDialog`), one
  composition: compact (`DialogFlow.PickAsync`, no close cross),
  the verbs in the title row after the title: Create / New (`Color.Primary`),
  Use (`Color.Success`), Close (default colour); the list inside
  `MudPaper Outlined` with a fixed height (`wslc-picker-list`) so the dialog
  opens at one size and never resizes; a click selects (green), a
  double-click uses or opens; the hint as a caption at the foot. Every
  picker opens positioned on the field's current value (`Pickers.*` take
  it): the name selected in the list, or the folder's parent with the folder
  selected. The folder
  picker adds ↑ Up, ⌂ Home and the path in mono above the list, a first row
  `..` that goes up, the yellow folder pictogram
  (`WslcIcons.Files`, drives `WslcIcons.Drive`), and New asks the name in a
  `PromptDialog` (`DialogFlow.PromptAsync`).
- List editors (`ValueListDialog`, opened by `DialogFlow.EditValuesAsync`):
  the same shell as a picker, with Save (`Color.Success`) and Close in the
  title row — Save, not Use, because it edits values instead of choosing an
  existing thing. `RowsHeader` carries Add; one row per value (text field +
  red ✕) inside the fixed-height `wslc-picker-list`, so Add never grows the
  dialog and only the list scrolls; blank rows are dropped on Save.
- The ports link (`PortsLink`): the green mono `host→container` link opens
  the port popup (`OpenPortDialog`: every port with Local and, when the port
  has a public name, Remote with the https name in full); after the text,
  the published mark, a `MudIcon` (`Public`), `Color.Info` when the
  container has a public name and `Color.Default` when not, the names in
  its title. Link and mark share the cell as a flex pair (`wslc-ports`): the
  text gives way with an ellipsis, the mark always shows. Its glyph is the
  0.9rem step of the bars, one under the icon size.
- Ports on the launch form (`PortsField`): the `--publish` values on one
  line as the other multi-value fields, and its ⋮ opens `PortRowsDialog`,
  the list editor with three columns and a check per row — host port,
  container port, Reverse proxy, name (the container's name by default,
  editable, greyed while the check is off; the suffix and the domain are
  Settings → Publishing's). The line shows a published pair's name in
  parentheses. The ticked rows are the form's public names,
  `containerPort:name`, and saving the form is what publishes.
- The launch form's rail has one more switch before Export: Full variables
  (`DataObject`, green while on, off by default). Off, Export is what `wslc`
  knows; on, the form as a launch request with the agent's own fields, the
  restart policy and the public names. The row menu offers both as their
  own entries (Copy run command / Export JSON, and their "full variables"
  twins).
- Multi-value fields (`MultiValueField`: Env): the values on one
  line plus a ⋮ that opens the list editor. The field's model is the list
  (`List<string>` on `LaunchForm`), never a joined string, so several ports
  or variables survive View & edit; a hand-typed line is read back with the
  field's own rule — `ValueList.Split` for plain values, `ValueList.SplitPairs`
  for `KEY=value` ones, where a line splits on commas only when every chunk
  starts a new key (`MSG=hello, world` stays one value).
- Terminal (`TerminalView`): the command field, Connect / Reconnect, Clear and
  the session state on one toolbar row, xterm.js filling the rest
  (`wslc-terminal` / `wslc-terminal-pane`). It is mounted with its tab or its
  dialog, never behind a tab nobody opened — a shell is a process on the
  agent. The details page's Exec tab and the row menu's Exec
  (`ContainerTerminalDialog`) are the same component. The session outlives
  the component: `wslc-terminal.js` keeps this tab's sessions by key (`host`,
  `job:compact-vhdx`, `exec:<container>`), leaving a page detaches the
  surface and keeps the socket, and the next mount of the same key takes the
  surface back with the shell still running and everything it printed. A
  shell that had already ended goes with its page. The rail's Close verb
  ends a session on purpose; Log out ends them all; another tab or device
  has its own registry.
- Saving a file on the user's machine: the browser downloads it (a link with
  `download`, or `NavigateTo(url, forceLoad: true)`), and a native client,
  whose WebView has no download UI, writes it itself through `IClientFiles`
  (`ClientFileSave.TextAsync`). Branch on `IClientFiles.Supported`; never
  send the file to the agent host to be written there.
- Empty list: `ListGrid` and `CardsGrid` show `EmptyText` through `EmptyState`,
  centred in the rows' area.
- Loading: `MudProgressLinear Indeterminate` above an empty list, first load
  only — in cards view, an indeterminate `MudProgressCircular` centred in the
  area instead (`.wslc-cards-loading`): everything under way in that view is a
  ring. A busy row disables its buttons and shows the work in place: the state
  dot pulses (`StateDot Key`) and, in cards view, the avatar becomes that ring
  (`EntityCard Key`, which wins over the page's own `Avatar` fragment); no
  spinner in the actions column. A row with a percentage to tell — a pull, a run
  on its way, files moving in or out of it — puts `ProgressRing` there instead,
  with the percentage inside and its two verbs under it; a start or a stop has
  none, so its ring just turns. The magnifier is drawn only when there is
  something behind it — a pull's console, a transfer's list — and a job with
  nothing to open passes no `OnLog`, which takes the button away: one that
  opens nothing is worse than no button. Several jobs of one kind
  on one row are one ring, not one each (`TransferFlow`): the percentage is
  their bytes as a whole, the tooltip names them, the magnifier opens the list
  of them (`TransfersDialog`, built on `TransfersList`: a line each, with its
  bar, its step and its own cross, and under them the files still waiting their
  turn in this client, which a cross simply takes out of the queue), and the
  ring's own cross asks once and stops them all — a row that grows with each
  file makes the table dance.
  What the client itself carries goes in two queues, one each way
  (`FileTransfers`), one file at a time and first in, first served: a batch
  never interleaves with the next, an upload does not hold a download back,
  and two Save As pickers are never open at once. The status line of the view
  that started them says how many are still waiting their turn.
  A row that failed (`ProgressCell` in the table, `ProgressRing` in a card) turns
  to the error colour, says why in its tooltip, keeps the console when its
  output holds the reason (a pull; a run that failed after its pull has none,
  `LogAfterFailure="false"`) and its cross becomes Dismiss: the agent remembers
  a failed pull five minutes and a failed run 45 seconds, and the cross forgets it now, for every client. No confirmation:
  nothing is lost.
- The agent not answering (`AgentLink`, read by `AgentLinkHandler` from every
  call the client makes: a call that could not be sent at all takes the link
  down, any answer brings it back): the application is not made to work
  without it, so the layout says it once, over the whole screen — a
  `MudOverlay` with a ring, "Waiting for the agent…" and the address, the
  content inert under it — and asks for `/api/v1/health` every 2 s until an
  answer comes. Nothing else says it: `ErrorsStaySnackbar` shows no error while
  the link is down, a list page leaves `Error` empty and skips its 5 s polls, a
  page's own follower (pulls, runs) catches the failed tick and goes on. When
  the link is back every list page reads again (`AgentLink.Changed`), as it
  does when the session starts. The way out, on the panel: a native client
  offers **Change agent** (the sign-in screen, which holds the chooser and has
  a layout of its own, so the veil goes with this one) and **Close**
  (`IClientLifetime`, the client ending itself); the browser, which can do
  neither, offers **Reload**. No Cancel: a page under the veil with no agent
  behind it does nothing.
- The last catch (`UnhandledErrors`): Blazor's yellow bar, "An unhandled error
  has occurred", is gone from both `index.html` files — it looked final
  and was not: a WebAssembly app goes on after such an error. What nothing
  caught (a dialog, a provider, the layout: anything outside the page's
  `ErrorBoundary`) is reported instead — through
  `UnhandledErrorLoggerProvider`, the renderer's own error log, in the browser
  (`WebAssemblyRenderer`) and in the native web view (`WebViewRenderer`)
  alike; MAUI's `BlazorWebView` has no event for it, WPF's does — and the
  layout shows it as an error toast that stays and can be copied.
- Exceptions caught: `AgentApiException` and `HttpRequestException` only —
  and the 401 among them is caught too and says nothing (`IsSignInRequired()`),
  because the sign-in screen is already on its way. Filtering it out of the
  `when` needs a second catch that swallows it (`ContainerStatsView`); with no
  such catch it leaves a lifecycle method and becomes the app's unhandled-error
  bar, which is what the native client showed while signed out.

## Icons

One icon per action, the same wherever the action appears — a row, a card, a
menu, a page verb, a rail, a dialog — so a button is read before its tooltip
is. Actions are `Icons.Material.Filled.*`; `WslcIcons.*` (the project's own
glyph set, in colour) keeps the navigation, the session block and the Files
browser. A new action takes its icon
from this table, or adds a row to it; nothing is inlined twice with two icons.

| Action | Icon | Colour | Where |
| --- | --- | --- | --- |
| Run (create and start) | `PlayArrow` | Success | Containers page verb, image row, launch rail |
| Start | `PlayArrow` | Success | container row and card |
| Stop | `Stop` | Primary | container row and card |
| Restart | `RestartAlt` | Warning | container row and card |
| Create, Add, New | `Add` | Primary | page verbs (Containers, Networks, Volumes), image row, pickers' Create / New, rows' Add |
| Save (a form, an image to a file) | `Save` | Primary; Error when it removes first | launch rail, Settings, image menu |
| Remove, Delete, Unpublish | `Delete` | Error | every row and card, Settings lists |
| Prune, Delete all | `DeleteSweep` | Error | page verbs |
| Cancel a change, Dismiss, Remove a row, Close a pane | `Close` | Error (Warning in the launch rail) | rails, rows, progress cells |
| More actions, Pick (inside a field) | `MoreVert` | — | row menus, `wslc-field-pick` buttons |
| View & edit, Edit & run again | `EditNote` | — | rows, cards, menus, failed launch rows |
| Edit text (a file, a saved login) | `Edit` | — | Files browser, Settings |
| View details | `Article` | — | container row and card |
| Logs, output of a pull or a run | `Subject` | — | rows, progress cells and rings, the launch dialog's pull output |
| Exec, terminal | `Terminal` | — | rows, navigation verbs |
| Files | `Folder` | — | rows, cards |
| Open with browser | `OpenInBrowser` | — | container row |
| Browser sessions | `Visibility` | — | container row |
| Container usage | `Inventory2` | — | image and volume menus |
| Image packages and CVEs | `Security` | — | container menu |
| Pull, Download | `Download` | Primary as a page verb | Images verb, image row, Files |
| Push, Upload | `Upload` | — | image menu, Files |
| Build | `Build` | — | Images verb |
| Import an archive | `Input` | — | Images verb |
| Load an archive | `Unarchive` | — | Images verb |
| Search on Docker Hub | `TravelExplore` | — | Images verb |
| Tag | `Sell` | — | image menu |
| Connect, Disconnect | `Link`, `LinkOff` | — | network row |
| Network map | `Hub` | — | Networks verb |
| Refresh | `Refresh` | — | page headers, panes |
| Reset zoom | `SettingsBackupRestore` | — | charts |
| Zoom in, Zoom out | `ZoomIn`, `ZoomOut` | — | charts, browser pane |
| Copy | `ContentCopy` | Info in rails | everywhere a value is copied |
| Search, filter | `Search` | — | search boxes and their toggles |
| Fill from a command | `AutoFixHigh` | Success | launch form |
| Load JSON | `UploadFile` | Success | launch rail |
| Full variables | `DataObject` | Success while on | launch rail |
| Export | `FileDownload` | Info | launch rail |
| Keep running | `AllInclusive` | Success while on | launch form |
| Edit the dashboard | `DashboardCustomize` | — | Home verb in View |
| Done (leave Edit) | `Check` | Primary | Home verb in Edit |
| Open (a card's page or full chart) | `Launch` | — | the selected card's verb in View |
| Card settings | `Tune` | — | the selected card's verb in Edit |
| Add a card | `Add` | Primary | Edit's button with nothing selected |
| Remove the selected card | `Remove` | Error | Edit's button with a card selected |
| Resize handle | `SouthEast` | Success | the selected card's corner in Edit |

## Spacing classes a page may use

`mb-1` under the header row, `mb-2` under an alert, `ml-1` for an inline
spinner, `flex-shrink-0` on rows above a grid, `mt-2` between the fields of
a dialog, `pa-1` around a list's cards (`CardsGrid`).
Nothing else; if a screen seems to need more, the shared component is
missing a feature, not the page a class.

## Where the application's look is written

The numbers behind the controls — how tall a bar's buttons and fields are,
what a cell keeps to its sides, the glyphs of a row's actions, the elevation
the tabs and the lists' header row stand at, and which standard MudBlazor typo
each of the four type roles takes — live in one place:
`WslcAgent.UI/Components/Styles/UiStyleValues.cs`. They are the application's
values, the same on every client and on every machine.

`UiStyle` writes them as CSS custom properties on the document, and the
stylesheet reads them with the same numbers as fallbacks, so a screen painted
before the client has applied anything already looks right. Where a look is a
component's parameter rather than CSS, it reaches every screen through the one
component that draws it: `ListGrid` (every list), `EntityCard` (cards view),
`ViewModeToggle` (the table-or-cards switch) and the details screen's tabs.

Settings › Layout edits those same values at runtime: **Apply** puts a draft on
every screen without saving it (it survives navigation and a reload of the page,
and is forgotten when the application closes), **Save** keeps it on that device
alone, and **Reset** puts one control back. It is a bench for trying a look; the
look the product ships with is the defaults in that file.
