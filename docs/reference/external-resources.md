# 外部资源清单

> 最后更新：2026-11-19
> 状态：生效

## 1. 获取方式

外部资源统一放在仓库根目录的 `.external/`，该目录已被 `.gitignore` 忽略，不随仓库提交。

```bash
./scripts/fetch-external.sh
```

脚本会克隆或更新以下仓库，并输出当前 commit。更新外部资源后，必须同步更新本文档和 `docs/PROGRESS.md`。

## 2. 外部仓库

| 资源 | 地址 | Commit | 大小 | 许可证 | 用途 | 本地路径 |
| --- | --- | --- | --- | --- | --- | --- |
| Crystal.Database | `https://github.com/Suprcode/Crystal.Database` | `a19f6dca8f5e238d4ed79801820777abbf0a9ca4` | 3.4 GB | Unlicense（public domain） | 标准数据库、服务端配置、Envir 脚本、地图 | `.external/Crystal.Database` |
| Crystal.MapEditor | `https://github.com/Suprcode/Crystal.MapEditor` | `71f653f5a6d48c24246583402bec9b7a0f57c47c` | 33 MB | 未发现明确许可证文件 | 地图编辑和地图格式参考 | `.external/Crystal.MapEditor` |

Crystal.MapEditor 在分发前必须补充许可证确认；当前只作为本地参考和集成对象，不进入发布包。

### 2.1 Jev 客户端素材（本地研究输入）

| 资源 | 地址 | SHA256 | 大小 | 本地路径 |
| --- | --- | --- | --- | --- |
| Client With Monk | `https://mirfiles.co.uk/resources/mir2/users/Jev/Mir%202/Client%20With%20Monk.rar` | `9534eb3cfed4b31de986bbff663e7a69bfc97428fe0f12e40bdf1c8f1ff6ae98` | 7,543,876,769 字节 | `.external/jev-client/client-with-monk.rar` |

解压后的客户端根目录为 `.external/jev-client/client/Client`，不提交仓库：

- `Data/` 约 8.2 GB，含 2,304 个 `.lib`，覆盖角色、怪物、NPC、UI、特效和地图图库。
- `Sound/` 约 145 MB，624 个文件；解压工具报告 `160-8.wav` checksum error，但该文件仍可按 44.1 kHz、16-bit、单声道 WAV 读取，未做音频内容逐字节比对。
- `Data/Map/` 保留客户端原始地图图库；`Map/` 软链到 `.external/jev-client/supported-maps`。
- 地图图库由 `src/Crystal.WebAssets/MapLibraryCatalog.cs` 映射；Linux 下新增大小写不敏感解析以兼容 `SmTiles.Lib`/`Smtiles.lib`、`Objects27.Lib`/`Objects27.lib` 等真实文件名。

Jev 活跃地图共 496 张。当前支持 479 张；排除 17 张：`0`、`2`、`DogYoArena2`、`NAMMAN`、`NAMMAN2`、`NAMMAND_1`、`NAMMAND_2`、`bonguk1`～`bonguk5`、`hyunwol2`、`hyunwol3`、`n0`、`ok10`、`zl`。其中 `0`、`2` 缺 `WemadeMir3/Snow/Object1c`（地图库 ID `257`）；`n0` 缺 `138`（`ShandaMir2/Objects19`）；其余依赖 `MapLibraryCatalog` 未覆盖的 Jev 私有高编号图库。`Maps&Tiles.zip` 仅提供 `ShandaMir3/Object1c`，不能替代 Wemade 雪地图库；根目录 `Object1c.Lib` 也不是同一状态路径。缺失图库不得用猜测文件替代；未覆盖地图在运行时明确使用地形占位。

## 3. Crystal.Database 内容

每个数据库变体目录通常包含：

```text
<variant>/
├── Server.MirDB      核心数据库
├── Configs/          服务端配置
├── Envir/            NPC、掉落、任务、配方、预览等脚本和数据
└── Maps/             .map 地图文件
```

以 `Jev` 为例：

| 目录 | 大小 | 内容 |
| --- | --- | --- |
| `Configs/` | 160 KB | Setup、经验、职业、系统配置 |
| `Envir/` | 229 MB | NPC、掉落、任务、配方、预览、系统脚本 |
| `Maps/` | 882 MB | 1705 个 `.map` 文件 |
| `Server.MirDB` | 528 KB | 核心数据库 |

`Envir/Previews` 包含地图预览，可用于后续 Web 地图浏览和缩略图。

`Maps/*.map` 已用 Jev 全量样本验证 `MapAssetParser`：1,698 张地图解析成功，格式分布为
`0:237`、`1:1441`、`3:1`、`5:6`、`100:13`，无失败样本。解析结果只保存尺寸、可走/阻挡/门格和
哈希元数据；原始地图文件不进入仓库，由部署环境通过 `CRYSTAL_CONTROL_ASSET_ROOT` 提供下载。

## 4. 数据库变体

详见 `docs/reference/db-inventory.md`。

| 变体 | 状态 | 说明 |
| --- | --- | --- |
| Jev | 可用，黄金样本 | 版本 111，内容较新，已提交 `tests/fixtures/golden/Server.MirDB` |
| Daneo1989 | 可用 | 版本 83，旧版本兼容性样本 |
| cjlaaa | 可用 | 版本 105，旧版本兼容性样本 |
| NightWolf | 不可用 | 头部版本 34，低于当前最低支持版本 60 |

## 5. 仍缺资源

| 资源 | 用途 | 状态 |
| --- | --- | --- |
| 客户端 `Data/`、`Map/`、`Sound/` | 客户端运行、图库、地图、音效 | 已从 Jev 客户端包准备到 `.external/jev-client`，不入库 |
| 未被 `MapLibraryCatalog` 覆盖的 17 张地图图库 | 地图完整覆盖 | `0`、`2` 缺 `257`，`n0` 缺 `138`，其余为 Jev 私有高编号图库；当前明确排除 |
| 真实音频转码产物 | 音效播放 | 尚未实现/验证 |
| 地图编辑器依赖的部件 | 地图编辑 | 已有仓库，未验证构建 |
| 支付渠道凭证 | 充值 | 后续由部署环境注入，不入库 |
| 生产域名、TLS、CDN | 发布与补丁 | 后续配置 |

## 6. 更新规则

- 更新外部仓库后，记录新 commit、大小和许可证状态。
- 不把 `.external/` 提交到仓库。
- 不把没有明确授权的素材放入发布包。
- 对外分发前，重新核对 `docs/reference/license-inventory.md`。

