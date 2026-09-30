# RULES

Not guidelines. These are settled, and they are not reopened by whoever happens
to be editing a screen. Take them or leave them.

## Type: five sizes, and nothing else

Every piece of text in this application is one of these five. The first four
come from the theme (`WslcTheme.cs`); the fifth is the one the theme has no
slot for, declared once as a variable of ours; nothing declares a size of its
own.

| step | size | theme | used for |
|---|---|---|---|
| 1 | 16.96px (1.06rem) | `H6` | page and section titles |
| 2 | 13.12px (0.82rem) | `Default`, `Body1`, `Button` | body text and buttons |
| 3 | 11.52px (0.72rem) | `Body2`, `Subtitle2`, `Caption` | everything dense: tables, field values, tabs, headers |
| 4 | 10.88px (0.68rem) | `Overline` | the smallest: paths, counts, the note under a field |
| 5 | 9.6px (0.6rem) | `--wslc-typography-tiny-size` (the stylesheet's `:root`) | a dashboard part set to Small whose text already stands at step 4 |

**Step 5 exists for a dashboard part's Small.** A dashboard part's Small has to
be smaller than its Medium, and the System card's labels already stand at step
4, so Small and Medium would draw the same. It is the existing ratio between
steps 3 and 2 (0.82 / 0.72 = 1.139) taken once more below step 4:
0.68 / 1.139 = 0.597, so 0.6rem. It has three more uses: the texts at the left
of a header bar that no longer fits take it before they are hidden (a smaller
word is still read); and the title bar's zoom percentage on a phone (600 px or
less), its number and its %. It is for those and nothing else: a screen that
wants its text smaller than step 4 asks first, as for any size.

**One exception, outside the scale**: the code an opened log or Activity row
shows — the command and its JSON — in a narrow table (a phone, 600 px or less)
is 8px (0.5rem), `--wslc-typography-detail-code-size` (the stylesheet's
`:root`). So is everything else that opened row says — its summary line and its
labels (Command and output, Output, Errors) — so the detail is one size and not
two. There is a great deal of it, and at step 5 it still cannot be read on a
phone. It is an exception and not a sixth step: nothing else may use it, and a
second one is asked for like any size.

**The Home dashboard's objects scale, outside the scale**: a ring and its text
grow together, in the same proportion. The text of an object on the Home
dashboard is one of the theme's sizes above times the object's scale,
`--wslc-dash-scale` — 0.75 for Small, 1 for Medium, 1.3333 for Large — as its
ring, its glyphs and its avatar are. Medium is always a size of the scale;
Small and Large are that size scaled, not steps of it. Each part of such an
object — a dial's figure and name, a reading's label, value and hint — may take
a size of its own on top of it, the same three factors again
(`--bz-part-{key}-scale`), so a part's text is a theme size times the object's
factor times the part's. Nothing outside those objects may use it. Their
buttons' glyphs follow the same factors from a base of 20px, one rule for every
object's buttons. Two of their sizes are MudBlazor's own instead of the
theme's, so a resource card on the dashboard draws as the list pages' card
does: the header's avatar letter, MudAvatar's 1.25rem, and its state chip,
MudChip's 0.75rem, each times the object's scale; and a dial of bytes is the
list card's 56px at Medium, not a percent's 60px. One field follows them too: a
choice inside an object — the File transfers card's filter,
`wslc-dash-choice`, which has no label — its value and the items of its list at
body2, each times the object's scale; bare (`Variant.Text`, no underline,
MudBlazor's own parameters); its ground and its text the object's, not the
form fields' grey box; and everything else of the field MudBlazor's.

A rule that needs a size names the variable — `var(--mud-typography-caption-size)`,
`var(--mud-typography-overline-size)` — never a number. A number in a
stylesheet is a fifth size waiting to happen: the screen this was decided on
was carrying nine, and two of them were three tenths of a pixel from another.

Icon sizes are not type sizes. The units inside an SVG glyph's viewBox are a
proportion, not a size on the screen, and they are not counted here.

## Fields: the one we already have

A field is `MudTextField` (or `MudSelect`) with these parameters, and it is not
restyled:

- **`Variant.Filled`** — the box comes from MudBlazor. We do not draw one.
- **`Margin.Dense`** — MudBlazor's own way of shortening a field. Nothing tighter.
- **`HelperTextOnFocus`** — the example shows while the field has the cursor.
- **`Immediate`** — the model follows every keystroke, not the blur.
- A field that opens something — a list of values, an image, a volume, a
  network, a folder — puts its button **inside itself**, as the green disc in
  `wslc-field-pick`, the same one the Files rows carry.

These four are every shape there is. They are copied from the code that runs,
so a new field is one of them with its own binding and its own words.

### A plain field

`ContainerForm.razor:73`

```razor
<MudTextField Margin="Margin.Dense" T="string" @bind-Value="Form.User" Label="User"
              Immediate="true" HelperTextOnFocus="true" Variant="Variant.Filled"
              HelperText="Example: 1000 or www-data" />
```

### A field that opens a picker

The button goes inside, in the wrapper. `has-note` only when the field carries
a `HelperText`: it gives back the line the note reserves underneath, so the
button centres on the value instead of sitting low. A field with nothing but a
`Placeholder` uses `wslc-field-pick` alone. `ContainerForm.razor:41`

```razor
<div class="wslc-field-pick has-note">
    <MudTextField T="string" @bind-Value="Form.ImageRef" Label="Image" Immediate="true"
                  HelperTextOnFocus="true" Required="true" RequiredError="Image is required"
                  Variant="Variant.Filled" Margin="Margin.Dense" HelperText="Example: alpine:latest" />
    <MudFab Color="Color.Success" StartIcon="@WslcIcons.More" Size="Size.Small"
            title="Pick a local image" OnClick="PickImageAsync" />
</div>
```

### A choice

Same parameters, plus `Dense` — which on a select is a different thing from
`Margin.Dense`: it tightens the rows of the list that drops open.
`ContainerForm.razor:76`

```razor
<MudSelect Margin="Margin.Dense" T="string" @bind-Value="Form.RestartPolicy" Label="Restart policy"
           Dense="true" HelperTextOnFocus="true" Variant="Variant.Filled"
           HelperText="Dashboard-owned; production container">
    <MudSelectItem T="string" Value="@RestartPolicyInfo.No">No</MudSelectItem>
    <MudSelectItem T="string" Value="@RestartPolicyInfo.UnlessStopped">Unless stopped</MudSelectItem>
    <MudSelectItem T="string" Value="@RestartPolicyInfo.Always">Always</MudSelectItem>
</MudSelect>
```

### Several values in one field

`MultiValueField` is ours and already holds the field, the green button and the
list editor. Never build this by hand. `ContainerForm.razor:61`

```razor
<MultiValueField Values="Form.Publish" Label="Publish" Split="ValueList.Split" Placeholder="8080:80"
                 HelperText="Example: 8080:80. Several separated by commas, or edited as rows with ⋮."
                 Hint="One port publication per row: [host:]hostPort:containerPort. Each row becomes a --publish flag." />
```

## Before touching any of this

**Changing a type size, or the CSS behind fields, needs the maintainers'
approval in an issue or a pull request before it is made, every time.** Not a
judgement call, not "while I was in there", not a tidy-up. Ask first.

The reason is not ceremony. Every rule of ours that reaches into MudBlazor's
internals is a rule that breaks on the next version and takes a screen with it,
and each one starts as somebody's small improvement. The five sizes and the
field above are what is left after taking those out, one measurement at a time.
