# 外部资源清单

> 最后更新：2026-11-09
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
| 客户端 `Data/`、`Map/`、`Sound/` | 客户端运行、图库、地图、音效 | 未准备 |
| 客户端 `.lib` 图库 | 物品、怪物、NPC、UI、特效 | 未准备 |
| 地图编辑器依赖的部件 | 地图编辑 | 已有仓库，未验证构建 |
| 支付渠道凭证 | 充值 | 后续由部署环境注入，不入库 |
| 生产域名、TLS、CDN | 发布与补丁 | 后续配置 |

## 6. 更新规则

- 更新外部仓库后，记录新 commit、大小和许可证状态。
- 不把 `.external/` 提交到仓库。
- 不把没有明确授权的素材放入发布包。
- 对外分发前，重新核对 `docs/reference/license-inventory.md`。

