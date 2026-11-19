# VERA20K 架构对标与 Crystal 重构建议

> 最后更新：2026-11-19
> 状态：评审中

## 1. 范围与方法

本文对 `https://www.ra20k.com/` 暴露的公开前端、下载通道和 `YuriPlanet/vera20k` 公开仓库做架构级逆向，并与当前 Crystal 代码库对照，给出可执行的重构建议。

观察方式仅限公开 `GET` 请求和公开仓库文档/源码，不登录、不注册、不上传、不修改目标站点。目标站点的后台源码未公开，因此本文严格区分：

- **直接观察**：页面、静态资源、公开接口响应、公开仓库和文档。
- **高置信推断**：由前端 SDK 契约和路由行为推导出的后端形态。
- **未验证**：需要目标团队或服务端权限才能确认的部分。

目标站点是 `VERA20K 红警2-尤里的复仇联机平台`，不是传奇类游戏站点。其架构经验不能整体照搬到 Crystal，必须按游戏类型和能力边界筛选。

## 2. 直接观察到的 VERA20K 架构

### 2.1 交付面与前端

| 层次 | 观察结果 |
| --- | --- |
| 官网 | `www.ra20k.com` 返回静态营销页，包含玩法介绍、下载入口、地图/Mod 发布入口和帮助 |
| 玩家大厅 | `/lobby`、`/maps`、`/mods` 返回同一 Vue 3 + Vite SPA 壳，实际路由由 `launcher--*.js` 加载 |
| SPA 路由 | `/solo`、`/campaign`、`/lan`、`/quick-match`、`/rooms/:id`、`/maps`、`/mods`、`/replays`、`/stats`、`/leaderboard`、`/friends`、`/creators/:userId`、`/me`、`/settings`、登录/注册/找回/OAuth 回调等 |
| CDN/WAF | 响应头出现 ESA、Cloudflare 互操作头、缓存和跨域头；静态资源和安装包由独立下载域提供 |
| 节点发现 | `https://www.ra20k.com/nodes.json` 返回节点注册表；当前公开内容只有一个节点，URL 为 `https://sh2-vera20k.libisky.com` |
| 运行时配置 | 前端打包了 `VITE_NAKAMA_HOST`、`VITE_NAKAMA_PORT`、`VITE_NAKAMA_SSL`、`VITE_NAKAMA_SERVER_KEY`、`VITE_NODES_URL` 等配置 |

### 2.2 身份、大厅与社交契约

大厅前端直接打包 Nakama JavaScript SDK 的调用形态，使用 `/v2/account/*`、`/v2/rpc/{id}`、`/v2/storage`、`/v2/friend`、`/v2/group`、`/v2/leaderboard`、`/v2/tournament`、`/v2/iap/*` 等接口，并通过 `/ws?lang=en&status=...&token=...` 建立实时连接。

这说明至少**契约层是 Nakama 兼容的**；后端是否为官方 Nakama、还是同契约的定制服务，公开资料不足以确认。

在 Nakama 契约之外，站点还有一组自定义接口：

| 接口 | 用途 |
| --- | --- |
| `GET /vera/config` | 返回功能开关、节点、协议版本、排位队列、赛季、公共频道、地图/Mod 能力等 |
| `GET /vera/ready` | 返回节点状态、在线人数、房间数、对局数 |
| `GET /vera/stats` | 返回公告、24 小时对局数、总对局数、在线、进行中对局、开放房间、注册人数 |
| `/vera/auth/reset_request`、`/vera/auth/reset_confirm` | 邮箱密码重置 |
| `/vera/auth/github/start` | GitHub OAuth 入口 |
| `/vera/maps/detail`、`/vera/maps/creator` | 地图详情与作者作品 |
| `/vera/leaderboard` | 排行榜查询 |

自定义 RPC 名称进一步暴露了业务域：

```text
room.create / room.join / room.leave / room.ready / room.options / room.start
room.invite / room.kick / room.chat / room.transfer / room.spectate
game.spawn / game.active / game.rejoin / game.abandon / game.crash
lobby.presence / lobby.rooms / lobby.join / lobby.watch
map.upload.begin / map.upload.finish / map.publish / map.library / map.version.*
mod.create / mod.update / mod.version.begin / mod.version.commit / mod.report
stats.summary / stats.history / stats.standings / leaderboard.list
replay.upload.begin / replay.upload.finish / replay.download
account.change_password / account.change_email / account.logout_all
creator.list / creator.stats / creator.maps / creator.mods
```

