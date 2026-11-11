#!/usr/bin/env bash
# Turns the "## Unreleased" section of CHANGELOG.md into the next version, ready to tag.
#
#   prepare-release.sh [YYYY-MM-DD]
#
# Prints the new version (2.2.0) after updating CHANGELOG.md, Cooldown.csproj and app.manifest, or
# prints nothing and changes nothing when there's nothing to release: no Unreleased section, or only
# entries under "### Maintenance", which users don't see.
#
# The version goes up by:
#   minor (2.1.1 -> 2.2.0)  when anything is under Added, Changed or Removed
#   patch (2.1.1 -> 2.1.2)  when it's only fixes
# A major version is never picked automatically; release that one by hand.
set -euo pipefail

date="${1:-$(date -u +%Y-%m-%d)}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
changelog="$root/CHANGELOG.md"
csproj="$root/src/Cooldown/Cooldown.csproj"
manifest="$root/src/Cooldown/app.manifest"

# Counts the entries ("- ...") in the Unreleased section, by kind.
read -r minor patch < <(tr -d '\r' < "$changelog" | awk '
  /^## Unreleased[[:space:]]*$/ { on = 1; next }
  on && /^## / { exit }
  on && /^### / { kind = tolower($2); next }
  on && /^- / {
    if (kind == "added" || kind == "changed" || kind == "removed") minor++
    else if (kind != "maintenance") patch++
  }
  END { print minor + 0, patch + 0 }
')

if [ "$minor" -eq 0 ] && [ "$patch" -eq 0 ]; then
  exit 0
fi

current="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$csproj")"
if ! [[ "$current" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)$ ]]; then
  echo "error: Cooldown.csproj has no version like 2.1.1 (found '$current')" >&2
  exit 1
fi
major="${BASH_REMATCH[1]}" min="${BASH_REMATCH[2]}" pat="${BASH_REMATCH[3]}"
if [ "$minor" -gt 0 ]; then version="$major.$((min + 1)).0"; else version="$major.$min.$((pat + 1))"; fi

# Keeps each file's own line endings.
sed -i -E "s/^## Unreleased[[:space:]]*(\r?)$/## $version ($date)\1/" "$changelog"
sed -i -E "s:<Version>[^<]*</Version>:<Version>$version</Version>:" "$csproj"
sed -i -E "s:(<assemblyIdentity version=\")[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+:\1$version.0:" "$manifest"

echo "$version"
