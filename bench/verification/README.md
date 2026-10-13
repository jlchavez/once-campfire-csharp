# Verification harness

[`checkout/`](checkout) is [basecamp/once-campfire-verification](https://github.com/basecamp/once-campfire-verification)
pinned to `ec02debb3e44f40b8778a6a42db3c4515b2f9eaf`. Nothing in that checkout is edited.

[`csharp.patch`](csharp.patch) adds a `csharp` app the way that harness defines Go, Rust and C:
an allowed name, an image (`campfire-csharp:app`, overridable with `CSHARP_IMAGE`) and a
topology entry. [`run`](run) applies the patch to a disposable git worktree and runs
`bin/benchmark`. Rails is `reference/` at `edbc779` (`RAILS_IMAGE`, default
`campfire-reference:app-edbc779`). C# is this repo (`CSHARP_IMAGE`, default `campfire-csharp:b07`).

The host has no Ruby or `sqlite3` CLI, so the orchestrator runs in `campfire-b07-runner`
([`Dockerfile`](Dockerfile)). App containers are started on the host daemon. The runner is
kept off CPUs 8–11; the load generator is `taskset` to 12–15.

The harness seed is built by its `bin/seed` (Rails `90b3300`). That image is built on the
host, because `bin/seed`'s `docker build` inside the runner selects the legacy builder and
the Rails Dockerfile uses `COPY --chmod`. C# refuses a database with pending migrations,
and `edbc779` has three migrations after that pin, so `run prepare` then runs
`bin/rails db:prepare` in the edbc779 image. Both measured apps boot that same migrated
seed. Credentials stay in `tmp/b07-seed/` and are not committed.

Published numbers are in [`../RESULTS.md`](../RESULTS.md).

### Without Thruster

The production image runs Thruster in front of Kestrel, as the reference does, and the harness
measures that port. Go, Rust and C serve HTTP from the app process instead, and on these four
CPUs Thruster caps C# reads at about a third of what Kestrel serves directly. To measure the
app itself, build [`Dockerfile.direct`](Dockerfile.direct) on top of an image and pass it as
`CSHARP_IMAGE`: its `bin/boot` runs `campfire server` on `HTTP_PORT` with no Thruster. Label
such results "C# (Kestrel, no Thruster)"; they are not the ONCE image.

```sh
docker build -f bench/verification/Dockerfile.direct --build-arg BASE_IMAGE=campfire-csharp:app \
  -t campfire-csharp:app-direct bench/verification
CSHARP_IMAGE=campfire-csharp:app-direct bench/verification/run bench normal "$PWD/bench/results/<name>"
```

```sh
bench/verification/run prepare
bench/verification/run sessions
python3 bench/verification/summarize.py bench/results/verify-session-1-normal
```
