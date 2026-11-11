# 内容快照、二进制导入与导出

> 最后更新：2026-11-11
> 状态：生效（M1 初版）

## 1. 组成

| 组件 | 位置 | 许可证边界 | 作用 |
| --- | --- | --- | --- |
| `Crystal.Content` | `src/Crystal.Content/` | MIT | 快照模型、canonical JSON、manifest、哈希、读取、校验、签名和验签 |
| `Crystal.ContentTool` | `tools/Crystal.ContentTool/` | MIT | 对内容快照进行 ECDSA P-256 签名和验证，只依赖 `Crystal.Content` |
| `Crystal.LegacyImporter` | `tools/Crystal.LegacyImporter/` | 链接 GPL `Server.Library` | 读取旧 `Server.MirDB`、映射实体、写快照、可选写 PostgreSQL、从快照/PostgreSQL 重建并导出旧二进制格式 |
| PostgreSQL schema | `db/migrations/*.sql` | 控制面 | 内容表、控制面身份/RBAC/会话和审计表 |

控制面只能引用 `Crystal.Content` 和数据库，不引用 `Crystal.LegacyImporter` 或 `Server.Library`。
导入器按文件名顺序应用全部 migration，因此 `--dsn` 路径也会创建 Control API 所需 schema。

## 2. 命令

只生成快照：

```bash
./scripts/import-db.sh tests/fixtures/golden/Server.MirDB artifacts/content/golden
```

同时导入 `Envir/Drops`、`Envir/Recipe`、`Envir/NPCs` 脚本和资源元数据：

```bash
./scripts/import-db.sh tests/fixtures/golden/Server.MirDB artifacts/content/golden \
  --content-root .external/Crystal.Database/Jev
```

`--content-root` 指向旧内容根目录。掉落文件按怪物的 `DropPath` 解析；`DropPath` 为空时使用
怪物名称。未找到掉落文件不会创建占位文件。存在 `Envir/Recipe/*.txt` 时，配方按文件名顺序导入，
产出、材料、工具、成功率、金币和解锁条件均写入快照。`Envir/NPCs/**/*.txt` 按相对路径排序导入，
保留脚本文本、哈希、页面、`#INCLUDE`、`#INSERT`、`CALL` 和对应 NPC 索引，并生成页面/块/命令/按钮/跳转
AST、校验问题和稳定编译哈希；`Maps/**/*.map` 与 `Envir/Previews` 下的图片导入路径、类型、大小和 SHA256
元数据，地图还会解析格式、尺寸、可走、阻挡和门格，但不复制二进制资源。缺失目录会跳过，不阻断其他内容。

同时写入 PostgreSQL：

```bash
./scripts/import-db.sh tests/fixtures/golden/Server.MirDB artifacts/content/golden \
  --dsn 'Host=127.0.0.1;Port=5432;Database=crystal;Username=crystal;Password=...'
```

CI 使用：

```bash
./scripts/verify-content.sh
```

导出旧二进制数据库并自动回读验证：

```bash
./scripts/export-db.sh tests/fixtures/golden/Server.MirDB artifacts/legacy-export/Server.MirDB
```

等价 CLI：

```bash
$HOME/.dotnet/dotnet run --project tools/Crystal.LegacyImporter/Crystal.LegacyImporter.csproj \
  -c Release -- --db tests/fixtures/golden/Server.MirDB \
  --export artifacts/legacy-export/Server.MirDB
```

默认拒绝覆盖已有文件；需要替换时追加 `--force`。导出先写同目录临时文件，再原子替换目标。
默认会用 `LoadDB()` 重新加载导出文件，并比较实体内容、局部索引、全局计数器和各类集合数量。
仅需要生成文件时可使用 `--no-verify`，但不建议用于回滚制品。

从 JSONL 快照或 PostgreSQL 活动批次重建旧二进制数据库：

```bash
./scripts/rebuild-db.sh artifacts/content/golden artifacts/legacy-rebuild/Server.MirDB
./scripts/rebuild-db.sh --postgres \
  'Host=127.0.0.1;Port=5432;Database=crystal;Username=crystal;Password=...' \
  artifacts/legacy-rebuild/Server.MirDB
```

