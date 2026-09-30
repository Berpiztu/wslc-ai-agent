# Installing the WSLC skill in an AI agent

The WSLC AI Agent serves MCP itself at `<agent>/api/v1/mcp` (Streamable HTTP,
with sessions: that is what lets a destructive tool ask through the client's own
approval prompt). An AI
agent (Claude Code, Hermes, OpenClaw, any MCP client) needs two things and
nothing else:

1. **this folder** (`SKILL.md` plus the config snippets) copied into the
   client's skills directory, and
2. **the URL** registered as an MCP server in the client.

No runtime, no package, no process on the client side.

## 1. Which URL

| The client runs... | URL | Credentials |
|---|---|---|
| on the WSLC host, against the installed agent | `http://127.0.0.1:8069/api/v1/mcp` | none |
| on the WSLC host, against a development build | `http://127.0.0.1:8070/api/v1/mcp` | none |
| on the far end of an SSH tunnel to that port | `http://127.0.0.1:<tunnel port>/api/v1/mcp` | none (loopback for the agent) |
| anywhere else | `https://<public-host>/api/v1/mcp` | `Authorization: Bearer <API token>` |

Port **8069 is the installed agent, 8070 the development build**, so one
host can run both at once. The path is `/api/v1/mcp`:
the endpoint is versioned with the API, because the tools are what a client
depends on and a tool that changes shape has to leave the old ones reachable.
That is the only path: nothing answers at `/mcp`.

Authentication is the agent's own (`src/WslcAgent.Server/Auth/AgentAuth.cs`):
the endpoint counts as API, so a direct loopback caller is let through and a
request that arrives proxied (`X-Forwarded-*`) needs the token, as a Bearer
header or `X-WSLC-Token`. Nothing about MCP is special.

The four gated tools (`remove_container`, `kill_container`,
`exec_in_container`, `stop_session`) **ship off**. The operator turns them on
in the agent's own **Settings → MCP server**, which also shows the URL to
register and how many tools a client would find, and can stop serving MCP
altogether; what it saves lives in `mcp.json` in the agent's data folder.
`Mcp:AllowDestructiveTools` in `appsettings.json` is only the default a fresh
agent starts from.
With it off they are not listed at all, so no model can offer them; with it on
they are. The agent reads the setting on every request, so editing that file
takes effect at once — no restart. (An MCP client caches the tool list per
session, so it sees the change on its next one; a stale tool called anyway is
refused with `{"disabled": true, "action": …, "message": …}`, because each of
those tools asks again before it acts.) Clients cannot turn the switch on.
`Mcp__AllowDestructiveTools` in the environment works too, but only as the
value the agent starts with: environment variables are not re-read.

Even enabled, each gated tool is two calls: the first returns
`confirmationRequired`, the exact action and a one-time `confirmToken`, the
user approves, and the second call carries `confirm=<token>`.

**Copy only `SKILL.md` into the client's skills folder.** The other files
here (`INSTALL.md`, the config snippets) are operator references: they carry
the base URL and raw `curl` examples, and must not become model context, or
the agent may start calling the HTTP API directly instead of the tools.

## 2. Claude Code

```bash
# skill: project-wide (SKILL.md only)
mkdir -p .claude/skills/wslc && cp <repo>/mcp-skill/skills/wslc/SKILL.md .claude/skills/wslc/
#        or for the user
mkdir -p ~/.claude/skills/wslc && cp <repo>/mcp-skill/skills/wslc/SKILL.md ~/.claude/skills/wslc/
# server: project .mcp.json (see claude-code.mcp.json) or
claude mcp add --transport http wslc http://127.0.0.1:8069/api/v1/mcp
```

Restart the session, approve the project server when asked, and check
`/mcp`. Remote: `claude mcp add --transport http wslc <url> --header
"Authorization: Bearer <token>"`.

## 3. Hermes Agent

```bash
# skill (SKILL.md only)
mkdir -p ~/.hermes/skills/wslc && cp <repo>/mcp-skill/skills/wslc/SKILL.md ~/.hermes/skills/wslc/
# server (answer Y to "Enable all N tools?")
hermes mcp add wslc --url http://127.0.0.1:8069/api/v1/mcp
hermes mcp test wslc          # expects "Connected" and the tool count
hermes skills list | grep wslc
```

Remote: add `--auth header` or edit `~/.hermes/config.yaml` so the entry has
`headers: {Authorization: "Bearer ${MCP_WSLC_API_KEY}"}` and export that
variable. Snippet: `hermes.config.yaml`.

After the agent gains tools, re-register (`hermes mcp remove wslc` then
`hermes mcp add ...`) so Hermes enables the new ones; it stores the enabled
list at registration time.

## 4. OpenClaw

```bash
mkdir -p <openclaw skills dir>/wslc && cp <repo>/mcp-skill/skills/wslc/SKILL.md <openclaw skills dir>/wslc/
openclaw mcp add wslc --url http://127.0.0.1:8069/api/v1/mcp
openclaw mcp doctor wslc --probe
```

Snippet for the config file: `openclaw.config.json5`.

## 5. Any other MCP client

Register a **Streamable HTTP** server at the URL above (no command, no
args). The endpoint keeps a session, so the client has to do the usual
`initialize` handshake — every real MCP client does, and a call without it is a
400. If the client only supports local-process servers, put a generic HTTP
bridge in front of it; do not install anything on the WSLC host for it.

## 6. Verify from a shell

```bash
curl -s -i -X POST http://127.0.0.1:8069/api/v1/mcp \
  -H "Accept: application/json, text/event-stream" -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"curl","version":"1"}}}'
```

The handshake first, because the endpoint keeps a session: a bare `tools/list`
with no session is a 400, which is the server working, not failing. What comes
back is the server's name and version, and an `Mcp-Session-Id` header to send
with anything after it.

The answer is a `text/event-stream` line (`event: message` then `data: {...}`)
carrying `serverInfo`: the endpoint is up and you are allowed in. `401` means
the request was treated as remote and needs the token; `404`, or the dashboard's
HTML page, means the URL or the port is wrong, or the agent predates this
endpoint.

## 7. First orders to try

`Comprueba la salud del servidor WSLC`, `Lista los contenedores`, then the
script in `mcp-skill/mytest/mytesthermes.md`.
