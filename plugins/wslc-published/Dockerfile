# wslc-published: the publishing proxy of WSLC AI Agent, as a plugin of its own.
# nginx in front of the published names, and its own program (app/): the login of
# every name, each name's own users and administration, and the root's API and
# page on port 8082, which the agent calls. Published as
# ghcr.io/berpiztu/wslc-published by .github/workflows/published-image.yml.
#
# Nothing that changes is in the image: /data, a folder of the host, holds the
# sites, their users, the key their passwords are encrypted with, the session
# secret and the admin token, so an update of the image loses nothing.
#
# Debian, not Alpine: in a musl container (Alpine) wslc's host.wslc.internal does
# not resolve for getaddrinfo (https://github.com/microsoft/WSL/issues/41769);
# glibc resolves it.
FROM nginx:stable

RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 python3-flask python3-waitress python3-cryptography \
    && rm -rf /var/lib/apt/lists/*

COPY app /opt/wslc-published/app
# nginx's entrypoint runs these before nginx starts.
COPY 40-wslc-published.sh /docker-entrypoint.d/40-wslc-published.sh
RUN chmod +x /docker-entrypoint.d/40-wslc-published.sh

EXPOSE 80 8082
VOLUME /data
