# 运行手册索引

> 最后更新：2026-11-10
> 状态：生效

运行手册用于生产故障和高压操作，必须短、可执行、可回滚。每个手册包含：触发条件、影响、前置检查、操作步骤、验证、回滚、升级联系人和审计要求。

| 手册 | 场景 | 状态 |
| --- | --- | --- |
| `zone-server-linux.md` | Ubuntu 区服部署、启动、验证、升级和回滚 | 生效 |
| `zone-startup-failure.md` | 区服无法启动 | 待编写 |
| `content-release-failure.md` | 内容发布失败 | 待编写 |
| `hot-reload-rollback.md` | 热重载失败或内容异常 | 待编写 |
| `payment-callback-incident.md` | 支付回调异常/重复发货 | 待编写 |
| `merge-failure-recovery.md` | 合区中断或校验失败 | 待编写 |
| `database-failover.md` | 数据库主备切换 | 待编写 |
| `client-patch-incident.md` | 客户端补丁事故 | 待编写 |
| `security-incident.md` | 账号/内容/支付安全事件 | 待编写 |

运行手册中的命令必须经过 staging 演练，禁止直接在生产试错。

