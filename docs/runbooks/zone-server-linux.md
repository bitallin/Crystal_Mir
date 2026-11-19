# Linux 区服部署与运行

> 最后更新：2026-11-18
> 状态：生效

## 1. 适用范围

在 Ubuntu 20.04+ 主机上部署单个 Crystal 区服。当前支持两种互斥模式：

- legacy/直接启动：使用 `crystal-zone.service` 和旧格式 `Server.MirDB`、`Configs/`、`Envir/`、`Maps/`，或手工传入已签名
  `--content-release` 做启动期消费。
- Agent 模式：使用 `crystal-zone-agent.service`，由 `Crystal.ZoneAgent` 负责注册、心跳、下载验签、staging、进程级
  激活/回滚和异常退出重启。安装与换版步骤见 `zone-agent-linux.md`。

同一区服只能启用其中一种 systemd 单元，不能同时占用游戏端口和状态端口。

`Crystal.ZoneServer` 链接 GPL-3.0-only 的 `Server.Library`。对外分发区服二进制时，必须同时提供对应完整源码和构建脚本，参见 `docs/adr/0004-gpl-boundary.md`。

## 2. 前置检查

```bash
./scripts/build.sh
./scripts/zone-smoke.sh
./scripts/publish-zone.sh artifacts/zone-publish
```

`zone-smoke.sh` 使用 `.external/Crystal.Database/Jev`，验证数据库、463 张地图、NPC/任务路径、端口监听和 SIGTERM 优雅退出。若内容在其他目录：

```bash
CRYSTAL_ZONE_SMOKE_CONTENT=/srv/crystal/content ./scripts/zone-smoke.sh
```

## 3. 一键安装

默认安装位置：

| 内容 | 路径 |
| --- | --- |
| 程序 | `/opt/crystal/zone` |
| 运行内容 | `/var/lib/crystal/zone` |
| 环境变量 | `/etc/crystal/zone.env` |
| systemd 单元 | `/etc/systemd/system/crystal-zone.service` |

```bash
sudo ./scripts/install-zone.sh \
  --content .external/Crystal.Database/Jev \
  --start
```

脚本行为：

- 发布自包含 `linux-x64` 程序，不依赖目标机安装 .NET。
- 若运行目录没有 `Server.MirDB`，复制指定内容；已有内容不会被覆盖。
- 创建系统用户 `crystal`，安装并启用 systemd 服务。
- `--start` 会立即重启服务。

## 4. 手工安装

```bash
sudo useradd --system --home /var/lib/crystal --shell /usr/sbin/nologin crystal
sudo install -d /opt/crystal/zone
sudo install -d -o crystal -g crystal -m 0750 /var/lib/crystal/zone
sudo cp -a artifacts/zone-publish/. /opt/crystal/zone/
sudo cp -a /path/to/content/. /var/lib/crystal/zone/
sudo install -d /etc/crystal
sudo install -m 0644 deploy/systemd/crystal-zone.service /etc/systemd/system/
sudo install -m 0640 -o root -g crystal deploy/systemd/zone.env.example /etc/crystal/zone.env
sudo chown -R crystal:crystal /var/lib/crystal/zone
sudo systemctl daemon-reload
sudo systemctl enable --now crystal-zone
```

## 5. 配置

编辑 `/etc/crystal/zone.env` 后重启服务：

```bash
sudoedit /etc/crystal/zone.env
sudo systemctl restart crystal-zone
```

`Setup.ini` 仍是游戏规则配置来源；环境变量覆盖监听和运行参数：

| 变量 | 默认/说明 |
| --- | --- |
| `CRYSTAL_ZONE_ID` | 日志中的区服标识 |
| `CRYSTAL_ZONE_IP` | 游戏监听地址；远端连接使用 `0.0.0.0` |
| `CRYSTAL_ZONE_PORT` | 游戏端口，默认 `7000` |
| `CRYSTAL_ZONE_STATUS_PORT` | 状态端口，默认 `3000` |
| `CRYSTAL_ZONE_MAX_USER` | 最大在线数 |
| `CRYSTAL_ZONE_MAX_IP` | 单 IP 最大连接数 |
| `CRYSTAL_ZONE_THREADS` | 怪物处理线程数 |
| `CRYSTAL_ZONE_SAVE_DELAY_MINUTES` | 自动保存间隔 |
| `CRYSTAL_ZONE_VERSION_HASH_VALUES` | 追加到版本白名单的字面量哈希，逗号分隔；Web 构建哈希使用此项 |
| `CRYSTAL_ZONE_READY_FILE` | ready JSON 路径；Agent 模式使用，legacy 可留空 |
| `CRYSTAL_ZONE_METRICS_FILE` | 运行时指标 JSON 路径；Agent 模式使用，legacy 可留空 |
| `CRYSTAL_ZONE_CONTENT_RELEASE` | 可选，已验签发布目录；不设置时使用 legacy `Server.MirDB` |
| `CRYSTAL_ZONE_CONTENT_PUBLIC_KEY` | 发布验签公钥；使用发布模式时必填 |
| `CRYSTAL_ZONE_CONTENT_KEY_ID` | 期望的发布签名 key id；使用发布模式时必填 |

状态端口和游戏端口使用同一监听地址。若绑定 `0.0.0.0`，用防火墙限制 `3000` 只允许监控网访问：

