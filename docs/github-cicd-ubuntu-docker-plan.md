# BzsOIDC IDP GitHub CI/CD 与 Ubuntu Docker 部署方案

## 目标

为当前 `BzsOIDC` 仓库设计一套适合本地 Ubuntu 服务器的 GitHub CI/CD 方案，实现以下目标：

- GitHub 上自动执行构建、格式检查、测试
- 自动构建并推送 `BzsOIDC.Idp` 容器镜像
- 自动部署到本地 Ubuntu 机器上的 Docker
- 将数据库迁移与种子初始化纳入发布流程
- 支持版本化发布与失败回滚

这份方案基于当前仓库的真实结构整理，而不是通用模板。

---

## 一、当前仓库结构与部署含义

当前仓库里与部署直接相关的部分如下：

- `src/BzsOIDC.AppHost/AppHost.cs`
  - 当前用 Aspire 在本地编排 `postgres`、`redis`、`idp`、`idp-migrator`
- `src/BzsOIDC.Idp/`
  - 实际长期运行的 IDP Web 应用
- `src/BzsOIDC.Idp.Migrator/`
  - 启动后执行数据库迁移和 `IdentitySeeder`

当前已确认的事实：

- 仓库里**还没有** Dockerfile
- 仓库里**还没有** `docker-compose.yml`
- 仓库里**还没有** GitHub Actions workflow
- `BzsOIDC.Idp.csproj` 在 `Build/Publish` 前会自动执行前端资源构建：
  - `npm run css:build`
  - `npm run gsap:copy`

这意味着 CI 机器除了 .NET SDK 之外，还必须具备 Node.js 环境。

---

## 二、推荐的生产部署形态

### 结论

**生产环境只部署 `BzsOIDC.Idp` 和 `BzsOIDC.Idp.Migrator`，不要把 Aspire AppHost 原样搬到生产。**

### 原因

`AppHost` 更适合本地开发编排和观测；你当前的生产目标只是将 IDP 部署到一台本地 Ubuntu Docker 主机。对于这种场景，最稳定、最直接、维护成本最低的方式是：

- Docker Compose 管理长期运行容器
- GitHub Actions 负责 CI 和镜像发布
- 通过 SSH 在 Ubuntu 主机上完成拉镜像与重启

### 生产拓扑建议

建议 Ubuntu 上运行以下组件：

- `reverse-proxy`：Nginx 或 Caddy
- `idp`：`BzsOIDC.Idp` 主应用
- `postgres`：数据库
- `redis`：缓存
- `idp-migrator`：一次性迁移任务，不常驻

其中：

- `idp` 是常驻服务
- `idp-migrator` 只在部署时运行一次

---

## 三、为什么必须把 Migrator 纳入发布流程

`src/BzsOIDC.Idp.Migrator/Program.cs` 已明确说明：

- 它会执行数据库迁移
- 它会执行 `IdentitySeeder().SeedAsync()`

因此在生产发布时，不能只重启 `idp` 容器，而必须把 `idp-migrator` 纳入流程。

### 推荐发布顺序

1. 拉取最新镜像
2. 运行 `idp-migrator`
3. migrator 成功后，再启动或重启 `idp`
4. 做健康检查

### 不推荐的方式

不建议让 `idp` Web 进程自己在启动时自动迁移数据库，因为：

- 发布顺序不可控
- 失败时定位困难
- Web 容器可能半启动状态卡住

---

## 四、应用在生产环境的关键配置项

根据当前代码，生产环境至少要提供这些配置：

### 1. 数据库连接

- `ConnectionStrings__DefaultConnection`

因为 `AddIdpService()` 中会直接读取 `DefaultConnection`，缺失会启动失败。

### 2. 管理员种子账号

- `Identity__Admin__UserName`
- `Identity__Admin__Password`

这两个值在 `IdpServiceRegistrar.AddIdpOptions()` 中被 `ValidateOnStart()` 校验，生产环境不能为空。

### 3. OIDC 相关配置

- `IdpIssuer`
- `Oidc__SigningCertificatePath__0`
- `Oidc__SigningCertificatePassword__0`
- `Oidc__EncryptionCertificatePath__0`
- `Oidc__EncryptionCertificatePassword__0`

当前代码中，非 Development 环境不会使用开发证书，而是要求显式加载生产证书。

### 4. DataProtection 持久化

- `DataProtection__StorageDirectory`
- `DataProtection__ApplicationName`
- `DataProtection__KeyLifetimeDays`

这个目录必须映射到宿主机卷，否则容器重建后用户登录态和 Cookie 保护密钥会丢失。

### 5. Forwarded Headers

- `ForwardedHeaders__KnownProxies__0`
  或
