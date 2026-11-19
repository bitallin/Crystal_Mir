# 浏览器客户端与网关架构

> 最后更新：2026-11-19
> 状态：生效（W0 已闭环，W1/W2 实施中，W3 未开始）

## 1. 结论与范围

浏览器直玩采用“独立原生 Web 客户端 + WebSocket 字节网关 + 现有权威区服”的路线。玩家打开网页即可登录、选角和进入同一区服，不安装桌面客户端，不使用云游戏串流，也不把 WinForms 客户端嵌入浏览器。

首版范围是需求文档定义的可玩 MVP：现代桌面浏览器和横屏手机/平板；服务端战斗、地图、AI 和主循环保持权威。浏览器只做输入、表现层预测、动画插值和 HUD，不拥有角色状态。

当前已建立的垂直闭环是：应用壳 -> bootstrap -> WSS -> `ClientVersion`/`Login` -> 选角 -> `StartGame` -> `MapInformation` -> 地图包/图库懒加载 -> 移动与基础交互。真实素材上的完整动画、光照、音效、Safari/真机性能和完整业务回归仍属于后续验收项。

## 2. 拓扑与边界

```text
Browser
  -> Nginx/CDN: /game/、/assets/、/api/web/、/ws/
  -> Crystal.Web.Gateway: WebSocket <-> TCP 字节桥
  -> Crystal.ZoneServer: 现有游戏服务端与二进制协议
```

Web 客户端和网关是新的 MIT 边界；网关只使用 `Shared` 的协议字节语义，不链接 `Server/`、`Server.MirForms/` 或 WinForms。区服仍是 GPL 边界内的权威运行时，`Envir`/`Map` 行为不因浏览器入口而改变。

## 3. 组件

| 组件 | 职责 | 不负责 |
| --- | --- | --- |
| `web-client/` | 应用壳、登录/选角、场景、渲染、HUD、输入、资源缓存、重连 | 不计算战斗结果、不直接写角色状态 |
| `Crystal.Web.Gateway` | WebSocket/TCP 字节桥、Origin、子协议、帧上限、限流、背压、空闲关闭、健康与指标 | 不解析业务包、不修改包体 |
| `Crystal.WebAssets` | `.lib`/`.map` 解析、PNG 图集、地图包、manifest、确定性哈希 | 不依赖 WinForms/SlimDX |
| `tools/Crystal.WebAssetCompiler` | 离线资源转换 CLI；必须接收 Web 构建哈希 | 不参与运行时游戏 |
| 静态资源层 | `/assets/<version>/`、内容哈希、不可变缓存、CDN 回源 | 不信任客户端拼接的路径 |

## 4. Web 客户端模块

```text
src/
  app/         启动、错误边界和会话入口
  session/     bootstrap、WebSocket、登录、选角、StartGame、重连状态机
  protocol/    二进制分帧、PacketCodec、MVP 包注册表
  world/       地图模型、实体、状态 reducer、可走格寻路
  render/      PixiJS 地图层、实体、摄像机、裁剪、伤害数字、聊天气泡
  ui/          Preact 登录/选角/HUD/聊天/背包/装备/NPC/商店
  assets/      manifest、地图包、图库、纹理缓存和哈希校验
  input/       桌面键鼠、点击寻路、移动虚拟摇杆与动作按钮
  styles/      响应式布局、安全区、横屏和竖屏门禁
```

客户端状态分为 `SessionState`、`WorldState`、`UiState` 和 `AssetState`。服务端包进入显式 handler/reducer；未知包只做采样日志并忽略，关键包解码失败则关闭会话。

## 5. 渲染与交互

- PixiJS 绘制地图后/中/前层、按 Y 排序的实体、摄像机、伤害数字和聊天气泡；地图按可见区域裁剪，图库按地图和服务端图库 ID 懒加载。
- 当前实体在没有真实角色图集时使用稳定的几何占位；真实角色/怪物/NPC 动画、门动画、混合图块、光照和音效尚未完成，不以占位渲染冒充最终资源验收。
- Preact/CSS 负责登录、选角、角色状态、聊天、背包、装备、NPC、商店、断线遮罩和移动操作层；不复制 WinForms 的固定坐标控件树。
- 桌面支持点击寻路、WASD/方向键、鼠标选目标、`Space` 攻击、`F` 拾取和面板快捷键；手机/平板横屏使用左侧摇杆、右侧攻击/拾取/背包按钮，竖屏只显示旋转提示。
- 点击怪物/物品/NPC 时，客户端可在本地可走格上规划路径并发送 `Walk`/`Run`，但每一步位置和动作都以服务端包为准。

## 6. 协议、编解码与版本门禁

- 帧格式沿用 `2 字节长度 + 2 字节包号 + 包体`；一个 WebSocket 二进制消息可以包含一个或多个完整包。
- WebSocket 子协议固定为 `crystal-v1`，只接受二进制消息；网关不解析业务的 `Packet`。
- Web 客户端按包号显式注册 `PacketCodec`，不使用运行时反射；`tools/Crystal.ProtocolSamples` 从 `Shared` 包类生成二进制样本，TypeScript 测试逐字节回放。
- `web-client/dist/client-build.json` 是构建身份来源。其 `clientBuildHash` 必须同时传给：
  - Gateway：`CRYSTAL_WEB_CLIENT_BUILD_HASH`；
  - WebAssetCompiler：`--build-hash`；
  - 区服版本白名单：`Configs/Setup.ini` 的 `[General] VersionHashValues` 或环境变量 `CRYSTAL_ZONE_VERSION_HASH_VALUES`。