公开配置还给出了一个关键运营信号：`/vera/config` 中的 `protocol`、`ranked_queues`、`season`、`public_channels`、`map_upload`、`map_review`、`mods`、`guest_enabled` 都是运行时下发的。客户端不需要硬编码“当前开哪些功能”。

### 2.3 游戏运行时

公开仓库显示 VERA20K 是一个 Rust 多目标游戏引擎，公开文档明确给出以下边界：

```text
vera20k-core   assets / rules / map / sim / net / headless
vera20k        app / render / ui / audio / asset tools
relay-protocol 票据、信封、排序、网络模拟
relay          游戏数据报中继
```

关键原则：

- `sim/` 只负责确定性游戏状态和逻辑，不依赖 `render/`、`ui/`、`audio/`、`net/`。
- `app/` 把输入变成 simulation command，把 simulation state 交给 renderer。
- 渲染只读 simulation state；应用层负责窗口、输入、加载、存档和平台集成。
- 模拟优先使用定点数，固定随机流和对象行动顺序；正常游戏每 15 帧计算一次状态哈希，多人游戏另有逐帧同步 checksum。
- 渲染使用 `wgpu`，桌面使用 `winit`；支持 Windows、macOS、Linux、Android 和 WASM 浏览器目标。
- 浏览器包是可选第四个目标，单机默认；联网通过独立的 `wasm-net` feature 和 WS/UDP 网关接入。

这条边界是 VERA20K 最值得 Crystal 学习的地方：**不是“用 Rust”，而是“模拟、渲染、应用、网络各自拥有明确的数据流方向”**。

### 2.4 联机模型

VERA20K 的在线联机不是传统 MMO 服务端权威模型，而是确定性 RTS 的会话模型：

```text
HTTPS Lobby
  -> 签发会话、房间、relay ticket、packet key
  -> 玩家进入匹配/房间/排位

Authenticated UDP Relay
  -> 认证每个数据报
  -> 维护 match clock 和不可变 command stream
  -> 对命令排序、补包、限流、暂停、观战、回放

Clients
  -> 保留 simulation authority
  -> 按 relay 排序后的命令推进同一确定性模拟
  -> 交换 checksum，检测失步
```

公开文档还定义了：

- 登录时的 `Vera-Version`、`Vera-Build`、`Vera-Quick-Match` 等头。
- 客户端版本、build、rules digest、map digest、effective map digest 必须一致才能开局。
- 不兼容版本返回 `426 upgrade_required`，并触发客户端更新检查。
- relay ticket 有签名、有效期、重放窗口和逐跳 HMAC；每个数据报有明文 payload 和 4 字节 MAC。
- relay 负责命令流和 match clock，但不替客户端计算游戏世界。
- 浏览器通过 Node 网关把二进制 WebSocket 消息一一映射为 UDP 数据报，网关不解释游戏包。

这种模型适合“短局、可重放、双方必须看到同一世界”的 RTS，不适合直接替换 Crystal 的持久世界 MMO。

### 2.5 内容与 UGC

VERA20K 把地图和 Mod 作为独立 UGC 域：

- 以 SHA-256 内容寻址，上传分 `begin` / `finish`，并带字节数、摘要、封面、版本和变更说明。
- 地图有 `publish`、`version.attach`、`version.update`、`version.delete`、`withdraw`、`report`、`library`、`ticket` 等生命周期操作。
- Mod 有创建、更新、版本开始/提交/撤回、文件摘要、举报和详情。
- 地图和 Mod 作者有独立 creator 页、作品统计和排序。
- 开局前传递并校验地图摘要；地图文件不放进引擎仓库，也不由平台分发原版游戏资源。
- 地图有审核开关 `map_review`，上传模式 `map_upload` 由运行时配置控制。

这比 Crystal 当前“官方内容快照 + 控制面编辑”的模型多了一层**玩家创作内容供应链**。两者不应混在同一个发布生命周期里。

### 2.6 客户端分发与更新

观察到两条更新链路：

1. 官网下载页读取 `download.ra20k.com/client/update/latest.json`，其中包含版本、平台安装包 URL 和 Tauri 风格签名；当前安装包覆盖 Windows、macOS Apple Silicon、macOS Intel。
2. 游戏仓库内另有 Ed25519 签名的 release manifest 逻辑，签名覆盖 manifest 原始字节，下载后校验 SHA-256；大厅返回 426 时会触发更新检查。

