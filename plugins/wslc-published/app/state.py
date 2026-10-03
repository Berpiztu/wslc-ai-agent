"""The proxy's state: every published name (a site), its users and its check.

Kept in /data/state.json, a folder of the host, so an update of the image loses
nothing. Each site has its own users, never shared with another site, each an
administrator (manages the site's users) or a user (only signs in). A password is
kept twice: as a PBKDF2 hash, which is what signing in checks, and encrypted with
this proxy's key (/data/key), which only the root (the agent's owner) reads back.

A site is "own" when its application asks for a login of its own (the proxy lets
it through), otherwise "login": the proxy's login page first, and while the site
has no administrator yet, the page creates one with the site's claim code.
"""

import base64
import hashlib
import hmac
import json
import os
import re
import secrets
import threading
import time
from pathlib import Path

from cryptography.fernet import Fernet, InvalidToken

DATA = Path(os.environ.get("WSLC_PUBLISHED_DATA", "/data"))
STATE_PATH = DATA / "state.json"
KEY_PATH = DATA / "key"
ITERATIONS = 210_000
USER_NAME = re.compile(r"^[A-Za-z0-9._@-]{1,64}$")
HOST_NAME = re.compile(r"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$")
CONTAINER_NAME = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$")

OWN = "own"
LOGIN = "login"
ADMIN = "admin"
USER = "user"


class StateError(ValueError):
    """A request the state refuses: the message says why, in words for the person."""


def _secret_file(path: Path, make) -> str:
    """A secret made once on this proxy's data and kept: an update of the image keeps it."""
    DATA.mkdir(parents=True, exist_ok=True)
    if path.exists():
        return path.read_text(encoding="utf-8").strip()
    value = make()
    path.write_text(value, encoding="utf-8")
    try:
        path.chmod(0o600)
    except OSError:
        pass
    return value


def hash_password(password: str) -> str:
    salt = secrets.token_bytes(16)
    derived = hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"), salt, ITERATIONS)
    return f"pbkdf2_sha256${ITERATIONS}${base64.b64encode(salt).decode()}${base64.b64encode(derived).decode()}"


def verify_password(password: str, stored: str) -> bool:
    try:
        scheme, iterations, salt, expected = stored.split("$", 3)
        if scheme != "pbkdf2_sha256":
            return False
        derived = hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"), base64.b64decode(salt), int(iterations))
        return hmac.compare_digest(derived, base64.b64decode(expected))
    except (ValueError, TypeError):
        return False


def new_claim_code() -> str:
    """Twelve characters a person can read out: no 0/O or 1/l to mix up."""
    alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"
    return "-".join("".join(secrets.choice(alphabet) for _ in range(4)) for _ in range(3))


def access_of(site: dict) -> str:
    """Own when the application asks for a login of its own, unless the root forced the proxy's login in front of it too."""
    return OWN if site.get("ownLogin") and not site.get("forceLogin") else LOGIN


