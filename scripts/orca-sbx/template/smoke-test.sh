#!/usr/bin/env sh
set -eu

test "$(dotnet --version)" = "10.0.300"
test "$(bun --version)" = "1.4.0"
test "$(omp --version)" = "omp/18.0.4"
