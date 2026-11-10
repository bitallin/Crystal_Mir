# 0004 GPL 边界与代码隔离

- 状态：已接受
- 日期：2026-11-09

## 背景

仓库许可证不一致：

- `Client/`、`Shared/` 是 MIT。
- `Server/`、`Server.MirForms/`、`Tests/` 是 GPL-3.0-only。

控制面如果链接 GPL 代码，可能使整个分发物受 GPL 约束。

## 决策

- `Crystal.GameCore`、`Crystal.ZoneServer` 视为 GPL 边界内代码。
- 控制面、Web、支付、运营服务不链接这些程序集。
- 跨边界使用 MIT 许可的 `Crystal.Shared`、`Crystal.Agent.Contracts` 和纯数据契约。
- 如果分发区服二进制，提供对应 GPL 源码归档和获取方式。
- 素材单独记录许可证和来源。

## 备选方案

1. 全仓库统一 GPL：简单，但限制客户端闭源分发。
2. 全仓库统一 MIT：不可行，不能单方面改变现有 GPL 授权。

## 影响

- 项目拆分和依赖方向必须严格遵守。
- CI 增加许可证检查。
- 发布流程需要生成源码归档和第三方notice。

## 重新评估条件

如果获得所有版权持有人的额外授权，可调整许可证；否则边界不变。