对应 CLI 为 `--snapshot <dir> --export <path>` 和 `--from-postgres <connection> --export <path>`。
重建会解析 `$ref`、恢复实体列表顺序、全局索引、`RespawnTick` 和 `GTMap` 元数据，然后执行与直接导出相同的冷回读校验。

### 2.1 内容签名

生成 ECDSA P-256 私钥和公钥：

```bash
openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -out release-private.pem
openssl pkey -in release-private.pem -pubout -out release-public.pem
```

签名快照并验证：

```bash
$HOME/.dotnet/dotnet run --project tools/Crystal.ContentTool/Crystal.ContentTool.csproj \
  -c Release -- sign \
  --snapshot artifacts/content/golden \
  --key release-private.pem \
  --key-id content-release-2026-01

$HOME/.dotnet/dotnet run --project tools/Crystal.ContentTool/Crystal.ContentTool.csproj \
  -c Release -- verify \
  --snapshot artifacts/content/golden \
  --public-key release-public.pem \
  --key-id content-release-2026-01
```

签名写入 `manifest.json` 的可选 `signature` 对象，覆盖 canonical manifest 和签名元数据；实体文件 SHA256、
`contentHash` 和旧二进制重建结果不受影响。私钥不得进入仓库、数据库、日志或内容包，生产发布应使用
KMS/Vault 或硬件签名服务。算法、密钥轮换和重新评估条件见 `adr/0005-content-signature-format.md`。

## 3. 快照格式

```text
snapshot/
├── manifest.json
├── summary.json
└── entities/
    ├── item.jsonl
    ├── monster.jsonl
    ├── map.jsonl
    ├── npc.jsonl
    ├── quest.jsonl
    ├── magic.jsonl
    ├── gameshop.jsonl
    ├── conquest.jsonl
    ├── dragon.jsonl
    ├── respawn.jsonl
    ├── buff.jsonl          # 启动时 BuffInfo.Load() 生成的内建定义
    ├── drop.jsonl          # 仅使用 --content-root 且找到掉落文件时生成
    ├── recipe.jsonl        # 仅使用 --content-root 且存在 Envir/Recipe 文件时生成
    ├── script.jsonl        # 仅使用 --content-root 且存在 Envir/NPCs 脚本时生成
    └── asset.jsonl         # 仅使用 --content-root 且存在 Maps/Envir/Previews 资源时生成
```

- 每个实体一行，按 `localIndex` 排序。
- 顶层字段使用 camelCase；`data` 保留旧代码字段名（PascalCase），便于逐字段对照和回滚。
- 对象键递归排序；同一输入重复导入产生相同实体文件和相同 `contentHash`。
- `manifest.json` 记录每个实体文件的 SHA256、字节数、实体数和来源数据库 SHA256。
- `manifest.legacy` 额外保存旧库索引、实体列表顺序、`RespawnTick` 和 `GTMap` 元数据，供确定性重建使用。
- 读取时校验文件哈希、字节数、manifest 计数和 `contentHash`。

实体结构：

```json
{
  "entityType": "item",
  "localIndex": 1001,
  "contentKey": "item.1001",
  "schemaVersion": 1,
  "data": {
    "Index": 1001,
    "Name": "..."
  }
}
```

引用结构：

```json
{"$ref": "item", "index": 1001, "key": "item.1001"}
```

引用避免把物品、怪物、地图等完整对象重复嵌套，并打断 `MapInfo -> SafeZoneInfo.Info -> MapInfo` 这类循环。

## 4. 实体映射

