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

From the root of the repository, with nothing uncommitted:

```powershell
.\deploy-release.ps1
```

It takes the agent and the client to the next patch above the higher of the
release's version and `private\version.props`, and the versionCode one above
both. To choose the version yourself, above every current one:

```powershell
.\deploy-release.ps1 -Version 0.3.0
```

Then it:

1. Builds `wslc-ai-agent.msi` (with `-Release`: its wizard offers
   `C:\Berpiztu\wslc-ai-agent` as the package folder), `wslc-ai-client.msi`
   and `wslc-ai-client.apk` into `dist\`, at exactly the release's versions.
2. Only when all three built: commits the two version files, tags
   `v<agent version>` and pushes the commit and the tag. When a build fails,
   the two files are put back and nothing is committed.
3. Says which three files to upload to the GitHub release.

To also create the GitHub release and upload the installers (the GitHub CLI,
`gh`, signed in):

```powershell
.\deploy-release.ps1 -Publish
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
