"""The root's side: the API the agent calls and the page of every site.

Listens on port 8082, published on the PC's loopback only, and answers only with
the proxy's admin token (/data/admin-token, made on the first start; the agent
reads it from the data folder it mounts): "Authorization: Bearer <token>". The
agent shows the page inside its own UI by passing requests through, the token
added on the way, so it is reached only through the agent, behind its login.

The root sees and manages every site and every user, passwords included: this
proxy's owner decides who gets in, and the users of each site are theirs to see.
"""

import hmac

from flask import Flask, jsonify, redirect, render_template, request

import nginxconf
import probe
from state import USER, StateError, access_of

ROOT_COOKIE = "wslc_published_root"


def create(state, token: str) -> Flask:
    app = Flask(__name__, template_folder="templates", static_folder="static")

    @app.before_request
    def require_token():
        # The agent sends the token on every request; a browser at the PC opens the
        # page once with ?token=<token>, kept then as a cookie of this port only.
        if request.path == "/" and hmac.compare_digest(request.args.get("token", ""), token):
            response = redirect("./")
            response.set_cookie(ROOT_COOKIE, token, httponly=True, samesite="Strict")
            return response
        sent = request.headers.get("Authorization", "").removeprefix("Bearer ").strip() or request.cookies.get(ROOT_COOKIE, "")
        if not sent or not hmac.compare_digest(sent, token):
            return jsonify({"ok": False, "error": "The proxy's admin token is required."}), 401
        return None

    def refused(error: Exception, status: int = 400):
        return jsonify({"ok": False, "error": str(error)}), status

    def describe(host: str, site: dict, passwords: bool) -> dict:
        return {
            "host": host,
            "container": site.get("container", ""),
            "port": site.get("port", 0),
            "access": access_of(site),
            "forceLogin": bool(site.get("forceLogin")),
            "check": site.get("check", ""),
            "claimCode": site.get("claimCode", ""),
            "users": [{"name": u["name"], "role": u["role"], **({"password": state.password_of(u)} if passwords else {})}
                      for u in site.get("users", [])],
        }

    def changed():
        nginxconf.write(state.sites())

    @app.get("/")
    def page():
        return render_template("admin.html", host="", root=True, static="static/")

    @app.get("/api/sites")
    def sites():
        passwords = request.args.get("passwords") == "1"
        return jsonify([describe(host, site, passwords) for host, site in sorted(state.sites().items())])

    @app.put("/api/sites/<host>")
    def put_site(host: str):
        data = request.get_json(silent=True) or {}
        try:
            site = state.put_site(host, str(data.get("container") or ""), int(data.get("port") or 0))
        except (StateError, ValueError) as error:
            return refused(error)
        changed()
        probe.check_in_background(state, host.strip().lower())
        return jsonify(describe(host.strip().lower(), site, False))

    @app.delete("/api/sites/<host>")
    def remove_site(host: str):
        if not state.remove_site(host):
            return refused(StateError(f"{host} is not published."), 404)
        changed()
        return jsonify({"ok": True})

    @app.post("/api/sites/<host>/check")
    def check(host: str):
        site = state.site(host)
        if site is None:
            return refused(StateError(f"{host} is not published."), 404)
        _, own_login, note = probe.ask(site["container"], site["port"])
        state.checked(host.strip().lower(), own_login, note)
        return jsonify(describe(host.strip().lower(), state.site(host), False))

    @app.put("/api/sites/<host>/force-login")
    def force_login(host: str):
        data = request.get_json(silent=True) or {}
        try:
            state.force_login(host, bool(data.get("force")))
        except StateError as error:
            return refused(error, 404)
        return jsonify(describe(host.strip().lower(), state.site(host), False))

    @app.post("/api/sites/<host>/claim-code")
    def claim_code(host: str):
        try:
            return jsonify({"claimCode": state.new_claim(host)})
        except StateError as error:
            return refused(error, 404)

    @app.put("/api/sites/<host>/users/<name>")
    def put_user(host: str, name: str):
        data = request.get_json(silent=True) or {}
        try:
            state.put_user(host, name, str(data.get("password") or ""), str(data.get("role") or USER))
        except StateError as error:
            return refused(error)
        return jsonify({"ok": True})

    @app.delete("/api/sites/<host>/users/<name>")
    def remove_user(host: str, name: str):
        try:
            removed = state.remove_user(host, name)
        except StateError as error:
            return refused(error, 404)
        return jsonify({"ok": removed}), (200 if removed else 404)

    @app.get("/healthz")
    def healthz():
        return jsonify({"ok": True})

    return app
