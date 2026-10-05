# Private files

Some parts of WSLC AI Agent need a secret that cannot live in a public
repository: the key that signs the Android app, and the two Firebase files
that carry push notifications to phones. This page tells you how to make each
one, step by step, and where to put it.

**You do not need any of them to start.** A fresh clone builds, runs the agent
and the clients, and passes every test with no private file at all. Make only
the ones for what you want to switch on:

| You want | You need |
|---|---|
| To build and run everything, and to debug the clients | Nothing |
| To build release APKs that update each other when installed | 1. The Android signing key |
| Push notifications on Android phones | 1, 2. The Firebase project and `google-services.json`, and 3. the service account key |

## Where they go

Everything goes in `private/`, at the root of your checkout:

```
private/
  README.md                      tracked
  env.example.psd1               tracked
  android.keystore               yours
  android.keystore.pass          yours
  google-services.json           yours
  firebase-service-account.json  yours
  env.psd1                       yours
```

Git tracks nothing in `private/` but the two files marked above; the rest is
ignored (`.gitignore`), so nothing you put there can be committed by
mistake. The same file names are ignored anywhere else in the tree too.

At any moment, this tells you what your checkout has and what each file
enables, without printing any secret:

```powershell
.\check-private.ps1
```

## 1. The Android signing key

Android installs an update of an app only when it is signed with the same key
as the version already installed; otherwise it answers "App not installed" and
the only way forward is to uninstall, losing the app's data. So every APK you
release has to be signed with one key, kept for as long as the app exists.

### The easy way: let the build make it

```powershell
.\build-client-apk.ps1
```

When no key exists, the script generates one in `private\android.keystore`,
with a random password in `private\android.keystore.pass`, and signs the APK
with it. That is all.

### By hand

