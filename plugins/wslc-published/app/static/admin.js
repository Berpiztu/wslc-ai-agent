// One page for two people: a site's administrator (its users, through /__login/api/users)
// and the root (every site, through api/sites: passwords shown, the claim code, the check).
const container = document.getElementById("sites");
const root = container.dataset.root === "true";

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

/** A message at the bottom for a few seconds, as the agent's snackbar. */
function snack(message, failed = false) {
  const bar = element("div", message, failed ? "snackbar error" : "snackbar");
  document.body.append(bar);
  setTimeout(() => bar.remove(), failed ? 6000 : 3000);
}

/**
 * The clipboard, and where the browser refuses it (a page over plain http that is
 * not this machine) the old way: a selected text area and the copy command.
 */
async function copy(text, what) {
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    const area = element("textarea");
    area.value = text;
    area.style.position = "fixed";
    area.style.opacity = "0";
    document.body.append(area);
    area.select();
    const done = document.execCommand("copy");
    area.remove();
    if (!done) {
      snack(`${what} could not be copied`, true);
      return;
    }
  }
  snack(`${what} copied`);
}

function iconButton(name, title, tone, onClick, filled = false) {
  const button = element("button", undefined, `icon-button small ${tone}${filled ? " filled" : ""}`);
  button.type = "button";
  button.title = title;
  button.append(icon(name));
  button.addEventListener("click", onClick);
  return button;
}

/**
 * The address people open: https, TLS ending at the VPS; a name under localhost is
 * this PC's own, tried out with no VPS, over http on the port the proxy listens on.
 */
function address(host) {
  return host.endsWith(".localhost") ? `http://${host}:8081/` : `https://${host}/`;
}

function opens(text, href, className) {
  const anchor = element("a", text, className);
  anchor.href = href;
  anchor.target = "_blank";
  anchor.rel = "noopener";
  return anchor;
}

/** Sixteen characters nobody has to think of, without the ones that read alike. */
function newPassword() {
  const letters = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  return Array.from(bytes, (b) => letters[b % letters.length]).join("");
}

// The dialog: a title, a text, fields, and what its button says. Resolves with the
// fields' values, or null when cancelled; check() may refuse with a reason.
const dialog = document.getElementById("dialog");

function field(spec) {
  const label = element("label", undefined, "field");
  label.append(element("span", spec.label));
  let input;
  if (spec.options) {
    input = element("select");
    for (const [value, text] of spec.options) {
      const option = element("option", text);
      option.value = value;
      input.append(option);
    }
  } else {
    input = element("input");
    input.type = spec.type || "text";
    input.autocomplete = spec.autocomplete || "off";
    input.autocapitalize = "none";
    input.spellcheck = false;
  }
  input.name = spec.name;
  // A list without a value shows its first option; an empty value would leave it blank.
  if (spec.value !== undefined) input.value = spec.value;
  label.append(input);
  if (spec.generate) {
    // Generate fills the password and its repeat, and shows them, so it can be copied and handed over.
    label.classList.add("with-button");
    label.append(iconButton("AutoFixHigh", "Generate a password", "success", () => {
      const password = newPassword();
      for (const name of [spec.name, spec.generate]) {
        const twin = dialog.querySelector(`[name="${name}"]`);
        twin.value = password;
        twin.type = "text";
      }
    }, true));
  }
  return label;
}

function ask({ title, text, fields = [], ok = "OK", destructive = false, check }) {
  document.getElementById("dialog-title").textContent = title;
  const textBox = document.getElementById("dialog-text");
  textBox.hidden = !text;
  textBox.textContent = text || "";
  const errorBox = document.getElementById("dialog-error");
  errorBox.hidden = true;
  document.getElementById("dialog-fields").replaceChildren(...fields.map(field));
  const okButton = document.getElementById("dialog-ok");
  okButton.textContent = ok;
  okButton.className = destructive ? "button auto error" : "button auto";
  return new Promise((resolve) => {
    const form = dialog.querySelector("form");
    const finish = (values) => {
      form.onsubmit = null;
      document.getElementById("dialog-cancel").onclick = null;
      dialog.onclose = null;
      dialog.close();
      resolve(values);
    };
    form.onsubmit = (event) => {
      event.preventDefault();
      const values = Object.fromEntries(fields.map((f) => [f.name, dialog.querySelector(`[name="${f.name}"]`).value]));
      const refused = check ? check(values) : "";
      if (refused) {
        errorBox.hidden = false;
        errorBox.textContent = refused;
        return;
      }
      finish(values);
    };
    document.getElementById("dialog-cancel").onclick = () => finish(null);
    dialog.onclose = () => finish(null);
    dialog.showModal();
    dialog.querySelector("input, select")?.focus();
  });
}

