// One page for two people: a site's administrator (its users, through /__login/api/users)
// and the root (every site, through api/sites, passwords shown, the claim code and the check).
const container = document.getElementById("sites");
const root = container.dataset.root === "true";
const errorBox = document.getElementById("error");

function showError(message) {
  errorBox.hidden = !message;
  errorBox.textContent = message || "";
}

async function call(method, path, body) {
  const response = await fetch(path, {
    method,
    headers: body ? { "Content-Type": "application/json" } : {},
    credentials: "include",
    body: body ? JSON.stringify(body) : undefined,
  });
  const data = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new Error(data.error || `HTTP ${response.status}`);
  }
  return data;
}

function element(tag, text, className) {
  const node = document.createElement(tag);
  if (text !== undefined) node.textContent = text;
  if (className) node.className = className;
  return node;
}

/**
 * The address people open: https, TLS ending at the VPS; a name under localhost is
 * this PC's own, tried out with no VPS, over http on the port the proxy listens on.
 */
function address(host) {
  return host.endsWith(".localhost") ? `http://${host}:8081/` : `https://${host}/`;
}

/** A link that opens in a tab of its own, so this page stays where it is. */
function link(text, href) {
  const anchor = element("a", text, "button ghost small");
  anchor.href = href;
  anchor.target = "_blank";
  anchor.rel = "noopener";
  return anchor;
}

/** A user's password, hidden until its eye is pressed (the root only). */
function passwordCell(password) {
  const cell = element("td", undefined, "password-col");
  if (password === undefined) return cell;
  const shown = element("span", "••••••••", "mono");
  const eye = element("button", "Show", "button ghost small");
  eye.type = "button";
  eye.addEventListener("click", () => {
    const hidden = shown.textContent === "••••••••";
    shown.textContent = hidden ? password : "••••••••";
    eye.textContent = hidden ? "Hide" : "Show";
  });
  cell.append(shown, " ", eye);
  return cell;
}

/** The users table and its add form, against one site's API base. */
function usersBlock(users, base) {
  const block = document.getElementById("users-template").content.cloneNode(true);
  const body = block.querySelector("tbody");
  if (!root) block.querySelectorAll(".password-col").forEach((n) => n.remove());
  if (users.length === 0) {
    const row = element("tr");
    const cell = element("td", "No users yet.", "muted");
    cell.colSpan = 4;
    row.append(cell);
    body.append(row);
  }
  for (const user of users) {
    const row = element("tr");
    row.append(element("td", user.name, "mono"), element("td", user.role === "admin" ? "Administrator" : "User"));
    if (root) row.append(passwordCell(user.password));
    const remove = element("button", "Remove", "button danger small");
    remove.type = "button";
    remove.addEventListener("click", () => act(() => call("DELETE", `${base}/${encodeURIComponent(user.name)}`)));
    const actions = element("td");
    actions.append(remove);
    row.append(actions);
    body.append(row);
  }
  const form = block.querySelector("form");
  form.addEventListener("submit", (event) => {
    event.preventDefault();
    const name = form.elements.name.value.trim();
    act(() => call("PUT", `${base}/${encodeURIComponent(name)}`, { password: form.elements.password.value, role: form.elements.role.value }));
  });
  return block;
}

/** One site, as the root sees it. */
function siteBlock(site) {
  const section = element("section", undefined, "site");
  const head = element("div", undefined, "site-head");
  head.append(element("h2", site.host, "mono"), element("span", site.access === "own" ? "Own login" : "Proxy login", `chip ${site.access}`));
  if (site.forceLogin) head.append(element("span", "Login forced", "chip"));
  // The address as people open it (TLS ends at the VPS), and the site's own administration.
  head.append(link("Open", address(site.host)));
  if (site.access !== "own") head.append(link("Its administration", `${address(site.host)}__admin`));
  const facts = element("p", `${site.container}:${site.port} · ${site.check}`, "muted");
  const tools = element("div", undefined, "tools");
  const check = element("button", "Check again", "button ghost small");
  check.type = "button";
  check.addEventListener("click", () => act(() => call("POST", `api/sites/${site.host}/check`)));
  tools.append(check);
  if (site.access !== "own") {
    const claim = element("span", site.claimCode ? `Code for the first administrator: ${site.claimCode}` : "Has an administrator.", "mono");
    const renew = element("button", "New code", "button ghost small");
    renew.type = "button";
    renew.addEventListener("click", () => act(() => call("POST", `api/sites/${site.host}/claim-code`)));
    tools.append(claim, renew);
  }
  section.append(head, facts, tools);
  if (site.access !== "own") section.append(usersBlock(site.users, `api/sites/${site.host}/users`));
  return section;
}

async function load() {
  try {
    container.replaceChildren();
    if (root) {
      const sites = await call("GET", "api/sites?passwords=1");
      if (sites.length === 0) container.append(element("p", "Nothing is published yet.", "muted"));
      for (const site of sites) container.append(siteBlock(site));
    } else {
      container.append(usersBlock(await call("GET", "/__login/api/users"), "/__login/api/users"));
    }
  } catch (error) {
    showError(error.message);
  }
}

async function act(action) {
  showError("");
  try {
    await action();
  } catch (error) {
    showError(error.message);
  }
  await load();
}

load();
