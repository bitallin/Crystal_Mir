# 开发规范

保持优秀的审美、架构、设计和代码品味。`docs/` 与 ADR 是事实来源，本文是执行约束；涉及架构、协议、数据或许可边界，先更新文档或新增 ADR。这是深度研究项目，暂不处理授权、防沉迷和商业化，但 GPL 隔离、素材不入库和不可变发布必须遵守。

## 定位与流程

- 仓库从 `Crystal / suprcode-mir2` 演进为可配置、可发布、可运营的通用传奇引擎；浏览器是并行入口，不替代旧客户端。
- 浏览器 W0 已闭环，W1/W2 核心链路已落地，W3 未完成。范围看 `docs/product/browser-client-requirements.md`，进度看 `docs/PROGRESS.md`。
- 开始前读 `docs/README.md`、`docs/PROGRESS.md` 和相关架构/ADR；浏览器任务至少读架构 09、开发 04、Web 契约和 ADR 0017/0018。
- 先确认 `Client/Data`、`Client/Map`、`Client/Sound`、测试区服真实可用，并区分“代码完成”和“验收闭环”。
- 完成后更新 `docs/PROGRESS.md`（范围、改动、验证、未验证、下一步）；新决策写 ADR 并登记，边界、接口或部署变化同步文档。

## 架构边界

- 控制面与数据面分离；控制面不得链接 GPL `Server/`，跨边界使用 MIT 契约和 gRPC/HTTP。`Crystal.GameCore` 不依赖 EF Core、ASP.NET Core、WinForms 或具体存储。
- 内容使用不可变、已签名、已版本化的快照；保持旧客户端协议兼容，破坏性变更必须版本协商和迁移；不重写 `Envir`/`Map`，用绞杀者模式逐步抽离。
- 浏览器固定为“原生 Web 客户端 + WebSocket 字节网关 + 权威区服”：不云串流、不嵌入 WinForms、不做客户端权威。Gateway 只转发字节，不链接 `Server/` 或 WinForms；WebAssets 不依赖 WinForms/SlimDX。
- 首版单区；旧 TCP 客户端必须继续可登录、选角和进服。

## Web 客户端

- 协议使用显式 `PacketCodec`，不用运行时反射；帧为 16 位小端长度 + 16 位包号 + 包体，单包受 16 位长度限制，WebSocket 消息上限 256 KiB。协议改动扩展 `tools/Crystal.ProtocolSamples`，C# 与 TypeScript 逐字节一致；关键包解码失败以 `4002` 关闭并显示重试入口。
- 服务端是唯一事实来源；客户端只做动画、摄像机、路径预览和输入反馈，不得乐观修改生命、位置、经验、背包、装备、金币或 NPC/商店状态。
- 会话、世界、UI、资源状态分离；Preact 不直接持有连接或修改世界对象。PixiJS 负责世界、地图分层、实体、摄像机和特效；Preact/CSS 负责登录、选角、HUD、聊天、背包、NPC 和操作层。
- HUD 响应式并适配安全区；桌面键鼠/点击寻路，手机和平板横屏摇杆/动作按钮，竖屏只显示旋转提示；触控按钮要有标签、足够点击面积和反馈，关键操作不能依赖 hover。
- 真实角色/怪物/NPC 动画、门、混合图块、光照和音效属于 W1/W2 完成项；占位几何不能作为最终视觉验收。
- 真实素材和生成物不入库；manifest、地图、图库带内容哈希并按需懒加载，同一 `assetVersion` 不可原地覆盖。`clientBuildHash` 必须同时用于 Web 构建、Gateway、WebAssetCompiler 和区服白名单；编译器缺 `--build-hash`、缺图库、重复 ID、未知格式或哈希不一致直接失败。
- 版本、资源、WebGL2 或 `DecompressionStream` 不可用时明确阻断，不静默降级为 Canvas2D 或旧资源。
- 生产只走 HTTPS/WSS；默认拒绝缺失/不匹配 Origin，只接受 `crystal-v1` 和二进制消息，不记录密码、聊天内容或包体。
- 性能目标：桌面 60 FPS，中端手机不低于 30 FPS，应用壳约 2.5 MB gzip 内，30 分钟会话无持续内存增长。基线为最新两个版本 Chrome/Edge/Firefox/Safari、iOS Safari 16.4+、Android Chrome 110+，要求 WebGL2 和 `DecompressionStream`。
- 首版不含账号注册/改密、技能、交易/邮件/拍卖/公会/任务/商城、桌面全功能对等、竖屏 HUD、离线和云串流；扩范围先改需求文档。

## 完成与验证

- MVP 至少覆盖登录、选角、创建/删除角色、进图、移动、普通战斗、死亡/复活、拾取、聊天、背包/装备、消耗品、NPC 对话、基础商店和断线重连；真实验收还覆盖地图分层、动画、门、光照和音效。
- 完成需同时满足：边界正确；单测、协议逐字节测试和构建通过；新业务覆盖成功、边界和失败路径；UI/输入改动更新 Playwright 桌面与移动横屏冒烟；网关改动覆盖握手、Origin、限流、超时、上游不可用和关闭；资源改动说明素材、版本、哈希和是否仅为 fixture；`docs/PROGRESS.md` 已记录验证与未验证项。
- 最终回复区分“代码完成”“自动化验收通过”“真实设备/真实区服验收通过”。不能把冒烟通过替代真实素材、Safari、真机、内存或性能验收。
- 收口顺序：真实素材全量编译 → 真实区服版本门禁 → 动画/门/光照/音效 → 业务异常回归 → W3 真机、性能、内存和故障演练。除阻断缺陷外，不要跳步宣布完成。

```bash
cd web-client && npm ci && npm run typecheck && npm test && npm run build
cd .. && ./scripts/test-browser-client.sh && ./scripts/test.sh && ./scripts/build.sh && ./scripts/check-boundaries.sh && ./scripts/zone-smoke.sh
```

按改动范围选最小充分集合；协议、资源、网关和浏览器入口不能只跑单测。服务端目标 `net8.0`，WinForms 宿主 `net8.0-windows7.0`；Web 使用 Node 24、npm 锁文件、TypeScript strict、Vite、PixiJS v8 和 Preact，新增依赖需有明确收益并更新锁文件。

## 不要做

- 不把 `Server.MirForms` 改造成 Web 后台，不移植 WinForms 控件树；不在客户端复制服务端战斗、移动、掉落或 NPC 裁决。
- 不为 Web 端修改旧协议语义或要求旧客户端升级；不无 ADR 更换协议、资源格式、哈希算法、数据库或发布模型。
- 不信任客户端拼接的资源路径、构建哈希、区服 ID 或 Origin；不提交真实素材、生成物、私钥、账号密码或包体日志。
- 不提交与任务无关的重构、格式化或依赖升级。