class State:
    """state.json in memory, saved whole through a temporary file after every change."""

    def __init__(self):
        self._lock = threading.RLock()
        self._fernet = Fernet(_secret_file(KEY_PATH, lambda: Fernet.generate_key().decode()).encode())
        self._sites: dict[str, dict] = {}
        if STATE_PATH.exists():
            self._sites = (json.loads(STATE_PATH.read_text(encoding="utf-8")) or {}).get("sites", {})

    # Reading

    def sites(self) -> dict[str, dict]:
        with self._lock:
            return json.loads(json.dumps(self._sites))

    def site(self, host: str) -> dict | None:
        with self._lock:
            found = self._sites.get(host.strip().lower())
            return json.loads(json.dumps(found)) if found else None

    def mode(self, host: str) -> str:
        site = self.site(host)
        return "" if site is None else access_of(site)

    def has_admin(self, host: str) -> bool:
        site = self.site(host) or {}
        return any(u.get("role") == ADMIN for u in site.get("users", []))

    def check_user(self, host: str, name: str, password: str) -> dict | None:
        """The user, when the name and the password are right; None otherwise."""
        site = self.site(host) or {}
        user = next((u for u in site.get("users", []) if u["name"].lower() == name.strip().lower()), None)
        return user if user and verify_password(password, user["hash"]) else None

    def user(self, host: str, name: str) -> dict | None:
        site = self.site(host) or {}
        return next((u for u in site.get("users", []) if u["name"] == name), None)

    def password_of(self, user: dict) -> str:
        """The root's view of a password: decrypted with this proxy's key."""
        try:
            return self._fernet.decrypt(user.get("secret", "").encode()).decode()
        except (InvalidToken, ValueError):
            return ""

    # Sites, from the agent

    def put_site(self, host: str, container: str, port: int) -> dict:
        host = host.strip().lower()
        if not HOST_NAME.match(host):
            raise StateError(f"{host} is not a host name.")
        if not CONTAINER_NAME.match(container) or not 1 <= int(port) <= 65535:
            raise StateError("A container name and a port between 1 and 65535 are needed.")
        with self._lock:
            site = self._sites.get(host)
            if site is None:
                # A new site starts as login, with no administrator and a fresh claim
                # code, until its check says the application has a login of its own.
                site = {"users": [], "ownLogin": False, "claimCode": new_claim_code(), "check": "Not checked yet."}
                self._sites[host] = site
            site.update({"container": container, "port": int(port)})
            self._save()
            return json.loads(json.dumps(site))

    def remove_site(self, host: str) -> bool:
        with self._lock:
            removed = self._sites.pop(host.strip().lower(), None) is not None
            if removed:
                self._save()
            return removed

    def checked(self, host: str, own_login: bool, note: str) -> None:
        with self._lock:
            site = self._sites.get(host)
            if site is not None:
                site.update({"ownLogin": own_login, "check": note, "checkedAt": int(time.time())})
                self._save()

    def force_login(self, host: str, force: bool) -> None:
        """The proxy's login in front of the site even when its application has its own."""
        with self._lock:
            site = self._require(host)
            site["forceLogin"] = bool(force)
            if force and not site.get("claimCode") and not any(u.get("role") == ADMIN for u in site["users"]):
                site["claimCode"] = new_claim_code()
            self._save()

    def new_claim(self, host: str) -> str:
        with self._lock:
            site = self._require(host)
            site["claimCode"] = new_claim_code()
            self._save()
            return site["claimCode"]

    # Users

    def claim(self, host: str, code: str, name: str, password: str) -> dict:
        """The first administrator of a site, made at its login page with the site's claim code."""
        with self._lock:
            site = self._require(host)
            if any(u.get("role") == ADMIN for u in site["users"]):
                raise StateError("This address already has an administrator.")
            if not site.get("claimCode") or not hmac.compare_digest(code.strip().upper(), site["claimCode"]):
                raise StateError("The code is not right. Ask whoever published this address for it.")
            user = self._put_user(site, name, password, ADMIN)
            site["claimCode"] = ""
            self._save()
            return user

    def put_user(self, host: str, name: str, password: str, role: str) -> dict:
        with self._lock:
            site = self._require(host)
            user = self._put_user(site, name, password, role)
            self._save()
            return user

    def remove_user(self, host: str, name: str) -> bool:
        with self._lock:
            site = self._require(host)
            before = len(site["users"])
            site["users"] = [u for u in site["users"] if u["name"] != name]
            if len(site["users"]) == before:
                return False
            if not any(u.get("role") == ADMIN for u in site["users"]) and not site.get("claimCode"):
                # The last administrator gone: the page asks for a new one, with a new code.
                site["claimCode"] = new_claim_code()
            self._save()
            return True

    # Inside

    def _put_user(self, site: dict, name: str, password: str, role: str) -> dict:
        name = name.strip()
        if not USER_NAME.match(name):
            raise StateError("A user name is 1 to 64 letters, digits, '.', '_', '@' or '-'.")
        if len(password) < 8:
            raise StateError("The password needs at least 8 characters.")
        if role not in (ADMIN, USER):
            raise StateError("The role is admin or user.")
        user = {"name": name, "role": role, "hash": hash_password(password),
                "secret": self._fernet.encrypt(password.encode()).decode()}
        site["users"] = [u for u in site["users"] if u["name"].lower() != name.lower()] + [user]
        return user

    def _require(self, host: str) -> dict:
        site = self._sites.get(host.strip().lower())
        if site is None:
            raise StateError(f"{host} is not published.")
        return site

    def _save(self) -> None:
        DATA.mkdir(parents=True, exist_ok=True)
        temp = STATE_PATH.with_suffix(".tmp")
        temp.write_text(json.dumps({"sites": self._sites}, indent=2), encoding="utf-8")
        temp.replace(STATE_PATH)
