// Patches the Archify diagrams in src/WslcAgent.UI/wwwroot/archify after
// they are generated, so their two play buttons always play:
//
//   node packaging/patch-archify-motion.mjs
//
// Archify disables the motion button (Still / Live) and "Play story", and
// stops every animation, when the system asks for reduced motion, which
// Windows does in a virtual machine or over Remote Desktop, and phones in
// power saving: the diagram could then never move. Here that preference only
// decides that the diagram starts paused; the reader can still press either
// button, an animation the user starts. Archify also plays the trace once,
// on load: here pressing Live plays it again, and "Play story" pressed while
// the diagram is still switches it to Live first.
//
// Each patch is applied once: one already in the file is skipped, and one
// whose anchor is not found exactly once leaves the file untouched and fails
// the script, as a new Archify version needs this script looked at again.

import { readdirSync, readFileSync, writeFileSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const folder = join(dirname(fileURLToPath(import.meta.url)), "..", "src", "WslcAgent.UI", "wwwroot", "archify");

const patches = [
  {
    what: "reduced motion no longer pauses on its own",
    from: "function reducedMotion() {\n        return !!(motionQuery && motionQuery.matches);\n      }",
    to: "function reducedMotion() {\n        return false;\n      }",
  },
  {
    what: "the motion button is never disabled",
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
  {
    what: "the story plays under reduced motion",
    from: "buildChapterIndex();\n\n      function reducedMotion() {\n        return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);\n      }",
    to: "buildChapterIndex();\n\n      function reducedMotion() {\n        return false;\n      }",
  },
  {
    what: "Play story is never disabled",
    from: "play.disabled = !playing && !automaticPlaybackAllowed;",
    to: "play.disabled = false;",
  },
  {
    what: "Play story switches a still diagram to Live",
    from: "function togglePlayback() {\n        return playing ? (pausePlayback(), false) : startPlayback();\n      }",
    to: [
      "function togglePlayback() {",
      "        var governor = Archify.motionGovernor;",
      "        if (!playing && governor && governor.capable && governor.isPaused()) governor.resume();",
      "        return playing ? (pausePlayback(), false) : startPlayback();",
      "      }",
    ].join("\n"),
  },
];

let failed = false;
for (const file of readdirSync(folder).filter((name) => name.endsWith(".html"))) {
  const path = join(folder, file);
  let html = readFileSync(path, "utf8");
  const pending = patches.filter((patch) => !html.includes(patch.to));
  const missing = pending.filter((patch) => html.split(patch.from).length !== 2);
  if (missing.length > 0) {
    failed = true;
    console.error(`${file}: not patched, anchor not found exactly once: ${missing.map((patch) => patch.what).join("; ")}`);
    continue;
  }
  for (const patch of pending) html = html.replace(patch.from, patch.to);
  if (pending.length > 0) writeFileSync(path, html);
  console.log(`${file}: ${pending.length > 0 ? `patched (${pending.map((patch) => patch.what).join("; ")})` : "already patched"}`);
}
process.exit(failed ? 1 : 0);
