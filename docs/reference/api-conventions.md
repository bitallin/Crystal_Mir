# API 约定

> 最后更新：2026-11-13
> 状态：生效（M1）

## 1. 版本与路径

- 管理 API 统一使用 `/api/v1`。
- 路径使用小写复数名词，例如 `/import-runs`、`/content/{entityType}`。
- 破坏性变更提升主版本；兼容字段只做新增。
- 响应字段使用 camelCase；内容实体的 `data` 保留旧字段名（PascalCase）。

## 2. 认证与授权

- 管理端使用账号登录，访问令牌为短时 JWT，刷新令牌每次使用后轮换。
- Production 环境必须配置 `CRYSTAL_CONTROL_JWT_SIGNING_KEY`，长度至少 32 字节。
- 健康检查、`/auth/login` 和 `/auth/refresh` 不需要认证；其余接口需要有效 JWT 或 API key。
- API key 使用 `X-Api-Key` 或 `Authorization: Bearer`，保留给机器到机器和兼容场景。
- 权限按“资源 + 动作”授权，例如 `content:read`、`release:publish`、`zone:merge`。
- 账号登录、刷新、登出、账号创建和授权拒绝写入只追加审计日志。
- 草稿写入使用 `content:read`/`content:write`；发布读取、写入、发布和回滚分别使用
  `release:read`、`release:write`、`release:publish`、`release:rollback`。

## 3. 分页、排序和过滤

- `page` 从 1 开始，默认 1。
- 内容列表 `pageSize` 默认 50，范围 1～200；地图目录和刷新点关系查询默认 500/200，最大 500。
- 响应返回 `page`、`pageSize`、`total` 和当前页 `items`。
- `sort` 只允许白名单值；当前为 `index`、`key`、`name`。
- `direction` 只允许 `asc`、`desc`。
- `q` 为名称模糊搜索，最长 100 字符；SQL 使用参数和通配符转义。
- 不接受任意 SQL、任意列名或跨表自由过滤。

## 4. 错误

错误使用 RFC 7807/9457 `application/problem+json`：

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Validation failed",
  "status": 400,
  "errors": {
    "pageSize": ["pageSize must be between 1 and 200."]
  }
}
```

| 状态码 | 含义 |
| --- | --- |
| 400 | 参数或实体类型不合法 |
| 401 | 缺少或无效 JWT/API key，或会话已撤销 |
| 403 | 已认证但缺少所需权限 |
| 404 | 导入批次或内容实体不存在 |
| 409 | revision 冲突、发布状态不允许该动作、目标版本不可回滚 |
| 422 | 内容校验失败，响应体为校验报告 |
| 429 | 超过限流 |
| 503 | 数据库或 schema 未就绪 |

错误响应不得包含堆栈、SQL、连接串、密钥或内部路径。详细异常只写服务端日志。

草稿实体删除使用 `DELETE` 加 JSON 请求体：

```json
{
  "expectedRevision": 3,
  "reason": "删除重复条目"
}
```

revision 不一致必须返回 409，调用方应重新读取实体后合并，而不是覆盖他人修改。

## 5. 限流与缓存

- 管理 API 按客户端 IP 固定窗口限流：240 次/分钟。
- 登录和刷新按客户端 IP 固定窗口限流：10 次/分钟。
- 管理 API 响应设置 `Cache-Control: no-store`。
- 关闭 Kestrel `Server` 头。
- 后续按用户、IP、接口维度细化，并为发布/发货/合区引入幂等键。

## 6. 可观测性

- 使用 ASP.NET Core 结构化日志。
- 后续接入 OpenTelemetry，统一 `trace_id`、`request_id`、`service`、`zone_id`。
- 健康检查区分 liveness 和 readiness；readiness 检查数据库连接和 schema。