| 实体 | 来源 | `localIndex` | `contentKey` |
| --- | --- | --- | --- |
| `item` | `Envir.ItemInfoList` | `Index` | `item.<Index>` |
| `monster` | `Envir.MonsterInfoList` | `Index` | `monster.<Index>` |
| `map` | `Envir.MapInfoList` | `Index` | `map.<Index>` |
| `npc` | `Envir.NPCInfoList` | `Index` | `npc.<Index>` |
| `quest` | `Envir.QuestInfoList` | `Index` | `quest.<Index>` |
| `magic` | `Envir.MagicInfoList` | `Spell` | `magic.<Spell>` |
| `gameshop` | `Envir.GameShopList` | `GIndex` | `gameshop.<GIndex>` |
| `conquest` | `Envir.ConquestInfoList` | `Index` | `conquest.<Index>` |
| `dragon` | `Envir.DragonInfo` | `1` | `dragon.default` |
| `respawn` | `MapInfo.Respawns` 展平 | `mapIndex * 1000000 + ordinal + 1` | `respawn.map<mapIndex>.<ordinal>` |
| `buff` | `BuffInfo.Load()` | `BuffType` 枚举值 | `buff.<Type>` |
| `drop` | `Envir/Drops` 文本 | 怪物 `Index` | `drop.monster.<Index>` |
| `recipe` | `Envir/Recipe/*.txt` | 文件名排序序号 | `recipe.<文件名>` |
| `script` | `Envir/NPCs/**/*.txt` | 相对路径排序序号 | `script.<规范化路径>` |
| `asset` | `Maps/**/*.map`、`Envir/Previews` 图片 | 相对路径排序序号 | `asset.<规范化路径>` |

`RespawnIndex` 在旧数据库中不保证全局唯一，因此展平实体使用地图和序号定位；原始值保留在
`legacyRespawnIndex` 字段。

掉落实体保存怪物索引、名称、配置路径、相对文件名和已解析的 `DropInfo` 列表。物品引用继续使用
`{"$ref":"item","index":...,"key":...}`；分组掉落保留在 `GroupedDrop` 中。`drop` 不写回旧二进制
数据库，快照重建时会被忽略，因此二进制回滚仍只依赖 `Server.MirDB` 中的实体。

配方实体保存文件名、产出、材料、工具、成功率、金币、职业/性别/等级/Flag/Quest 条件。物品引用使用
`itemIndex` 和物品名称；Control API 会再解析为 `item.<Index>` 内容键。配方同样不属于
`Server.MirDB`，二进制重建时忽略。

Buff 实体来自服务端启动时的 `BuffInfo.Load()`，保存类型、叠加方式、属性、图标和可见性。它们不由旧
数据库保存，也不参与旧二进制重建，但作为运行时内容进入快照、PostgreSQL 和控制面只读查询。

脚本实体按文件去重，保存规范化路径、UTF-8 文本（换行统一为 LF）、SHA256、大小、行数、页面列表、
包含/插入/调用关系、引用该文件的 NPC 索引、解析 AST 和校验结果。校验覆盖重复页面、缺失跳转目标、未知
指令和空页面；`compiledSha256` 对 AST 做稳定摘要。文件名按大小写不敏感匹配旧 `NPCInfo.FileName`，
Control API 通过 `fileNameKey` 建立 NPC→脚本关系。脚本和资源均不参与旧二进制重建。

资源实体是元数据而不是二进制内容：每条记录保存相对路径、文件名、扩展名、资源类型、大小和 SHA256。
当前覆盖 `Maps` 下 `.map` 文件与 `Envir/Previews` 下的图片。地图额外保存格式编号/名称、宽高、格子数、
可走格、阻挡格和门格；支持旧地图格式 0/1/2/3/4/5/6/7/100。Control API 可通过资源文件端点下载原始文件，
下载前校验配置根目录、路径穿越、大小和 SHA256；图库、音效、特效和客户端补丁仍需后续资源服务接入。

## 5. 当前边界

`Server.MirDB` 只包含核心实体。掉落、配方、NPC 脚本和资源元数据可通过 `--content-root` 导入，
Buff 由启动代码生成并始终导入。以下内容仍不在本次导入范围，后续由文件/资源管线处理：

- 图库、音效、特效、客户端补丁等未位于 `Maps`/`Envir/Previews` 的资源二进制。
- GT 地图运行时状态（仅保存可重建的 `legacy.gtMaps` 元数据）。
- NPC 脚本 DSL 在旧运行时中的端到端执行结果；M1 已保存 AST、编译哈希和静态校验，但不执行脚本。

未提供 `--content-root` 时，限制仍写入 manifest 的 `legacy.notImported`，避免把“未导入”误判为
“数据为空”；提供后写入掉落/配方/脚本/资源的实际文件数、解析条目数和实体数。Buff 来源和实体数始终
写入 `legacy.buffs`。

