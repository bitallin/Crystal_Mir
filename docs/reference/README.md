# 参考手册索引

> 最后更新：2026-11-10
> 状态：生效

本目录存放由代码、schema 或工具生成的参考文档。原则是“可再生成”，不手工维护易过期的字段清单。

| 文档 | 内容 | 生成方式 | 状态 |
| --- | --- | --- | --- |
| `content-schema.md` | 内容实体、字段、只读属性 | 反射 `Server.Library` + `Shared` 生成 | 生效（生成） |
| `packet-catalog.md` | 客户端/服务端包索引和字段 | 反射 `Shared` 生成 | 生效（生成） |
| `config-keys.md` | 服务端配置项和默认值 | 反射 `Server.Settings` 生成 | 生效（生成） |
| `content-import.md` | 快照格式、实体映射、二进制导入/导出、PostgreSQL schema、黄金样本结果 | 手工维护，引用导入器输出 | 生效 |
| `legacy-db-mapping.md` | `Server.MirDB` 字段到新 schema 的逐字段映射 | 从迁移工具生成 | 待生成（初版见 `content-import.md`） |
| `api-conventions.md` | REST/gRPC 命名、错误码、分页、幂等 | 手工维护 | 生效（M1 初版） |
| `control-api.md` | Control API 配置、接口、认证边界和限制 | 手工维护 | 生效（M1 认证、账号生命周期与只读初版） |
| `glossary.md` | 领域术语中英文对照 | 手工维护 | 待编写 |
| `external-resources.md` | 外部仓库、commit、资源和缺口 | 手工维护 | 生效 |
| `db-inventory.md` | 标准数据库版本、实体数量和黄金样本 | 检查器生成 | 生效 |
| `license-inventory.md` | 代码、外部资源和素材的许可证状态 | 手工维护 | 生效 |

生成文档必须标注生成命令、输入版本和生成时间。手工文档变更仍需同步更新 `docs/PROGRESS.md`。

生成命令：

```bash
./scripts/generate-reference.sh
```

