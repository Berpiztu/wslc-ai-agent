// The login page: sign in, or create the address's first administrator with its code.
const errorBox = document.getElementById("error");

function showError(message) {
  if (errorBox) {
    errorBox.hidden = !message;
    errorBox.textContent = message || "";
  }
}

async function post(path, body) {
  const response = await fetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    credentials: "include",
    body: JSON.stringify(body),
  });
  const data = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new Error(data.error || "It did not work.");
  }
  return data;
}

function onSubmit(formId, path, body) {
  const form = document.getElementById(formId);
  if (!form) {
    return;
  }
  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    showError("");
    const confirm = document.getElementById("confirm");
    if (confirm && confirm.value !== value("password")) {
      showError("The two passwords are not the same.");
      return;
    }
    const button = document.getElementById("submit");
    button.disabled = true;
    try {
      const data = await post(path, body());
      window.location.href = data.next || "/";
    } catch (error) {
      showError(error.message);
    } finally {
      button.disabled = false;
    }
  });
}

const value = (id) => document.getElementById(id).value;

onSubmit("login-form", "/__login/api/login", () => ({
  username: value("username").trim(),
  password: value("password"),
  next: value("next"),
}));

onSubmit("claim-form", "/__login/api/claim", () => ({
  code: value("code").trim(),
  username: value("username").trim(),
  password: value("password"),
  confirm: value("confirm"),
  next: value("next"),
}));

const logout = document.getElementById("logout");
if (logout) {
  logout.addEventListener("click", async () => {
    await post("/__login/api/logout", {});
    window.location.reload();
  });
}