- 客户端把该字符串按 UTF-8 写入 `ClientVersion.VersionHash`。区服保留原有 `VersionPath` 文件 MD5 白名单，并额外接受 `VersionHashValues` 中的字面量；旧 Windows 客户端行为不变。
- 开发环境可设 `CheckVersion=false`，但测试区/生产区不得以关闭校验替代 Web 版本门禁。
- 关键包解码失败时客户端以 `4002` 关闭浏览器 WebSocket；网关自身使用标准关闭码：非二进制 `1003`、限流/空闲 `1008`、过大 `1009`、上游不可用 `1013`。

## 7. Web 资源包

资源编译器从外部 `Client/Data`、`Client/Map` 和 `Client/Sound` 读取研究素材，产物写入独立目录，素材与生成物不提交仓库。当前 `.lib` 图集页面格式为 **PNG**；不使用 WebP。

顶层 `/assets/<version>/manifest.json` 的实际字段：

```json
{
  "schemaVersion": 1,
  "assetVersion": "2026.11.19.1",
  "clientBuildHash": "sha256:...",
  "libraries": {},
  "maps": {},
  "mapLibraries": {}
}
```

- `libraries[id]`：`sourceHash`、`framesUrl`、`pages`、`maskPages`、`hash`。
- `maps[id]`：`sourceHash`、`format`、`width`、`height`、`cellOrder`、`cellStride`、`metaUrl`、`cellsUrl`、`libraryIds`、`hash`。
- `mapLibraries[id]`：地图库数字 ID 到 `maplib-{id}` 图库键的映射。
- `map/<id>/meta.json`：`schemaVersion`、`file`、`sourceHash`、`format`、尺寸、`cellOrder`、`cellStride`、`cellsUrl` 和 `libraries`；`meta.json` 自身不重复保存 `hash`。
- `cells.bin`：小端、固定 32 字节/格、`x-major` 顺序，包含后/中/前层索引和图像、门、动画、光照、可走性和钓鱼格。
- `lib/<id>/frames.json`：图集页面、帧矩形、偏移、阴影、遮罩页面和 `maskOffsetX`/`maskOffsetY`；`pages` 与 `maskPages` 使用相对路径。

编译器要求输入 `--build-hash`，缺失直接失败；同一输入、版本和构建哈希生成相同文件与哈希。引用地图库时缺失 `.lib` 直接失败，不生成半成品。音频索引与转码尚未实现，不阻塞首版核心闭环，后续必须补入 manifest 后再启用声音。

## 8. 失败模式

| 故障 | 行为 |
| --- | --- |
| 网关不可用 | 启动页显示连接错误和重试入口 |
| 区服 TCP 不可用 | WebSocket `1013` 关闭，客户端显示区服不可用 |
| WebGL2 不可用 | 阻断式浏览器不支持页面，不降级 Canvas2D |
| 资源缺失/哈希不符 | 阻断进入地图，提示资源版本不匹配 |
| 版本哈希不匹配 | 区服返回 `ClientVersion.Result=0`，客户端阻断并提示更新版本 |
| 关键包解码失败 | 关闭会话、记录采样遥测、提示重连 |
| 网络断开 | 暂停输入，最多 5 次退避重连（1/2/4/8/16 秒），重新登录并回选角 |
| 移动 Tab 挂起 | 恢复后检查连接，失效则重连 |

## 9. 部署与观测

- Nginx 提供 `/game/` 与 `/assets/`，代理 `/api/web/`、`/ws/` 和 `/health/`；生产只开放 HTTPS/WSS。
- 网关默认拒绝缺失 `Origin` 的 WebSocket；只有显式设置 `CRYSTAL_WEB_ALLOW_MISSING_ORIGIN=true` 才允许非浏览器客户端。仅允许配置中的 Origin、`crystal-v1` 子协议和二进制帧。
- 默认限制：单帧 256 KiB、180 帧/秒、4 MiB/秒、256 连接、30 秒空闲超时。Nginx 的 WebSocket 读/写超时应长于网关空闲超时。
- 网关在 `Production` 环境启动时拒绝 `sha256:dev`、`assetVersion=dev` 和通配 Origin，避免把开发占位版本带到测试区/生产。
- 网关输出健康、连接计数、拒绝原因、字节数、背压和关闭原因等结构化指标；不记录密码、聊天内容或游戏包体。
- 首版固定单区服，`/ws/game?zone=<id>` 已保留路由参数；多区服、票据 SSO、跨区会话和迁移不在首版范围。

## 10. 实施状态与剩余门禁

| 阶段 | 状态 | 已交付 | 未完成 |
| --- | --- | --- | --- |
| W0 垂直切片 | 已闭环 | 网关、协议样本、登录/选角/进图、单地图转换、基础移动、浏览器冒烟 | 真实区服版本门禁联调 |
| W1 世界与资源 | 部分完成 | 完整编译链路、地图分层、摄像机、桌面/移动输入、基础 HUD | 真实角色/怪物动画、门/光照/音效、真实 `.lib` 回归 |
| W2 可玩 MVP | 部分完成 | 普通战斗、死亡/复活、拾取、聊天、背包/装备、背包内物品移动、NPC/商店、创建/删除、重连 | 真机端到端验收、更多业务边界和异常回归 |
| W3 设备与稳定性 | 未开始 | 网关限流和基础冒烟已具备 | Safari/真机矩阵、性能/内存、视觉回归、故障演练 |

浏览器入口通过独立 URL 或功能开关上线，先在一个测试区服验证，不替换旧客户端入口。
