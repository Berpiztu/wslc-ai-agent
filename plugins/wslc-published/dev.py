#!/usr/bin/env python3
"""The plugin's pages on this machine, with no container and no nginx, for working on them.

    python dev.py          (dev.ps1 makes the virtual environment and runs this)

One server on http://127.0.0.1:8091 with the paths nginx gives the pages in the
container: /__login/ and /__admin on every name (open them at a name under
localhost, http://team-pc.localhost:8091/__admin), and the root's page at
/__root/. Its data is .dev-data beside this file, never the proxy's, seeded with
two names the first time. A template or a stylesheet saved shows on the next
reload of the page; a .py saved restarts the server.

Not in the image: the Dockerfile copies app/ only.
"""

import os
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
PORT = 8091
os.environ.setdefault("WSLC_PUBLISHED_DATA", str(HERE / ".dev-data"))
sys.path.insert(0, str(HERE / "app"))

import secrets  # noqa: E402

from werkzeug.middleware.dispatcher import DispatcherMiddleware  # noqa: E402
from werkzeug.serving import run_simple  # noqa: E402

import admin  # noqa: E402
import nginxconf  # noqa: E402
import public  # noqa: E402
from state import ADMIN, DATA, USER, State, _secret_file  # noqa: E402

# No nginx here: what it would be told is left unsaid.
nginxconf.write = lambda sites: None

SAMPLE_USERS = {"ana": ("ana-password-1", ADMIN), "luis": ("luis-password-1", USER)}


def seed(state: State) -> None:
    """Two names the first time: one waiting for its first administrator, one with users."""
    if state.sites():
        return
    state.put_site("demo-pc.localhost", "nothing-here", 80)
    state.put_site("team-pc.localhost", "nothing-here", 80)
    for name, (password, role) in SAMPLE_USERS.items():
        state.add_user("team-pc.localhost", name, password, role)


def live(app):
    """Templates read again when they change, and static files never kept by the browser."""
    app.config["TEMPLATES_AUTO_RELOAD"] = True
    app.config["SEND_FILE_MAX_AGE_DEFAULT"] = 0
    return app


def nginx_paths(login_app, root_app):
    """The container's paths: /__login/ is the login app, /__admin its /admin, /__root/ the root's page."""
    dispatcher = DispatcherMiddleware(lambda environ, start: _not_found(start), {"/__login": login_app, "/__root": root_app})

    def route(environ, start_response):
        if environ.get("PATH_INFO") == "/__admin":
            environ["SCRIPT_NAME"], environ["PATH_INFO"] = "/__login", "/admin"
            return login_app(environ, start_response)
        return dispatcher(environ, start_response)

    return route


def _not_found(start_response):
    start_response("404 Not Found", [("Content-Type", "text/plain")])
    return [b"Not a page of the plugin: /__login/, /__admin or /__root/."]


def main() -> None:
    state = State()
    seed(state)
    token = _secret_file(DATA / "admin-token", lambda: secrets.token_urlsafe(32))
    session_secret = _secret_file(DATA / "secret", lambda: secrets.token_urlsafe(48))
    if os.environ.get("WERKZEUG_RUN_MAIN") != "true":
        print(f"\n  Root's page:   http://127.0.0.1:{PORT}/__root/?token={token}")
        print(f"  A name's admin: http://team-pc.localhost:{PORT}/__admin  "
              + ", ".join(f"{name} / {password}" for name, (password, _) in SAMPLE_USERS.items()))
        print(f"  Claim a name:  http://demo-pc.localhost:{PORT}/__login/  (its code is on the root's page)")
        print(f"  Data:          {DATA}\n", flush=True)
    app = nginx_paths(live(public.create(state, session_secret)), live(admin.create(state, token)))
    run_simple("127.0.0.1", PORT, app, use_reloader=True, extra_files=None, threaded=True)


if __name__ == "__main__":
    main()
