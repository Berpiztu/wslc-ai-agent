// Patches the Archify diagrams in src/WslcAgent.UI/wwwroot/archify after
// they are generated, so their motion button always plays:
//
//   node packaging/patch-archify-motion.mjs
//
// Archify disables the button and stops every animation when the system asks
// for reduced motion, which Windows does in a virtual machine or over Remote
// Desktop, and phones in power saving: the diagram could then never move.
// Here that preference only decides that the diagram starts paused; the
// reader can still press Live, an animation the user starts. And Archify
// plays the trace once, on load: here pressing Live plays it again.
//
// Each anchor must be found exactly once, or the file is left untouched and
// the script fails: a new Archify version needs this script looked at again.
// A file already patched is skipped.

import { readdirSync, readFileSync, writeFileSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const folder = join(dirname(fileURLToPath(import.meta.url)), "..", "src", "WslcAgent.UI", "wwwroot", "archify");
const marker = "/* wslc-agent: motion patched */";

const patches = [
  {
    what: "reduced motion no longer pauses on its own",
    from: "function reducedMotion() {\n        return !!(motionQuery && motionQuery.matches);\n      }",
    to: "function reducedMotion() {\n        return false;\n      }",
  },
  {
    what: "the button is never disabled",
    from: "btn.disabled = systemPaused;",
    to: "btn.disabled = false;",
  },
  {
    what: "reduced motion starts the diagram paused",
    from: "readerPaused = readStored() === 'still';",
    to: "readerPaused = readStored() === 'still' || !!(motionQuery && motionQuery.matches);",
  },
  {
    what: "Live plays the trace again",
    from: "btn.addEventListener('click', function () { setPaused(!readerPaused); });",
    to: [
      "function replayAmbient() {",
      "        if (!capable || effectivePaused() || owner) return;",
      "        ambientStarted = false;",
      "        html.setAttribute('data-ambient-motion', 'settled');",
      "        void svg.getBoundingClientRect();",
      "        startAmbient();",
      "      }",
      "      btn.addEventListener('click', function () {",
      "        if (!readerPaused && html.getAttribute('data-ambient-motion') === 'settled') { replayAmbient(); return; }",
      "        var resuming = readerPaused;",
      "        setPaused(!readerPaused);",
      "        if (resuming) replayAmbient();",
      "      });",
    ].join("\n"),
  },
  {
    what: "the CSS stops the trace only while the diagram is still",
    from: "      svg[data-animation=\"trace\"] [data-animate] {\n        animation: none !important;",
    to: "      html[data-motion=\"still\"] svg[data-animation=\"trace\"] [data-animate] {\n        animation: none !important;",
  },
];

let failed = false;
for (const file of readdirSync(folder).filter((name) => name.endsWith(".html"))) {
  const path = join(folder, file);
  let html = readFileSync(path, "utf8");
  if (html.includes(marker)) {
    console.log(`${file}: already patched`);
    continue;
  }
  const missing = patches.filter((patch) => html.split(patch.from).length !== 2);
  if (missing.length > 0) {
    failed = true;
    console.error(`${file}: not patched, anchor not found exactly once: ${missing.map((patch) => patch.what).join("; ")}`);
    continue;
  }
  for (const patch of patches) html = html.replace(patch.from, patch.to);
  html = html.replace("</head>", `<style>${marker}</style>\n</head>`);
  writeFileSync(path, html);
  console.log(`${file}: patched`);
}
process.exit(failed ? 1 : 0);
