# Releasing

The version in the repository is the last release's: `<Version>` in
`Directory.Build.props` for the agent, and `<ApplicationDisplayVersion>` and
`<ApplicationVersion>` (the Android versionCode) in
`src/WslcAgent.App/WslcAgent.App.csproj` for the client. Everyday builds never
change them, nor take a number of their own: each installer build carries the
last release's version with the next fourth part (`1.0.27.3`), written to
`private\version.props`, which git does not track
([updating.md](../updating.md)).

A release takes a version above the last one and every local build, builds
the installers at exactly that version, and publishes them. The Android
versionCode is the client's version as one number (`1.0.18` is `100018000`,
`1.6.1.4` is `106001004`), so it rises with each release too.

## Version numbers

| What | Version | How |
|---|---|---|
| A release | `1.6.1` | its fourth part is 0, and is not written |
| Local builds | `1.6.1.1`, `1.6.1.2`, `1.6.1.3` | every installer build |
| A patch | `1.6.1.4` | `.\deploy-release.ps1`: the next fourth part after every build |
| A release for a WSL preview | `1.6.4` | `-Version 1.6.4`: WSL 3.0.4's patch number as the third part |
| Its patches | `1.6.4.1`, `1.6.4.2`… | `.\deploy-release.ps1`, as any patch |
| That WSL made official | `1.7.0` | `-Version 1.7.0`: the next minor |

Local builds and patches share the fourth part as one counter, so a patch is
always above the builds before it and neither ever takes the other's number.
A patch's number therefore jumps (`1.6.1.4`, then `1.6.1.9`): the numbers in
between were local builds, never published. The third part of a release for
a WSL preview says which preview it was made for: on `develop`, while its
version is still `main`'s, the script asks for `-Version` rather than make a
patch of the official release a preview; a WSL preview whose patch
number would not be above the last release (a `.0`) is decided when it comes.

## Branches

| Branch | What it holds | Its releases |
|---|---|---|
| `develop` | the work, with the newest WSL, previews included | previews: GitHub pre-releases |
| `main` | what works with the official WSL | official releases |

Work, and pull requests, go into `develop`. What a WSL preview brings is used
when the agent's `wslc` has it, and the official one keeps working as before,
so `develop` can go to `main` whenever it is tested; what cannot work without
a preview waits in `develop` until that WSL is official. A fix to an official
release is made on `main`, and taken into `develop` straight after, so it is
not lost.

A pre-release is never in GitHub's `releases/latest`, so an agent from an
official release is never offered a preview: not in Settings → Update, not
by the install line. An agent from a preview is offered the newest of all,
the next preview, or an official release above it, and Install from GitHub
runs that release's own install script ([api-v1.md](../api-v1.md)).

## One command

From the root of the repository, on any branch, straight after a build:

```powershell
.\deploy-release.ps1
```

On `main` the release is an official one. On `develop`, or any other branch,
it is a preview: made on `develop`, its tag's message starts with `Preview`,
and the release is published as a pre-release.

It takes what this checkout holds: it goes to the release's branch and
commits the changes not committed yet, with a message it writes itself (new
files are listed, and taken only when you answer `y`). It brings that branch
up to date. Then it takes each open pull request into it in turn: it waits
for its checks, and asks whether to merge it into this release (`y` merges
it, squashed, and deletes its branch; anything else leaves it open). One
whose checks fail is reported and left out. The branch is brought up to date
again, so the release carries what was merged.

It takes the agent and the client to the next patch ([above](#version-numbers)),
and the versionCode to that version as one number. To choose the version
yourself, above the last release and every local build:

```powershell
.\deploy-release.ps1 -Version 1.6.4
```

Then it commits the two version files, tags `v<agent version>` and pushes
the commit and the tag; nothing is built or uploaded from this machine.
**GitHub Actions** sees the tag (`.github/workflows/release.yml`) and, in
about 20 minutes:

1. Builds `wslc-ai-agent.msi` (with `-Release`: its wizard offers
   `C:\Berpiztu\wslc-ai-agent` as the package folder), `wslc-ai-client.msi`
   and `wslc-ai-client.apk`, at exactly the versions the tag's commit carries.
2. Creates the GitHub release with the three installers and the two defaults
   files. Its page opens with what the version brings, when the repository
   says it ([below](#what-the-releases-page-says)), and ends with the list
   GitHub writes itself: the pull requests merged since the last release
   and the link that compares the two.

A failed build leaves the tag without a release: fix what failed, then run
the workflow again on the tag (Actions → Release → Run workflow, the tag as
the branch).

### What the release's page says

Every release, preview and patch alike, has its notes written before it is
made, in `docs/release-notes/<version>.md` (`docs/release-notes/1.5.0.md` for
the tag `v1.5.0`, `1.6.1.4.md` for a patch): Markdown, for the person who is
about to install it, not a list of commits. They are whole, in one voice: a
patch's hold everything its minor version brought, with what the patch adds,
since its page may be the first one a person reads. The file has to be
committed with the release. The script looks for it before anything else,
and stops when the version has none, before committing, merging or pushing
anything; once the branch is up to date it stops again if the file is not in
the repository yet. It takes a new file only when you answer `y` to its
question about new files.

The file is read when the release is created. To change the page of a
release that exists, edit it on GitHub, or:

```powershell
gh release edit v1.5.0 --notes-file docs\release-notes\1.5.0.md
```

### The repository's secrets

The APK is signed on GitHub with the same key as every earlier one, and
carries the Firebase configuration that gives it notifications. Both come
from three repository secrets (Settings → Secrets and variables → Actions),
set once from `private\` with the GitHub CLI:

```powershell
gh secret set ANDROID_KEYSTORE_BASE64 --body ([Convert]::ToBase64String([IO.File]::ReadAllBytes("private\android.keystore")))
gh secret set ANDROID_KEYSTORE_PASS --body (Get-Content private\android.keystore.pass -Raw).Trim()
gh secret set GOOGLE_SERVICES_JSON_BASE64 --body ([Convert]::ToBase64String([IO.File]::ReadAllBytes("private\google-services.json")))
```

Without them the workflow stops before building: with no key it would sign
with a new one, which no installed app accepts.

### Building here instead

`-Local` builds the three installers on this machine first, as before the
workflow existed (a failure puts the versions back and commits nothing),
then commits, tags and pushes; its tag says it was built locally, and the
workflow leaves it alone. `-Publish` also creates the release and uploads
the files from here (the GitHub CLI, `gh`, signed in):

```powershell
.\deploy-release.ps1 -Local -Publish
```

## Before releasing

- The APK is signed with `private\android.keystore`. Android installs an
  update only when it is signed with the same key as the installed app: a
  release must always be signed with the same key
  ([private-files.md](private-files.md)).
- A release's agent installer never carries
  `private\firebase-service-account.json`: that key is a secret, and a
  published installer would hand it to whoever downloads it. A released agent
  pushes no notifications to phones until its user copies a key of their own
  into its data folder ([private-files.md](private-files.md#3-the-service-account-key)).
  Installers you build for yourself, without `-Release`, still carry yours.
- The APK carries `private\google-services.json`, which names your Firebase
  project and app but grants nothing: it is what every Android app ships.
- The installers are not digitally signed: the README says so, and how to get
  past Windows' and the browser's warnings.