- `ForwardedHeaders__KnownIpNetworks__0`

因为当前应用会启用 `UseForwardedHeaders()`，而且 OIDC issuer、回调地址、HTTPS 判断都依赖反向代理头。
如果你的反向代理没有转发 `X-Forwarded-Proto` / `X-Forwarded-Host`，或者应用没有把代理 IP / 网段加入受信任列表，像 GitHub 这类外部登录的 `redirect_uri` 就可能错误地生成为 `http://.../signin-github`。

### 6. Redis

当前 `AppHost` 会注入：

- `CacheOptions__CacheType=Redis`

因此生产建议也保留 Redis 配置，并确保连接方式与应用侧缓存配置一致。

---

## 五、推荐的 GitHub CI/CD 架构

推荐拆成两个 workflow：

### 1. `ci.yml`

用于 PR 和 push 的持续集成：

1. Checkout
2. 安装 .NET 10 SDK
3. 安装 Node.js
4. `dotnet restore BzsOIDC.sln`
5. `dotnet build BzsOIDC.sln -c Release`
6. `dotnet format BzsOIDC.sln --verify-no-changes --verbosity minimal`
7. `dotnet test BzsOIDC.sln -c Release`
8. 构建 `BzsOIDC.Idp` 容器镜像
9. `main` 推送发布 `edge` 快照；`X.Y.Z` tag 推送发布稳定版本到 GHCR

### 2. `deploy.yml`

用于部署到 Ubuntu：

1. 仅在 `main` 分支或手动触发时运行
2. 通过 SSH 登录 Ubuntu
3. 登录 GHCR
4. 拉取最新镜像
5. 运行 `idp-migrator`
6. 成功后重启 `idp`
7. 做部署后健康检查
8. 清理旧镜像

---

## 六、镜像构建建议

### 推荐方式

优先使用以下两种之一：

1. **传统 Dockerfile 多阶段构建**
2. **.NET SDK Container Publish**

对于当前仓库，我更建议先用 **Dockerfile 多阶段构建**，原因是：

- 你项目有前端构建步骤
- 你后续还要给 `Idp` 和 `Migrator` 做分离镜像
- Dockerfile 对调试和排错更直观

### 推荐镜像划分

建议至少准备两个镜像：

- `bzsoidc-idp`
- `bzsoidc-idp-migrator`

这样部署流程会更清晰：

- Web 镜像只负责提供服务
- Migrator 镜像只负责迁移和种子任务

---

## 七、镜像仓库建议

推荐使用 **GitHub Container Registry (GHCR)**。

### 标签策略

稳定版本由无前缀的 `X.Y.Z` Git tag 触发，例如 `1.2.3`。CI 会校验三个数字段，并为两个镜像同时发布：

- `1.2.3`：不可变的完整发布版本，生产部署首选
- `1.2`：同一 minor 系列的最新版本
- `1`：同一 major 系列的最新版本
- `latest`：最新稳定版本
- `sha-<commit>`：精确提交版本

普通 `main` 推送只发布 `edge` 和 `sha-<commit>`，不会覆盖 `latest`。这样开发快照与稳定发布不会混用。

---

## 八、Ubuntu 服务器目录建议

建议在 Ubuntu 服务器上固定部署目录：

```text
/opt/bzsoidc/
  docker-compose.yml
  .env
  deploy.sh
  certs/
  data-protection/
  postgres/
```

### 目录说明

- `docker-compose.yml`
  - 管理 `idp`、`postgres`、`redis`
- `.env`
  - 放非敏感或半敏感运行变量引用
- `deploy.sh`
  - 被 GitHub Actions 通过 SSH 调用的部署脚本
- `certs/`
  - OIDC 签名/加密证书文件
- `data-protection/`
  - ASP.NET Core DataProtection key ring
- `postgres/`
  - PostgreSQL 数据卷

---

## 九、GitHub Secrets / Environments 建议

建议至少准备以下 GitHub Secrets：

- `SSH_HOST`
- `SSH_PORT`
- `SSH_USER`
- `SSH_PRIVATE_KEY`
- `GHCR_PAT`（如不直接使用 `GITHUB_TOKEN`）

如果你选择由 GitHub Actions 远程写入环境文件，还需要：

- `PROD_ENV_FILE`

或者拆成多个 Secret：

- `DEFAULT_CONNECTION_STRING`
- `IDP_ISSUER`
- `ADMIN_USERNAME`
- `ADMIN_PASSWORD`
- `OIDC_SIGNING_CERT_PASSWORD`
- `OIDC_ENCRYPTION_CERT_PASSWORD`

### 推荐做法

使用 GitHub 的 `production` Environment：

- 将部署 workflow 绑定到 `production`
- 开启手工审批
- 把生产机相关 secrets 放到该 Environment 下

