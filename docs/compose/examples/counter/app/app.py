"""A page that counts its visits, the count kept in Redis."""

import os
import socket
import sys

from flask import Flask, render_template_string
from redis import Redis

# The line step 5 of the tutorial changes: the page's title.
TITLE = "Compose on WSLC"

app = Flask(__name__)
redis = Redis(host=os.environ.get("REDIS_HOST", "redis"), port=6379)

PAGE = """<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{{ title }}</title>
<style>
  :root { color-scheme: dark; }
  * { box-sizing: border-box; margin: 0; }
  body {
    min-height: 100vh; display: grid; place-items: center; padding: 24px;
    font-family: "Segoe UI", system-ui, sans-serif; color: #e2e8f0;
    background: radial-gradient(circle at 20% 15%, #312e81 0, transparent 45%),
                radial-gradient(circle at 85% 90%, #0e7490 0, transparent 40%), #0b1020;
  }
  main {
    width: min(560px, 100%); padding: 40px 36px; border-radius: 20px; text-align: center;
    background: rgba(15, 23, 42, .78); border: 1px solid rgba(129, 140, 248, .35);
    box-shadow: 0 30px 80px rgba(0, 0, 0, .45);
  }
  .tags { display: flex; gap: 8px; justify-content: center; flex-wrap: wrap; margin-bottom: 22px; }
  .tag {
    padding: 4px 12px; border-radius: 999px; font-size: 13px; font-weight: 600; letter-spacing: .02em;
    background: rgba(129, 140, 248, .16); color: #c7d2fe; border: 1px solid rgba(129, 140, 248, .4);
  }
  .tag.green { background: rgba(52, 211, 153, .14); color: #a7f3d0; border-color: rgba(52, 211, 153, .4); }
  h1 { font-size: 34px; font-weight: 700; line-height: 1.2; }
  .lead { margin-top: 8px; color: #94a3b8; font-size: 16px; }
  .count {
    margin: 30px 0 4px; font-size: 96px; font-weight: 800; line-height: 1;
    background: linear-gradient(90deg, #818cf8, #22d3ee); -webkit-background-clip: text; background-clip: text; color: transparent;
  }
  .unit { color: #94a3b8; font-size: 15px; letter-spacing: .08em; text-transform: uppercase; }
  .stack { margin-top: 32px; display: grid; gap: 10px; text-align: left; }
  .box {
    display: flex; justify-content: space-between; gap: 12px; padding: 12px 16px; border-radius: 12px;
    background: rgba(30, 41, 59, .8); border: 1px solid rgba(148, 163, 184, .18); font-size: 14px;
  }
  .box b { color: #e2e8f0; }
  .box span { color: #94a3b8; text-align: right; }
  footer { margin-top: 26px; font-size: 13px; color: #64748b; }
  footer a { color: #a5b4fc; text-decoration: none; }
</style>
</head>
<body>
<main>
  <div class="tags">
    <span class="tag">WSLC</span>
    <span class="tag">wslc-compose</span>
    <span class="tag green">running</span>
  </div>
  <h1>{{ title }}</h1>
  <p class="lead">Two services, one Compose file, on Windows. No Docker Desktop.</p>
  <div class="count">{{ visits }}</div>
  <div class="unit">visits, kept in Redis</div>
  <div class="stack">
    <div class="box"><b>web</b><span>built from ./app &middot; Python {{ python }} &middot; {{ host }}</span></div>
    <div class="box"><b>redis</b><span>redis {{ redis_version }} &middot; volume counter_data</span></div>
  </div>
  <footer>Brought up by <a href="https://github.com/Berpiztu/wslc-ai-agent">WSLC AI Agent</a></footer>
</main>
</body>
</html>"""


@app.route("/")
def counter():
    """The page, one visit more."""
    return render_template_string(
        PAGE,
        title=TITLE,
        visits=redis.incr("visits"),
        python=".".join(map(str, sys.version_info[:3])),
        host=socket.gethostname(),
        redis_version=redis.info("server").get("redis_version", "?"),
    )