If you prefer to choose the password yourself, you need `keytool`, which comes
with any JDK (the Android workload of Visual Studio or of the .NET SDK installs
one under `C:\Program Files (x86)\Android\openjdk\`):

```powershell
keytool -genkeypair -v -keystore private\android.keystore -alias wslc-agent -keyalg RSA -keysize 2048 -validity 10000 -dname "CN=wslc-agent"
```

`keytool` asks for a password twice. Then write the same password, alone on
one line, into `private\android.keystore.pass`.

### Keep it safe

- **Back up both files** (a password manager holds them well). If you lose the
  key, every phone with the app has to uninstall it before it can install your
  next APK.
- **Use the same key on every machine that builds APKs**: copy both files into
  that machine's `private\`.
- A machine that builds from several checkouts can keep one pair in
  `%USERPROFILE%\.wslc-agent\` instead; it is used when `private\` has none.
- A key kept anywhere else is named by `WSLC_AGENT_KEYSTORE` and
  `WSLC_AGENT_KEYSTORE_PASS` (see [Settings](#settings-envpsd1)).

Debug builds (`debug-android.ps1`) sign with the key too when there is one,
and with the Android SDK's debug key when there is none; a debug-signed app
cannot be updated by a release APK, nor the other way round.

## 2. The Firebase project and `google-services.json`

Push notifications reach Android phones through Firebase Cloud Messaging
(FCM). The agent hands each notification to Firebase, and Firebase wakes the
phone, whether or not the phone can reach the agent at that moment. This needs
a Firebase project of your own; the free plan is enough.

1. Open the [Firebase console](https://console.firebase.google.com/) and sign
   in with a Google account.
2. **Create a project** (any name, for example `wslc-ai-agent`). Google
   Analytics is not needed: you can switch it off.
3. In the project's overview, **add an app** and choose **Android**.
4. **Android package name**: `ai.berpiztu.wslcaiagent`, the client's application
   id. It has to match exactly, or the client gets no token. (If you publish
   the client under an id of your own, change `ApplicationId` in
   `src/WslcAgent.App/WslcAgent.App.csproj` and the activity name in
   `src/WslcAgent.App/Platforms/Android/MainActivity.cs`, and register that id
   here instead.)
5. Nickname and SHA-1 are optional; skip them. Click **Register app**.
6. **Download `google-services.json`** and save it as
   `private\google-services.json`. Skip the remaining steps of the wizard (the
   SDK is already in the project).
7. Rebuild the Android client. It now registers with Firebase when it signs
   in to an agent.

`google-services.json` identifies your project and app; it holds no key that
grants access, but it stays out of the repository because it is yours.

## 3. The service account key

The agent sends notifications with a key of the Firebase project's service
account. **This one is a real secret**: whoever has it can send notifications
to every phone registered in your project.

1. In the Firebase console, open your project, click the gear next to
   **Project Overview**, then **Project settings**.
2. Open the **Service accounts** tab.
3. Click **Generate new private key**, then **Generate key**. The browser
   downloads a JSON file.
4. Save it as `private\firebase-service-account.json`.

From then on:

- `build-agent-installer.ps1` puts the key into the MSI, and the installed
  agent finds it in its data folder,
  `%LOCALAPPDATA%\WSLC-AI-Agent\data\firebase-service-account.json`.
- An agent already installed, or one run from source, takes it when you copy
  the file into that folder by hand. **Settings → Notifications** shows
  whether the agent has a key and which project it sends through.

The Cloud Messaging API (V1) is enabled by default in new Firebase projects.
If the agent's log reports that it is disabled, enable it in **Project
settings → Cloud Messaging**.

**To revoke a key** (you lost it, or it was exposed): in the
[Google Cloud console](https://console.cloud.google.com/iam-admin/serviceaccounts),
choose your project, open the `firebase-adminsdk` service account, go to
**Keys**, and delete the key. Then generate a new one as above.

Both Firebase files have to belong to **the same project**: a client
registered in one project never receives what an agent sends through another.
`check-private.ps1` checks it.

## Settings: `env.psd1`

The build scripts read `private\env.psd1` when it exists. Copy
`private\env.example.psd1` to `private\env.psd1` and fill in only what you
use; an empty value sets nothing.

| Variable | Default | Use |
|---|---|---|
| `WSLC_AGENT_KEYSTORE` | `private\android.keystore`, then `%USERPROFILE%\.wslc-agent\android.keystore` | A keystore kept elsewhere |
| `WSLC_AGENT_KEYSTORE_PASS` | Its `.pass` file | Its password |
| `WSLC_AGENT_KEY_ALIAS` | `wslc-agent` | The key's alias inside the keystore |
| `WSLC_AGENT_KEY_PASS` | The keystore's password | The key's own password, when it differs |
| `WSLC_AGENT_GOOGLE_SERVICES` | `private\google-services.json` | The client's Firebase configuration kept elsewhere |
| `WSLC_AGENT_PUSH_KEY` | `private\firebase-service-account.json` | The service account key kept elsewhere |

The same names work as ordinary environment variables, and a variable already
set in the environment wins over the file. The file is a PowerShell data
file: the scripts read it as data and never run it.

## In GitHub Actions

The release workflow (`.github/workflows/release.yml`) signs the APK and
gives it notifications from three repository secrets (**Settings → Secrets
and variables → Actions**), which it writes back into files before building.
Binary files travel as base64; so does `google-services.json`.

On your machine, each line below copies one value to the clipboard; paste it
into the secret named above it. Do one at a time: the clipboard keeps only the
last.

`ANDROID_KEYSTORE_BASE64`:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("privatendroid.keystore")) | Set-Clipboard
```

`ANDROID_KEYSTORE_PASS`:

```powershell
Get-Content privatendroid.keystore.pass -Raw | Set-Clipboard
```

`GOOGLE_SERVICES_JSON_BASE64`:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("private\google-services.json")) | Set-Clipboard
```

[Releasing](releasing.md#the-repositorys-secrets) sets the same three with the
GitHub CLI in one go. Set them again whenever the key or the Firebase project
changes, or the next release is signed and configured with the old ones.

There is no secret for `firebase-service-account.json`, on purpose: a release's
agent installer never carries it, because whoever downloads the installer
would get it. A released agent pushes once its user copies a key of their own
into its data folder ([above](#3-the-service-account-key)).

Pull requests from forks get no secrets, so their builds are unsigned and
without push, exactly like a fresh clone.
