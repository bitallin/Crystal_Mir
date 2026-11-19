# 浏览器客户端本地开发

> 最后更新：2026-11-19
> 状态：生效（W0/W1，W2 实施中）

## 前置条件

- .NET 8 SDK；默认脚本使用 `$HOME/.dotnet/dotnet`，可用 `DOTNET` 覆盖。
- Node.js 24 与 npm 10+；`web-client/package-lock.json` 是唯一依赖锁文件。
- 可用的区服 TCP 地址，默认 `127.0.0.1:7000`。
- 外部客户端素材根目录：`Client/Data`、`Client/Map`、`Client/Sound`。仓库不包含这些运行素材。

## 安装与构建

```bash
cd web-client
npm ci
npm run typecheck
npm test
npm run build
```

`npm run build` 会生成 `dist/client-build.json`。读取其中的 `clientBuildHash`，后续三个位置必须使用同一个值：

```bash
HASH="$(node -e 'const fs=require("node:fs");console.log(JSON.parse(fs.readFileSync("web-client/dist/client-build.json","utf8")).clientBuildHash)')"
```

也可以从仓库根目录运行：

```bash
./scripts/build-web-client.sh
```

## 本地运行

```bash
# 终端 1：区服（沿用现有 ZoneServer / legacy 配置）

# 终端 2：网关
CRYSTAL_WEB_ZONE_TCP=127.0.0.1:7000 \
CRYSTAL_WEB_ALLOWED_ORIGINS=http://127.0.0.1:5173 \
CRYSTAL_WEB_CLIENT_BUILD_HASH="$HASH" \
dotnet run --project src/Crystal.Web.Gateway

# 终端 3：Web 客户端
cd web-client
npm run dev
```

Vite 开发服务器将 `/ws`、`/api/web` 和 `/health` 代理到网关，并从 `CRYSTAL_WEB_ASSET_ROOT` 提供开发资源。相对路径按 `web-client/` 工作目录解析，默认是 `../artifacts/web-assets`；也可传绝对路径。浏览器开发入口为 `http://127.0.0.1:5173/game/`。

## 区服版本校验

区服默认 `CheckVersion=true`。开发环境可以临时设 `CheckVersion=false`；需要验证真实门禁时，把 Web 构建哈希加入 `Configs/Setup.ini`：

```ini
[General]
CheckVersion=True
VersionPath=./Mir2.Exe
VersionHashValues=sha256:replace-with-client-build-hash
```

`VersionPath` 的旧文件 MD5 白名单保持兼容，`VersionHashValues` 支持逗号或分号分隔的多个字面量。ZoneServer 也可在启动时追加：

```bash
CRYSTAL_ZONE_VERSION_HASH_VALUES="$HASH" ./crystal-zone
```

客户端发送的哈希是 `clientBuildHash` 字符串的 UTF-8 字节，不是十六进制解码后的 32 字节；不要把 MD5 文件和 Web 哈希混用。

## 资源编译

```bash
dotnet run --project tools/Crystal.WebAssetCompiler -- \
  --client-root /path/to/Client \
  --out artifacts/web-assets/dev \
  --version dev \
  --build-hash "$HASH" \
  --all-maps \
  --map-libraries
```

- `--build-hash` 必填，缺失直接失败。
- `--all-maps` 递归编译 `Map/` 下全部 `.map`，扩展名大小写不敏感；同名地图 ID 仅保留第一个。也可重复 `--map <path>` 选择单图。
- `--map-libraries` 会把地图引用的图库一起编译；引用但缺失时失败。
- `--lib <path>` 可编译额外图库。
- 产物通过静态服务器暴露为 `/assets/<version>/`，素材和生成物不提交仓库。

单图垂直切片示例：

```bash
dotnet run --project tools/Crystal.WebAssetCompiler -- \
  --client-root /path/to/Client \
  --out artifacts/web-assets/dev \
  --version dev \
  --build-hash "$HASH" \
  --map /path/to/Client/Map/0.map \
  --map-libraries
```

## 网关配置

常用环境变量：

