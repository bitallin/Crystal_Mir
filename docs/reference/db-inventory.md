# 数据库清单

> 最后更新：2026-11-09
> 状态：生成结果

## 1. 检查工具

```bash
./scripts/inspect-db.sh
./scripts/inspect-all-db.sh
```

检查器使用现有 `Envir.Edit.LoadDB()` 读取数据库，输出版本、索引和实体数量。它在临时目录中运行，不会污染仓库。

## 2. 结果

| 变体 | 头部版本 | 加载 | 大小 | SHA256 前缀 | 地图 | 物品 | 怪物 | 技能 | NPC | 任务 | 商城 | 攻城 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Jev | 111 | 成功 | 540,512 | `37358b0d` | 463 | 1,628 | 555 | 111 | 375 | 154 | 105 | 1 |
| Daneo1989 | 83 | 成功 | 472,269 | `6781323b` | 400 | 1,348 | 506 | 111 | 293 | 157 | 106 | 0 |
| cjlaaa | 105 | 成功 | 452,106 | `ace4fa6a` | 389 | 1,349 | 544 | 111 | 309 | 157 | 82 | 0 |
| NightWolf | 34 | 失败 | 223,823 | `f7213878` | - | - | - | - | - | - | - | - |

说明：

- `NightWolf` 头部版本 34，低于当前 `Envir.MinVersion=60`，不能由当前引擎加载。
- 数量为加载后的列表计数，不等同于最大索引；检查器同时输出 `indices`。
- 配方、Buff、GT 地图在部分旧库中为 0，可能是版本未包含或没有数据，不代表引擎不支持。

## 3. 黄金样本

黄金样本固定为：

```text
tests/fixtures/golden/Server.MirDB
SHA256: 37358b0d95eafe458b060c1db2ebc0524757fe524fb93125ccb98d9b4a8ef78b
```

来源：`Crystal.Database/Jev/Server.MirDB`，commit `a19f6dca8f5e238d4ed79801820777abbf0a9ca4`。

用途：

- 旧数据库导入器回归测试。
- 内容快照编译和实体计数对比。
- CI 的数据库加载冒烟测试。

## 4. 内容导入结果

```bash
./scripts/import-db.sh tests/fixtures/golden/Server.MirDB artifacts/content/golden
```

黄金样本导入 9,734 个实体：物品 1,628、怪物 555、地图 463、NPC 375、任务 154、技能 111、
商城 105、攻城 1、龙 1、刷怪点 6,341。`contentHash` 为
`7e7a6dd14c091b63eed9bb271ae5bbb6ec080d081669048beeefb6c46a373825`。

映射规则和限制见 `content-import.md`。

