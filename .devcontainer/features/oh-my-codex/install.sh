#!/bin/sh
set -eu

readonly omx_version="0.20.5"

npm install --global "oh-my-codex@${omx_version}"
omx --version | grep -Fq "${omx_version}"
