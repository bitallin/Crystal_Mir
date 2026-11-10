# 构建、测试与发布

> 最后更新：2026-11-10
> 状态：生效

## 1. 解决方案结构

当前：

```text
Shared/                      协议与共享代码（MIT）
Server/                      服务端库（GPL）
Server.MirForms/             服务端宿主 + WinForms 编辑器（GPL）
Client/                      Windows 客户端（MIT）
src/Crystal.Content/         内容快照契约、哈希和校验（MIT）
src/Crystal.Control.Api/     内容、账号、RBAC 和审计 API（ASP.NET Core + Npgsql）
src/Crystal.ZoneServer/      Linux 区服控制台宿主（GPL 边界，链接 Server.Library）
tools/Crystal.LegacyImporter 旧二进制导入/导出器（链接 GPL Server.Library）
db/migrations/               PostgreSQL schema
Tests/ItemSecurity/          旧物品安全回归
Tests/ContentSnapshot/       内容快照回归
LibraryEditor/               图库编辑工具
LibraryViewer/               图库查看工具
AutoPatcherAdmin*            补丁管理工具
```

目标新增：

```text
src/Crystal.GameCore
src/Crystal.Agent.Contracts
src/Crystal.Control.Web
src/Crystal.Patcher
src/Crystal.Cli
```

## 2. 构建矩阵

| 目标 | 平台 | 说明 |
| --- | --- | --- |
| 服务端库 | Windows/Linux | Linux 已验证构建 |
| Zone Server | Linux 优先 | 生产目标；Windows 用于开发 |
| WinForms 宿主 | Windows | Linux 可交叉构建，运行仍需 Windows |
| 客户端 | Windows | Linux 可交叉构建，现有 Direct3D9/SlimDX 限制运行平台 |
| Control API | Linux | ASP.NET Core |
| Control Web | 任意 | 静态资源，无构建链；`node --check` 校验 |

2026-11-10 已验证：

```bash
./scripts/build.sh
./scripts/test.sh
./scripts/inspect-db.sh
./scripts/verify-content.sh
./scripts/zone-smoke.sh
```

结果：旧服务端、客户端、内容库、Control API 和工具构建成功；`ItemSecurity` 通过 358 项检查；
`ContentSnapshot` 通过 13 项检查；黄金数据库可加载；黄金内容导入和二进制导出 round-trip 通过；
Control.Web 全部 JavaScript 通过 `node --check`；Linux 区服加载 463 张地图并以 SIGTERM 正常退出。

发布自包含 Linux 区服：

```bash
./scripts/publish-zone.sh artifacts/zone-publish
```

自包含产物约 74 MB，不要求目标机安装 .NET 8。`SELF_CONTAINED=false` 可生成依赖框架的产物。

单独导出旧二进制数据库：

```bash
./scripts/export-db.sh tests/fixtures/golden/Server.MirDB artifacts/legacy-export/Server.MirDB
```

导出默认执行冷回读语义校验；已有目标文件必须显式使用 `--force` 覆盖。

浏览器联调需要真实 PostgreSQL 和 Python Playwright：

```bash
CRYSTAL_DATABASE_URL='Host=127.0.0.1;Port=5432;Database=crystal;Username=crystal;Password=...' \
  ./scripts/test-control-web.sh
```

该脚本先运行 Control API 集成（schema、黄金导入、认证/RBAC/账号生命周期/审计/CORS 预检），再启动
真实 API 和静态后台，验证登录、内容总览 KPI、导入批次、内容分页与搜索、访问令牌刷新、账号创建、
停用/启用、密码重置、角色调整、角色权限矩阵、审计 CSV 导出、登出和控制台错误。测试还会创建临时
财务账号，验证导航隐藏、直达 403 和不会发起越权内容/账号请求。

需要真实 PostgreSQL 时：

```bash
CRYSTAL_DATABASE_URL='Host=127.0.0.1;Port=5432;Database=crystal;Username=crystal;Password=...' \
  ./scripts/test-control-api.sh
```