```bash
sudo ufw allow 7000/tcp
sudo ufw deny 3000/tcp
```

## 6. 启动与验证

启动前检查：

```bash
sudo -u crystal /opt/crystal/zone/Crystal.ZoneServer \
  --content-root /var/lib/crystal/zone --check
```

启动：

```bash
sudo systemctl start crystal-zone
sudo systemctl status crystal-zone
sudo journalctl -u crystal-zone -f
```

预期日志：

```text
Content root: /var/lib/crystal/zone
Starting zone server...
Network Started.
Zone ready: maps=..., mapsLoaded=..., items=..., monsters=..., npcs=..., quests=...
```

检查端口：

```bash
ss -ltnp | grep -E ':(7000|3000)\b'
tail -f /var/lib/crystal/zone/Logs/zone.log
```

## 7. 停止、升级与回滚

### 7.1 legacy/直接启动

```bash
sudo systemctl stop crystal-zone
```

升级程序时保留 `/var/lib/crystal/zone`：

```bash
./scripts/publish-zone.sh artifacts/zone-publish
sudo systemctl stop crystal-zone
sudo find /opt/crystal/zone -mindepth 1 -depth -delete
sudo cp -a artifacts/zone-publish/. /opt/crystal/zone/
sudo -u crystal /opt/crystal/zone/Crystal.ZoneServer \
  --content-root /var/lib/crystal/zone --check
sudo systemctl start crystal-zone
```

回滚时恢复上一版 `/opt/crystal/zone` 和内容备份，再执行 `--check`。数据库和 `Envir/Goods` 等运行数据必须在升级前备份。

### 7.2 Agent 管理模式

Agent 模式下不要直接替换运行目录或手工停止 ZoneServer；使用 Control API/CLI 命令：

```bash
./scripts/zone-control.sh stage <zoneId> <releaseId>
./scripts/zone-control.sh activate <zoneId> <releaseId>
./scripts/zone-control.sh rollback <zoneId> <releaseId>
./scripts/zone-control.sh reload <zoneId>
./scripts/zone-control.sh stop <zoneId>
```

`stage` 只下载和验签，不改变运行版本；`activate`、`rollback` 和 `reload` 会短暂停机并重启 ZoneServer；候选 ready 失败时
Agent 自动恢复上一 active 版本。完整目录、systemd 和故障处理见 `zone-agent-linux.md`。

### 7.3 ready 文件

手工启动或排查时可指定：

```bash
sudo -u crystal /opt/crystal/zone/Crystal.ZoneServer   --content-root /var/lib/crystal/zone   --content-release /var/lib/crystal/agent/releases/123-<hash>   --content-public-key /etc/crystal/release-public.pem   --content-key-id local-dev   --ready-file /var/lib/crystal/agent/state/ready.json
```

ready 文件是原子写入的 JSON，包含 `pid`、`readyAt`、内容根和地图/物品等计数；进程正常或异常退出时会删除。
不要用旧 ready 文件判断新进程状态，必须核对 `pid`。

### 7.4 指标文件

ZoneServer ready 后可通过 `--metrics-file <path>` 或 `CRYSTAL_ZONE_METRICS_FILE` 每秒原子写入运行时指标：

```json
{
  "schemaVersion": 1,
  "zoneId": "zone-1",
  "pid": 1234,
  "processStartedAt": "2026-11-18T00:00:00Z",
  "observedAt": "2026-11-18T00:00:10Z",
  "playerCount": 37,
  "tickP95Ms": 42.5,
  "errorRate": 0.03,
  "completedTicks": 12345,
  "errorCount": 7,
  "sampleCount": 600,
  "windowSeconds": 60
}
```

- 采样窗口 60 秒，每 100 ms 检查一次世界循环计数；P95 使用最近秩法。
- 错误率为窗口异常增量/Tick 增量，上限 1；没有 Tick 但有异常时按 1 处理。
- 指标文件在启动和退出时删除；写入失败只记日志，不终止区服。
- Agent 会校验 `schemaVersion`、`zoneId`、`pid`、时间范围和 15 秒时效；手工排查时也必须核对 PID，不能只看文件名。

## 8. 故障排查

| 现象 | 检查 |
| --- | --- |
| 启动超时 | `journalctl -u crystal-zone`；确认 `Server.MirDB` 版本在支持范围内 |
| 端口占用 | `ss -ltnp`；修改 `CRYSTAL_ZONE_PORT`/`CRYSTAL_ZONE_STATUS_PORT` |
| 地图或任务缺失 | `--check` 输出的缺失引用；确认文件名大小写和反斜杠引用已由解析器处理 |
| 权限错误 | `/var/lib/crystal/zone` 必须属于 `crystal` |
| 客户端连不上 | 检查 `CRYSTAL_ZONE_IP`、云安全组和 `7000/tcp` |
| 日志未生成 | 检查工作目录和 `Logs/` 写权限 |
| 指标一直为空 | 确认传入 `--metrics-file`/`CRYSTAL_ZONE_METRICS_FILE`；检查目录写权限和日志中的指标写入错误 |
| 指标显示 unavailable | 核对 `schemaVersion`、`zoneId`、`pid`、`observedAt` 和 15 秒时效；旧 PID 文件不可复用 |
| Tick P95 为 0 | 世界循环尚未完成采样或区服刚启动；等待一个采样窗口后复查 |
