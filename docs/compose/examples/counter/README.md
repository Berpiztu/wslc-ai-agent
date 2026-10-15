# A first Compose project on WSLC: the counter

Ten minutes, start to end: a web page that counts its visits, made of two
services, an app **built** from this folder and a Redis that keeps the
count in a volume. You read the file, bring it up, open it, change it,
build it on another machine and take it down, with `wslc-compose`, and then
the same file from the web dashboard.

```text
counter/
├── compose.yaml        the two services, the port and the volume
└── app/
    ├── Dockerfile      Python 3.12 and Flask
    ├── app.py          the page
    └── requirements.txt
```

## Before you start

- **WSLC AI Agent** installed and running
  ([install](../../../../README.md#quick-start)). It brings `wslc-compose`
  with it, on your `PATH` for every terminal opened after the install. One
  that was open already (and a terminal of an editor that was) does not
  know it: open another, or read the `PATH` again in that one:

  ```powershell
  $env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
  ```
- **WSL 3.0.0 or later** (`wsl --version`); 3.0.3 for step 6, on the
  machine that builds.
- PowerShell in this folder:

  ```powershell
  cd docs\compose\examples\counter
  ```

  Every command below is one line, to paste and run on its own.

`wslc` itself has no Compose yet, and says so:

```powershell
wslc compose up
```

```text
Unrecognized command: 'compose'
```

`wslc-compose` is Compose's own command line on WSLC, answered by the agent.

## 1. Read the file

```powershell
wslc-compose config
```

Nothing is run. The file comes back with what each line becomes on WSLC,
as a comment: the services are containers `counter-web-1` and
`counter-redis-1`, the build is this folder's `app`, the volume is
`counter_data`. The last line counts them:

```text
counter: 2 services; 5 lines converted, 0 not supported, 0 with a warning.
```

A line WSLC cannot do would say `# NOT SUPPORTED:` and why, here, before
anything is made.

## 2. Bring it up

```powershell
wslc-compose up -d
```

The agent builds the `web` image from `app/`, line by line, makes the
network and the volume, and starts Redis first, since `web` depends on it.
`-d` comes back once both are up; without it, `up` stays and shows their
logs as they are written, and Ctrl+C stops them.

The first time takes as long as the base images take to download
(`python:3.12-slim`, `redis:7-alpine`); after that, seconds.

## 3. Open it

<http://localhost:8000>

Reload it: the number goes up. It is kept in Redis, in the volume, so it
survives the containers.

## 4. Watch it

The project's containers, their state and ports:

```powershell
wslc-compose ps
```

The last lines of both, as one log:

```powershell
wslc-compose logs -n 5
```

The same, and go on following it until Ctrl+C:

```powershell
wslc-compose logs -f
```

Every project on this machine:

```powershell
wslc-compose ls
```

## 5. Change it

Change `TITLE` in `app/app.py`, `Compose on WSLC`, to anything, and:

```powershell
wslc-compose up -d
```

Only what changed moves: `web` is built again, from the cache where
nothing changed, and its container is made again from the new image;
Redis is left as it is, and so is the count.

## 6. Build it on another machine

Your sources stay on your desk; the images are built where the containers
will run. It needs WSL 3.0.3 or later **on that machine**, and its agent's
address and API token (its Settings shows it).

The agent's address and its token, one line for both:

```powershell
$env:WSLC_COMPOSE_AGENT = "https://your-server"; $env:WSLC_COMPOSE_TOKEN = "…"
```

And the same `up` as before:

```powershell
wslc-compose up -d
```

The build context, this folder's `app` as `.dockerignore` would leave it,
is packed as a tar archive and sent to that agent, which builds it there.
Change `app.py` again and run the same line: this time one file travels,
and the agent says it had the rest. The two variables stand for `--agent`
and `--token`; set them, and nothing secret is ever typed on the command
line.

What the file mounts, and its `.env`, are the other machine's, at the same
path: only the sources travel.

## 7. The same file, without a terminal

In the web dashboard or the apps: **Containers** → the round button →
**Add Compose** → open `compose.yaml`. The window shows the file as a tree,
each line in the colour of what it becomes; **Run** brings it up, and the
window closes while it does. The project is one card, its containers' cards
after it with a line round them, green once they all run, and its CPU and
memory added up on two dials.

On the dashboard, the **Project card** puts it all in one card, its
containers inside.

## 8. Take it down

The containers and the network; the volume, and the count, stay:

```powershell
wslc-compose down
```

The volume too, and the count with it:

```powershell
wslc-compose down -v
```

## Next

Every command and option, what is converted and what is not there yet:
[wslc-compose](../../wslc-compose.md).
