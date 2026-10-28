# Zaindari

*Zaindari* is Basque for safekeeper: the one who watches over the flock. Here
the flock is your containers, and what it watches is their images. It reads
what each image carries, in the image's own files and never by running it,
and asks the public vulnerability database, [OSV.dev](https://osv.dev), what
is known against each one:

- **Its Python libraries**, in every site-packages, a virtual environment's
  too, checked against the GitHub Advisory Database and PyPI's own. Not a
  library Debian or Ubuntu installed (`python3-flask`, in
  `/usr/lib/python3/dist-packages`): it is the distribution's, which mends it
  without changing the version PyPI would read, so it is checked as its
  package, and a patch offers no pip for it.
- **Its system packages**, by its distribution's database: Alpine's `apk`
  (`/lib/apk/db/installed`), Debian's and Ubuntu's `dpkg`
  (`/var/lib/dpkg/status`, and a distroless image's `status.d`), checked
  against Alpine's, Debian's and Ubuntu's own security trackers. A package is
  named as they name it: Alpine's by its origin, Debian's and Ubuntu's by
  their source package (`libssl3` is `openssl`).

A system package's severity is the one **its own distribution** gives it,
since the distribution decides whether and when a fix comes: Ubuntu's
priority (`medium` is moderate), Debian's urgency on its release. A CVSS
critical that Ubuntu rates medium is moderate, yellow, not red. Only when
the distribution gives none (Alpine never does, Debian's "not yet assigned"
is none) is an advisory given the one its CVSS vector scores, a 3.x vector
or a 4.0 one, scored as NVD scores it; a Python library's is GitHub's.

Debian's and Ubuntu's kernel (`linux`, whose headers an image carries) is
left out: a container runs on the kernel of WSL's virtual machine, never on
its image's, so the kernel's vulnerabilities, hundreds of them, are not the
container's. So is what the distribution says is not a security problem for
it, Debian's `unimportant` and Ubuntu's `negligible`: it will not fix it, and
nobody else can.

A fix only **Ubuntu Pro** carries (Canonical's subscription, `esm` versions)
depends on the image. One left attached to Pro, its ESM sources
(`/etc/apt/sources.list.d/ubuntu-esm-*`) and their credentials
(`/etc/apt/auth.conf.d/90ubuntu-advantage`) in it, reaches it with its own
upgrade: it is a fix like any other, which a patch installs. In any other,
an image of the free archive or one built with Pro and detached at the end
as Canonical's Dockerfile does, it is said beside the vulnerability, "fixed
only in Ubuntu Pro", and counted in what is left; an image built with Pro
takes it by being built again with Pro.

An image with neither, or of a distribution OSV.dev has no tracker for (Kali,
`scratch`, distroless with no package list), is said to be unchecked, never
clean.

## What it finds belongs to the image

Zaindari checks images, so what it finds is kept on the image itself, by its
id: two tags of two different images are two marks, and an old tag can be
red while a new one is clean. A container reads the mark of its image, by
its image's name and tag; it keeps nothing of its own. An image and its
containers therefore always say the same.

## The one rule: no new container from your image while it is vulnerable

A container is always yours: it is started, stopped and restarted whatever
its image carries. What Zaindari guards is what is made **new**.

- **An image built on this machine is yours.** You can mend it: raise the
  library to the version Zaindari names, or for a system package build on a
  newer base image or upgrade it in the Dockerfile, and build the image again. Until you
  do, while it has a vulnerability of moderate severity or worse, the agent
  makes **no new container** from it, and its build stops.
- **An image from a registry is not yours.** Only whoever publishes it can
  mend it. Containers are made from it as always, and marked: whether one
  runs is yours to decide, not the agent's.

| | Your image, vulnerable | A registry's image, vulnerable |
|---|---|---|
| A build of the image | stopped, naming the version that fixes each library | (not built here) |
| A new container: Run, Create, View & edit's Save, an image update, a project's save | refused, nothing touched: a container it was to replace runs on as it was | made, and marked |
| Start, Stop, Restart, a project's start, the restart policy | as always | as always |

## Colours

A colour is an alarm, and an alarm is raised only for what something can be
done about: a fix the image can reach (a patch, a pull, a newer base), or a
Python library, which can be raised, replaced or taken out.

- **Red**: such a vulnerability of high or critical severity, or your image,
  refused: no new container is made from it until it is built again.
- **Yellow**: such a vulnerability of moderate severity, in any other image.
- **No colour**: what is left is a system package its distribution has not
  fixed for the image (no fix, or one only Ubuntu Pro carries in an image
  not attached to it). Nobody downstream can mend it, and an alarm nobody can
  answer only wears people out; a container says **CVE** in grey, and
  Zaindari lists it all. When the distribution publishes a fix, the next
  check raises the alarm, for the patch to take it.

On the **Images** page a marked image is red or yellow from one side to the
other: its whole row in the table, its whole card. On the **Containers**
page, and on the dashboard, a container of a marked image says **CVE** after
its state, in that colour, and so does its project's chip; the Zaindari item
of every menu wears it too. No tooltip: what matters is seen, on a phone too.

## When it checks

Only when something happens, never on a timer: an image nobody uses costs
nothing.

- **When an image is built** by the agent.
- **Before a container is made** from an image.
- **When you open it**: *Zaindari: check vulnerabilities*, in a container's,
  a project's or an image's menu. Its window says "Checking Zaindari -
  Vulnerabilities for …" while it asks OSV.dev, then shows only
  vulnerabilities: each one in the colour of its severity with the version
  that fixes it, a line per library, and, for your image, a red line saying
  the agent refuses it. What it finds marks the image again, worse, better
  or clean. A project's window lists its containers at the left, each in
  its image's colour, and for the one chosen shows, in tabs of its pane,
  its **Vulnerabilities**, what to **Accept**, and its **Patch**, each with
  its verb at the right end of their bar, and *Up to date: nothing to accept
  or patch.* on that bar when neither has anything to do; Cancel goes
  back to the vulnerabilities. The window stays open after a Save
  or a Patch, the next container is chosen, and one being patched turns its
  wheel in the list until the agent has moved it, and shows its build's log
  in the pane, its steps waiting.
- **When a container runs**: the image of a running container with no mark
  yet is checked once while the agent runs, whoever started it (the agent,
  `wslc` by hand, a script). A stopped container is not: one stopped for a
  month is checked when it is started again, not while it sits there.

A container's logs, and its project's, are its own: Zaindari's findings are
not written in them, since a log with no line about it would read as an
image with nothing wrong. Every finding is written in the agent's own log.

## Patch: an image mended without waiting

A fix published by Alpine, Debian or Ubuntu reaches an official image only
when that image is built again, which can take weeks, and a Python library's
only when whoever made the image raises it. Zaindari's window, opened for a
container, an image or a project's container, offers **Patch**, as a Windows update patches
Windows, a fix published or not: what has none is mended only by taking it
out or replacing it. Its window shows the **Dockerfile** of the patch, whole,
as it will be built into `<image>-patched` (`alpine:latest-patched`; a patch
patched again builds on it and keeps its name):

```dockerfile
FROM ghcr.io/iosub/ia-pdf-excel-backend:latest
USER root
RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get upgrade -y && rm -rf /var/lib/apt/lists/*
COPY --from=ghcr.io/astral-sh/uv:0.13.6 /uv /usr/local/bin/uv
COPY requirements.txt /backend/requirements.txt
RUN uv pip install --no-cache --no-build --python /usr/local/bin/python3.12 "pillow>=13.0.0"
RUN uv pip uninstall --python /usr/local/bin/python3.12 python-jose
USER app
```

- **Before anything is chosen, Zaindari says what it thinks** of patching
  the image's Python libraries, read from the image: **patchable**; **with
  care**, each thing said (Python in several environments, and which; builds
  for one variant, as `torch 2.4.0+cpu`, which stay one; lines the
  application's own files pin, with the reason their authors wrote beside
  them; libraries no version fixes yet); or **not patchable**, and why (none
  of its vulnerable libraries is in an environment the agent can read). In a
  box of its own height, so what it says never moves the window.
- **Try the patch** runs the patch's Python lines before it is built, in a
  container of the image, with `--dry-run`: nothing is written, and it takes
  seconds where the build takes minutes. It says what each would change,
  library by library (`torch 2.4.0+cpu → 2.14.1+cpu`, `new: httpx2 2.16.0`),
  what calls for care (a build for one variant that would become another,
  NVIDIA's libraries it would bring, libraries the image does not have), or
  why it would fail, in uv's own words. A choice changed forgets what was
  tried.
- The agent writes it first: `FROM` the image, as root its system packages
  upgraded when one has a fix the image can install (`apk upgrade`, or
  `apt-get update` and `apt-get upgrade`), and its own user given back last.
  It is changed as you will: what you leave is what is built.
- **requirements.txt**: every `requirements*.txt` the image carries of its
  own (not a library's) is read with its packages, and listed a line a row;
  so is each vulnerable Python library they do not name (a dependency, as
  Pillow under pdfplumber). Each row says what to do with it: **Keep**,
  **Raise** to the version Zaindari names (`pillow==12.2.0` → `pillow>=13.0.0`),
  **Latest**, or **Take out**, shown red and struck through. **Raise the
  vulnerable** raises every row a version fixes; taking one to its latest is
  chosen row by row, never all at once: a line its authors pin has its reason
  (agent-zero's `pyreqwest-impersonate==0.5.3`, the last with a wheel). The file is rewritten by the
  choices, comments, options and each line's condition kept
  (`; sys_platform == "win32"`), a dependency added with the version that
  fixes it, and shown whole under the rows, with a button to copy it to your
  sources. As each choice is made, the Dockerfile is written by it: the lines
  that copy the file in over the old one, install what was raised and take
  out what was taken out, put in and taken away again with the choices, so
  the Dockerfile shown is always what is built; the file goes beside it.
  What is installed is the libraries chosen, by name, never a file whole:
  installing a file installs every line of it not there yet, and a tool's
  file (SearXNG's development one) would bring Sphinx and Selenium into the
  application's environment. The
  installer is always uv, quicker than pip and never missing as pip is from
  an environment uv made (Hermes's `/opt/hermes/.venv`). An image may hold
  several Python environments (agent-zero has its application's virtual
  environment, SearXNG's, pyenv's and the system's), and the agent reads them
  from the image: where each library is installed, and which of those folders
  is a virtual environment (its `pyvenv.cfg`). A library is raised, or taken
  out, in every environment that holds it, with its interpreter
  (`--python /opt/venv-a0/bin/python3.12`), and a Python of the system's,
  which its distribution marks as its own, is told to let it
  (`--break-system-packages`); one installed nowhere is not installed, and
  the Dockerfile says so. A file none of whose libraries is installed (a
  tool's) is not copied either; one in an installer's cache is not listed.
  Never with `-U`, which takes every library and every one they need to its
  latest: what is installed and still meets what was asked stays as the
  image has it, and a library taken to its latest is named
  (`--upgrade-package fastapi`). Nor ever built from its sources
  (`--no-build`): only a version with a wheel for the image is taken, so a
  library whose newer version has its sources alone stays where it is
  instead of failing the build. A library installed as a build for one
  variant (agent-zero's `torch 2.4.0+cpu`, which PyPI does not have: its
  torch is built for CUDA, gigabytes of NVIDIA libraries with it) is read
  from its version, and its index added to every install in its environment
  (PyTorch's, `--extra-index-url https://download.pytorch.org/whl/cpu
  --index-strategy unsafe-best-match`), so a `+cpu` build that has to change,
  chosen or needed by another, stays one; a build whose index is not known
  is left as it is, and the Dockerfile says so. An image with no `uv`
  where its system looks for commands, nor the library, gets it first, copied from Astral's image
  (`COPY --from=ghcr.io/astral-sh/uv:…`). A library raised
  can break the program, which is why it is never done alone. An image with
  no requirements file was installed from files left outside it: its
  vulnerable dependencies can still be chosen, into a file of their own.
- **The containers** of the image, every one ticked to begin with, are moved
  to the patch once it is built, each recreated with its ports, mounts,
  environment and name, and started if it was running. A project's are
  listed with their project, and moved the same. Its compose file still
  names the original, so the agent records the service on the patch
  (`patched-services.json`), and every save of the project brings it up on
  the patch and says so in its question and its log: while the file names
  that image and the image is the one patched. A file that names another
  image, the original pulled anew or built again (a newer version, checked
  as any), or the patch gone from the machine, and the save brings the
  service up on what its file says, says why, and lets the record go.

The patch is checked as any build, and never refused: what is left in it, a
fix nobody has published yet, is its original's to mend, and refusing it
would leave the containers on the original, which carries all of it. If it
does not work, **View & edit** puts a container back on the image it had.

The window closes when the patch starts, and the title bar's activity line
follows it to its end, step by step (`[2/3] RUN apt-get …`), then says it
was built and how many containers moved; a patch that fails says why in a
window of its own. While it is built, the image's row and card and those of
the containers it moves say *Patching: building …*, with its console and
its cancel; every other container of the image, one not ticked, says
*Patching its image: building …*, on the Containers page and
on its header on the Home, and runs on as it is. A patch of a patch
builds on it: a library taken out, tried, then the next, one patch at a
time. A patch is published nowhere, so **Pull** on it pulls the image it was
made from. A container that runs a patch says **patched** after its state,
and so does its project's chip; its **Update image** goes to the original as
published, and says so, with **Patch…** to patch it again while the
original still carries what it mended.

A container's **Update image** and **View & edit** offer the same patch, in
one line that does not change size: a turning wheel while Zaindari is asked,
and Update, Save and Run wait for it; then what a patch would mend, or that
there is no system package to patch, with **Patch…** beside it, which opens
the same patch window, that container alone ticked. The patch moves it, and
the window it was opened from closes; View & edit asks first when what was
changed in it is not saved, since the patch moves the container with the
settings it has. Its arrow, as the Compose window's, opens the whole note
over the form.

### What a patch changed

Zaindari's window has two tabs: **Vulnerabilities**, its report, and
**Changes**, pressed when the image shown is a patch (a container on it, or
the patch itself). Changes says what the patch changed from the image it was
built from, in a table, a row a package: the system's packages first, then
each Python environment's libraries, by interpreter
(`/opt/venv-a0/bin/python3.12`), with the version before and after (`new`
for one it brought, `taken out` for one it took out) and **why**: the
vulnerabilities of it the image had and the patch no longer has, by CVE and
severity, the worst first; or, when it fixed none, why it changed all the
same (the system's upgrade took it to a newer version, or a library raised
needs it); and how many of its own the patch still has, which no version
fixes yet. Both images are compared as the agent read them and keeps them
under their ids, and the vulnerabilities by advisory, so no distribution's
version order is guessed at. Under the table, the Dockerfile the patch was
last built by. A package is the unit: a library is many files, and the
files of one are in Files.

For anything else, **Build image** takes a Dockerfile from your device,
pasted in its field or loaded into it, and builds it with no folder on the
agent's machine.

## Accept: what nobody can mend, in a part you do not use

A Python library is always raised, fix or no fix, since it can be raised,
replaced or taken out. Sometimes none of the three is sensible: no version
fixes the vulnerability yet, and it is in a part of the library the image
does not use (a library's own server, in an image that only uses its client).
Left as it is, the image and its containers stay coloured until somebody
upstream publishes a fix.

**Accept…**, beside Patch in Zaindari's window, lists the image's Python
libraries with a vulnerability moderate or worse that no version fixes, a row
each, and the ones accepted already. Tick a row, say why once, and save. An
accepted vulnerability:

- is shown in grey in the window, with who accepted it, when and why;
- gives the image and its containers no colour, and refuses nothing;
- belongs to the image's repository, not its tag: the next version you build
  keeps it;
- stops counting the day a version fixes it, and alarms again, for the patch
  to take it. Any other vulnerability alarms as ever.

Untick a row to withdraw it. A vulnerability with a fix is never offered: it
is mended, not accepted. Acceptances are kept in `image-acceptances.json`; an
AI agent over MCP sees them in `image_vulnerabilities`, and accepting stays a
person's decision.

## Settings

**Settings → Security → Image security check** sets what a vulnerability of
moderate severity or worse in your image does: **Block** (the default)
refuses it as above; **Warn** only marks it; **Off** checks nothing.

Each image's libraries are read once and kept by the image's id, and each
advisory is kept for a day, so a check after the first is quick, after a
restart of the agent too.