因此 VERA20K 至少把“安装包/启动器分发”和“游戏引擎版本更新”分开处理，而不是让游戏主循环直接管理所有文件。Crystal 已有签名 manifest 和事务回滚，方向上接近；差距主要在 launcher 自更新、渠道/灰度、发布指针和多平台产物。

### 2.7 运营与可观测性

VERA20K 的公开运营面包括：

- 节点注册表和节点健康/延迟选择。
- 公共 `/ready` 和 `/stats`，不暴露玩家身份。
- 公告、在线人数、开放房间、24 小时对局数。
- 排位赛季、队列、排行榜、对局历史、回放。
- 好友、组、在线状态、邀请、观战。
- 地图/Mod 举报、审核、作者主页。

这部分更接近“玩家运营平台”，而 Crystal 当前的 Control.Web 更接近“版本作者/管理员后台”。两者需要区分，不能把玩家功能塞进管理后台。

## 3. Crystal 当前基线

| 维度 | Crystal 当前事实 | 证据 |
| --- | --- | --- |
| 游戏类型 | 持久世界传奇 MMO，角色、装备、公会、交易、任务、攻城等状态长期存在 | `docs/architecture/01-system-overview.md`、`docs/product/feature-matrix.md` |
| 服务端权威 | 移动、战斗、掉落、交易由服务端主循环裁决 | `docs/architecture/06-security-and-compliance.md` |
| 世界循环 | `Envir.WorkLoop` 单主循环，怪物处理可多线程 | `Server/MirEnvir/Envir.cs`、`docs/architecture/02-runtime-and-modules.md` |
| 网络 | 单 TCP 端口承载登录、选角、进服和游戏 | `Server/MirEnvir/Envir.cs`、`Server/MirNetwork/MirConnection.cs` |
| 协议 | 自定义二进制包，客户端版本先做哈希校验 | `Shared/ClientPackets.cs`、`Shared/ServerPackets.cs`、`Server/MirNetwork/MirConnection.cs` |
| 内容 | `Server.MirDB` 二进制库，控制面导入为不可变快照，发布使用 ECDSA P-256 签名 | `docs/architecture/03-data-and-content.md`、`docs/architecture/04-hot-update.md` |
| 控制面 | .NET 8 ASP.NET Core + PostgreSQL，内容草稿/发布/回滚、RBAC、JWT、审计、区服 Agent、指标 | `src/Crystal.Control.Api/`、`docs/reference/control-api.md` |
| 区服换版 | Zone Agent 下载验签，进程级 activate/rollback；尚无零停机切换 | `docs/architecture/04-hot-update.md`、`docs/architecture/07-deployment-and-observability.md` |
| 客户端 | WinForms + SlimDX/Direct3D9，Windows 专用 | `docs/architecture/02-runtime-and-modules.md` |
| 客户端更新 | 签名 manifest、SHA256 文件级增量、事务应用和回滚 | `docs/adr/0015-client-signed-patch-manifest-and-transactional-update.md` |
| GameCore | 当前只有运行时端口骨架，`Server/` 仍是行为事实来源 | `src/Crystal.GameCore/README.md` |

结论：Crystal 的**控制面、内容治理、审计和发布安全已经强于 VERA20K 公开可见的部分**；VERA20K 领先的是**会话边缘、确定性验证、联机协议、UGC 供应链、跨平台客户端和玩家运营面**。

## 4. 差距矩阵

