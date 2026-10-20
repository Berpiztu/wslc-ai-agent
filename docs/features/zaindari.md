# Zaindari

*Zaindari* is Basque for safekeeper: the one who watches over the flock. Here
the flock is your containers, and what it watches is their images. It reads
what each image carries, in the image's own files and never by running it,
and asks the public vulnerability database, [OSV.dev](https://osv.dev), what
is known against each one:

- **Its Python libraries**, in every site-packages, a virtual environment's
  too, checked against the GitHub Advisory Database and PyPI's own.
- **Its system packages**, by its distribution's database: Alpine's `apk`
  (`/lib/apk/db/installed`), Debian's and Ubuntu's `dpkg`
  (`/var/lib/dpkg/status`, and a distroless image's `status.d`), checked
  against Alpine's, Debian's and Ubuntu's own security trackers. A package is
  named as they name it: Alpine's by its origin, Debian's and Ubuntu's by
  their source package (`libssl3` is `openssl`).

An advisory that names no severity of its own is given the one its CVSS
vector scores, a 3.x vector or a 4.0 one (Alpine's give only 4.0), scored
as NVD scores it.

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

- **Red**: a vulnerability of high or critical severity, or your image,
  refused: no new container is made from it until it is built again.
- **Yellow**: a vulnerability of moderate severity, in any other image.

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
  or clean.
- **Once for an image with no mark yet**, while the agent runs, when a
  container made around the agent (`wslc` by hand, a script) shows up.

A container's logs, and its project's, are its own: Zaindari's findings are
not written in them, since a log with no line about it would read as an
image with nothing wrong. Every finding is written in the agent's own log.

## Patch: a system package mended without waiting

A fix published by Alpine, Debian or Ubuntu reaches an official image only
when that image is built again, which can take weeks. When an image carries
a system package with a fix published, Zaindari's window, opened for a
container or an image, offers **Patch**, as a Windows update patches
Windows:

- It builds `<image>-patched` (`alpine:latest-patched`; a patch patched again
  keeps its name): the image itself, its packages upgraded as root
  (`apk upgrade`, or `apt-get update` and `apt-get upgrade`) and its own user
  given back. It is built on this machine, so it is yours, and checked as any
  build: under Block, a patch still vulnerable moves nothing.
- Then it moves the containers ticked, every one of the image's to begin
  with, to the patch, each recreated with its ports, mounts, environment and
  name, and started if it was running.
- A project's containers are left as they are: its compose file names its
  image, so the image there is changed by hand.
- Python libraries are not patched: raising one is a change to the program,
  made in its requirements.

The window closes when the patch starts; the agent builds it and says when
it is done. A patch is published nowhere, so **Pull** on it pulls the image
it was made from. A container that runs a patch says **patched** after its
state, and so does its project's chip; its **Update image** goes to the
original as published, and says so, with **Patch it** to stay on a patch
while the original still carries what it mended. An image without a package manager (distroless) is not offered
one.

A container's **Update image** and **View & edit** offer it too: when the
image named is on the machine and has a patch, a note says that pulling it or
recreating on it again does not mend it, and **Patch it**, unticked, has the
agent patch the image and move the container to the patch.

## Settings

**Settings → Security → Image security check** sets what a vulnerability of
moderate severity or worse in your image does: **Block** (the default)
refuses it as above; **Warn** only marks it; **Off** checks nothing.

Each image's libraries are read once and kept by the image's id, and each
advisory is kept for a day, so a check after the first is quick, after a
restart of the agent too.
