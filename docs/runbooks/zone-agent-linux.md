# Zone Agent Linux 安装、换版与恢复

> 最后更新：2026-11-17
> 状态：生效（研究项目闭环；未执行真实 systemd 安装演练）

## 1. 适用范围与边界

本手册用于一个区服由一个 `Crystal.ZoneAgent` 监督一个 `Crystal.ZoneServer` 子进程的 Linux 部署。
Agent 负责注册、心跳、命令领取、发布包下载/验签、本地 staging、SIGTERM 和 ready 检查；Control API 不直接操作进程。

当前模型接受短暂停机：

- 激活/回滚会停止旧 ZoneServer，再启动候选版本。
- 候选 ready 失败时自动恢复上一 active 版本。
- 候选和旧版本都不可用时进入 degraded，保留日志和错误供排查。
- 不做双实例、进程内热切换、灰度/CDN、玩家状态迁移或多 Agent HA。

`crystal-zone.service` 仍用于 legacy 或直接启动模式。启用 `crystal-zone-agent.service` 前必须停止并禁用
`crystal-zone.service`，避免两个进程同时占用游戏和状态端口。

## 2. 前置检查

```bash
./scripts/build.sh
./scripts/check-boundaries.sh
./scripts/test.sh

dotnet publish src/Crystal.ZoneAgent/Crystal.ZoneAgent.csproj \
  -c Release -r linux-x64 --self-contained true -o artifacts/zone-agent-publish
./scripts/publish-zone.sh artifacts/zone-publish
```

Agent 只引用 `Crystal.Agent.Contracts` 和 `Crystal.Content`；`check-boundaries.sh` 必须通过。ZoneServer 仍为 GPL
边界内程序，分发时按 `adr/0004-gpl-boundary.md` 提供完整源码和构建脚本。

准备：

- 已发布且签名有效的 release；状态必须是 `published` 或 `active`。
- Release 公钥 PEM 和 key id；必须与控制面签名配置一致。
- 区服内容根，至少包含 `Maps/`、`Configs/`、`Envir/` 和 legacy `Server.MirDB`。
- Control API 地址和现有 `X-Api-Key`（如果启用）。

## 3. 目录约定

| 内容 | 示例路径 |
| --- | --- |
| Agent 程序 | `/opt/crystal/agent` |
| ZoneServer 程序 | `/opt/crystal/zone` |
| Agent 状态/缓存/日志 | `/var/lib/crystal/agent` |
| Zone 运行内容根 | `/var/lib/crystal/zone` |
| 环境变量 | `/etc/crystal/zone-agent.env` |
| Release 公钥 | `/etc/crystal/release-public.pem` |
| systemd 单元 | `/etc/systemd/system/crystal-zone-agent.service` |

Agent home 下：

```text
releases/{releaseId}-{contentHash}/   已验签发布目录
staging/                              临时 ZIP 和解压目录
state/active.json                     当前/上一版本、内容哈希和激活时间
state/ready.json                      ZoneServer ready 文件（含 pid）
state/command-results/                已执行命令结果
logs/zone-{releaseId}-{time}.log      子进程 stdout/stderr
```

## 4. 安装

```bash
sudo useradd --system --home /var/lib/crystal --shell /usr/sbin/nologin crystal
sudo install -d /opt/crystal/agent /opt/crystal/zone
sudo install -d -o crystal -g crystal -m 0750 /var/lib/crystal/agent /var/lib/crystal/zone
sudo cp -a artifacts/zone-agent-publish/. /opt/crystal/agent/
sudo cp -a artifacts/zone-publish/. /opt/crystal/zone/
sudo cp -a /path/to/content/. /var/lib/crystal/zone/
sudo install -d /etc/crystal
sudo install -m 0640 -o root -g crystal /path/to/release-public.pem /etc/crystal/release-public.pem
sudo install -m 0640 -o root -g crystal deploy/systemd/zone-agent.env.example /etc/crystal/zone-agent.env
sudo install -m 0644 deploy/systemd/crystal-zone-agent.service /etc/systemd/system/
sudo chown -R crystal:crystal /var/lib/crystal/agent /var/lib/crystal/zone
```

编辑 `/etc/crystal/zone-agent.env`，至少核对：

```bash
CRYSTAL_AGENT_CONTROL_URL=https://control.example.com
CRYSTAL_AGENT_API_KEY=change-me
CRYSTAL_ZONE_ID=zone-1
CRYSTAL_ZONE_NAME=Zone 1
CRYSTAL_AGENT_ID=zone-1-agent
CRYSTAL_AGENT_HOME=/var/lib/crystal/agent
CRYSTAL_ZONE_HOME=/var/lib/crystal/zone
CRYSTAL_AGENT_ZONE_EXECUTABLE=/opt/crystal/zone/Crystal.ZoneServer
CRYSTAL_ZONE_CONTENT_PUBLIC_KEY=/etc/crystal/release-public.pem
CRYSTAL_ZONE_CONTENT_KEY_ID=local-dev
CRYSTAL_ZONE_IP=0.0.0.0
CRYSTAL_ZONE_PORT=7000
CRYSTAL_ZONE_STATUS_PORT=3000
```

## 5. 启动与验证

```bash
sudo systemctl daemon-reload
sudo systemctl disable --now crystal-zone 2>/dev/null || true
sudo systemctl enable --now crystal-zone-agent
sudo systemctl status crystal-zone-agent
sudo journalctl -u crystal-zone-agent -f
```

预期 Agent 日志包含注册成功；首次没有 active 版本时保持 degraded，等待 activate 命令。

