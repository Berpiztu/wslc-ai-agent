---
name: wslc
description: Operate a WSLC (Windows Subsystem for Linux Container) host through the WSLC AI Agent MCP tools - list, inspect, run, create, start, stop, restart and remove containers, read logs and stats, exec commands, pull, tag, push and save images, manage volumes, networks and sessions, and explain failures from the agent's CLI activity. Use whenever the user talks about containers, images, volumes, networks or sessions on a WSLC host.
---

# WSLC skill

## 1. What WSLC is

WSLC is Microsoft's container runtime on Windows (Windows Subsystem for Linux
Container). The **WSLC AI Agent** is the only process that runs the `wslc`
CLI; every MCP tool here is a call into that agent.

- **Use the MCP tools and nothing else to reach WSLC.** Do **not** call the
  agent's HTTP API directly — no `curl`, `wget`, `httpie`, `python`,
  `requests`, `fetch`, or any script against `http://…:8069/api/v1/...` or
  the `/api/v1/mcp` URL. Those raw calls skip every safeguard that lives in the
  tools: the approval gate, the destructive-tool switch and the agent's own
  confirmations. A `DELETE /api/v1/containers/{id}` would delete with no
  confirmation. If a task seems to need an endpoint with no tool, say so and
  stop; do not reach for the shell.
- WSLC is **not Docker**. Do not reach for `docker`, Docker Desktop or
  `docker compose`, and do not assume Docker-only flags exist.
- **Never run `wslc` from a shell tool.** The tools are the only path; shell
  access bypasses session selection, restart policies and the activity log.
- Tool names may appear prefixed by the MCP client (some clients show
  `mcp_wslc_list_containers`). This document uses the bare names.

## 2. Connection

The agent serves MCP itself at `<agent>/api/v1/mcp` (Streamable HTTP). Nothing is
installed next to you; the operator registers that URL in your MCP client:

| Where you run | URL | Credentials |
|---|---|---|
| On the WSLC host (installed agent) | `http://127.0.0.1:8069/api/v1/mcp` | none (loopback) |
| On the WSLC host against a development build | `http://127.0.0.1:8070/api/v1/mcp` | none (loopback) |
| Anywhere else | `https://<public-host>/api/v1/mcp` | `Authorization: Bearer <API token>` set by the operator |

**Start every session with `health`.** It answers `{"status": "ok",
"version": "…"}` — that version is the **agent's**, not `wslc`'s; the
runtime's version is in `system_info` and `home_overview`. If your client
cannot reach the URL at all (connection refused, 401, 404), stop and tell the
user: the operator must fix the URL, the token, or update the agent.

## 3. Resolving names

Every `container` argument accepts a container **name**, full **id**, or a
unique **id prefix** — the runtime resolves it. An ambiguous one comes back
as the runtime's error, not as a list of candidates, so the listing is yours
to do.

- When the user gives a partial name, call `list_containers` first and pick
  the unique match. **Never guess an id.**
- With two or three candidates and a **read-only** tool (`inspect_container`,
  `container_logs`, `container_stats`) you may query each one by its exact
  name and present the results side by side, labelled. Ask when there are
  more.
- For **any action** (start, stop, restart, kill, remove, exec, connect,
  disconnect, set_restart_policy): show the candidates and ask which one.
  Never act on several containers to cover an ambiguity.
- Names in the user's sentence may be approximate ("the nginx one"). Match on
  image as well as name from the `list_containers` output, then confirm the
  exact name back to the user in your answer.
- Volumes, networks and sessions are addressed by exact name. Read them from
  `list_volumes`, `list_networks`, `list_sessions` first.

## 4. Do exactly what was asked, nothing more

**Every request has one scope: the action the user named.** Do that action
and stop. Never chain a second mutating step the user did not ask for, even
when it seems like the obvious next thing.

- "Pull / download image X" → `pull_image` only. Do **not** create or run a
  container from it afterwards. Report that the image is local and stop.
- "Create container X" → `create_container` only. Do **not** start it.
- "Stop X" → `stop_container` only. Do **not** remove it, restart it, or
  change its restart policy.
- "Create volume / network X" → create it only. Do **not** attach anything.
- A read request (list, logs, stats, inspect) never triggers an action.

If the outcome of the requested step suggests a next step, **say it and
ask**; do not do it. When a wording is ambiguous between one step and two
("get nginx running" could mean pull, or pull and run), ask before acting
rather than guessing the wider reading.

## 5. Confirmation policy

Ten tools change something that cannot be undone by the same tool, or put a
container on the internet, and are gated: `remove_container`,
`kill_container`, `exec_in_container`, `stop_session`, `remove_image`,
`remove_volume`, `remove_network`, `prune_images`, `prune_volumes` and
`publish_container_port`. **They are off unless the operator turned them on**, so if
they are not in your tool list, this agent does not do those things: say so
and stop. **The rule: a gated tool runs only after the user has
approved that exact action, in person.** Not because the request was clear,
not because the user said it twice, and never on your own say-so.