| 能力 | VERA20K | Crystal | 是否应对齐 | 结论 |
| --- | --- | --- | --- | --- |
| 游戏权威模型 | 客户端确定性模拟 + relay 排序 | 服务端权威持久世界 | 否 | 不照搬 lockstep；保留服务端裁决 |
| 模拟/渲染/应用分层 | `sim`、`render`、`app` 单向依赖 | `Envir`/`Map` 与 WinForms 耦合 | 是 | 继续抽离 GameCore，先定依赖方向再搬逻辑 |
| 会话入口 | HTTPS lobby + Bearer token + 节点路由 | 登录和游戏同 TCP 连接 | 是 | 新增玩家会话边缘，不破坏旧协议 |
| 版本门禁 | version/build/rules/map digest + 426 | 只校验客户端哈希，缺内容/规则握手 | 是 | 补协议、客户端、内容、规则、地图多维握手 |
| 联机传输 | UDP relay + 逐跳认证 + 重放保护 | 单 TCP，无独立 relay | 部分 | MMO 保留 TCP；实例 PvP 可评估 UDP/relay |
| 房间/匹配 | 成熟房间、快速匹配、排位、观战 | 无独立玩家大厅/匹配服务 | 是 | 先做实例化 PvP 的 room/match 服务 |
| 有效内容校验 | 开局前地图/规则摘要一致 | 客户端版本哈希，缺内容摘要协商 | 是 | 增加 content/map/rules digest |
| UGC 地图/Mod | 内容寻址、上传、审核、版本、举报 | 官方内容为主，无玩家发布链路 | 是 | 新建 UGC 域，不混入官方签名发布 |
| 回放/观战 | 命令流、watch grant、replay | 无通用回放/观战协议 | 是 | 先做服务端命令日志和审计回放 |
| 节点/区域 | `nodes.json`、健康探测、延迟选择 | Control API + Zone Agent，无节点目录 | 是 | 增加区域/节点注册和公开状态 |
| 玩家官网 | 账号、下载、排行、统计、作者页 | 管理后台为主，无玩家门户 | 是 | 新建玩家门户，不改造 Control.Web |
| 跨平台客户端 | Windows/macOS/Linux/Android/WASM | Windows WinForms | 有条件 | 先抽 ClientCore，跨平台渲染单独立项 |
| 更新 | 安装包与引擎更新分离、签名 manifest | 已签名 manifest + 事务回滚 | 部分 | 补 launcher 自更新、渠道、灰度和发布指针 |
| 后端产品 | Nakama 兼容契约 + 自定义 RPC | 自建 .NET 控制面 | 否 | 学契约，不盲目引入 Nakama |
| 许可证 | GPL-3.0 Rust 引擎 | GPL 服务端 + MIT 客户端/契约 | 约束 | 只借鉴思想，控制面继续 MIT 边界 |

## 5. 目标架构建议

### 5.1 总体分层

```text
玩家 Web / Launcher / 现有客户端
        |
        v
Edge Gateway (MIT)
  - TLS/限流/连接预算
  - 会话 token 校验
  - protocol/client/content/rules/map 版本门禁
  - 区服路由
        |
        +--> Player Session API (MIT)
        |      - 玩家账号/登录方式/会话
        |      - 节点目录、公共配置、公告、统计
        |      - 房间/匹配/排位/观战（实例化玩法）
        |      - UGC 地图/Mod 目录与审核
        |
        +--> Zone Gateway (TCP 兼容代理)
               - 旧协议透传
               - 新握手后携带 token/zone_id
               - 逐步支持连接迁移和停写

Control API (现有，管理面)
  - 内容编辑/发布/签名/回滚
  - 账号/RBAC/审计
  - Zone Agent 命令和指标

ZoneServer (现有，GPL 边界)
  - 服务端权威世界循环
  - TCP 游戏会话
  - 只读消费已验证内容

Crystal.ZoneAgent (现有，MIT)
  - 生命周期、发布包验签、指标桥接

Object Storage / CDN
  - 官方内容包
  - UGC 地图/Mod/回放
  - 客户端补丁

PostgreSQL / Redis
  - 管理面和玩家面分 schema
  - 会话、节点、房间、UGC 元数据、审计
```

### 5.2 新增模块与边界

| 模块 | 职责 | 不应该做什么 |
| --- | --- | --- |
| `Crystal.Player.Contracts` | 玩家会话、节点、房间、UGC 的 MIT DTO/错误码/版本字段 | 不引用 GPL 游戏代码 |
| `Crystal.Player.Api` | 玩家账号、会话、节点目录、配置、匹配、UGC 目录 | 不复制 Control API 的管理权限模型 |
| `Crystal.Gateway` | 边缘限流、版本门禁、路由、旧协议透明转发 | 一开始不要解析全部 432 个游戏包 |
| `Crystal.Ugc` | 内容寻址上传、审核、版本、下载票据、举报 | 不把未审核内容直接放进官方内容包 |
| `Crystal.ClientCore` | 协议编解码、会话状态、内容版本、错误模型、重连 | 不依赖 WinForms/SlimDX |
| `Crystal.GameCore` | 逐步承接 `Envir`/`Map` 的游戏逻辑 | 不依赖数据库、HTTP、WinForms |
| `Crystal.ZoneServer` | 权威世界循环和游戏端口 | 不承担玩家门户/内容审核 |
| `Crystal.Control.Api` | 管理面内容、权限、审计、Agent 编排 | 不承担玩家社交和公开统计 |

