# 测试固定数据

## golden/Server.MirDB

- 来源：`Crystal.Database/Jev/Server.MirDB`
- 上游：`https://github.com/Suprcode/Crystal.Database`
- Commit：`a19f6dca8f5e238d4ed79801820777abbf0a9ca4`
- 许可证：Unlicense（public domain）
- SHA256：`37358b0d95eafe458b060c1db2ebc0524757fe524fb93125ccb98d9b4a8ef78b`
- 数据库版本：111

用途：旧数据库加载、导入器、内容快照和实体计数回归测试。不要直接修改该文件；需要变更时新增样本并记录来源和哈希。

检查命令：

```bash
./scripts/inspect-db.sh
```

## content/Envir/Drops

- 内容根最小样本，用于验证 `--content-root` 掉落导入。
- `Envir/Drops/Misc/ArcherGuard.txt` 对应黄金库怪物 1 的 `DropPath`。
- 条目 `1/1 GreenPoison` 解析为物品 710，用于快照和 Control API 关系测试。
- 来源同 `Crystal.Database/Jev`，许可证 Unlicense。

## content/Envir/Recipe

- 内容根最小样本，用于验证 `--content-root` 配方导入。
- `Envir/Recipe/GreenPoison.txt` 以黄金库物品 710 为产出、材料和工具，覆盖成功率、金币和条件解析。
- 来源同 `Crystal.Database/Jev`，许可证 Unlicense。



## content/Envir/NPCs

- 内容根最小样本，用于验证 NPC 脚本导入和地图 NPC 关系。
- `Envir/NPCs/BichonProvince/BorderVillage/Transport.txt` 对应黄金库 NPC 1 的 `FileName`，覆盖页面解析和 NPC→脚本关系；实现同时记录 `#INCLUDE`、`#INSERT` 与 `CALL` 元数据。
- 来源同 `Crystal.Database/Jev`，许可证 Unlicense。

## content/Maps

- 内容根最小样本，用于验证资源元数据、地图尺寸和阻挡解析。
- `Maps/0.map` 是 10×10 的 v0 格式二进制地图，1 个阻挡格、1 个门格；对应黄金库地图 1 的 `FileName=0`，用于资源下载、哈希校验和地图关系测试。
- 文件由测试工程生成的确定性样本，不包含上游地图素材。
- 来源标记同 `Crystal.Database/Jev`，许可证 Unlicense。
