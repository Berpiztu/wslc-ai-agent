#!/usr/bin/env python3
"""wslc-published's own program, beside nginx in the same container.

    main.py --render   nginx's configuration written from the sites (before nginx starts)
    main.py            the two servers: the public login on 127.0.0.1:5010, which only
                       nginx reaches, and the root's API and page on 0.0.0.0:8082, which
                       the container publishes on the PC's loopback only
"""

import secrets
import sys
import threading

from waitress import serve

import admin
import nginxconf
import public
from state import DATA, State, _secret_file


def main() -> None:
    state = State()
    if "--render" in sys.argv:
        nginxconf.write(state.sites())
        return

    session_secret = _secret_file(DATA / "secret", lambda: secrets.token_urlsafe(48))
    admin_token = _secret_file(DATA / "admin-token", lambda: secrets.token_urlsafe(32))
    root = threading.Thread(target=serve, args=(admin.create(state, admin_token),),
                            kwargs={"host": "0.0.0.0", "port": 8082, "threads": 4}, daemon=True)
    root.start()
    serve(public.create(state, session_secret), host="127.0.0.1", port=5010, threads=8)


if __name__ == "__main__":
    main()
