# Crystal.ZoneAgent

The Zone Agent owns the `Crystal.ZoneServer` child process for one zone. It registers
with the control plane, sends heartbeats, claims durable commands, downloads and verifies
signed content releases, stages them locally, and performs a process-level activation or
rollback.

The agent links only the MIT-licensed `Crystal.Agent.Contracts` and `Crystal.Content`
projects. It never references `Server/`, `Crystal.LegacyRuntime`, or `Crystal.ZoneServer`.
The zone executable is started as a child process.

Activation semantics are intentionally process-level for the research milestone: the old
zone process is stopped with `SIGTERM`, the candidate process is started with
`--content-release` and `--ready-file`, and a failed candidate is replaced by the previous
release. A short maintenance gap is accepted; in-process content hot swapping is out of
scope.