`LegacyBinaryExporter` 现在支持三种输入：已加载的旧二进制数据库、JSONL 快照、PostgreSQL 最新
`imported` 批次。输出为当前 `Envir.Version=117` 格式。重建器通过反射恢复旧实体，解析内容引用，
并按 `manifest.legacy.order` 恢复原始列表顺序，因此黄金样本三条路径生成的文件逐字节一致。

导出反映“加载并校验后的旧库状态”：`GameShop` 中无法绑定物品的条目、运行时 GT 状态等不会
原样保留。当前 `LoadDB()` 不读取旧文件尾部的 `GTMap` 列表，因此黄金样本导出会写空列表；旧服务端
启动时会从 `MapInfo.GT/GTIndex` 重建运行时 GT 地图。完整服务启动验证仍需要 Windows 环境。

## 6. 黄金样本结果

截至 2026-11-11，对 `tests/fixtures/golden/Server.MirDB`（版本 111）的导入结果：

| 实体 | 数量 |
| --- | ---: |
| item | 1,628 |
| monster | 555 |
| map | 463 |
| npc | 375 |
| quest | 154 |
| magic | 111 |
| gameshop | 105 |
| conquest | 1 |
| dragon | 1 |
| respawn | 6,341 |
| buff | 59 |
| **合计** | **9,793** |

- 数据库 SHA256：`37358b0d95eafe458b060c1db2ebc0524757fe524fb93125ccb98d9b4a8ef78b`
- `contentHash`：`1c6b6134ebc293e67e22710e8d45130d5e900ff2b5f3b5f07012835ee3e49e1c`
- 使用 `tests/fixtures/content` 作为 `--content-root` 时额外导入 1 个掉落、1 个配方、
  1 个 NPC 脚本和 1 个资源元数据，测试批次合计 9,797 个实体，`contentHash` 为
  `1b93509c81d7c4839f009411d46c3e42f3596c9e7bab368cf711d2dd8ec22291`。
- 测试脚本编译为 11 个页面、26 条命令、0 error、1 warning，`compiledSha256` 为
  `c0e3f43676a6d5bd978d57c2586636efa404e17fd84014102a3d7d377ecdf8ff`。
- 测试地图资源 `Maps/0.map` 为 10×10 v0、1,252 字节、99 个可走格、1 个阻挡格、1 个门格，
  SHA256 为 `3ab00f38634c7c9c839a3085819a1807b14d0e00841a833a36e147c97aff237f`。
- Jev 全量 1,698 张地图解析成功，`bad=0`；格式分布为 `0:237`、`1:1441`、`3:1`、`5:6`、`100:13`。
- 两次独立导入的实体文件逐字节一致。
- 导出文件版本 117，大小 548,567 字节，SHA256
  `3775771c893d95f490bbb54b868650c91f7200e8d268d356a9c22d0a7e11284a`。
- 两次独立导出的文件逐字节一致；导出文件重新加载后的语义哈希与源库一致。
- 从导出文件重新生成的快照与源库快照实体文件逐字节一致，`contentHash` 仍为
  `1c6b6134ebc293e67e22710e8d45130d5e900ff2b5f3b5f07012835ee3e49e1c`。
- 从 JSONL 快照重建并导出的文件与直接导出文件逐字节一致，SHA256 同为
  `3775771c893d95f490bbb54b868650c91f7200e8d268d356a9c22d0a7e11284a`。
- 从 PostgreSQL 最新导入批次重建并导出的文件与直接导出文件逐字节一致。
- PostgreSQL schema 与 `COPY` 导入在本地 PostgreSQL 12 验证通过，重复导入生成新的 `content_import_run`，不覆盖历史批次。

目标生产版本仍为 PostgreSQL 16+；12 仅用于当前环境验证，schema 未使用 16 专属语法。

## 7. 回滚与后续

- 快照和导入批次都是不可变的；回滚通过切换活动批次或恢复数据库备份完成。
- `LegacyBinaryExporter` 已提供旧二进制格式的确定性写入、冷回读验证和快照/PostgreSQL 重建。
- 纯控制面回滚已具备数据导出路径；脚本 AST/校验、资源完整性下载已进入只读 API；后续仍需补齐
  内容发布 API、其余资源二进制和 staging 演练。
- 控制面 API 按 `import_run_id` 或未来活动发布读取实体。