const sameTwice = (values) => (values.password !== values.confirm ? "The two passwords are not the same." : "");
const roles = [["user", "User"], ["admin", "Administrator"]];
const passwordFields = [
  { name: "password", label: "Password (8 or more characters)", type: "password", autocomplete: "new-password", generate: "confirm" },
  { name: "confirm", label: "Repeat password", type: "password", autocomplete: "new-password" },
];

/** Does something, says it went well or why not, and reads the page again. */
async function act(action, done) {
  try {
    await action();
    if (done) snack(done);
  } catch (error) {
    snack(error.message, true);
  }
  await load();
}

// The users of one site, against its API: base is api/sites/<host>/users (the root)
// or /__login/api/users (the site's administrator, who is "me").

async function addUser(base) {
  const values = await ask({
    title: "Add user",
    fields: [{ name: "name", label: "User", autocomplete: "off" }, ...passwordFields, { name: "role", label: "Role", options: roles }],
    ok: "Add",
    check: sameTwice,
  });
  if (values) {
    await act(() => call("POST", base, { name: values.name.trim(), password: values.password, role: values.role }), `${values.name.trim()} added`);
  }
}

async function changePassword(base, user) {
  const values = await ask({ title: `New password for ${user.name}`, fields: passwordFields, ok: "Change", check: sameTwice });
  if (values) {
    await act(() => call("PATCH", `${base}/${encodeURIComponent(user.name)}`, { password: values.password }), `Password of ${user.name} changed`);
  }
}

async function changeRole(base, user) {
  const values = await ask({ title: `Role of ${user.name}`, fields: [{ name: "role", label: "Role", options: roles, value: user.role }], ok: "Change" });
  if (values && values.role !== user.role) {
    await act(() => call("PATCH", `${base}/${encodeURIComponent(user.name)}`, { role: values.role }), `${user.name} is now ${values.role === "admin" ? "an administrator" : "a user"}`);
  }
}

async function removeUser(base, user) {
  const yes = await ask({ title: "Remove user", text: `Take ${user.name} out? Their session ends at once.`, ok: "Remove", destructive: true });
  if (yes) {
    await act(() => call("DELETE", `${base}/${encodeURIComponent(user.name)}`), `${user.name} removed`);
  }
}

/** A password as the root sees it: hidden until its eye is pressed, and copied as it is. */
function passwordCell(password) {
  const cell = element("td");
  const box = element("span", undefined, "password");
  const value = element("span", "••••••••", "value");
  const eye = iconButton("Visibility", "Show", "primary", () => {
    const hidden = value.textContent === "••••••••";
    value.textContent = hidden ? password : "••••••••";
    eye.replaceChildren(icon(hidden ? "VisibilityOff" : "Visibility"));
    eye.title = hidden ? "Hide" : "Show";
  });
  box.append(value, eye, iconButton("ContentCopy", "Copy the password", "primary", () => copy(password, "Password")));
  cell.append(box);
  return cell;
}

function usersGrid(users, base, me) {
  const grid = element("div", undefined, "grid");
  const table = element("table");
  const head = element("tr");
  head.append(element("th", "User"), element("th", "Role"));
  if (root) head.append(element("th", "Password"));
  head.append(element("th", "Actions", "actions-col"));
  const body = element("tbody");
  if (users.length === 0) {
    const row = element("tr");
    const cell = element("td", "No users yet.", "muted");
    cell.colSpan = root ? 4 : 3;
    row.append(cell);
    body.append(row);
  }
  for (const user of users) {
    const row = element("tr");
    const role = element("span", user.role === "admin" ? "Administrator" : "User", `role ${user.role}`);
    role.prepend(icon(user.role === "admin" ? "AdminPanelSettings" : "Person"));
    const roleCell = element("td");
    roleCell.append(role);
    row.append(element("td", user.name === me ? `${user.name} (you)` : user.name, "mono"), roleCell);
    if (root) row.append(passwordCell(user.password || ""));
    const actions = element("td", undefined, "actions-col");
    const remove = iconButton("Delete", "Remove", "error", () => removeUser(base, user));
    if (user.name === me) {
      remove.disabled = true;
      remove.title = "You cannot take yourself out; another administrator can";
    }
    actions.append(
      iconButton("Key", "Change the password", "primary", () => changePassword(base, user)),
      iconButton("ManageAccounts", "Change the role", "primary", () => changeRole(base, user)),
      remove,
    );
    row.append(actions);
    body.append(row);
  }
  const thead = element("thead");
  thead.append(head);
  table.append(thead, body);
  grid.append(table);
  return grid;
}

