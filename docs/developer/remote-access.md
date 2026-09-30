# Remote access through a VPS

How to reach WSLC AI Agent, and the containers it manages, from anywhere on
the internet, with your own VPS and your own domain, without opening a single
port on the Windows PC that runs them.

This guide uses placeholders throughout. Replace them with your own values:

| Placeholder | Stands for |
|---|---|
| `example.com` | Your domain |
| `agent.example.com` | The public name of the agent itself |
| `user@vps.example.com` | The SSH account on your VPS |
| `-home` | The name suffix of this PC's published containers |
| `published` | The user-defined network the proxy and the published containers share |
| `wslc-published` | The proxy container (nginx) on the PC |
| `C:\wslc\published.conf` | The map file the agent writes and the proxy reads |

## What you get

Two kinds of public name, both over HTTPS:

- **The agent**, at `https://agent.example.com/`: the web UI, the API, the
  MCP endpoint, the terminal. The native clients connect to the same address.
- **Published containers**, at `https://<name>-home.example.com/`: a
  container's own web page (Open WebUI, Grafana, anything that serves HTTP),
  opened natively in any browser, one public name per container port.

```text
browser ── https://agent.example.com ──► VPS nginx (TLS ends here)
            └─► VPS 127.0.0.1:8069 ══ reverse SSH tunnel ══► PC 127.0.0.1:8069  the agent

browser ── https://webui-home.example.com ──► VPS nginx, wildcard block (TLS ends here)
            └─► VPS 127.0.0.1:8081 ══ reverse SSH tunnel ══► PC 127.0.0.1:8081  wslc-published (nginx)
                 └─► reads the Host header, looks it up in the map
                      └─► http://open-webui:8080 by name, on the network `published`
```

### Why it is built this way

- **Nothing on the PC is exposed.** The agent listens on `127.0.0.1:8069`,
  the proxy container publishes on `127.0.0.1:8081`, and the PC opens no
  inbound port and needs no firewall rule. The PC dials *out* to the VPS over
  SSH and asks the VPS to listen on the VPS's own loopback; whatever arrives
  there travels back down that connection.
- **TLS ends on the VPS.** One wildcard certificate there covers the agent's
  name and every published name. From the VPS on, traffic travels inside the
  SSH connection, which is encrypted; the PC has no certificate and needs
  none.
- **The name carries the destination.** The VPS passes the `Host` header
  through untouched, and the proxy on the PC picks the container from it. Only
  one forward is needed for every published container, however many there
  are, and adding one never touches the VPS or the tunnel.
- **Containers are reached by name, not by port.** The proxy and the published
  containers share a user-defined network, where WSLC's embedded DNS resolves
  container names. The proxy goes straight to the container's internal port,
  so a published container needs no published host port at all. Going through
  the host would not work anyway: ports published on the PC's `127.0.0.1` are
  unreachable from inside a container.
- **Two forwards, not one.** The agent has its own forward (8069) and does not
  go through the proxy: it listens on `127.0.0.1` only, which no container can
  reach, and it should keep working when the proxy is stopped.
- **A subdomain per container, not a path.** An application that believes it
  lives at `/` writes absolute links (`/static/app.js`); a path prefix such as
  `/p/3000/` breaks them, and rewriting pages on the fly is fragile. On its own
  subdomain the application is at the root and nothing needs rewriting.

## Requirements

| What | Why |
|---|---|
| A VPS with a public address, SSH and nginx | It holds the public names and terminates TLS. The examples assume a Debian or Ubuntu layout (`/etc/nginx/sites-available`, `sites-enabled`) |
| A domain whose DNS you control | `agent.example.com` and a wildcard record `*.example.com` pointing at the VPS |
| A wildcard certificate for `example.com` and `*.example.com` | Let's Encrypt issues wildcards only through the DNS challenge (certbot with your DNS provider's plugin, or `--manual`) |
| OpenSSH Client on the Windows PC | Built into Windows 11 (`C:\Windows\System32\OpenSSH\ssh.exe`); the forwards run on it |
| Key-based SSH from the PC to the VPS | The tunnels run unattended and never answer a password prompt |
| WSLC AI Agent installed and running on the PC | Listening on `127.0.0.1:8069`, the default |

