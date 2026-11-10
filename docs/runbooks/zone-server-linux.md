# Linux 区服部署与运行

> 最后更新：2026-11-10
> 状态：生效

## 1. 适用范围

在 Ubuntu 20.04+ 主机上部署单个 Crystal 区服。当前区服仍直接使用旧格式 `Server.MirDB`、`Configs/`、`Envir/` 和 `Maps/`，尚未接入内容快照发布和 Zone Agent。

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

## 8. 故障排查

| 现象 | 检查 |
| --- | --- |
| 启动超时 | `journalctl -u crystal-zone`；确认 `Server.MirDB` 版本在支持范围内 |
| 端口占用 | `ss -ltnp`；修改 `CRYSTAL_ZONE_PORT`/`CRYSTAL_ZONE_STATUS_PORT` |
| 地图或任务缺失 | `--check` 输出的缺失引用；确认文件名大小写和反斜杠引用已由解析器处理 |
| 权限错误 | `/var/lib/crystal/zone` 必须属于 `crystal` |
| 客户端连不上 | 检查 `CRYSTAL_ZONE_IP`、云安全组和 `7000/tcp` |
| 日志未生成 | 检查工作目录和 `Logs/` 写权限 |
