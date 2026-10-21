#!/bin/sh
# wslc-published's program, started by nginx's entrypoint before nginx: nginx's
# configuration written from the saved sites first, then the login and the root's
# API in the background. If they stop, nginx's auth subrequest fails and every
# name answers an error: the proxy fails closed, never open.
set -e
mkdir -p /data
cd /opt/wslc-published/app
python3 main.py --render
python3 main.py >>/data/wslc-published.log 2>&1 &
