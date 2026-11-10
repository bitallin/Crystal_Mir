# 内容快照、二进制导入与导出

> 最后更新：2026-11-10
> 状态：生效（M1 初版）

## 1. 组成

| 组件 | 位置 | 许可证边界 | 作用 |
| --- | --- | --- | --- |
| `Crystal.Content` | `src/Crystal.Content/` | MIT | 快照模型、canonical JSON、manifest、哈希、读取和校验 |
| `Crystal.LegacyImporter` | `tools/Crystal.LegacyImporter/` | 链接 GPL `Server.Library` | 读取旧 `Server.MirDB`、映射实体、写快照、可选写 PostgreSQL、从快照/PostgreSQL 重建并导出旧二进制格式 |
| PostgreSQL schema | `db/migrations/*.sql` | 控制面 | 内容表、控制面身份/RBAC/会话和审计表 |

控制面只能引用 `Crystal.Content` 和数据库，不引用 `Crystal.LegacyImporter` 或 `Server.Library`。
导入器按文件名顺序应用全部 migration，因此 `--dsn` 路径也会创建 Control API 所需 schema。

## 2. 命令

只生成快照：

```bash
./scripts/import-db.sh tests/fixtures/golden/Server.MirDB artifacts/content/golden
```

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
    └── respawn.jsonl
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

`RespawnIndex` 在旧数据库中不保证全局唯一，因此展平实体使用地图和序号定位；原始值保留在
`legacyRespawnIndex` 字段。

## 5. 当前边界

`Server.MirDB` 只包含核心实体。以下内容不在本次导入范围，后续由文件/资源管线处理：

- 怪物掉落：`Envir/Drop` 文本。
- NPC 脚本：`Envir/NPC` DSL。
- 配方：`Envir/Recipe`。
- Buff 定义：服务端启动时初始化。
- GT 地图运行时状态（仅保存可重建的 `legacy.gtMaps` 元数据）。
- 图库、地图文件、音效和特效资源。

这些限制写入 manifest 的 `legacy.notImported`，避免把“未导入”误判为“数据为空”。

`LegacyBinaryExporter` 现在支持三种输入：已加载的旧二进制数据库、JSONL 快照、PostgreSQL 最新
`imported` 批次。输出为当前 `Envir.Version=117` 格式。重建器通过反射恢复旧实体，解析内容引用，
并按 `manifest.legacy.order` 恢复原始列表顺序，因此黄金样本三条路径生成的文件逐字节一致。

导出反映“加载并校验后的旧库状态”：`GameShop` 中无法绑定物品的条目、运行时 GT 状态等不会
原样保留。当前 `LoadDB()` 不读取旧文件尾部的 `GTMap` 列表，因此黄金样本导出会写空列表；旧服务端
启动时会从 `MapInfo.GT/GTIndex` 重建运行时 GT 地图。完整服务启动验证仍需要 Windows 环境。

## 6. 黄金样本结果

截至 2026-11-09，对 `tests/fixtures/golden/Server.MirDB`（版本 111）的导入结果：

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
| **合计** | **9,734** |

- 数据库 SHA256：`37358b0d95eafe458b060c1db2ebc0524757fe524fb93125ccb98d9b4a8ef78b`
- `contentHash`：`7e7a6dd14c091b63eed9bb271ae5bbb6ec080d081669048beeefb6c46a373825`
- 两次独立导入的实体文件逐字节一致。
- 导出文件版本 117，大小 548,567 字节，SHA256
  `3775771c893d95f490bbb54b868650c91f7200e8d268d356a9c22d0a7e11284a`。
- 两次独立导出的文件逐字节一致；导出文件重新加载后的语义哈希与源库一致。
- 从导出文件重新生成的快照与源库快照实体文件逐字节一致，`contentHash` 仍为
  `7e7a6dd14c091b63eed9bb271ae5bbb6ec080d081669048beeefb6c46a373825`。
- 从 JSONL 快照重建并导出的文件与直接导出文件逐字节一致，SHA256 同为
  `3775771c893d95f490bbb54b868650c91f7200e8d268d356a9c22d0a7e11284a`。
- 从 PostgreSQL 最新导入批次重建并导出的文件与直接导出文件逐字节一致。
- PostgreSQL schema 与 `COPY` 导入在本地 PostgreSQL 12 验证通过，重复导入生成新的 `content_import_run`，不覆盖历史批次。

目标生产版本仍为 PostgreSQL 16+；12 仅用于当前环境验证，schema 未使用 16 专属语法。

## 7. 回滚与后续

- 快照和导入批次都是不可变的；回滚通过切换活动批次或恢复数据库备份完成。
- `LegacyBinaryExporter` 已提供旧二进制格式的确定性写入、冷回读验证和快照/PostgreSQL 重建。
- 纯控制面回滚已具备数据导出路径；后续仍需补齐资源/脚本导入、内容发布 API 和 staging 演练。
- 控制面 API 按 `import_run_id` 或未来活动发布读取实体。
