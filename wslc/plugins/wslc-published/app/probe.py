"""Whether a published application asks for a login of its own.

Asked as a visitor with no session would, from inside this container, by name on
the shared network as nginx reaches it. It asks for one when it answers 401 or
403, when it redirects to a sign-in page, or when its page holds a password
field. An application that only draws its login with JavaScript after the page
loads is not seen as having one: the proxy's login stands in front of it.
"""

import re
import threading
import time
import urllib.error
import urllib.request

SIGN_IN = re.compile(r"log-?in|sign-?in|auth|sso|account|session", re.IGNORECASE)
PASSWORD_FIELD = re.compile(r"""type\s*=\s*["']?password""", re.IGNORECASE)
RETRIES = (0, 5, 15, 30)


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


def ask(container: str, port: int) -> tuple[bool, bool, str]:
    """Whether it answered at all, whether that answer asks for a login, and the answer in words."""
    url = f"http://{container}:{port}/"
    opener = urllib.request.build_opener(_NoRedirect)
    request = urllib.request.Request(url, headers={"User-Agent": "wslc-published-probe", "Accept": "text/html,*/*"})
    try:
        response = opener.open(request, timeout=10)
        status, headers, body = response.status, response.headers, response.read(512 * 1024)
    except urllib.error.HTTPError as error:
        status, headers, body = error.code, error.headers, error.read(512 * 1024) if error.fp else b""
    except (urllib.error.URLError, OSError, ValueError) as error:
        return False, False, f"No answer yet ({getattr(error, 'reason', error)})."
    location = headers.get("Location", "") if headers else ""
    if status in (401, 403):
        return True, True, f"Asks for a login of its own (HTTP {status})."
    if 300 <= status < 400 and SIGN_IN.search(location):
        return True, True, f"Sends to its own sign-in page ({location})."
    if PASSWORD_FIELD.search(body.decode("utf-8", errors="replace")):
        return True, True, "Its page asks for a password."
    return True, False, f"Opens without asking for anything (HTTP {status}): the proxy's login stands in front of it."


def check_in_background(state, host: str) -> None:
    """The check, again while a container just started does not answer yet; recorded in the state."""

    def run():
        for wait in RETRIES:
            time.sleep(wait)
            site = state.site(host)
            if site is None:
                return
            answered, own_login, note = ask(site["container"], site["port"])
            state.checked(host, own_login, note)
            if answered:
                return

    threading.Thread(target=run, daemon=True).start()
