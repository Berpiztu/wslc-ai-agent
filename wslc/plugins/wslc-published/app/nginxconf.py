"""nginx's configuration, written from the sites and reloaded in place.

One map line per site, its destination the container's name and internal port on
the shared network (Docker's embedded DNS, 127.0.0.11, resolves it at request
time). Every request asks the login first (auth_request), which answers from the
state: let through, sign in first, or not served.
"""

import subprocess
from pathlib import Path

CONF_PATH = Path("/etc/nginx/conf.d/default.conf")

HEAD = """# Written by wslc-published from its sites: a hand edit lasts until the next
# change. The destinations are container names on the shared network and their
# own internal ports.

map $host $wslc_upstream {
    hostnames;
    default                      "";
"""

TAIL = """}

server {
    listen 80;
    server_name ~^.+$;

    # proxy_pass with a variable resolves the name at request time, and for that
    # nginx needs a resolver: 127.0.0.11 is the embedded DNS of a user-defined
    # network. Without it every request is a 502.
    resolver 127.0.0.11 valid=30s ipv6=off;

    # TLS ends at the VPS: a redirect written whole would say http.
    absolute_redirect off;

    # A name nobody published is not a mystery, it is a 404.
    if ($wslc_upstream = "") {
        return 404;
    }

    location /__login/ {
        proxy_pass http://127.0.0.1:5010/;
        proxy_set_header Host $host;
    }

    # The site's own administration: its users, for its administrators.
    location = /__admin {
        proxy_pass http://127.0.0.1:5010/admin;
        proxy_set_header Host $host;
    }

    location = /__login/auth {
        internal;
        proxy_pass http://127.0.0.1:5010/auth;
        proxy_pass_request_body off;
        proxy_set_header Content-Length "";
        proxy_set_header Host $host;
    }

    location @wslc_sign_in {
        return 302 /__login/?next=$request_uri;
    }

    location @wslc_blocked {
        rewrite ^ /__login/blocked last;
    }

    location / {
        auth_request /__login/auth;
        error_page 401 = @wslc_sign_in;
        error_page 403 = @wslc_blocked;

        proxy_pass $wslc_upstream;
        proxy_http_version 1.1;

        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto https;

        # Terminals and chats speak WebSocket.
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_read_timeout 3600s;
        proxy_send_timeout 3600s;

        client_max_body_size 64m;
    }
}
"""


def render(sites: dict[str, dict]) -> str:
    lines = "".join(f"    {host.ljust(28)} http://{site['container']}:{site['port']};\n"
                    for host, site in sorted(sites.items()))
    return HEAD + lines + TAIL


def write(sites: dict[str, dict]) -> None:
    """The configuration written whole; nginx reloaded when it runs (before it starts, nothing to reload)."""
    CONF_PATH.write_text(render(sites), encoding="utf-8")
    subprocess.run(["nginx", "-s", "reload"], check=False, capture_output=True)