`test-control-api.sh` 同时验证受控 CORS 预检；Control.Web 使用 `?api=` 指定 API 地址，
API 使用 `CRYSTAL_CONTROL_CORS_ORIGINS` 允许静态站点来源。

该脚本执行 schema 迁移、黄金导入、API 启动、认证/账号生命周期和只读接口验证。CI 使用 PostgreSQL 16 service container
运行同一脚本；本地已验证 PostgreSQL 12 兼容。本地设置 `CRYSTAL_DATABASE_URL` 或
`CRYSTAL_DB_CONNECTION` 后，`./scripts/ci.sh` 也会自动运行该集成测试，否则明确跳过。

## 3. 测试策略

### 3.1 单元测试

- 内容 schema 校验。
- 数值边界和协议字段范围。
- 掉落/经验/货币计算。
- 脚本解析和沙箱限制。
- 合并 ID 映射和冲突策略。

### 3.2 集成测试

- PostgreSQL 内容 CRUD、发布、回滚。
- 区服启动、Agent 注册、内容拉取、热重载。
- 客户端 manifest 校验、断点续传、回滚。
- 支付回调、幂等发货、对账。

### 3.3 黄金样本测试

用同一份旧 `Server.MirDB`：

- 旧实现加载后导出实体快照。
- 新导入器加载后导出实体快照。
- 旧二进制数据库导出后重新加载，并对比语义哈希和全部实体快照。
- 对比数量、索引、关键字段、掉落期望、脚本。

### 3.4 协议测试

- 记录现有客户端/服务端包序列。
- 新网关/区服重放关键流程：版本校验、登录、选角、进服、移动、交易。
- 协议变更必须更新兼容矩阵。

### 3.5 压测与浸泡

- 区服 Tick 延迟、内存、GC、连接数。
- 长时间运行的内存泄漏和对象增长。
- 发布/热重载期间在线玩家的稳定性。

### 3.6 数据库回归

- 使用 `tests/fixtures/golden/Server.MirDB` 作为固定样本。
- 旧数据库导入器和内容快照必须输出可对比的实体数量、索引和关键字段。
- `./scripts/inspect-all-db.sh` 覆盖多个历史版本。

## 4. CI 流水线

```text
restore -> build -> unit tests -> integration tests
-> schema validation -> security scan -> license scan
-> artifact -> deploy dev -> smoke test
```

当前最小 CI 入口：

```bash
./scripts/ci.sh
```

它执行构建、回归测试、黄金数据库检查、黄金内容导入、旧二进制导出 round-trip，并在 Node
可用时检查 Control.Web JavaScript 语法。GitHub Actions 配置见
`.github/workflows/ci.yml`；独立 `control-api-integration` job 使用 PostgreSQL 16 验证迁移、导入和 API。

要求：

- 构建失败阻断合并。
- 测试报告和覆盖率作为制品保存。
- 数据库迁移在 CI 的空库和升级库上都执行。
- 发布包生成 SBOM 和校验和。

## 5. 版本号

建议同时维护：

| 版本 | 示例 | 说明 |
| --- | --- | --- |
| 产品版本 | `0.1.0` | 控制面和工具 |
| 区服版本 | `0.1.0+zone.3` | 区服构建 |
| 客户端版本 | `0.1.0` | 启动器和客户端 |
| 内容版本 | `2026.11.09.1` | 内容发布 |
| 内容 schema | `1` | 内容字段结构 |
| 协议版本 | `1` | 网络包结构 |

## 6. 发布检查清单

- [ ] 构建和测试通过。
- [ ] 数据库迁移在 staging 演练通过。
- [ ] 内容包通过校验和签名。
- [ ] 客户端 manifest 可回滚。
- [ ] 区服可回滚到上一内容版本。
- [ ] 监控和告警已配置。
- [ ] 备份完成。
- [ ] 发布说明和影响范围已记录。
- [ ] 回滚负责人和窗口明确。

