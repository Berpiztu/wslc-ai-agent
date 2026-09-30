# Environment variables

Nothing about your machines is written in the repository's scripts: where
your signing key and Firebase files live comes from a variable when they are
not in `private\`. This page lists them all.

## Where to set them

The easiest place is `private\env.psd1`, which git never tracks. Copy the
example once:

```powershell
Copy-Item private\env.example.psd1 private\env.psd1
```

Then open `private\env.psd1` and fill in only what you use; an empty value
sets nothing. The file is read as data, never run.

The same names also work as ordinary environment variables. When both are
set, the environment wins over the file, and a parameter given on a script's
command line wins over both. A script that needs a variable nobody set stops
and names it.

## Build and signing

Read by `build-client-apk.ps1`, `build-client-installer.ps1`,
`build-agent-installer.ps1` and `check-private.ps1`. All optional: by default
the files are looked for in `private\` ([private-files.md](private-files.md)).

| Variable | Default | What it is |
|---|---|---|
| `WSLC_AGENT_KEYSTORE` | `private\android.keystore`, then `%USERPROFILE%\.wslc-agent\android.keystore` | The Android signing key, when it is kept elsewhere |
| `WSLC_AGENT_KEYSTORE_PASS` | The keystore's `.pass` file | Its password |
| `WSLC_AGENT_KEY_ALIAS` | `wslc-agent` | The key's alias inside the keystore |
| `WSLC_AGENT_KEY_PASS` | The keystore's password | The key's own password, when it differs |
| `WSLC_AGENT_GOOGLE_SERVICES` | `private\google-services.json` | The Android client's Firebase configuration |
| `WSLC_AGENT_PUSH_KEY` | `private\firebase-service-account.json` | The Firebase key the agent installer carries |

## The agent itself

The agent reads none of these. Its own settings (bind address and port) are
chosen when it is installed, and its Publishing settings (domain, name
suffix, proxy, network, map file) are set in its UI, **Settings →
Publish**.

Where it looks for its own updates and its clients', the package folder, is
asked by its installer and can be changed in **Settings → Update**:
[updating.md](../updating.md).