### 5.3 会话与版本协议

建议新增一个**预会话**阶段，而不是立即改现有二进制包：

```text
Client -> Gateway:
  protocol_version, client_version, build_id, platform,
  content_schema_version, content_hash, rules_hash, map_hash,
  capabilities, locale

Gateway -> Client:
  session_token, expires_at, zone_endpoint,
  minimum_client_version, required_content_hash,
  feature_flags, download_url?, upgrade_required?
```

兼容策略：

1. 旧客户端继续走现有 TCP 端口和 `ClientVersion` 哈希校验。
2. 新客户端先访问 HTTPS Gateway 获取短时会话，再带 token 连接 Zone Gateway。
3. 区服在进服前校验 `content_hash`/`rules_hash`/`map_hash`；不一致返回结构化错误和下载入口。
4. 旧客户端在新内容不兼容时返回明确升级提示，而不是在登录后出现难解释的包错误。

对应到现有代码，优先扩展现有 `ClientVersion`/登录状态机，而不是重写 `Shared/` 的包体系。具体包格式和兼容窗口需要单独 ADR。

### 5.4 房间、匹配与观战

不要第一版就做“通用 MMO 大厅”。建议把玩家面限制在**实例化玩法**：

- 竞技场、跨服战场、活动副本、房间制 PvP。
- 房间创建/加入/准备/开局/转移房主/踢人/观战。
- 快速匹配和排位队列。
- 对局结束后把结果写回区服，资产变更仍由区服权威执行。

协议可以借鉴 VERA20K 的 `request_id + after cursor + event` 模型：

```text
POST /api/v1/player/commands
{ request_id, after, operation, payload }

GET /api/v1/player/events?after=<cursor>
{ events:[{sequence,kind,payload}], cursor }
```

关键要求：

- 命令幂等，重复 `request_id` 不重复执行。
- 事件按会话内单调序列返回，断线后可按 cursor 恢复。
- 房间状态和游戏世界状态分离；房间服务不直接改角色资产。
- 所有开局都校验版本、内容、规则和地图摘要。

### 5.5 确定性验证与回放

Crystal 不应改成客户端权威 lockstep，但可以吸收 VERA20K 的“可验证性”：

1. 给主循环增加只追加的 command journal：`tick`、`source`、`operation`、`payload`、`command_id`。
2. 周期性计算世界状态摘要，记录到对局/区服日志。
3. 对实例化 PvP 记录完整命令流，支持服务端回放和争议审计。
4. 先做“服务端权威 + 可回放”，不要承诺完整确定性重演；`Envir` 的多线程、浮点和随机源需要单独治理。
5. 如果未来要做跨平台客户端或客户端预测，再评估把实例战斗抽成固定步长、单写入者、种子随机流的 `MatchCore`。

这一步的价值是反作弊、客服、回放和回归测试，而不是把 MMO 改成 RTS 的 P2P 模型。

### 5.6 UGC 地图与 Mod 供应链

建议新增独立于官方内容发布的 UGC 域：

```text
ugc_asset(id, kind, sha256, byte_length, storage_key, created_at)
ugc_version(id, asset_id, version_label, changelog, status, created_by)
ugc_review(id, version_id, reviewer_id, status, reason, reviewed_at)
ugc_report(id, version_id, reporter_id, reason, status, created_at)
ugc_download_ticket(id, version_id, expires_at, nonce, max_bytes)
ugc_creator(user_id, display_name, bio, links, stats_jsonb)
```

生命周期：

```text
draft -> uploading -> uploaded -> validating -> in_review
      -> published -> withdrawn
      -> rejected
```

约束：

- 内容按 SHA-256 不可变；同一摘要重复上传直接复用。
- 地图上传后先解析尺寸、玩家数、起始点、阻挡和预览图，再进入审核。
- Mod 必须声明依赖、兼容版本、文件清单和许可证；禁止把原版游戏资源打进包。
- 下载走短时 ticket 或 CDN 签名 URL，不能暴露对象存储内部路径。
- 区服只消费 `published` 的摘要引用；官方内容包和 UGC 包使用不同信任级别。
- 举报、下架和审核都要审计；作者不能直接修改已发布版本。

### 5.7 节点目录与玩家运营面

新增玩家可见的只读/低权限接口：

