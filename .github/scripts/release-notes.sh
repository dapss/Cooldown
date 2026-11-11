#!/usr/bin/env bash
# Checks that a version tag is ready to release, and prints that version's release notes.
#
#   release-notes.sh v2.1.1
#
# Fails unless the tag's version matches Cooldown.csproj and app.manifest, and CHANGELOG.md has a
# "## 2.1.1 (date)" section with something in it. That section is what it prints.
set -euo pipefail

tag="${1:?usage: release-notes.sh vX.Y.Z}"
version="${tag#v}"
root="$(cd "$(dirname "$0")/../.." && pwd)"

if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "error: '$tag' is not a version tag like v2.1.1" >&2
  exit 1
fi

csproj="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$root/src/Cooldown/Cooldown.csproj")"
manifest="$(sed -n 's:.*<assemblyIdentity version="\([0-9]*\.[0-9]*\.[0-9]*\)\.[0-9]*".*:\1:p' "$root/src/Cooldown/app.manifest")"

if [ "$csproj" != "$version" ]; then
  echo "error: tag is $version but Cooldown.csproj says '$csproj'" >&2
  exit 1
fi
if [ "$manifest" != "$version" ]; then
  echo "error: tag is $version but app.manifest says '$manifest'" >&2
  exit 1
fi

# The lines between "## X.Y.Z (" and the next "## " heading.
notes="$(tr -d '\r' < "$root/CHANGELOG.md" | awk -v heading="## $version (" '
  index($0, heading) == 1 { on = 1; next }
  on && /^## / { exit }
  on { print }
')"

if ! grep -q '[^[:space:]]' <<< "$notes"; then
  echo "error: CHANGELOG.md has no '## $version (date)' section, or it is empty" >&2
  exit 1
fi

# Without the blank lines before it (the ones after are already gone).
printf '%s\n' "$notes" | sed '/./,$!d'
