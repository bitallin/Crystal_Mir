# 0015 客户端签名补丁清单与事务更新

- 状态：已接受
- 日期：2026-11-18

## 背景

现有客户端更新依赖 `PList.gz`：清单只记录文件长度和修改时间，下载后原地覆盖文件，仅对
`.dll`/`.exe`/`.pdb` 使用 `Old__` 备份。该机制没有发布者签名、没有逐文件 SHA256、不能可靠拒绝路径穿越，
也无法在应用失败时恢复整个更新事务。M3 需要先把客户端更新变成可验证、可回滚的最小闭环，同时保留旧补丁
路径以兼容既有部署。

本轮按研究项目口径跳过授权、许可证、CDN、灰度和客户端可执行文件自更新。目标是形成可用
`Crystal.PatcherTool` 打包、签名、验证、应用和回滚的垂直切片，并能被旧 WinForms Launcher 选择性调用。

## 决策

### 1. 独立 MIT 更新库

新增 `src/Crystal.Patcher`（MIT、`net8.0`、无项目依赖），负责 manifest 模型、canonical JSON、签名、路径校验、
本地哈希扫描、差异计划、下载、事务应用和回滚。`tools/Crystal.PatcherTool` 提供 CLI；`Client` 只通过项目引用
使用该库，不把更新逻辑写入游戏主循环，也不跨入 GPL `Server/`。

### 2. 复用 ECDSA P-256 签名原则

客户端 manifest 使用与 ADR 0005 相同的 ECDSA P-256 + SHA-256、IEEE P1363 签名格式和 `keyId` 轮换原则，
但使用独立载荷前缀 `crystal-client-patch-v1`。签名覆盖去掉 `signature` 的完整 manifest 和
`algorithm`/`keyId`/`signedAt` 元数据，使用 camelCase canonical JSON 编码。

### 3. Manifest 与文件级增量

manifest 是 camelCase JSON，包含 `channel`、`release`、最低 launcher/client 版本、协议版本、内容 schema 版本、
生成/过期时间、文件列表、删除列表和签名。每个文件记录相对路径、大小、SHA256 和下载 URL。

本轮实现文件级增量：只下载新增或哈希变化的文件，未变化文件跳过；全部文件采用全量内容下载，HTTP 下载支持
Range 断点续传。二进制 delta、并发下载限速和 CDN 内容寻址仍属于后续范围。

### 4. 路径与版本门禁

更新器拒绝绝对路径、`..`、空路径段、冒号/控制字符、Windows 非法字符/设备名/尾部点或空格、重复路径（大小写不敏感）
和 `.crystal-update/` 保留目录。
校验 channel、最低 launcher/client 版本、协议版本、内容 schema 版本、manifest 有效期和未来生成时间。
本地状态记录上一次已应用 manifest 的 `generatedAt`，旧 manifest 重放按降级拒绝。

### 5. 事务应用、失败恢复与回滚

更新状态放在客户端根目录的 `.crystal-update/`：

```text
staging/                 下载与哈希校验临时目录
backups/{transaction}/   被替换或删除文件的备份
pending.json             应用前写入的待提交事务
state.json               当前发布、上一次发布、备份目录和动作列表
```

应用顺序为：下载并校验全部文件 -> 写 `pending.json` -> 备份旧文件 -> 移入新文件 -> 写 `state.json` -> 删除
`pending.json`。任一失败会恢复旧文件；进程在事务中崩溃时，下次更新或显式回滚会读取 `pending.json` 恢复。
回滚只恢复本事务涉及的文件，不删除 manifest 未声明的未知本地文件。

### 6. 与旧启动器的关系

`Client` 的 `Launcher:ManifestUrl` 为空时保持 `PList.gz` 流程完全不变；配置后改用签名 manifest 更新，并读取
`Launcher:ManifestPublicKey`、`ManifestKeyId`、`Channel`、`ProtocolVersion`、`ContentSchemaVersion` 以及可选的
client/launcher 版本覆盖。签名更新失败时不会静默回退到旧清单。

本轮不更新正在运行的 launcher 可执行文件；manifest 应排除 launcher 自身。两阶段自更新留待后续。

## 备选方案

1. 继续使用 `PList.gz`：改动小，但无法证明发布者身份和文件完整性，拒绝。
2. 原地覆盖并只保留 `.exe/.dll` 的 `Old__` 备份：失败时无法保证整批文件一致，拒绝。
3. 立即实现二进制 delta：需要额外补丁格式、生成器和基线管理，研究阶段收益不足，推迟。
4. Ed25519：长期更合适，但 .NET 8 BCL 没有稳定 API；沿用 ADR 0005 的 ECDSA 以保持实现简单。

## 影响

- 客户端发布方需要生成签名 manifest，并分发公钥和 `keyId`。
- 已配置签名 manifest 的启动器不再依赖文件时间戳，改为 SHA256 差异。
- 失败更新和进程崩溃有本地恢复路径；回滚只覆盖最近一次已应用更新。
- 未实现 launcher 自更新、灰度/CDN、二进制 delta、并发限速、Windows 运行冒烟和发布服务渠道分发。
- 旧 `PList.gz` 与 `AutoPatcherAdminV2` 继续作为未配置签名 manifest 时的兼容路径。

## 重新评估条件

- 需要 Windows 客户端真实登录/进服前的启动器运行验证时。
- 需要 launcher 自更新、灰度、CDN 或二进制 delta 时。
- .NET 提供稳定 Ed25519 API 或密钥管理迁移到 KMS 时。
- 需要多版本保留、自动回滚到更早版本或服务端控制更新状态时。