---

## 十、Docker Compose 生产建议

推荐在生产环境中由 Compose 管理这些常驻服务：

- `postgres`
- `redis`
- `idp`

而 `idp-migrator` 有两种方式：

### 方式 A：临时执行容器

部署时：

```bash
docker run --rm ... bzsoidc-idp-migrator:1.2.3
```

### 方式 B：Compose 中定义 service，但不常驻

部署时：

```bash
docker compose run --rm idp-migrator
docker compose up -d idp
```

### 推荐

优先推荐 **方式 B**，因为：

- 环境变量与网络可复用
- 与主服务共享同一 compose 网络
- 运维脚本更统一

---

## 十一、发布流程建议

推荐的完整发布流程如下：

1. 创建并推送严格的 `X.Y.Z` Git tag
2. GitHub Actions 完成构建、测试和启动检查
3. GitHub Actions 构建并推送 `idp` 镜像
4. GitHub Actions 构建并推送 `idp-migrator` 镜像
5. SSH 进入 Ubuntu 服务器
6. 登录 GHCR
7. 将 `IMAGE_TAG` 设置为完整版本号并执行 `docker compose pull`
8. `docker compose run --rm idp-migrator`
9. 如果 migrator 成功，执行 `docker compose up -d idp`
10. 检查应用是否健康可用
11. 清理悬空镜像

### 成功判定

至少应满足：

- `idp-migrator` 退出码为 0
- `idp` 容器成功启动
- 反向代理可访问站点
- OIDC issuer、登录页和数据库连接正常

---

## 十二、回滚策略

推荐采用“镜像版本回滚”。

### 最低要求

- 每次部署都固定到完整的 `X.Y.Z` tag；`sha-<commit>` 作为精确提交的备用定位方式
- 服务器上始终记录当前版本和上一个版本

### 回滚方式

如果新版本发布失败：

1. 将 `idp` 镜像 tag 切回上一版
2. 将 `idp-migrator` 镜像 tag 切回上一版（如需要）
3. `docker compose up -d`

### 注意事项

数据库回滚永远比应用回滚更复杂，因此：

- migration 设计要尽量向前兼容
- 不要把不可逆 schema 变更和应用切换混在同一个不可控流程里

---

## 十三、反向代理与 HTTPS 注意事项

这个项目是 IDP，OIDC 对外部 URL 非常敏感，因此反向代理配置必须正确。

### 必须正确传递的头

- `X-Forwarded-For`
- `X-Forwarded-Proto`
- `X-Forwarded-Host`

### 为什么重要

如果这些头不对，会直接影响：

- `IdpIssuer`
- 登录跳转地址
- 回调 URL
- Cookie Secure 判断
- OpenIddict 生成的 issuer / endpoint 地址

### 配置建议

生产中要把你的反向代理地址或内网网段加入：

- `ForwardedHeaders:KnownProxies`
  或
- `ForwardedHeaders:KnownIpNetworks`

同时确保反向代理显式转发：

- `X-Forwarded-Proto`
- `X-Forwarded-Host`

---

## 十四、推荐的最小落地版本

如果目标是先尽快上线，推荐第一阶段只做下面这些：

1. 新增 `Dockerfile`（`Idp`）
2. 新增 `Dockerfile`（`Idp.Migrator`）
3. 新增 `docker-compose.yml`
4. 新增 `.github/workflows/ci.yml`
5. 新增 `.github/workflows/deploy.yml`
6. 新增 Ubuntu 端 `deploy.sh`

这套最小方案已经足够支撑：

- 自动构建
- 自动测试
- 自动推镜像
- 自动执行迁移
- 自动部署到 Ubuntu Docker

---

## 十五、最终建议

对于当前仓库，最合适的方案可以概括为一句话：

> 使用 GitHub Actions 做 CI 和镜像发布，使用 GHCR 做镜像仓库，使用 Ubuntu 上的 Docker Compose 管理 `postgres`、`redis`、`idp`，并在每次发布时先执行 `BzsOIDC.Idp.Migrator`，成功后再切换 `idp` 服务。

这套方案和当前仓库结构匹配度高，复杂度可控，后续也方便继续升级成更完整的自动化交付体系。

---

## 十六、下一步建议

下一步可以直接在仓库中补齐以下文件：

- `Dockerfile`（`src/BzsOIDC.Idp/`）
- `Dockerfile`（`src/BzsOIDC.Idp.Migrator/`）
- `docker-compose.yml`
- `.github/workflows/ci.yml`
- `.github/workflows/deploy.yml`
- `docs/ubuntu-server-setup.md`

如果继续推进，建议先完成容器化，再接 GitHub Actions 部署链路。
