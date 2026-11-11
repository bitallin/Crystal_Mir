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