/** Who may open a site, in the words of the agent's access icons. */
function access(site) {
  const admins = site.users.filter((u) => u.role === "admin").length;
  const [kind, name, words] = site.access === "own"
    ? ["own", "VerifiedUser", "Own login"]
    : admins > 0 ? ["users", "Lock", `Proxy login, ${site.users.length} user${site.users.length === 1 ? "" : "s"}`] : ["waiting", "Lock", "Proxy login, no administrator yet"];
  const node = element("span", site.forceLogin ? `${words} · login forced` : words, `access ${kind}`);
  node.prepend(icon(name));
  return node;
}

/** One site, as the root sees it. */
function siteBlock(site) {
  const section = element("section", undefined, "site");
  const base = `api/sites/${site.host}/users`;
  const head = element("div", undefined, "site-head");
  head.append(
    opens(site.host, address(site.host), "site-link"),
    access(site),
    element("span", `${site.container}:${site.port}`, "muted mono"),
    element("span", undefined, "grow"),
    iconButton("FactCheck", "Check again whether the application asks for a login of its own", "primary",
      () => act(() => call("POST", `api/sites/${site.host}/check`), `${site.host} checked`)),
  );
  if (site.access !== "own") {
    head.append(iconButton("PersonAdd", "Add a user", "primary", () => addUser(base)));
    const admin = opens("", `${address(site.host)}__admin`, "icon-button small primary");
    admin.title = "Its own administration";
    admin.append(icon("AdminPanelSettings"));
    head.append(admin);
  }
  section.append(head, element("p", site.check, "note"));
  if (site.access !== "own") {
    const claim = element("p", undefined, "claim");
    // A code left over from before the root made an administrator is no use: an address with one is not claimed.
    if (site.claimCode && !site.users.some((u) => u.role === "admin")) {
      claim.append("Code for the first administrator:", element("span", site.claimCode, "code"),
        iconButton("ContentCopy", "Copy the code", "primary", () => copy(site.claimCode, "Code")));
    } else {
      claim.append(element("span", "Has an administrator.", "muted"));
    }
    claim.append(iconButton("Refresh", "A new code for the first administrator", "primary", async () => {
      const yes = await ask({ title: "New code", text: `A new code for ${site.host}'s first administrator? The one given before stops working.`, ok: "New code" });
      if (yes) await act(() => call("POST", `api/sites/${site.host}/claim-code`), "New code made");
    }));
    section.append(claim, usersGrid(site.users, base, ""));
  }
  return section;
}

async function load() {
  try {
    if (root) {
      const sites = await call("GET", "api/sites?passwords=1");
      container.replaceChildren(...(sites.length ? sites.map(siteBlock) : [element("p", "Nothing is published yet.", "muted")]));
    } else {
      const { me, users } = await call("GET", "/__login/api/users");
      container.replaceChildren(usersGrid(users, "/__login/api/users", me));
    }
  } catch (error) {
    container.replaceChildren(element("p", error.message, "error"));
  }
}

function wireRail() {
  document.getElementById("refresh")?.addEventListener("click", () => load());
  document.getElementById("add-user")?.addEventListener("click", () => addUser("/__login/api/users"));
  document.getElementById("my-password")?.addEventListener("click", async () => {
    const values = await ask({
      title: "Change my password",
      fields: [{ name: "current", label: "Current password", type: "password", autocomplete: "current-password" }, ...passwordFields],
      ok: "Change",
      check: sameTwice,
    });
    if (values) await act(() => call("POST", "/__login/api/me/password", values), "Your password is changed");
  });
  document.getElementById("sign-out")?.addEventListener("click", async () => {
    await call("POST", "/__login/api/logout", {});
    window.location.href = "/";
  });
}

wireRail();
load();
