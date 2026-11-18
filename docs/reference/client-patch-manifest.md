# 客户端补丁 Manifest 与工具

> 最后更新：2026-11-18
> 状态：生效（研究项目 M3 垂直切片）

## 1. Manifest

客户端更新使用 `application/json`、camelCase 字段和 ECDSA P-256/SHA-256 签名。示例：

```json
{
  "formatVersion": 1,
  "channel": "stable",
  "release": "2026.11.18.1",
  "minimumLauncherVersion": "1.0.0",
  "minimumClientVersion": "1.0.0",
  "protocolVersion": 1,
  "contentSchemaVersion": 1,
  "generatedAt": "2026-11-18T00:00:00+00:00",
  "expiresAt": "2026-12-18T00:00:00+00:00",
  "files": [
    {
      "path": "Data/Items.lib",
      "size": 12345678,
      "sha256": "…64 位小写十六进制…",
      "url": "https://cdn.example.com/releases/2026.11.18.1/Data/Items.lib"
    }
  ],
  "deleted": ["Data/Old.lib"],
  "signature": {
    "algorithm": "ecdsa-p256-sha256",
    "keyId": "release-2026",
    "signedAt": "2026-11-18T00:00:00+00:00",
    "value": "…Base64 IEEE P1363 签名…"
  }
}
```

签名载荷前缀为 `crystal-client-patch-v1`，版本为 `1`，覆盖去掉 `signature` 的 manifest 以及签名的
`algorithm`、`keyId`、`signedAt`。字段排序使用 canonical JSON，文件数组顺序不重排。

## 2. 工具

```bash
# 生成未签名清单（URL 前缀 + 相对路径）
Crystal.PatcherTool pack --root ./client-build --output manifest.json \
  --channel stable --release 2026.11.18.1 \
  --url-prefix https://cdn.example.com/releases/2026.11.18.1 \
  --min-client-version 1.0.0 --min-launcher-version 1.0.0 \
  --protocol-version 1 --content-schema-version 1 \
  --generated-at 2026-11-18T00:00:00Z --expires-at 2026-12-18T00:00:00Z

# 签名与验证
Crystal.PatcherTool sign --manifest manifest.json --key private.pem --key-id release-2026
Crystal.PatcherTool verify --manifest manifest.json --public-key public.pem --key-id release-2026 \
  --channel stable --client-version 1.2.0 --launcher-version 1.2.0

# 查看差异、应用、查看状态和回滚
Crystal.PatcherTool plan --root ./client --manifest manifest.json --public-key public.pem
Crystal.PatcherTool apply --root ./client --manifest manifest.json --public-key public.pem \
  --client-version 1.2.0 --launcher-version 1.2.0
Crystal.PatcherTool state --root ./client
Crystal.PatcherTool rollback --root ./client
```

`apply` 的 `--manifest` 可以是本地路径或 HTTP(S) URL。相对下载 URL 以 manifest 所在位置为基准；本地测试可用
`--manifest-base file:///path/to/files/` 覆盖。`--dry-run` 只输出差异，不下载或修改客户端。

## 3. 启动器配置

`Mir2Config.ini` 的 `[Launcher]` 增加以下可选键。`ManifestUrl` 为空时继续使用旧 `PList.gz`：

```ini
ManifestUrl=https://patch.example.com/channels/stable/manifest.json
ManifestPublicKey=patch-public.pem
ManifestKeyId=release-2026
Channel=stable
ProtocolVersion=1
ContentSchemaVersion=1
ClientVersion=1.2.0
LauncherVersion=1.2.0
```

`ClientVersion`/`LauncherVersion` 为空时使用当前程序版本。签名更新失败会禁用本次启动并显示错误，不会静默回退
到旧清单或启动未验证客户端。`ManifestPublicKey` 的相对路径按客户端目录解析。

## 4. 本地状态

```text
.crystal-update/staging/                 下载临时目录
.crystal-update/backups/{transaction}/   被替换/删除文件备份
.crystal-update/pending.json             待提交事务，用于崩溃恢复
.crystal-update/state.json               当前与上一发布、备份和动作列表
```

扫描和差异只处理 manifest 声明的新增、替换和删除；未知本地文件（如玩家配置、截图）不会被删除。
