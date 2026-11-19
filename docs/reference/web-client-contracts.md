# Web 客户端契约

> 最后更新：2026-11-19
> 状态：生效（W0/W1 契约，W2 继续补齐）

## 1. Bootstrap

`GET /api/web/bootstrap` 返回：

```json
{
  "protocol": "crystal-v1",
  "zoneId": "default",
  "wsUrl": "/ws/game?zone=default",
  "assetVersion": "dev",
  "assetManifestUrl": "/assets/dev/manifest.json",
  "clientBuildHash": "sha256:83d8e0e11e9d1adb34fbd332aeaeecc611c111cba0231342ea876e5ebf8c10e2"
}
```

- `protocol` 必须等于 `crystal-v1`，否则客户端阻断。
- `zoneId` 是网关当前唯一区服；`wsUrl` 已包含区服路由参数。
- 客户端必须使用返回的 `assetManifestUrl` 和 `clientBuildHash`，不得自行拼接未经验证的资源路径或构建身份。
- 客户端把 `clientBuildHash` 按 UTF-8 编码后写入 `ClientVersion.VersionHash`。

## 2. WebSocket

- 路径：`/ws/game?zone=<id>`
- 子协议：`crystal-v1`，握手缺少该子协议时返回 HTTP 426。
- 消息类型：只允许二进制；文本消息以 `1003` 关闭。
- 单 WebSocket 消息上限：256 KiB；超过时以 `1009` 关闭。消息内可以包含多个游戏包。
- 游戏包长度前缀：无符号 16 位小端，单个包总长不超过 65,535 字节；不要把它与 WebSocket 消息上限混用。
- 限流：默认 180 帧/秒、4 MiB/秒；超过时以 `1008` 关闭。
- 空闲：默认 30 秒无双向流量时以 `1008` 关闭。
- 上游区服不可用：以 `1013` 关闭。
- 正常关闭：`1000`。
- 客户端本地关键包解码失败：浏览器以 `4002` 关闭，进入阻断错误页并提供重试入口。
- 网关只转发字节，不改变 `Packet` 包体和业务语义。

Origin 默认必须存在且匹配配置；只有显式设置 `CRYSTAL_WEB_ALLOW_MISSING_ORIGIN=true` 才允许缺失 Origin。首版固定单区服，未知 `zone` 返回 HTTP 404。

## 3. 资源 manifest

`/assets/<version>/manifest.json` 的实际字段：

```json
{
  "schemaVersion": 1,
  "assetVersion": "dev",
  "clientBuildHash": "sha256:...",
  "libraries": {
    "Hero": {
      "sourceHash": "sha256:...",
      "framesUrl": "lib/Hero/frames.json",
      "pages": ["lib/Hero/atlas-0.png"],
      "maskPages": ["lib/Hero/mask-0.png"],
      "hash": "sha256:..."
    }
  },
  "maps": {
    "0": {
      "sourceHash": "sha256:...",
      "format": 0,
      "width": 10,
      "height": 10,
      "cellOrder": "x-major",
      "cellStride": 32,
      "metaUrl": "map/0/meta.json",
      "cellsUrl": "map/0/cells.bin",
      "libraryIds": [0, 1, 2],
      "hash": "sha256:..."
    }
  },
  "mapLibraries": {
    "0": "maplib-0",
    "1": "maplib-1",
    "2": "maplib-2"
  }
}
```

`map/<id>/meta.json` 不保存自身 `hash`，实际字段为：

```json
{
  "schemaVersion": 1,
  "file": "0",
  "sourceHash": "sha256:...",
  "format": 0,
  "width": 10,
  "height": 10,
  "cellOrder": "x-major",
  "cellStride": 32,
  "cellsUrl": "map/0/cells.bin",
  "libraries": [0, 1, 2]
}
```

`cells.bin` 为小端、固定 32 字节/格、`x-major` 顺序，字段顺序由 `Crystal.WebAssets.MapReader.WriteCells` 唯一定义。运行时只接受 manifest 中的相对 URL 和已知布局；地图名必须经过路径规范化与 manifest 白名单。

`lib/<id>/frames.json` 包含 `version`、`pages`、`maskPages`、`images` 和 `frames`。`images[]` 关键字段为 `index`、`page`、`x`、`y`、`width`、`height`、`offsetX`、`offsetY`、`shadowX`、`shadowY`、`shadow`、`mask`、`maskPage`、`maskX`、`maskY`、`maskWidth`、`maskHeight`、`maskOffsetX`、`maskOffsetY`。图集页面格式为 PNG。

## 4. 构建哈希

`web-client/scripts/client-build-hash.mjs` 对 `dist/` 中除 `client-build.json` 外的文件按相对路径排序，以显式小端长度编码计算 SHA256，输出：

```json
{
  "schemaVersion": 1,
  "clientBuildHash": "sha256:...",
  "fileCount": 12
}
```

同一值必须同时用于 Gateway、WebAssetCompiler `--build-hash` 和区服版本白名单。资源编译器缺少该值时必须失败，不能用资源文件哈希替代客户端构建身份。

## 5. 协议编解码

每个 MVP 包实现：

```text
id: number
decode(view: DataView): Packet
encode(packet: Packet): Uint8Array
```

注册表按包号查表，不使用运行时反射。C# 样本工具从 `Shared` 的包类生成二进制 fixture；TypeScript 测试必须对每个 fixture 完成 decode/encode 回放并逐字节比较，覆盖空数组、最大字符串、分片帧和连续多包。

未知包号只记录采样遥测并忽略；关键包解码失败关闭会话。不能为了兼容未知包而跳过长度校验或破坏后续帧边界。

## 6. 版本兼容

- `assetVersion`、`clientBuildHash` 与区服内容版本按兼容组合发布。
- 区服版本白名单支持两种来源：`VersionPath` 指向旧 Windows 客户端文件的 MD5，以及 `VersionHashValues` 中的字面量（例如 Web 构建 SHA256）。
- `VersionHashValues` 支持逗号或分号分隔；ZoneServer 还支持环境变量 `CRYSTAL_ZONE_VERSION_HASH_VALUES` 追加值。
- 版本不匹配时区服返回 `ClientVersion.Result=0`，客户端阻断进入，不静默加载旧资源。