| 变量 | 默认值 | 说明 |
| --- | --- | --- |
| `CRYSTAL_WEB_ZONE_ID` | `default` | 当前区服 ID |
| `CRYSTAL_WEB_ZONE_TCP` | `127.0.0.1:7000` | 区服 TCP 上游 |
| `CRYSTAL_WEB_ALLOWED_ORIGINS` | 本地 Vite 两个 Origin | 逗号/分号分隔的 Origin 白名单 |
| `CRYSTAL_WEB_MAX_FRAME_BYTES` | `262144` | 单 WebSocket 消息上限 |
| `CRYSTAL_WEB_MAX_FRAMES_PER_SECOND` | `180` | 客户端消息速率上限 |
| `CRYSTAL_WEB_MAX_BYTES_PER_SECOND` | `4194304` | 客户端字节速率上限 |
| `CRYSTAL_WEB_MAX_CONNECTIONS` | `256` | 并发连接上限 |
| `CRYSTAL_WEB_IDLE_TIMEOUT_SECONDS` | `30` | 空闲超时 |
| `CRYSTAL_WEB_ALLOW_MISSING_ORIGIN` | `false` | 仅非浏览器测试客户端才可开启 |
| `CRYSTAL_WEB_ASSET_VERSION` | `dev` | bootstrap 返回的资源版本 |
| `CRYSTAL_WEB_ASSET_MANIFEST_URL` | `/assets/dev/manifest.json` | 资源 manifest URL |
| `CRYSTAL_WEB_CLIENT_BUILD_HASH` | `sha256:dev` | 必须与 Web 构建/区服白名单一致 |

Gateway 只接受 `crystal-v1` 子协议和二进制消息；生产必须使用 `wss://`，由 Nginx 终止 TLS。
`Production` 环境会拒绝 `sha256:dev`、`assetVersion=dev` 和通配 Origin；部署时必须传入真实 Web 构建哈希和不可变资源版本。
Compose 的 `web` profile 默认使用 `Development`，便于本地带占位版本启动；正式环境设置 `CRYSTAL_WEB_ENVIRONMENT=Production`，并同时提供真实构建哈希、不可变资源版本和 HTTPS/WSS 入口。

## 测试

```bash
./scripts/test.sh
dotnet run --project Tests/WebGateway
dotnet run --project Tests/WebAssets
dotnet run --project Tests/ZoneVersionGate
./scripts/test-browser-client.sh
./scripts/test-web-real-maps.sh
./scripts/test-web-version-gate.sh
```

`test-browser-client.sh` 会构建 Web 客户端、生成临时地图资源、启动假区服并用 Playwright Chromium 验证桌面、移动横屏和竖屏门禁。需要 Python `aiohttp`、`playwright` 和 Chromium；依赖见 `scripts/requirements-browser-test.txt`。

`test-web-real-maps.sh` 默认读取 `.external/Crystal.Database/Jev/Maps`，编译顶层活跃地图，并用真实 V1、V100 地图各跑一次浏览器冒烟。可用 `CRYSTAL_CLIENT_MAP_ROOT` 指定其他真实地图根目录，用 `CRYSTAL_WEB_REAL_MAP_OUT` 指定产物目录，或 `CRYSTAL_SKIP_WEB_BUILD=1` 跳过重复 Web 构建。缺少 `.lib` 时只产生 `Map library ... placeholder terrain` 告警，这是当前预期；准备 `Client/Data` 后应改用 `--map-libraries` 并消除该告警。

`test-web-version-gate.sh` 使用临时内容根开启 `CheckVersion=True`，启动真实 ZoneServer 和 Gateway，经 `crystal-v1` WebSocket 验证当前 `clientBuildHash` 被接受、错误哈希被拒绝。脚本会在两次探测间等待 6 秒，因为 legacy 区服对同一 IP 有 5 秒建连冷却。

真实浏览器矩阵、iPhone/Android 真机、Safari、音效和真实 `.lib` 图集仍需在具备外部素材和设备的环境执行，结果写入 `docs/PROGRESS.md`。