Optional: the OpenSSH Server on the PC, only for reaching the PC's own SSH
through the VPS (see [SSH into the PC through the VPS](#ssh-into-the-pc-through-the-vps)).

## 1. DNS and the certificate

Create two DNS records at your DNS provider, both pointing at the VPS's
public address:

| Name | Type | Points at |
|---|---|---|
| `agent.example.com` | `A` (and `AAAA` if the VPS has IPv6) | The VPS |
| `*.example.com` | `A` (and `AAAA`) | The VPS |

An exact record wins over the wildcard, so names you already use elsewhere
keep resolving where they do today.

Then obtain the certificate on the VPS. With certbot and the manual DNS
challenge (it asks you to create a TXT record, and it does not renew by
itself; a DNS plugin for your provider does):

```bash
sudo certbot certonly --manual --preferred-challenges dns -d example.com -d "*.example.com"
```

The certificate ends up in `/etc/letsencrypt/live/example.com/`. A wildcard
covers **one** label: `webui-home.example.com` is covered,
`a.webui.example.com` is not. That is why the published names are
`<name><suffix>.example.com` and not a deeper subdomain.

The nginx blocks below include `/etc/letsencrypt/options-ssl-nginx.conf` and
`/etc/letsencrypt/ssl-dhparams.pem`, which certbot's nginx plugin installs.
If your VPS does not have them, replace those two lines with your own
`ssl_protocols` and `ssl_ciphers`.

## 2. Key-based SSH from the PC to the VPS

On the PC, in PowerShell. Create a key if you do not have one yet. Leave the
passphrase empty, or load the key into the Windows `ssh-agent` service: a
scheduled task cannot type a passphrase.

```powershell
ssh-keygen -t ed25519
```

Append the public key to the VPS account's authorized keys:

```powershell
Get-Content $env:USERPROFILE\.ssh\id_ed25519.pub | ssh user@vps.example.com "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys"
```

Check that the VPS now lets you in without asking anything. This also records
the VPS's host key, which the tunnels need:

```powershell
ssh -o BatchMode=yes user@vps.example.com echo ok
```

It has to print `ok`. A password prompt or `Permission denied` means the key
is not accepted yet.

On the VPS, leave `GatewayPorts` at its default (`no`) in
`/etc/ssh/sshd_config`: the reverse forwards then bind to the VPS's loopback
only, which is what this setup relies on. `AllowTcpForwarding` must be `yes`
(the default).

## 3. The reverse forwards

Three reverse forwards carry the traffic from the VPS back to the PC. The PC
opens them, with the `ssh` built into Windows:

| Forward | The VPS listens on | Goes to, on the PC | What for |
|---|---|---|---|
| agent | `127.0.0.1:8069` | `127.0.0.1:8069` | The agent, `agent.example.com` |
| published | `127.0.0.1:8081` | `127.0.0.1:8081` | The proxy container, `*.example.com` |
| ssh | `127.0.0.1:2222` | `127.0.0.1:22` | The PC's own `sshd`, optional |

Each one is an `ssh` with no remote command (`-N`) and one reverse forward
(`-R`). The agent's:

```powershell
ssh -N -R 8069:127.0.0.1:8069 -o ExitOnForwardFailure=yes -o BatchMode=yes -o ServerAliveInterval=30 -o ServerAliveCountMax=3 user@vps.example.com
```

The published containers':

```powershell
ssh -N -R 8081:127.0.0.1:8081 -o ExitOnForwardFailure=yes -o BatchMode=yes -o ServerAliveInterval=30 -o ServerAliveCountMax=3 user@vps.example.com
```

The PC's own SSH, only if you want it (see
[SSH into the PC through the VPS](#ssh-into-the-pc-through-the-vps)):

```powershell
ssh -N -R 2222:127.0.0.1:22 -o ExitOnForwardFailure=yes -o BatchMode=yes -o ServerAliveInterval=30 -o ServerAliveCountMax=3 user@vps.example.com
```

What the options are for:

- **`-R <VPS port>:127.0.0.1:<PC port>`** with no address before the VPS
  port binds it on the VPS's loopback only (with `GatewayPorts no`, step 2),
  where nginx on the VPS reaches it and nobody else does. Write `127.0.0.1`,
  never `localhost`, which on Windows may resolve to `::1`.
- **`ExitOnForwardFailure=yes`**: an `ssh` whose port the VPS could not bind
  exits at once, instead of staying connected and useless.
- **`BatchMode=yes`**: it never waits for a prompt nobody will see.
- **`ServerAliveInterval=30`, `ServerAliveCountMax=3`**: a dead connection is
  noticed within a minute and a half, and `ssh` exits.

### Keep them open

Opened by hand, a forward lasts as long as its window. For remote access that
survives a dropped connection and a reboot, run each `ssh` unattended, as
your user, at logon, and start it again whenever it exits: the VPS may be
unreachable right after logon, and a connection drops now and then. Windows'
Task Scheduler does it (a task per forward, triggered at logon, restarted
every minute on failure, with no time limit); so do other service managers.

Keep **one `ssh` per forward**: an `ssh` carrying several `-R` exits when any
one of them cannot bind, and would take the others down with it.

### Check them

From the VPS side, each forward in use has its port bound on the loopback:

```powershell
ssh user@vps.example.com "ss -ltn | grep -E '127.0.0.1:(2222|8069|8081) '"
```

On the PC, the `ssh` processes carrying them:

```powershell
Get-CimInstance Win32_Process -Filter "Name = 'ssh.exe'" | Select-Object ProcessId, CommandLine
```

## 4. The VPS: nginx for the agent's own name

On the VPS, create `/etc/nginx/sites-available/agent.example.com` with this
content:

```nginx
server {
    listen 80;
    server_name agent.example.com;

    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl;
    server_name agent.example.com;

    ssl_certificate /etc/letsencrypt/live/example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/example.com/privkey.pem;
    include /etc/letsencrypt/options-ssl-nginx.conf;
    ssl_dhparam /etc/letsencrypt/ssl-dhparams.pem;

    add_header Strict-Transport-Security "max-age=31536000" always;

    client_max_body_size 64m;

    location / {
        proxy_pass http://127.0.0.1:8069;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-Host $host;
        proxy_read_timeout 86400;
        proxy_send_timeout 86400;
        proxy_cache_bypass $http_upgrade;
    }
}
```

What each part is for:

- **Port 80** only redirects to HTTPS.
- **`Upgrade` and `Connection`** carry WebSockets. The agent's terminal, its
  live event stream (`/api/v1/events/stream`) and the host browser pane are
  WebSockets; without these two headers they fail while the pages load
  fine.
- **One-day timeouts** (`86400` seconds) keep an idle terminal or event
  stream open; nginx's default of 60 seconds would cut them.
- **`Host`, `X-Forwarded-For` and `X-Forwarded-Proto` are required, not
  decoration.** The agent trusts a caller sitting at its own machine without
  a login. Through the tunnel every request arrives from `127.0.0.1`, so the
  agent tells a remote caller from a local one by these headers: a request
  that carries `X-Forwarded-For` or `X-Forwarded-Proto`, or that asked for a
  name other than a loopback address, is never treated as local. Keep all
  three. `X-Forwarded-Proto` also makes the agent mark its session cookie
  `Secure`.
- **`client_max_body_size 64m`** raises nginx's default of 1 MB. The agent
  itself sets no limit on uploads (files copied into a container, image
  archives); raise this value if you upload larger files through the public
  name.

Enable the site:

```bash
sudo ln -sf /etc/nginx/sites-available/agent.example.com /etc/nginx/sites-enabled/agent.example.com
```

Test the whole configuration. If this fails, nginx keeps running on the old
one; fix the error before going on:

```bash
sudo nginx -t
```

Load it without dropping connections:

```bash
sudo systemctl reload nginx
```

### The agent's Internet login

A request through the public name is never local, so it needs a login. Until
the agent has one, every API call through `agent.example.com` answers
`503 Internet login not set`. Set it at the PC itself, where no login is
asked: open `http://127.0.0.1:8069/settings`, tab **Security**, card
**Internet login**, and save a username and a password. The same card's
neighbour, **API token**, creates a bearer token for scripts and AI
assistants that call the API or the MCP endpoint from elsewhere.

Then, from anywhere:

```powershell
curl.exe -s -o NUL -w "%{http_code}`n" https://agent.example.com/api/v1/containers
```

`401` is the right answer: the agent is there and asks for its session.
`502` means the VPS could not reach `127.0.0.1:8069`: the `agent` forward is
down or the agent is not running. A browser at `https://agent.example.com/`
shows the sign-in page.

## 5. Publishing containers

### The proxy and its settings: Settings → Publish

Open the agent's **Settings**, tab **Publish**, card **Publishing**. It holds
five values, kept in `publishing.json` in the agent's data folder:

| Field | Example | What it is |
|---|---|---|
| Domain | `example.com` | What every public name ends in; the wildcard DNS record and certificate cover it |
| Name suffix | `-home` | Added to a name, so the containers of several PCs under one domain do not collide: `open-webui` becomes `open-webui-home.example.com` |
| nginx container | `wslc-published` | The proxy that reads the map; restarted after every change |
| Network | `published` | The user-defined network the proxy and the published containers share. Any user-defined network works; the default `bridge` does not resolve names |
| Map file | `C:\wslc\published.conf` | Where the agent writes the map the proxy mounts |

Until you save the card, the fields show those examples. Fill in your own and
press **Save publishing**.

Then press **Set up**, once per machine. After asking for confirmation, the
agent does three things, each **only if it is missing**; what is already
there is left as it is, and the answer lists which was which:

1. Creates the network (`published`).
2. Writes the map file, holding every name already published (none, the first
   time), creating its folder if needed.
3. Runs the proxy container: image `nginx:alpine`, named as the setting says,
   port `127.0.0.1:8081:80` (loopback only; it is where the `published`
   forward arrives), the map file mounted read-only at
   `/etc/nginx/conf.d/default.conf`, connected to the network, with the agent's
   restart policy `always`.

Set up does not touch the tunnel or the VPS, and it does not change a proxy
container that already exists: if yours was made by hand, make sure it is on
the network and mounts the map file at that path.

Check the proxy locally, before anything is public. The request goes to the
PC's own port 8081, but carries a public name in `Host`, exactly as the VPS
will send it. A name nobody published has to answer `404`:

```powershell
curl.exe -s -o NUL -w "%{http_code}`n" -H "Host: nothing-home.example.com" http://127.0.0.1:8081/
```

Use `curl.exe`, not `curl`: in Windows PowerShell, `curl` is an alias of
`Invoke-WebRequest`, which takes different arguments.

### The map file the agent writes

The agent writes the whole map file again on every publish and unpublish,
from its own list of published names (`publications.json` in its data
folder). With one published name, `webui-home.example.com` reaching port 8080
of the container `open-webui`, it reads:

```nginx
map $host $wslc_upstream {
    hostnames;
    default                      "";
    webui-home.example.com       http://open-webui:8080;
}

server {
    listen 80;
    server_name ~^.+$;

    resolver 127.0.0.11 valid=30s ipv6=off;

    if ($wslc_upstream = "") {
        return 404;
    }

    location / {
        proxy_pass $wslc_upstream;
        proxy_http_version 1.1;

        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto https;

        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_read_timeout 3600s;
        proxy_send_timeout 3600s;

        client_max_body_size 64m;
    }
}
```

The file the agent writes also carries a header of comments explaining the
same points; they are left out here.

- **The `map`** turns the requested name into a destination: one line per
  published name, the container's name and its **internal** port.
- **`resolver 127.0.0.11`**: `proxy_pass` with a variable makes nginx resolve
  the container's name at request time, and for that it needs a resolver of
  its own. Without one, every request is a `502` with `no resolver defined`
  in nginx's log, even with both containers on the network. `127.0.0.11` is
  the embedded DNS a container on a user-defined network is given (it is the
  `nameserver` in the container's `/etc/resolv.conf`).
- **An unknown name is a `404`**, answered by the proxy itself.
- **`X-Forwarded-Proto https`**: TLS ended at the VPS, so applications that
  build absolute URLs build them as `https://`.
- **`Upgrade` and `Connection`** carry WebSockets (chats, terminals), with
  one-hour timeouts.

A hand edit of this file lasts only until the next publish or unpublish,
which writes it whole again. There is one exception: the first time the agent
reads its list of names and finds it empty, it adopts the
`name http://container:port;` lines of a map already at that path, so a map
written by hand before the agent took over is not lost.

### Publishing a container's port

The usual way is the container's own form (Run, Create, or **View & edit** of
an existing container):

1. Open the **Ports** field's popup. Each row has **Host port**, **Container
   port**, **Reverse proxy** and **Name**.
2. Tick **Reverse proxy** on the row of the port to publish. **Name** becomes
   editable; it defaults to the container's name. Write the bare name
   (`webui`): the agent adds the suffix and the domain,
   `webui-home.example.com`. A name with dots is taken as a whole hostname,
   as written; it still has to be covered by your DNS, your certificate and
   the VPS's wildcard block.
3. In **Networks**, the container has to share a user-defined network with the
   proxy container. If it shares none, the form offers to add the network of
   Settings → Publish; accept, or the save stops with nothing changed. The
   agent never attaches a container to a network on its own.
4. Save the form.

What happens on save: before the container is touched, the agent checks every
row (the form `containerPort:name`, a port between 1 and 65535), that no other
container holds the same name, and that the form shares a network with the
proxy; a failure stops the save there. Once the container exists, the agent
compares the ticked rows with the names it already had for that container,
adds and removes the difference in `publications.json`, writes the map file
whole, and restarts the proxy container so nginx reads it. When nothing
changed, the proxy is not restarted.

Saving View & edit recreates the container; its names are applied to the new
one.

Other ways to publish, all ending in the same list:

- **Settings → Publish → Add name**: pick the container, its internal port and
  the name.
- **The API**: `POST /api/v1/publications` with
  `{ "container": "open-webui", "containerPort": 8080, "hostname": "webui-home.example.com" }`
  ([api-v1.md](../api-v1.md)).
- **The MCP tools**: `list_publications`, `publish_container_port` (it asks for
  your approval first, like the destructive tools, because it puts a
  container on the internet), `unpublish_hostname` and `setup_publishing`.

A direct publish names no network, so the container **and** the proxy have to
be on the network of Settings → Publish already; otherwise it is refused. It
also requires the name to be exactly one label under the domain (letters,
digits and hyphens).

One name reaches one port. A container with two ports that both need to be
reachable gets two names.

Once published, the Containers page shows a blue mark after the container's
ports (grey when it has no name); hovering it lists the names. A tap on the
ports offers each port's **Local** address and, for a published port, its
**Remote** one, `https://webui-home.example.com/`, which opens from anywhere.
The ports column lists host ports only, so a container published by name with
no host port shows none there; open its name from the list in Settings →
Publish instead.

To unpublish: untick the row in the form and save, or press **Unpublish** next
to the name in Settings → Publish (no confirmation), or
`DELETE /api/v1/publications/{hostname}`. The map is rewritten and the proxy
restarted; the name answers `404` from then on.

## 6. The VPS: nginx for the published names

One more block on the VPS sends every name under the domain that has no block
of its own to the `published` forward. Create
`/etc/nginx/sites-available/published.conf`:

```nginx
server {
    listen 80;
    server_name *.example.com;

    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl;
    server_name *.example.com;

    ssl_certificate /etc/letsencrypt/live/example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/example.com/privkey.pem;
    include /etc/letsencrypt/options-ssl-nginx.conf;
    ssl_dhparam /etc/letsencrypt/ssl-dhparams.pem;

    add_header Strict-Transport-Security "max-age=31536000" always;

    client_max_body_size 64m;

    location / {
        proxy_pass http://127.0.0.1:8081;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-Host $host;
        proxy_read_timeout 86400;
        proxy_send_timeout 86400;
        proxy_cache_bypass $http_upgrade;
    }
}
```

It is the agent's block with two differences, the name and the port. Why it
works the way it does:

- **An exact `server_name` wins over a wildcard.** nginx picks an exact name
  first, then the longest wildcard, then regular expressions, then the
  `default_server`. `agent.example.com`, and every other site you already
  serve under the domain with an exact name, keep their own blocks. What
  changes is where a name *without* a block lands: here, and the proxy on the
  PC answers `404`. Before this block, on a VPS with no `default_server` on
  443, such a name landed on the first block nginx loaded.
- **TLS ends here**, with the same wildcard certificate.
- **`Host` passes through intact**, because the name is the only thing that
  carries the destination.
- **No login gate here.** What is published either authenticates its own
  callers or is not published (see [Security](#security)).

Enable it, test, reload, as for the agent's block:

```bash
sudo ln -sf /etc/nginx/sites-available/published.conf /etc/nginx/sites-enabled/published.conf
```

```bash
sudo nginx -t
```

```bash
sudo systemctl reload nginx
```

If `nginx -t` warns about a `conflicting server name`, another block already
claims `*.example.com`; change that one rather than adding a second.

### Test from outside

These go through the whole route (DNS, VPS, TLS, tunnel, proxy, container),
even when run from the PC, because the names resolve to the VPS.

A published name, expecting `200`, or a `30x` redirect to the application's
own sign-in page:

```powershell
curl.exe -s -o NUL -w "%{http_code}`n" https://webui-home.example.com/
```

A name nobody published, expecting `404` from the proxy on the PC, which
proves the whole route and that unknown names go nowhere:

```powershell
curl.exe -s -o NUL -w "%{http_code}`n" https://nothing-home.example.com/
```

Then open the published name in a private window, ideally from a phone off
your network. It has to show the application's **sign-in** page; sign in and
use something that needs a WebSocket (a chat, a terminal) to prove that it
crosses both nginx and the tunnel.

## SSH into the PC through the VPS

Optional. With the OpenSSH Server running on the PC and the `ssh` forward
open, the VPS's `127.0.0.1:2222` reaches the PC's port 22, and the VPS
serves as a jump host from anywhere:

```powershell
ssh -J user@vps.example.com -p 2222 pcuser@127.0.0.1
```

`pcuser` is the Windows account on the PC. Your public key goes in that
account's `%USERPROFILE%\.ssh\authorized_keys` on the PC, or, when the account
is a local administrator, in `C:\ProgramData\ssh\administrators_authorized_keys`.

An SSH session is not the interactive session WSLC's containers live in:
`wslc container list` run over SSH shows an empty table however much is
running, and other `wslc` verbs find nothing either. Use the agent's API
(`curl.exe http://127.0.0.1:8069/api/v1/…` on the PC) instead; it always
answers from the right session.

## Surviving a reboot

The agent is a logon task of your account, as your forwards are if you run
them that way, and
WSLC's containers need an interactive session too: after a restart nothing
comes back until someone signs in at the PC. SSH cannot fix that, since an
SSH session is not an interactive one.

Two ways to make the PC recover by itself:

- **Settings → Accounts → Sign-in options → "Use my sign-in info to
  automatically finish setting up after an update"**: after an update restart,
  Windows signs you in once and locks the screen. It does nothing after other
  restarts.
- **Automatic sign-in** (Microsoft Sysinternals Autologon, which stores the
  password encrypted in the system's LSA secrets) plus a logon task that locks
  the screen at once (`rundll32.exe user32.dll,LockWorkStation`). The
  containers, the agent and the tunnels keep running behind the lock screen.
  Anyone who restarts the PC gets your session if the lock fails, so encrypt
  the disk (BitLocker) and do not skip the lock.

Prove it by restarting the PC and not touching it: after two or three
minutes, `https://agent.example.com/` and the published names answer from a
phone off your network.

## Security

- **Never bind a forward to `0.0.0.0` on the VPS.** Write the forwards as in
  step 3, with `127.0.0.1` on the PC's end and no address on the VPS's, and
  the VPS's `GatewayPorts no` keeps them on its loopback.
  A forward on `0.0.0.0` would expose the agent, and the PC's SSH, on the VPS's
  public address, bypassing nginx and TLS.
- **Keep the PC's services on the loopback.** The agent on `127.0.0.1:8069`,
  the proxy on `127.0.0.1:8081`, published containers with no host port or one
  bound to `127.0.0.1`. The tunnel is the only way in.
- **The agent asks for its own login** through the public name: the Internet
  login of Settings → Security, or the API token. It trusts callers without a
  login only when they sit at the PC and talk to it directly. That is why the
  VPS block for the agent must set `Host`, `X-Forwarded-For` and
  `X-Forwarded-Proto`: they are what tells a request that came through the
  tunnel from one typed at the PC. Never point a proxy at the agent that
  strips them.
- **A published name has nothing of the agent's in front of it.** Whoever
  knows the name reaches the container directly. Publish only what asks for
  its own password, and check it before publishing: open the container's page
  in a private window at the PC. Some applications greet their first visitor
  with a setup page that creates the administrator (Open WebUI does); create
  the administrator yourself first, or the first stranger to open the name
  becomes it.
- **The proxy adds no password.** The map the agent writes has no
  `auth_basic`, and a hand edit is overwritten by the next publish. A shared
  Basic password would also conflict with applications that send their own
  `Authorization: Bearer` header on every call, which nginx's Basic
  authentication reads too. For something with no login of its own, do not
  publish it: open it through the agent instead (below).
- **Unpublishing is immediate**: the name answers `404` as soon as the proxy
  has restarted.
- **The VPS sees everything in clear.** TLS ends there, so the VPS must be a
  machine you trust and keep up to date.

### Without publishing: the host browser pane

For a port that must not be public, the agent can show it remotely without
publishing anything. The port has to be published on the PC's loopback
(`127.0.0.1:3000:8080`, for instance); then, on a remote client, **Local** on
that port opens the host browser pane, a browser running on the PC whose picture is
streamed to you through the agent's own name, behind the agent's login. It
works with any application, at the cost of bandwidth and a browser process
on the PC per session.

## Troubleshooting

| Symptom | Where it comes from | What to do |
|---|---|---|
| `502` at `agent.example.com` | The VPS could not reach `127.0.0.1:8069` | Check the `agent` forward from the VPS side (step 3), and that the agent is running on the PC |
| `503 Internet login not set` at `agent.example.com` | The agent has no Internet login | Set it at `http://127.0.0.1:8069/settings`, tab Security, on the PC |
| `401` from `curl.exe` at `agent.example.com` | The agent asking for its session | Not a fault: that is the agent answering |
| Pages load but the terminal or live updates do not | WebSockets blocked on the way | The VPS block needs the `Upgrade` and `Connection` headers and `proxy_http_version 1.1` |
| `502` from the VPS on a published name | The `published` forward is down | Check the `published` forward from the VPS side (step 3) |
| `502` from the proxy, `no resolver defined` in its log | A map without the `resolver` line, written by hand | Let the agent write the map (publish or unpublish once), or add `resolver 127.0.0.11 valid=30s ipv6=off;` |
| `502` from the proxy, the name resolves | The container is not on the shared network, is not running, or the map names the host port instead of the internal one | Check the container's Networks and its port rows |
| `404` on a published name | The name is not in the map | Check Settings → Publish; the name must match the full hostname |
| `404` on the agent's name | Its VPS block is missing, so the wildcard block caught it | Enable the agent's block; an exact name always wins over the wildcard |
| A forward's `ssh` exits at once with `remote port forwarding failed` | An old connection still holds the port on the VPS | On the VPS, `sudo ss -ltnp` shows which `sshd` holds it; stop that process and start the forward again |
| A forward's `ssh` exits with `Permission denied` or `Host key verification failed` | The key is not accepted, or the VPS's host key changed | Repeat the `BatchMode` check of step 2 |
| The port is bound on the VPS but the name answers `502` | Nothing on the PC behind the forward | Start the agent, run Set up (the proxy), or install the OpenSSH Server for `ssh` |
| `wslc` over SSH shows no containers | An SSH session is not the containers' session | Use the agent's API on `127.0.0.1:8069` |

Checks on the PC for a `502` from the proxy. The address the proxy's `resolver`
line has to name, expected `nameserver 127.0.0.11`:

```powershell
wslc exec wslc-published cat /etc/resolv.conf
```

Whether the proxy can reach the container by name, as nginx would. HTML means
it can, and the problem is in the map; `bad address` or nothing means the two
do not share a network, or the port is wrong:

```powershell
wslc exec wslc-published wget -q -O - http://open-webui:8080/
```

The proxy's last log lines:

```powershell
wslc logs --tail 20 wslc-published
```

Two lines in the proxy's log are harmless: `10-listen-on-ipv6-by-default.sh:
can not modify /etc/nginx/conf.d/default.conf (read-only file system?)` is the
nginx image's IPv6 helper failing to edit the map, which is mounted
read-only; and requests arriving from a `169.254.x.x` address are WSLC's own
port forwarding on the loopback, not an intruder.

Run these from a PowerShell in your own signed-in session on the PC, not over
SSH, for the reason given above.
