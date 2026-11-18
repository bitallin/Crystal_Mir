# Crystal server tests

Cross-platform regression programs live under `Tests/`. They are run by
`scripts/test.sh`; `Tests/ClientUpdates` covers the MIT client patch manifest,
signature, hash scanning, transactional apply and rollback logic without
requiring Windows. `Tests/ClientUpdates` follows the MIT `Crystal.Patcher`
boundary; the original server tests below remain GPL.

Original tests in this directory are licensed under GPL v3 only
(`GPL-3.0-only`), matching the server they exercise. See the
[server distribution guidance](../Server/README.md), the
[full licence text](../LICENSE-GPL-3.0.txt), and the root
[licensing scope](../LICENSE), including its historical and contributor
permission qualifications.
