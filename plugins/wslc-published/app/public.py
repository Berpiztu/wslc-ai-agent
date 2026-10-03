"""What people see at a published name, served by nginx under /__login/ on every name.

- GET /auth: nginx's question before every request. 200 lets it through (the
  application has its own login, or a session of one of the site's users), 401
  sends to the login page, 403 is not served.
- The login page: while the site has no administrator it creates one, with the
  site's claim code (the agent shows it to whoever published the name); after
  that it signs in the site's users.
- /admin (/__admin on every name): the site's own administration, for its
  administrators: its users only,
  never another site's.

A session is a signed cookie bound to the name and the user, checked against the
site's users on every request, so a user taken out is out at once.
"""

import os
from urllib.parse import urlparse

from flask import Flask, jsonify, make_response, render_template, request
from itsdangerous import BadSignature, SignatureExpired, URLSafeTimedSerializer

from state import ADMIN, OWN, USER, StateError

# The site's own administration, on every name; the first administrator lands there.
ADMIN_PATH = "/__admin"
COOKIE_NAME = "wslc_published_login"
SESSION_TTL = int(os.environ.get("WSLC_LOGIN_SESSION_TTL", "43200"))


def create(state, secret: str) -> Flask:
    app = Flask(__name__, template_folder="templates", static_folder="static")
    sessions = URLSafeTimedSerializer(secret, salt="wslc-published-login")

    def host() -> str:
        return (request.host or "").strip().lower().split(":", 1)[0]

    def session_user() -> dict | None:
        token = request.cookies.get(COOKIE_NAME, "")
        if not token:
            return None
        try:
            payload = sessions.loads(token, max_age=SESSION_TTL)
        except (BadSignature, SignatureExpired):
            return None
        if payload.get("host") != host():
            return None
        return state.user(host(), str(payload.get("user") or ""))

    def next_target() -> str:
        """Only a path of this same name. nginx sends it unescaped (?next=/a?b=1&c=2): everything after next=."""
        query = request.query_string.decode("utf-8", errors="replace")
        from_query = query.split("next=", 1)[1] if query.startswith("next=") else request.args.get("next", "")
        value = ((request.get_json(silent=True) or {}).get("next") or from_query or "/").strip()
        parsed = urlparse(value)
        if parsed.scheme or parsed.netloc or not value.startswith("/") or value.startswith("//") or value.startswith("/__login"):
            return "/"
        return value

    def signed_in(response, user: dict):
        response.set_cookie(COOKIE_NAME, sessions.dumps({"host": host(), "user": user["name"]}),
                            max_age=SESSION_TTL, httponly=True, secure=True, samesite="Lax", path="/")
        return response

    def refused(error: Exception, status: int = 400):
        return jsonify({"ok": False, "error": str(error)}), status

    @app.get("/auth")
    def auth():
        mode = state.mode(host())
        if mode == OWN:
            return "", 200
        if mode == "":
            return "", 403
        return ("", 200) if session_user() else ("", 401)

    @app.get("/")
    def page():
        site = state.site(host())
        if site is None or state.mode(host()) == OWN:
            return render_template("blocked.html", host=host()), 403
        return render_template("login.html", host=host(), title=site.get("container") or host(),
                               next_target=next_target(), claim=not state.has_admin(host()),
                               user=session_user())

    @app.post("/api/claim")
    def claim():
        data = request.get_json(silent=True) or {}
        if "confirm" in data and data.get("confirm") != data.get("password"):
            return refused(StateError("The two passwords are not the same."))
        try:
            user = state.claim(host(), str(data.get("code") or ""), str(data.get("username") or ""), str(data.get("password") or ""))
        except StateError as error:
            return refused(error)
        return signed_in(jsonify({"ok": True, "next": ADMIN_PATH}), user)

    @app.post("/api/login")
    def login():
        data = request.get_json(silent=True) or {}
        user = state.check_user(host(), str(data.get("username") or ""), str(data.get("password") or ""))
        if user is None:
            return jsonify({"ok": False, "error": "Wrong user or password."}), 401
        return signed_in(jsonify({"ok": True, "next": next_target()}), user)

    @app.post("/api/logout")
    def logout():
        response = make_response(jsonify({"ok": True}))
        response.delete_cookie(COOKIE_NAME, path="/")
        return response

    # The site's own administration: its administrators, its users only.

    def site_admin() -> dict | None:
        user = session_user()
        return user if user and user.get("role") == ADMIN else None

    @app.get("/admin")
    def admin_page():
        if site_admin() is None:
            return render_template("login.html", host=host(), title=host(), next_target=ADMIN_PATH,
                                   claim=not state.has_admin(host()), user=session_user(), need_admin=True)
        return render_template("admin.html", host=host(), root=False, static="/__login/static/")

    @app.get("/api/users")
    def users():
        if site_admin() is None:
            return refused(PermissionError("Administrators only."), 403)
        site = state.site(host()) or {}
        return jsonify([{"name": u["name"], "role": u["role"]} for u in site.get("users", [])])

    @app.put("/api/users/<name>")
    def put_user(name: str):
        if site_admin() is None:
            return refused(PermissionError("Administrators only."), 403)
        data = request.get_json(silent=True) or {}
        try:
            state.put_user(host(), name, str(data.get("password") or ""), str(data.get("role") or USER))
        except StateError as error:
            return refused(error)
        return jsonify({"ok": True})

    @app.delete("/api/users/<name>")
    def remove_user(name: str):
        me = site_admin()
        if me is None:
            return refused(PermissionError("Administrators only."), 403)
        if name == me["name"]:
            return refused(StateError("You cannot take yourself out; another administrator can."))
        state.remove_user(host(), name)
        return jsonify({"ok": True})

    @app.get("/blocked")
    def blocked():
        return render_template("blocked.html", host=host()), 403

    @app.get("/healthz")
    def healthz():
        return jsonify({"ok": True})

    return app