```text
GET /api/v1/player/config
GET /api/v1/player/nodes
GET /api/v1/player/ready
GET /api/v1/player/stats
GET /api/v1/player/zones
```

要求：

- 不返回玩家身份、IP、角色资产或内部拓扑。
- 节点注册包含 `region`、`endpoint`、`protocol_version`、`content_release`、`capabilities`、`last_heartbeat`、`weight`。
- 客户端按区域、延迟、协议和内容版本选择节点。
- 公共统计短缓存、按 IP 限流，和 Control API 的管理员接口分开。

### 5.8 客户端与启动器

不建议立即重写 WinForms/Direct3D9 客户端。建议分两步：

1. **ClientCore 抽离**：把 `Shared` 协议、会话状态、内容版本、错误/重连、manifest 校验抽成 `net8.0` 库；WinForms 只保留窗口、输入和渲染适配。
2. **渲染/跨平台单独立项**：评估 SDL/OpenGL/Vulkan/WebGPU 或 WebAssembly 的可行性；没有明确产品目标前，不把跨平台渲染塞进 MMO 重构主线。

启动器方面，现有签名 manifest 和事务回滚是正确的。下一步优先补：

- launcher 两阶段自更新。
- 渠道、灰度、发布指针和回滚。
- 多平台产物和 CDN 内容寻址。
- 426/最低版本/内容版本与下载页联动。

### 5.9 可观测性与测试

优先新增以下门禁：

- 版本兼容矩阵：旧客户端/新客户端 × 旧内容/新内容 × 旧协议/新协议。
- Gateway 混沌测试：超时、断连、重复请求、限流、错误 token、节点切换。
- UGC 摘要测试：上传中断、重复摘要、篡改、审核状态机、越权下载。
- 回放测试：同一命令日志可重放，状态摘要一致；不一致时能定位 tick。
- 节点路由测试：区域、延迟、内容版本不匹配时拒绝并提供可解释错误。
- 公开接口测试：无 PII、缓存、限流、错误码。

现有 `ContractBoundary`、内容黄金样本、Zone Agent E2E 和客户端补丁测试继续保留，不因新增边缘层而降低覆盖率。

## 6. 分阶段重构路线

估算假设：3～4 人团队，包含 1 名 .NET 后端、1 名游戏服务端、1 名前端/客户端和兼职运维；不包含跨平台渲染器的完整重写。

### P0：门禁与可观察性（2～4 周）

- 新增 `ADR 0016`，确认“保留服务端权威、借会话边缘/版本门禁/确定性验证/UGC”的边界。
- 设计并原型验证预会话握手，不改旧客户端默认路径。
- 增加 `GET /player/config`、`/player/ready`、`/player/stats` 只读接口。
- 增加节点目录表和 `nodes` 查询；当前单区仍可只有一个节点。
- 建立版本兼容矩阵测试和协议错误码规范。

验收：旧客户端仍可登录；新客户端能拿到版本/内容/节点信息；不兼容时返回可解释错误。

### P1：玩家会话与边缘网关（4～8 周）

- 新建 `Crystal.Player.Api` 和 `Crystal.Gateway`（MIT），与控制面/区服进程隔离。
- 玩家账号与会话独立于 `control_account`；管理面和玩家面分 schema。
- Gateway 先做透明 TCP 转发、连接限流和节点路由；再做新握手 token 校验。
- 玩家门户最小闭环：登录、区服列表、公告、公共统计、下载入口。
- 抽离 `Crystal.ClientCore`，让 WinForms 客户端先接新会话信息。

验收：玩家可从门户登录并选择区服；旧客户端直连路径不回归；Gateway 故障可旁路恢复。

### P2：可回放与实例房间（6～10 周）

- ZoneServer 增加只追加 command journal 和周期状态摘要。
- 新增实例化房间/匹配服务，结果回写区服。
- 增加观战、回放查询和争议审计。
- 先选择 1～2 个低风险玩法试运行，不直接改主世界。

验收：一局实例 PvP 可创建、开局、结束、回放；重复请求幂等；资产变更只在区服侧生效。

### P3：UGC 地图/Mod（6～10 周）

- `Crystal.Ugc`、对象存储、上传 ticket、审核状态机、举报和作者页。
- 地图/Mod 摘要参与开局协商。
- 客户端增加地图库、详情、下载、更新和作者入口。

验收：玩家可上传、审核、发布、下架；区服拒绝未发布或摘要不匹配的内容；不打包原版资源。

