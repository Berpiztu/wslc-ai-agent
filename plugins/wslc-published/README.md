# wslc-published

**Image:** `ghcr.io/berpiztu/wslc-published` (`latest`, or a version such as `1.0`).

The publishing proxy of WSLC AI Agent, as a plugin of its own: one container with
nginx in front of the published names and its own Python program. It stands apart
from the agent's code (`src/`): the agent only talks to it over HTTP, so this
folder can go without anything else breaking.

What it does:

- **Publishes** container ports on public names. It writes nginx's configuration
  from its sites and reloads nginx in place; each name reaches a container by name
  on the shared network.
- **Puts a login in front** of every name whose application opens without asking
  for anything. When a name is published it asks the application, as a visitor
  with no session would: a `401`/`403`, a redirect to a sign-in page or a page with
  a password field is a login of its own, and the name is let through untouched.
- **Each name has its own users**, never shared with another. The first
  administrator of a name is created at its login page with the name's **claim
  code**, which the root hands over with the address; that administrator manages
  the name's users at `https://<name>/__admin`.
- **The root sees everything**: every name, its users and their passwords, the
  claim codes, and a new code or a new check when needed.

## Ports

| Port | Who | What |
|---|---|---|
| 80 | everyone, through the VPS tunnel (published on the PC's loopback as 8081) | the published names, and `/__login/` on each |
| 8082 | the root only (published on the PC's loopback) | the root's API and page; `Authorization: Bearer <admin token>`, or `/?token=<admin token>` once in a browser at the PC |

## Data

Everything that changes lives in `/data`, a folder of the host, so an update of
the image loses nothing:

| File | What |
|---|---|
| `state.json` | the sites: container, port, check, claim code, users (role, PBKDF2 hash, password encrypted with `key`) |
| `key` | the key the passwords are encrypted with, for the root to read them back |
| `secret` | what the sessions are signed with |
| `admin-token` | the root API's token; the agent reads it from this folder |
| `wslc-published.log` | the program's log |

## Root API

| Method | Path | Body | Answer |
|---|---|---|---|
| GET | `/api/sites[?passwords=1]` | | every site: host, container, port, access (`own`/`login`), check, claim code, users (with passwords when asked) |
| PUT | `/api/sites/{host}` | `{container, port}` | the site, published or changed; nginx reloaded, the check started |
| DELETE | `/api/sites/{host}` | | unpublished; nginx reloaded |
| POST | `/api/sites/{host}/check` | | the site, checked again now |
| PUT | `/api/sites/{host}/force-login` | `{force}` | the site: the login screen in front even of an application with its own login, or not |
| POST | `/api/sites/{host}/claim-code` | | `{claimCode}`, a new one |
| POST | `/api/sites/{host}/users` | `{name, password, role}` | a new user; 400 when the name is taken |
| PUT | `/api/sites/{host}/users/{name}` | `{password, role}` (`admin`/`user`) | the user added or changed |
| PATCH | `/api/sites/{host}/users/{name}` | `{password?, role?}` | the password, the role or both; with no administrator left the address gets a new claim code |
| DELETE | `/api/sites/{host}/users/{name}` | | the user taken out; their sessions end at once |

## A name's own administration

At `https://<name>/__admin`, for the name's administrators, through `/__login/api/`: `GET users`
(`{me, users}`), `POST users`, `PATCH users/{name}`, `DELETE users/{name}`, and for any user signed
in `POST me/password` (`{current, password, confirm}`). An address always keeps one administrator:
nobody takes out or demotes the last, nor themselves. A claim code lasts while the name has no
administrator: one made by the root ends it, as a claim does.

The pages wear WSLC AI Agent's look (`app/static/style.css`: its palette, type, frame, rail and grid,
light and dark; the agent's own Material icons in `app/static/icons.js`).

## Build and run by hand

```powershell
wslc build -t ghcr.io/berpiztu/wslc-published:latest plugins/wslc-published
wslc run -d --name wslc-published --network published `
  -p 127.0.0.1:8081:80 -p 127.0.0.1:8082:8082 `
  -v C:/wslc/published:/data ghcr.io/berpiztu/wslc-published:latest
```

The agent's Settings → Publish → Set up does the same, from the published image:
nothing has to be built or installed by hand.

## Publish a new image

By hand, and seldom: the image changes on its own clock, not the agent's releases.
On GitHub, **Actions → Published proxy image → Run workflow**, with the version
(`1.1`) and whether it is `latest` too (`.github/workflows/published-image.yml`).
Then a proxy already running takes it with **Update image** on its container in
the agent; its data folder stays.

The package has to be **public** on GitHub (Berpiztu → Packages → wslc-published
→ Package settings) for other machines to pull it without signing in.

Debian, not Alpine: in a musl container `host.wslc.internal` does not resolve
(https://github.com/microsoft/WSL/issues/41769).
