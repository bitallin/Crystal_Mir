# 0005 内容快照签名格式

- 状态：已接受
- 日期：2026-11-11

## 背景

内容快照已经通过 manifest、实体文件 SHA256 和 `contentHash` 保证完整性，但只能发现传输或存储错误，不能证明发布者身份。ADR 0002 要求发布不可变内容包和签名 manifest；安全文档要求使用 Ed25519 或等效算法。

当前运行时为 .NET 8，BCL 没有稳定的 Ed25519 公钥 API。引入 BouncyCastle/NSec 会增加依赖、许可证和供应链面，而 M1 只需要可验证的基本签名闭环。

## 决策

- 使用 ECDSA P-256 + SHA-256 作为当前内容签名算法，算法标识为 `ecdsa-p256-sha256`。
- 签名值使用 IEEE P1363 固定宽度格式，Base64 编码。
- manifest 新增可选 `signature` 对象：`algorithm`、`keyId`、`signedAt`、`value`。
- 签名载荷是 canonical JSON envelope：包含 payload 前缀/版本、去掉 `signature` 的完整 manifest，以及除 `value` 外的签名元数据。这样 release 元数据、实体哈希和 key id 都被签名覆盖。
- `contentHash` 仍只覆盖实体文件和计数，不包含签名；签名不改变快照内容哈希或旧二进制重建结果。
- 私钥只以 PEM 文件或外部 KMS/Vault 提供，不进入仓库、数据库、日志或内容包；发布侧通过 `keyId` 支持密钥轮换。
- `Crystal.Content` 提供签名和验证 API；MIT 的 `Crystal.ContentTool` 提供 `sign`/`verify` CLI。未签名快照仍可读取，区服是否强制验签由后续发布/Agent 配置决定。

## 备选方案

1. Ed25519：安全性和性能更适合发布签名，但 .NET 8 需要额外依赖；保留 `algorithm` 字段，后续可迁移。
2. RSA：BCL 支持，但密钥和签名更大，轮换和客户端内置成本更高。
3. 不签名，仅依赖 SHA256：无法证明发布者身份，不能满足供应链安全要求。

## 影响

- 发布流程需要密钥管理、`keyId` 登记和公钥分发。
- 签名验证必须先校验 manifest、实体文件和 `contentHash`，再验证签名。
- 算法迁移必须使用新的 `algorithm` 标识和兼容窗口，不得静默替换。
- 当前只完成 M1 基本签名；Control API 发布审批、KMS 集成、Agent 强制验签和客户端 manifest 验签仍属于 M2/M3。

## 重新评估条件

- .NET 运行时或目标部署环境提供稳定的 Ed25519 API。
- KMS 只支持特定非 ECDSA 算法。
- 内容包签名性能或密钥轮换成为发布瓶颈。
