// Light or dark, as the agent: inside the agent, the agent's own (the page comes through its
// address, so it can read it); otherwise what this browser chose with the toggle, else the
// system's. Loaded in the head, after icons.js, so the page is never painted in the other one.
(() => {
  const KEY = "wslc-published-theme";
  const page = document.documentElement;

  function agentTheme() {
    try {
      if (window.parent !== window) {
        return window.parent.document.querySelector(".wslc-dark") ? "dark" : "light";
      }
    } catch {
      // Another origin: not the agent's page.
    }
    return null;
  }

  function saved() {
    try {
      return localStorage.getItem(KEY);
    } catch {
      return null;
    }
  }

  const current = () => page.dataset.theme || (matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light");
  const inAgent = agentTheme();
  const chosen = inAgent || saved();
  if (chosen) page.dataset.theme = chosen;

  /** The agent's toggle: the sun on the dark theme, the moon on the light one. */
  function paint(button) {
    const dark = current() === "dark";
    button.replaceChildren(icon(dark ? "LightMode" : "DarkMode"));
    button.title = dark ? "Light theme" : "Dark theme";
  }

  document.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll("[data-theme-toggle]").forEach((button) => {
      // Inside the agent the agent's own toggle decides.
      if (inAgent) {
        button.hidden = true;
        return;
      }
      paint(button);
      button.addEventListener("click", () => {
        page.dataset.theme = current() === "dark" ? "light" : "dark";
        try {
          localStorage.setItem(KEY, page.dataset.theme);
        } catch {
          // Not kept: this page still changes.
        }
        paint(button);
      });
    });
  });
})();
