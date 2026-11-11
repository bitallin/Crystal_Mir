# Crystal 通用传奇引擎：技术文档

> 最后更新：2026-11-11
> 文档状态：生效

## 1. 文档目标

这套文档用于把当前 `Crystal / suprcode-mir2` 代码库，逐步演进为可配置、可发布、可运营的通用传奇引擎。文档与代码同仓库维护，作为架构决策、开发计划和进度记录的单一入口。

文档只记录可执行、可验证的内容：当前事实、目标架构、迁移路径、里程碑、风险和进度。愿景性描述必须落到具体模块或里程碑，不单独成篇。

## 2. 建议阅读顺序

1. `architecture/README.md`：架构原则、关键决策和文档索引。
2. `architecture/01-system-overview.md`：系统边界、角色、组件和核心流程。
3. `architecture/02-runtime-and-modules.md`：现有运行时与目标模块拆分。
4. `architecture/03-data-and-content.md`：数据模型、内容版本和资源管线。
5. `architecture/04-hot-update.md`：客户端与服务端热更新。
6. `architecture/05-operations.md`：开区、合区、支付、GM、监控和备份。
7. `architecture/06-security-and-compliance.md`：权限、审计、供应链和许可证边界。
8. `architecture/07-deployment-and-observability.md`：部署拓扑、环境和可观测性。
9. `product/feature-matrix.md`：需求能力与当前基础、里程碑的对应关系。
10. `product/control-web-prototype.md`：后台信息架构、页面清单、版式和关键业务闭环。
11. `development/`：本地环境、构建测试、迁移实施。
12. `roadmap/milestones.md`：阶段目标、交付物和验收标准。
13. `PROGRESS.md`：当前进度、变更记录和阻塞项。
14. `reference/`：由代码或 schema 生成的参考手册。
15. `runbooks/`：生产故障和高压操作手册。

## 3. 文档地图

```text
docs/
├── README.md                         文档入口与维护规则
├── PROGRESS.md                       进度台账（每次开发必须更新）
├── architecture/
│   ├── README.md                     架构索引与关键决策
│   ├── 01-system-overview.md         系统上下文与组件总览
│   ├── 02-runtime-and-modules.md     运行时、模块边界与重构路径
│   ├── 03-data-and-content.md        数据架构、内容模型与资源管线
│   ├── 04-hot-update.md              客户端与服务端热更新
│   ├── 05-operations.md              开区、合区、支付与运营
│   ├── 06-security-and-compliance.md 安全、审计与许可证
│   └── 07-deployment-and-observability.md 部署与可观测性
├── adr/
│   ├── README.md                     ADR 规范与索引
│   ├── 0001-control-plane-data-plane.md
│   ├── 0002-postgresql-and-content-snapshots.md
│   ├── 0003-legacy-protocol-with-gateway.md
│   └── 0004-gpl-boundary.md
├── development/
│   ├── README.md                     开发文档索引
│   ├── 01-local-environment.md       本地环境与外部资源
│   ├── 02-build-test-and-release.md  构建、测试与发布
│   └── 03-migration-strategy.md      从现有实现迁移的策略
├── product/
│   ├── README.md                     产品文档索引
│   ├── feature-matrix.md             需求能力矩阵
│   └── control-web-prototype.md      后台信息架构与高保真原型
├── roadmap/
│   ├── README.md                     路线图索引
│   └── milestones.md                 里程碑、交付物和验收标准
├── reference/
│   └── README.md                     参考手册索引（schema/包/配置/映射）
└── runbooks/
    ├── README.md                     运行手册索引（故障与高压操作）
    └── zone-server-linux.md          Ubuntu 区服部署与运行
```

## 4. 文档状态

| 状态 | 含义 |
| --- | --- |
| 草稿 | 正在讨论，不能作为实施依据 |
| 评审中 | 方案基本成型，等待验证或评审 |
| 生效 | 当前实施依据，变更需要同步更新代码或 ADR |
| 废弃 | 仅保留历史记录，不再执行 |

每篇文档顶部维护“最后更新”和“状态”。跨文档冲突时，以 ADR 和 `PROGRESS.md` 中记录的最近决策为准，并及时修正旧文档。

## 5. 维护规则

- 架构、协议、数据模型、许可证边界的变更，先新增或更新 `adr/`，再改代码。
- 每次有实质性代码、数据或部署变更，必须更新 `PROGRESS.md`。
- 文档与代码在同一个提交或同一批提交中维护，避免“代码已改、文档滞后”。
- 文档中的路径、命令、字段名必须能在仓库或运行环境中验证。
- 估算必须标注假设和不确定性，不能把估算写成承诺。
- 不在文档中复制大段代码；用文件路径和接口说明代替。

## 6. 事实来源

| 主题 | 事实来源 |
| --- | --- |
| 当前服务端行为 | `Server/`、`Server.MirForms/` |
| 当前客户端行为 | `Client/`、`Shared/` |
| 架构决策 | `docs/adr/` |
| 需求映射 | `docs/product/feature-matrix.md` |
| 后台信息架构 | `docs/product/control-web-prototype.md` |
| 实施顺序 | `docs/roadmap/milestones.md` |
| 实际进度 | `docs/PROGRESS.md` |