### P4：客户端平台与运营加固（持续）

- launcher 自更新、灰度、渠道、CDN、发布指针。
- 客户端渲染跨平台可行性 spike；只有产品目标明确后进入正式排期。
- OpenTelemetry/Prometheus、告警、会话漏斗、版本分布、UGC 审核时长。

验收：可以安全发布多平台产物；核心运营指标可查询、可告警、可回溯。

## 7. 不建议做的事

- 不要把 Crystal 的持久世界改成 VERA20K 的客户端权威确定性 lockstep。
- 不要为了“像 VERA20K”把服务端逻辑整体重写成 Rust；这是产品重写，不是架构重构。
- 不要直接引入 Nakama 作为控制面数据库或管理后台；可以借鉴契约，但不增加不必要的状态后端。
- 不要让 Control API 同时承担玩家账号、公开统计、UGC 审核和管理员权限。
- 不要把 UGC 内容和官方签名内容混在同一个发布生命周期。
- 不要让 Gateway 第一版解析全部游戏包；先做路由、限流和版本门禁。
- 不要在未解决浮点、线程和随机源问题前承诺“完全确定性”。
- 不要复制 VERA20K 的 GPL 代码到 MIT 控制面；跨边界只使用自己定义的 MIT 契约。

## 8. 风险与重新评估条件

| 风险 | 影响 | 缓解 |
| --- | --- | --- |
| 游戏类型差异 | 误把 RTS 会话模型套到 MMO | 以服务端权威为不变约束，只借边缘/可验证性 |
| 旧客户端协议 | 新 Gateway 可能破坏现有连接 | 旧路径透明转发，新握手下沉为可选能力 |
| 版本矩阵膨胀 | 多客户端/内容组合难测试 | 先定义 N-1 兼容窗口和升级门禁 |
| 确定性成本 | `Envir` 多线程/浮点难以完整重演 | 先做 command journal + state hash，不承诺全量重演 |
| UGC 审核 | 恶意地图/Mod、版权和资源携带 | 自动校验 + 人工审核 + 举报 + 下架 |
| 许可证 | 服务端 GPL、素材版权、目标站 GPL | 只借鉴架构，保持现有 GPL/MIT 边界和素材清单 |
| 跨平台客户端 | WinForms/Direct3D9 迁移量巨大 | ClientCore 与渲染分层，跨平台单独立项 |
| 运营面膨胀 | 玩家门户变成第二套后台 | 明确玩家面/管理面/区服面三个边界 |

重新评估条件：

- 产品明确需要跨平台或浏览器客户端时，重新评估确定性 MatchCore 和渲染层。
- 实例 PvP 延迟/作弊成为瓶颈时，重新评估 UDP relay、客户端预测和回放。
- 玩家账号规模超过现有 Control API 承载边界时，再评估独立身份服务或 Nakama 兼容实现。
- 目标团队公开后台实现或与 Crystal 达成代码复用协议时，重新核对本文的推断部分。

## 9. 证据与置信度

### 9.1 直接观察

- `https://www.ra20k.com/` 的标题、营销文案和下载入口。
- `/lobby`、`/maps`、`/mods` 返回同一 SPA 壳和 Vue/Vite 资源。
- `https://www.ra20k.com/nodes.json` 返回节点注册表。
- `https://sh2-vera20k.libisky.com/vera/config`、`/vera/ready`、`/vera/stats` 返回公开 JSON。
- `https://download.ra20k.com/client/update/latest.json` 返回平台安装包和签名。
- `github.com/YuriPlanet/vera20k` 的 README、`docs/index.md`、`docs/Networking.md`、`docs/lobby-protocol.md`、`docs/wasm*.md`、`services/relay/README.md` 和 Rust workspace 文件。

### 9.2 高置信推断

- 前端使用 Nakama 兼容的 `/v2` 契约和实时 socket；后端具体实现未公开。
- 站点后台、审核工作流、节点调度和运营配置的实现细节未公开。
- VERA20K 的 30 人/20,000 单位/500×500 地图是项目自述目标，本文未做性能复现。

### 9.3 未验证

- 目标站后台技术栈、数据库、对象存储、部署区域和容量。
- UGC 审核是否完全人工、是否有自动恶意文件扫描。
- 反作弊、账号安全和支付/订阅的真实边界。
- Nakama 契约是否由官方 Nakama 提供，还是定制兼容服务。