```bash
CRYSTAL_CONTROL_URL=https://control.example.com \
CRYSTAL_CONTROL_API_KEY=change-me \
  ./scripts/zone-control.sh zones

CRYSTAL_CONTROL_URL=https://control.example.com \
CRYSTAL_CONTROL_API_KEY=change-me \
  ./scripts/zone-control.sh zone zone-1
```

检查 Agent 本地状态：

```bash
sudo -u crystal cat /var/lib/crystal/agent/state/ready.json
sudo -u crystal cat /var/lib/crystal/agent/state/active.json
sudo -u crystal ls -l /var/lib/crystal/agent/releases
sudo -u crystal tail -f /var/lib/crystal/agent/logs/zone-*.log
```

`ready.json` 的 `pid` 必须与当前 ZoneServer 子进程一致；`active.json` 必须记录当前 release ID、版本、内容哈希和目录。

## 6. Stage、激活与回滚

先发布并签名内容，再 stage：

```bash
CRYSTAL_CONTROL_URL=https://control.example.com \
CRYSTAL_CONTROL_API_KEY=change-me \
  ./scripts/zone-control.sh stage zone-1 123
```

输出包含 `commandId=...`。等待并检查：

```bash
./scripts/zone-control.sh wait zone-1 <commandId> 300
./scripts/zone-control.sh zone zone-1
```

成功后 `stagedReleaseId` 应为目标版本，当前 ZoneServer 不应变化。激活：

```bash
./scripts/zone-control.sh activate zone-1 123
./scripts/zone-control.sh wait zone-1 <commandId> 600
./scripts/zone-control.sh zone zone-1
```

激活命令成功表示旧进程已停止、候选 ready、`active.json` 已写入；控制面投影的 `activeReleaseId` 和
`activeContentHash` 应匹配。

回滚必须指定一个已发布或已激活的目标：

```bash
./scripts/zone-control.sh rollback zone-1 122
./scripts/zone-control.sh wait zone-1 <commandId> 600
```

`reload` 使用当前 active 发布重启进程：

```bash
./scripts/zone-control.sh reload zone-1
```

`stop` 停止子进程并清空 active 状态：

```bash
./scripts/zone-control.sh stop zone-1
./scripts/zone-control.sh wait zone-1 <commandId> 120
```

## 7. 失败恢复

### 7.1 发布包篡改或下载失败

stage 命令返回 `failed`，Agent 删除 staging 临时目录，旧 ZoneServer 继续运行，active 版本不变。检查：

```bash
./scripts/zone-control.sh commands zone-1 --limit 20
sudo journalctl -u crystal-zone-agent --since '10 minutes ago'
```

修复发布或重新发布后，创建新的 stage 命令。不要手工修改 `releases/` 下的目录；哈希校验会拒绝。

### 7.2 候选启动失败或 ready 超时

Agent 会 SIGTERM 候选进程，并重新启动上一 active 版本。命令结果为 `failed`，通常带
`resultPayload.restoredPrevious=true`。检查：

```bash
sudo -u crystal cat /var/lib/crystal/agent/state/active.json
sudo -u crystal tail -n 200 /var/lib/crystal/agent/logs/zone-*.log
./scripts/zone-control.sh zone zone-1
```

如果旧版本也未恢复，控制面 active 会被清空并进入 degraded；修复内容或端口后重新 `activate` 已知可用 release。

### 7.3 ZoneServer 意外退出

Agent 按 2、5、10、30 秒退避重启当前 active 版本，并递增 `restartFailures`。心跳在进程未运行时为 degraded，
metadata 包含 `lastExitCode`、`lastError` 和 `lastLogPath`。

### 7.4 Agent 重启

systemd 重启 Agent 后，它会读取 `state/active.json`，重新验签本地发布并启动上次 active 版本。
如果本地缓存缺失或损坏，Agent 保持 degraded；重新执行 `stage` 和 `activate`。

## 8. 升级与回滚 Agent

升级 Agent 会触发其优雅停止 ZoneServer 子进程，因此必须在维护窗口执行：

```bash
sudo systemctl stop crystal-zone-agent
sudo find /opt/crystal/agent -mindepth 1 -depth -delete
sudo cp -a artifacts/zone-agent-publish/. /opt/crystal/agent/
sudo systemctl start crystal-zone-agent
```

回滚时恢复上一版 `/opt/crystal/agent`。不要删除 `/var/lib/crystal/agent` 中的 `releases/` 和 `state/`，否则
Agent 无法恢复 active 版本。

## 9. 故障排查

| 现象 | 检查 |
| --- | --- |
| Agent 未注册 | Control API URL、网络、`X-Api-Key`、zone id 格式和 API 日志 |
| stage 失败 | 发布是否 `published/active`、签名 key id、公钥、哈希、磁盘空间和 Agent 日志 |
| activate 超时 | ZoneServer 日志、端口占用、地图/Envir 文件、`ready.json` 是否写入且 pid 匹配 |
| 候选失败后区服仍不可用 | `active.json` 上一版本目录、旧版本 artifact 完整性、内容根依赖 |
| 心跳显示 offline | systemd 状态、Agent 日志、最后心跳时间和 Control API 数据库 |
| 端口冲突 | 停止 `crystal-zone.service`；检查 `CRYSTAL_ZONE_PORT`/`CRYSTAL_ZONE_STATUS_PORT` |
| 权限错误 | `/var/lib/crystal/agent` 和 `/var/lib/crystal/zone` 属于 `crystal`；公钥可读 |

## 10. 审计与记录

每次 stage/activate/rollback/reload/stop 都保留 `commandId`、操作者、时间、目标 release、状态、错误和结果。
换版记录应同时写入运维变更单，并保存：

- 目标 release ID、版本和内容哈希。
- 旧/新 active 版本。
- 命令 ID、开始/完成时间、是否发生自动恢复。
- ZoneServer ready 日志和异常退出日志路径。
