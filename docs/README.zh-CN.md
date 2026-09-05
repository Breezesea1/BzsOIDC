# BzsOIDC

[English](./README.en.md) | [仓库根目录 README](../README.md)

BzsOIDC 是一个基于 `.NET 10` 的纯后端身份平台，核心应用为 `BzsOIDC.Idp`。它使用 ASP.NET Core Web API、OpenIddict、EF Core、Redis、PostgreSQL，并通过 .NET Aspire 编排本地开发环境。

## 仓库结构

```text
BzsOIDC/
├── src/
│   ├── BzsOIDC.AppHost/                 # Aspire 本地编排入口
│   ├── BzsOIDC.AppHost.ServiceDefaults/ # 服务默认配置 / OTEL / 健康检查
│   ├── BzsOIDC.Idp/                     # 主 Web 应用
│   ├── BzsOIDC.Idp.Migrator/            # 数据库迁移可执行项目
│   └── Shared/
│       └── BzsOIDC.Shared.Infrastructure/
└── tests/
    ├── BzsOIDC.Idp.UnitTests/
    ├── BzsOIDC.Idp.IntegrationTests/
```

## 技术栈

- .NET 10 / C#
- ASP.NET Core Web API
- OpenIddict
- EF Core + PostgreSQL
- Redis
- .NET Aspire
- xUnit、NSubstitute

## 本地运行方式

`src/BzsOIDC.AppHost/AppHost.cs` 定义了本地开发时的运行拓扑：

- PostgreSQL（带数据卷）
- Redis
- `BzsOIDC.Idp`
- `BzsOIDC.Idp.Migrator`

AppHost 会把配置注入应用，并在 migrator 完成后再让主 IDP 服务进入就绪状态。

在开发环境下，如果未显式配置管理员账号，AppHost 会提供默认值：

- 用户名：`admin`
- 密码：`Passw0rd!`

## 环境要求

- .NET SDK 10
- 已安装并可在 `PATH` 中访问的 Aspire CLI
- Aspire 支持的容器运行时，例如 Docker Desktop

## 快速开始

### 还原与构建

```bash
dotnet restore BzsOIDC.sln
dotnet build BzsOIDC.sln
```

### 启动完整本地环境

```bash
aspire run
```

### 仅启动 IDP 应用

```bash
dotnet run --project src/BzsOIDC.Idp/BzsOIDC.Idp.csproj
```

### 数据库迁移

在 `src/BzsOIDC.Idp/` 目录执行：

```bash
dotnet ef migrations add <MigrationName> --context IdpDbContext
dotnet ef database update --context IdpDbContext
```

## 测试

### 运行全部测试

```bash
dotnet test BzsOIDC.sln
```

### 运行单个测试项目

```bash
dotnet test tests/BzsOIDC.Idp.UnitTests/BzsOIDC.Idp.UnitTests.csproj
dotnet test tests/BzsOIDC.Idp.IntegrationTests/BzsOIDC.Idp.IntegrationTests.csproj
```

### 运行单个测试

```bash
dotnet test tests/BzsOIDC.Idp.UnitTests/BzsOIDC.Idp.UnitTests.csproj --filter "FullyQualifiedName=BzsOIDC.Idp.UnitTests.Controllers.PermissionScopesControllerTests.GetByPermission_WhenPermissionEmpty_ReturnsValidationProblem"
```

### 测试分层

- 单元测试：xUnit + NSubstitute
- 集成测试：ASP.NET Core TestHost + SQLite

## 文档

- 面向代码代理的仓库说明：[AGENTS.md](../AGENTS.md)
- GitHub CI/CD 与 Ubuntu Docker 部署方案：[github-cicd-ubuntu-docker-plan.md](./github-cicd-ubuntu-docker-plan.md)

## 部署说明

当前仓库更偏向使用 Aspire 进行本地开发编排。已提交的 `docs/github-cicd-ubuntu-docker-plan.md` 建议在生产环境中部署 `BzsOIDC.Idp` 与 `BzsOIDC.Idp.Migrator`，而不是直接把 AppHost 原样搬到生产环境。

## 版本发布

稳定版本使用无前缀的 `X.Y.Z` Git tag，例如 `1.2.3`。三个数字段必须采用规范写法，因此 CI 会拒绝 `v1.2.3`、`01.2.3` 等标签。

```bash
git tag -a 1.2.3 -m "Release 1.2.3"
git push origin 1.2.3
```

构建、测试和启动检查全部通过后，CI 会为两套 GHCR 镜像发布 `1.2.3`、`1.2`、`1`、`latest` 和 `sha-<commit>` 标签。普通 `main` 推送只发布 `edge` 与 `sha-<commit>`。生产部署应把 `IMAGE_TAG` 固定为完整且不可变的版本号，例如 `1.2.3`。
