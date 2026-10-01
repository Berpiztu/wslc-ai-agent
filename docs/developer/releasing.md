# Releasing

The version in the repository is the last release's: `<Version>` in
`Directory.Build.props` for the agent, and `<ApplicationDisplayVersion>` and
`<ApplicationVersion>` (the Android versionCode) in
`src/WslcAgent.App/WslcAgent.App.csproj` for the client. Everyday builds never
change them: each installer build raises this checkout's own versions in
`private\version.props`, which git does not track
([updating.md](../updating.md)).

A release raises the repository's versions above every build so far, builds
the installers at exactly those versions, and publishes them.

## One command

From the root of the repository, on any branch, straight after a build:

```powershell
.\deploy-release.ps1
```

It takes what this checkout holds: it goes to `main` and commits the changes
not committed yet, with a message it writes itself (new files are listed,
and taken only when you answer `y`). It brings `main` up to date. Then it takes each open
pull request into `main` in turn: it waits for its checks, and asks whether
to merge it into this release (`y` merges it, squashed, and deletes its
branch; anything else leaves it open). One whose checks fail is reported and
left out. `main` is brought up to date again, so the release carries what was
merged.

It takes the agent and the client to the next patch above the higher of the
release's version and `private\version.props`, and the versionCode one above
both. To choose the version yourself, above every current one:

```powershell
.\deploy-release.ps1 -Version 0.3.0
```

Then it commits the two version files, tags `v<agent version>` and pushes
the commit and the tag; nothing is built or uploaded from this machine.
**GitHub Actions** sees the tag (`.github/workflows/release.yml`) and, in
about 20 minutes:

1. Builds `wslc-ai-agent.msi` (with `-Release`: its wizard offers
   `C:\Berpiztu\wslc-ai-agent` as the package folder), `wslc-ai-client.msi`
   and `wslc-ai-client.apk`, at exactly the versions the tag's commit carries.
2. Creates the GitHub release with the three installers and the two defaults
   files, its notes listing the pull requests merged since the last one.

A failed build leaves the tag without a release: fix what failed, then run
the workflow again on the tag (Actions → Release → Run workflow, the tag as
the branch).

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
