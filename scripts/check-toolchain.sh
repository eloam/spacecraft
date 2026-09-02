#!/usr/bin/env bash
#
# Checks that Node on this machine is the one the runner image ships.
#
# It matters because the build and publish steps shell out to `npm`, resolved from whatever
# PATH the process inherited, with no version check of its own — see
# src/SpaceCraft.Pipeline/Sandbox/LocalCommandRunner.cs. A binary that does not resolve is
# caught by PipelineExecutor and recorded as a failed run in the database, indistinguishable
# from a bad OpenAPI document. An IDE launched from the Dock rather than a shell is the usual
# way to arrive there: nvm is on PATH because a shell profile put it there.
#
# This used to check the Kiota CLI too, and that was the larger half. It is gone: generation
# is a NuGet reference now, so the generator cannot differ between a laptop and the image.
# npm is the last toolchain the pipeline looks up by name.
#
# The Dockerfile is the source of truth, because it is what ships. .nvmrc restates it for the
# host, and this checks both against what is really on PATH — so a pin that drifts from the
# image is as loud as a machine that drifts from the pin.
#
# Only needed when running the runner on the host. In compose it runs from the image, which
# carries its own.

set -u

root=$(cd "$(dirname "$0")/.." && pwd)
dockerfile="$root/src/SpaceCraft.Runner/Dockerfile"
problems=0

if [ -t 1 ]; then
    red=$(printf '\033[31m'); green=$(printf '\033[32m'); dim=$(printf '\033[2m'); off=$(printf '\033[0m')
else
    red=""; green=""; dim=""; off=""
fi

pass() { printf '%s✓%s %s\n' "$green" "$off" "$1"; }
fail() { printf '%s✗%s %s\n' "$red" "$off" "$1"; problems=$((problems + 1)); }
fix()  { printf '  %s%s%s\n' "$dim" "$1" "$off"; }

want_node=$(sed -n 's/^ARG NODE_VERSION=\(.*\)$/\1/p' "$dockerfile")

if [ -z "$want_node" ]; then
    printf '%s✗%s Could not read NODE_VERSION from %s\n' "$red" "$off" "$dockerfile"
    exit 2
fi

printf 'The runner image ships node %s.\n\n' "$want_node"

have_nvmrc=$(tr -d '[:space:]' < "$root/.nvmrc" 2>/dev/null)
if [ "$have_nvmrc" = "$want_node" ]; then
    pass ".nvmrc agrees with the image (node $have_nvmrc)"
else
    fail ".nvmrc says node '${have_nvmrc:-<missing>}', the image ships $want_node"
    fix "echo $want_node > .nvmrc"
fi

if command -v node >/dev/null 2>&1; then
    host_node=$(node --version 2>/dev/null | sed 's/^v//' | cut -d. -f1)
    if [ "$host_node" = "$want_node" ]; then
        pass "node on PATH is v$(node --version | sed 's/^v//') ($(command -v node))"
    else
        fail "node on PATH is major $host_node, the image ships $want_node"
        fix "nvm use   # reads .nvmrc; nvm install $want_node first if you do not have it"
    fi
else
    fail "node is not on PATH — a run would fail, and be recorded as a failed run"
    fix "nvm install $want_node"
fi

if command -v npm >/dev/null 2>&1; then
    pass "npm on PATH is $(npm --version) ($(command -v npm))"
else
    fail "npm is not on PATH — the build and publish steps would fail"
fi

echo
if [ "$problems" -eq 0 ]; then
    printf '%sThis machine builds what the image builds.%s\n' "$green" "$off"
    exit 0
fi

printf '%s%d mismatch(es).%s Until they are fixed, run the runner from compose rather than the\n' "$red" "$problems" "$off"
printf 'host: SDKs built here would not match the ones that ship.\n'
exit 1