How the approval happens depends on your client.

**A client that can show a prompt** (Hermes draws its yes/no box, Claude Code a
form): call the tool once, with no `confirm`. The prompt opens by itself and the
tool runs only if the user picks yes. You never see that prompt and cannot
answer it; `declined: true` means they said no — say so and stop.

**A client that cannot**: the same two calls, with the user in between:

1. Call the tool **without** `confirm`. Nothing is changed. The result is an
   approval request — `confirmationRequired: true`, the exact `action` it
   describes, a one-time `confirmToken`, and the message to show.
2. Tell the user exactly what `action` says and ask whether to go ahead.
   Then **stop and end your turn**. The order that started this ("delete
   open-webui-2") is not the confirmation, however precise it was.
3. Only when the user answers yes **to that question**, in a new message,
   call the tool again with the same arguments and `confirm=<confirmToken>`.
   Report the result. Anything else (silence, a new order, a question back, a
   "delete it" that repeats the original request) is not an answer: ask again
   and wait.

Rules the agent enforces: a token works **once**, expires after **five
minutes**, and only for the action and target it was issued for — a token
from "remove container a" does nothing for "remove container b". If the user
says no, or changes the target, do nothing and let the token die.

- **Before offering any deletion, check your tool list.** The switch
  (the agent's Settings → MCP server) takes effect at once on the running agent, so
  a list you took earlier can be stale in either direction: a tool that has
  gone answers `disabled: true` with the reason and changes nothing, and one
  that has appeared is simply there. Either way, say what happened and stop;
  never work around it with a shell.
- For a vague order ("clean up", "delete the old ones", "remove everything")
  list what would be affected first, then run the two-step flow only on what
  the user then names. No bulk destruction from a vague sentence.
- `stop_container` is safe and reversible; use it instead of kill unless the
  user asks for kill or a stop already failed.
- `exec_in_container`: run only the command the user asked for, quote it back,
  and never chain destructive shell commands (`rm -rf`, package removals,
  service stops) without the user naming them.

## 6. Workflows

- **Status check**: `health` → `list_containers` → report running versus
  stopped, with ports. `home_overview` gives the same counts plus host CPU,
  memory and disk in one call.
- **Troubleshoot a container**: `list_containers` → `container_logs` (raise
  `tail` if the cause is not in the last lines) → `container_stats` →
  `cli_activity` to see the exact `wslc` command and stderr behind an error.
  Quote the runtime's own message rather than paraphrasing it.
  `exec_in_container` with read-only commands (`ls`, `cat`, `ps`, `env`) is
  the next step — it is gated, so it needs the user's approval first.
- **Restart something**: `restart_container` (stop then start). If the start
  fails, read `container_logs` before trying again.
- **Fetch an image**: `pull_image` **waits for the whole pull** and answers when
  it is done, so a large image may take minutes. Call it once. If your client
  gives up waiting, do **not** call it again — a second pull of the same image
  supersedes the first; ask `pull_status`, which reports every pull the agent
  has running with its percentage and last line.
- **Deploy a service** ("run nginx on port 8080 with a volume for html"):
  1. `list_containers` — refuse to reuse a `name` that exists; propose another.
  2. `list_volumes` — create the volume with `create_volume` if the user
     wants a named volume that does not exist yet.
  3. `run_container` with one `request` object: `{"image": "nginx:latest",
     "name": "web", "publish": ["8080:80"], "volumes":
     ["html:/usr/share/nginx/html"], "restartPolicy": "unless-stopped"}`. It
     pulls the image when missing and waits for the container.
  4. `container_logs` on the new container, then report the exact name, state
     and `host->container` ports.
  Always pass a `name`: without one the result cannot identify the new
  container. Ports are `host:container`; `env` entries are `KEY=value`; a
  volume source is a volume name or a host path. The rest of the object is
  the Run form's own fields: `command`, `entrypoint`, `memory`, `cpus`,
  `workdir`, `user`, `network`, `ip`, `networkAliases`, `connectNetworks`,
  `stopTimeout`, `noHealthcheck` or the `health*` fields, and `start`.
  `create_container` takes the same object and leaves it stopped.
- **Prepare without starting**: `create_container` (image must be local:
  `pull_image` first), then `start_container` when the user says so.
- **A pasted `docker run` / `docker create` / `wslc run` line**, however long and
  whatever continuations it carries: pass it **verbatim** to
  `parse_run_command`, which reads it exactly as the agent's own Run form does.
  Report the image, name, ports, volumes, networks and the `unsupported` list —
  flags this runtime has no equivalent for (`--add-host`, `--gpus`,
  `--hostname`, `--dns`, `--label`, `--privileged`, `--rm`, `-it`) are not a
  failure, they are a thing the user has to be told once. Check
  `list_containers` for the parsed name before going on. Then
  `run_command_line` with the same line: a `create` line prepares the container
  and leaves it stopped. Never rewrite the flags by hand, and never run `docker`
  or `wslc` in a shell.
- **Keep it running across reboots**: `set_restart_policy` with
  `unless-stopped` or `always`. This is agent-managed, not a runtime flag;
  `no` turns it off.
- **Images**: `list_images`, `inspect_image`, `pull_image`, `tag_image`,
  `push_image` (needs a registry login done by the operator) and `save_image`
  (writes a tar on the **agent's** host, at a path on that machine).
- **Volumes and networks**: `list_volumes`, `create_volume`, `inspect_volume`,
  `volume_containers` (who uses it); `list_networks`, `inspect_network`,
  `network_topology` (the whole map: who is attached to what, with addresses),
  `create_network` (optional subnet and gateway),
  `connect_container_to_network` / `disconnect_container_from_network` by
  container name.
- **A container port on a public name**: `list_publications` (what is
  published, with the https URL of each), `publish_container_port` (gated:
  the container joins the proxy's network, the name goes in the map, the
  proxy serves it; the name is one label under the agent's domain. An
  application that asks for a login of its own is let through; any other gets
  the proxy's login, with users of its own, managed in the proxy's own page
  (Settings → Publishing → Users and passwords), which no tool reaches:
  passwords never go through you), `unpublish_hostname` (the name answers 404
  from then on), `setup_publishing` (the first use on a machine: the network,
  the data folder and the proxy container — the wslc-published plugin, nginx and
  its login, ghcr.io/berpiztu/wslc-published — made from the agent's settings
  when missing, or in place of a plain nginx one; left alone when there).
- **Notifications**: `list_notifications` (what the agent raised lately — a
  container that stopped on its own, a disk filling up, a job that failed —
  with `after` set to the last id seen for only the new ones),
  `get_notification_settings` and `set_notification_settings` (Settings ›
  Notifications, sent back whole: which are on, and for a reading the percent
  and the minutes past which it notifies). The dashboard's alarms are another
  thing, set on its objects, and these tools do not touch them.
- **Free disk**: `system_info` → `list_images` (look at `inUse`) and
  `list_volumes` → tell the user what is unused, then `remove_image` /
  `remove_volume` by name, or `prune_images` / `prune_volumes` for the lot.
  Every one of them is gated, so the user approves before anything goes.
- **Sessions**: `list_sessions`, then `switch_session` only to a listed name
  the user asked for; `start_session` to boot one. Containers, images and
  volumes are per session, so re-list after switching. Never invent a session
  name. `stop_session` is gated.

## 7. Reading results

- Tools answer with the agent's own JSON, the same shapes its dashboard
  reads. `list_containers`, `list_images`, `list_volumes` and `list_networks`
  answer a plain array; `list_sessions` answers `{"sessions": [...]}`;
  `system_info` and `home_overview` answer an object, and `system_info`
  carries a one-line `summary` you can quote as it is.
- `ports` is a list of `host->container` entries (`"84->80"`); empty means
  nothing is published.
- `state` is `running`, `exited`, `created`, `paused` or similar; `status` is
  the runtime's own text ("Up 5 minutes", "Exited (0) 16 minutes ago").
- `cpuPercent`, `memUsage` and `memPercent` come from `container_stats` and
  from the list itself; a stopped container reads `0.00%` / `0B`.
- `container_logs` returns the tail as text; ask for a larger `tail` only when
  needed, and never paste a whole log back to the user — quote the lines that
  matter.
- `cli_activity` rows are newest first and carry the exact command line, its
  exit code and its stderr: that is the evidence behind any failure.
- A gated tool called without `confirm` answers with `confirmationRequired`,
  `action`, `confirmToken` and `message` — that is not a failure, it is the
  approval request of §5. `disabled: true` instead means the operator turned
  those tools off: nothing ran and nothing will.

## 8. Limits

These are not available through the tools; tell the user the alternative.

- Interactive shells, attach, live log streaming, the host terminal and the
  in-browser container view: use the web UI or the desktop client.
  `exec_in_container` is one-shot, no TTY, no stdin.
- Recreating a container with new settings, container and volume files,
  backups, image builds, host cleanup and VHD compaction: the UI.
- Agent settings, credentials, SSH remote settings and registry login: never
  through this skill.
- The agent binds only to localhost on its host; remote access goes through
  the operator's published HTTPS URL or an SSH tunnel, never a LAN address.
