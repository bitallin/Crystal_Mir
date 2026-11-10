# 本地开发环境

> 最后更新：2026-11-10
> 状态：生效

## 1. 依赖

| 依赖 | 版本/说明 |
| --- | --- |
| .NET SDK | 8.0 |
| Windows | 客户端运行需要 Windows；Linux 可运行区服和交叉构建客户端 |
| PostgreSQL | 16+（控制面目标） |
| Redis | 7+ |
| MinIO/S3 | 本地对象存储 |
| Node.js | 20+（管理后台前端） |
| Docker | 本地基础设施 |
| Git | 仓库管理 |

检查 SDK：

```bash
dotnet --info
```

如果使用用户目录安装，可设置：

```bash
export PATH="$HOME/.dotnet:$PATH"
```

## 2. 外部资源

```bash
./scripts/fetch-external.sh
```

资源会放在 `.external/`，该目录已被忽略，不提交到仓库。

- `Crystal.Database`：标准数据库、`Configs/`、`Envir/`、`Maps/`。
- `Crystal.MapEditor`：地图编辑和地图格式参考。
- 客户端 `Data/`、`Map/`、`Sound/`：当前仍缺失，需要单独准备。
- 服务端运行目录：可使用 `Crystal.Database/<variant>/` 作为起点。

详细清单见 `docs/reference/external-resources.md`。

黄金样本：

```text
tests/fixtures/golden/Server.MirDB
SHA256: 37358b0d95eafe458b060c1db2ebc0524757fe524fb93125ccb98d9b4a8ef78b
```

## 3. 当前构建

### Linux（已验证）

```bash
export PATH="$HOME/.dotnet:$PATH"
./scripts/build.sh
./scripts/test.sh
./scripts/inspect-db.sh
```

`Server.MirForms` 和 `Client` 在 Linux 上需要交叉构建参数：

```bash
dotnet build Server.MirForms/Server.csproj -c Release -p:EnableWindowsTargeting=true
dotnet build Client/Client.csproj -c Release -p:EnableWindowsTargeting=true
```

2026-11-09 验证结果：

- `Shared`、`Server.Library`、`Server.MirForms`、`Client` 构建成功。
- `Tests/ItemSecurity` 通过 358 项回归检查。
- 客户端构建有 7 个警告，主要是 `WindowsBase` 版本冲突和未使用字段；没有编译错误。

### Windows

```powershell
dotnet restore "Legend of Mir.sln"
dotnet build "Legend of Mir.sln" -c Debug
```

项目目标：

- `Server/Server.Library.csproj`：`net8.0`。
- `Server.MirForms/Server.csproj`：`net8.0-windows7.0`。
- `Client/Client.csproj`：`net8.0-windows7.0`。
- `Shared/Shared.csproj`：`net8.0`。

## 4. 数据库检查

```bash
./scripts/inspect-db.sh
./scripts/inspect-all-db.sh
```

检查器在临时目录运行，输出数据库版本、索引和实体数量，不会在仓库根目录生成 `Configs/` 或 `Envir/`。

结果见 `docs/reference/db-inventory.md`。

## 5. 当前运行

Linux 区服宿主已落地，可直接使用外部数据库样本运行：

```bash
export PATH="$HOME/.dotnet:$PATH"
./scripts/zone-smoke.sh
dotnet run --project src/Crystal.ZoneServer -c Release -- \
  --content-root .external/Crystal.Database/Jev
```

内容目录需要包含 `Server.MirDB`、`Configs/`、`Envir/` 和 `Maps/`。先做只读校验：

```bash
dotnet run --project src/Crystal.ZoneServer -c Release -- \
  --content-root .external/Crystal.Database/Jev --check
```

生产部署、systemd、端口和升级步骤见 `docs/runbooks/zone-server-linux.md`。

`Server.MirForms` 仍可在 Windows 上作为编辑器和调试宿主使用。准备客户端 `Data/`、`Map/`、`Sound/` 后启动 `Client`。

具体目录定义见 `Server/Settings.cs` 和 `Client/Settings.cs`。

## 6. 目标本地环境

控制面本地联调：

```text
docker compose up -d postgres redis minio
CRYSTAL_CONTROL_CORS_ORIGINS='http://127.0.0.1:4173' dotnet run --project src/Crystal.Control.Api
dotnet run --project src/Crystal.ZoneServer -- --content-root .external/Crystal.Database/Jev
./scripts/preview-control-web.sh
```

`src/Crystal.ZoneServer` 已实现；运行时需要 `--content-root` 或 `CRYSTAL_ZONE_HOME` 指向内容目录。

Control.Web 当前无 npm 构建链。打开 `http://127.0.0.1:4173/?api=http://127.0.0.1:5080#/login`
连接 Control API；不传 `api` 参数时使用原型演示数据。

## 7. 常见问题

| 问题 | 排查 |
| --- | --- |
| 客户端无法加载图库 | 检查 `Data/` 目录、`.lib` 文件大小写和路径 |
| 区服启动失败 | 检查 `Configs/Setup.ini`、端口占用、数据库版本 |
| 地图黑屏/无法进入 | 检查 `Maps/*.map`、`MapInfo` 文件名和地图库 |
| 数据库版本不兼容 | 当前 `MinVersion=60`、`Version=117`；NightWolf 版本 34 不支持 |
| 检查器在仓库生成配置目录 | 确认使用最新 `tools/Crystal.DbInspector`，它在临时目录运行 |
| 字符/中文乱码 | 检查文件编码、客户端语言包和字体资源 |

