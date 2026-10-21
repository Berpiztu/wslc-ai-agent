#!/usr/bin/env bash
# Prints the next version of an image in ghcr.io: its highest numeric tag (1.8, 1.10...)
# with the last number plus one, or 1.0 when it has none yet.
# Usage: next-version.sh <owner>/<image>   (GITHUB_TOKEN in the environment)
set -euo pipefail
image="$1"

# GET a URL and print its body; exit 2 when it answers 404 (an image never published yet,
# which is no error: it starts at 1.0) and fail on any other error.
get() {
  local response code
  response=$(curl -sSL -w '\n%{http_code}' "$@")
  code=${response##*$'\n'}
  case "$code" in
    200) printf '%s' "${response%$'\n'*}" ;;
    404) return 2 ;;
    *) echo "ghcr.io answered $code: ${response%$'\n'*}" >&2; exit 1 ;;
  esac
}

# The registry takes a token of its own, exchanged for the workflow's GITHUB_TOKEN.
# A 404 on either call is an image never published: version 1.0. Any other error stops.
token_json=$(get -u "${GITHUB_ACTOR}:${GITHUB_TOKEN}" "https://ghcr.io/token?scope=repository:${image}:pull") && rc=0 || rc=$?
case $rc in 0) ;; 2) echo "1.0"; exit 0 ;; *) exit 1 ;; esac
registry_token=$(jq -r '.token' <<<"$token_json")
tags_json=$(get -H "Authorization: Bearer ${registry_token}" "https://ghcr.io/v2/${image}/tags/list?n=1000") && rc=0 || rc=$?
case $rc in 0) ;; 2) echo "1.0"; exit 0 ;; *) exit 1 ;; esac
tags=$(jq -r '.tags[]?' <<<"$tags_json")

last=$(printf '%s\n' "$tags" | grep -E '^[0-9]+(\.[0-9]+)*$' | sort -V | tail -n 1 || true)
if [ -z "$last" ]; then
  echo "1.0"
elif [[ "$last" == *.* ]]; then
  echo "${last%.*}.$(( ${last##*.} + 1 ))"
else
  echo "$(( last + 1 ))"
fi
