# private/

The private files of this checkout. Git tracks nothing in this folder but
this README and `env.example.psd1`; everything else you put here stays on
your machine (`.gitignore`).

Every file is optional. A fresh clone builds, runs and passes its tests with
this folder empty; each file only switches on something that needs a secret.

| File | What it switches on | Without it |
|---|---|---|
| `android.keystore` | APKs signed with your own key, so each one updates the app already installed | The build generates one here the first time it needs it; a Debug build signs with the SDK's debug key |
| `android.keystore.pass` | The keystore's password, on one line | Required beside `android.keystore` |
| `google-services.json` | The Android client registers for push notifications | The client builds and works, and receives no push |
| `firebase-service-account.json` | The agent sends push notifications to phones; the agent installer carries it | The agent notifies in its own UI and in Windows toasts only |
| `env.psd1` | Settings the build and deploy scripts read, copied from `env.example.psd1` | The scripts use their defaults |

How to make each one, step by step:
[docs/developer/private-files.md](../docs/developer/private-files.md).

To see what this checkout has and what each file enables, without printing
any secret:

```powershell
.\check-private.ps1
```
