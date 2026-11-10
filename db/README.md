# 数据库 schema

> 最后更新：2026-11-10
> 状态：生效（M1 初版）

## 迁移文件

| 文件 | 内容 |
| --- | --- |
| `migrations/0001_content_schema.sql` | 内容导入、内容实体、发布、资源、只读视图 |
| `migrations/0002_control_identity_audit_schema.sql` | 控制面账号、角色/权限、会话和只追加审计 |

迁移使用幂等 SQL。`Crystal.LegacyImporter --dsn` 会按文件名顺序执行 `db/migrations/*.sql`，
每个文件在单独事务中执行并写入 `schema_migrations`。
手工执行也可以：

```bash
for migration in db/migrations/*.sql; do
  psql "$CRYSTAL_DATABASE_URL" -v ON_ERROR_STOP=1 -f "$migration"
done
```

## 表

- `content_import_run`：一次二进制导入的来源、哈希、版本、状态和摘要。
- `content_entity`：导入后的规范化实体，`data jsonb` 保存完整字段。
- `content_release` / `content_release_entity`：不可变发布快照。
- `asset` / `content_release_asset`：内容寻址资源及其许可证来源。
- `control_account`：控制面管理账号，密码保存为 Argon2id 哈希。
- `control_role` / `control_permission` / `control_role_permission`：RBAC 角色和权限。
- `control_account_role`：账号角色分配。
- `control_session`：短时 JWT 对应的刷新会话，只保存刷新令牌哈希。
- `audit_event`：登录、刷新、登出、账号管理和授权拒绝等审计事件。
- `schema_migrations`：已应用迁移和校验和。

`audit_event` 使用数据库触发器禁止 UPDATE、DELETE 和 TRUNCATE，只允许追加。

`content_item`、`content_monster`、`content_map`、`content_npc`、`content_magic`、`content_quest`
是按实体类型过滤的只读视图。当前视图跨导入批次返回全部记录，控制面查询时必须显式限定
`import_run_id` 或未来的活动发布。

## 导入

```bash
./scripts/import-db.sh tests/fixtures/golden/Server.MirDB artifacts/content/golden
```

带 PostgreSQL 时：

```bash
CRYSTAL_DATABASE_URL='Host=localhost;Database=crystal;Username=crystal;Password=...' \
  ./scripts/import-db.sh tests/fixtures/golden/Server.MirDB artifacts/content/golden --dsn "$CRYSTAL_DATABASE_URL"
```

导入以快照目录为中间产物：先写不可变 JSONL 和 manifest，再写入 PostgreSQL。这样可以脱离数据库
验证实体数量、哈希和字段映射。
