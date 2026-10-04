# 选型调研：管理前端用 React 还是 Blazor WebAssembly（+ Bzs.Blazor）

日期：2026-09-29 ｜ 范围：BzsOIDC 管理前端技术栈选型
方法说明：仓库侧事实取自 ADR-0001~0004、issue #31、commit 历史；外部事实经一手来源核实（Microsoft Learn、react.dev、ant.design、NuGet 包页）。因环境限制（子代理并发上限、部分 web 工具间歇性失败），本次由主代理直接调研，未能核实的点已逐条标注。

## TL;DR

**倾向 Blazor WebAssembly + Bzs.Blazor**（judgement）。决定性权重不在技术参数，而在本仓库的具体约束：管理 API 与认证模型当初就是围绕 Blazor + Bzs.Blazor 设计的、`BzsOIDC.Contracts` 类型可直通前端、Bzs.Blazor 本体活跃且原生 net10.0、git 历史里有一套完整实现可考古复活。React 在生态与首屏上占优，但对本仓库意味着全新工具链、第二套类型契约和自建认证集成——收益主要落在"内部管理工具"权重最低的维度上。**一个条件**：若当初剥离前端的真实原因就是对 WASM/Bzs.Blazor 不满，则该原因压倒本报告的全部理由。

## 仓库现状与历史（已核实）

- 曾完整交付过 hosted WASM + Bzs.Blazor 前端（issue #31：18/18 子任务完成，commit `ab9f22a`），随后整体剥离转 backend-only（commit `2693e9b`，**剥离原因未记载**）。ADR-0001~0003 记录 WASM 决策；issue #31 至今 open。
- 管理 API（ADR-0004）为 SPA 设计：显式分页（默认 25/上限 100）、ETag/If-Match 乐观并发（缺头 428/过期 412）、ProblemDetails + 稳定错误码、OIDC 客户端密钥仅创建/轮换时返回。原文明确"maps **Bzs.Blazor provider requests** to browser-safe BzsOIDC contracts"——API 分层与该库存在设计耦合。
- 认证模型（ADR-0003）：同源 HttpOnly Cookie + 内存 antiforgery token、无浏览器 token 存储、计划生产强制 CSP、跨域不开。
- 团队画像：单人 C# 开发者，自研 Bzs.Blazor。

## 逐维度对比

| 维度 | Blazor WASM + Bzs.Blazor | React (Vite) | 本仓库权重 |
|---|---|---|---|
| 类型契约 | `BzsOIDC.Contracts`（browser-safe C# DTO，仍在仓库）编译期直通；重构安全 | 需 OpenAPI codegen 或手写 TS 类型，双份契约有漂移风险 | **高** |
| 认证/antiforgery | 一方支持：`AntiforgeryToken` 组件、`UseAntiforgery`、`[RequireAntiforgeryToken]`（[MS Learn](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/?view=aspnetcore-10.0)，访问 2026-09-29）；ADR-0003 即按此设计 | 全部自建：token 获取/附加、401 会话刷新、tab 同步（#31 里 22 条 user story 大半是这类 glue） | **高** |
| 组件库 | Bzs.Blazor 0.7.1（2026-08-30 更新，net10.0，MIT，[NuGet](https://www.nuget.org/packages/Bzs.Blazor)，访问 2026-09-29）：typed data grid、导航菜单、autocomplete、文件上传、明暗主题、零第三方 UI 依赖；维护者=用户本人 | Ant Design v6 稳定且极活跃（v6.6.2 2026-08-28，[GitHub #55804](https://github.com/ant-design/ant-design/issues/55804)）、Pro V6、CLI；生态与社区远大于自研 0.x 库 | **高（互有胜负）** |
| 历史资产 | git 历史含完整可考古实现（issue #31 亦为施工图） | 从零重写 | **高** |
| 工具链/心智 | 不引入 Node/npm；单人 C# 全栈 | 新增 Node/npm 工具链、第二套范式与升级节奏 | **高** |
| 首屏/体积 | WASM 下载量级 MB 级（.NET 10 已将 boot config 内联进 `dotnet.js` 并指纹化，减少一次阻塞请求，[MS Learn](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0?view=aspnetcore-10.0)，访问 2026-09-29） | Vite 产物量级 200–400KB gzip（general knowledge，未逐项核实） | **低**（同源部署的内部管理工具、低频访问） |
| 缓存陷阱 | 用户亲历：.NET 10 boot config 合并后旧缓存 404 + force-cache 致启动失败；.NET 10 指纹化本身即官方缓解方向，配合 `index.html` no-cache 部署头可控 | 标准 hash 指纹 + html no-cache，坑面更小 | **中** |
| CSP | 需 `'wasm-unsafe-eval'`（窄授权，官方明确不等于 `unsafe-eval`），且有 nonce+antiforgery 官方方案（[MS Learn](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/content-security-policy?view=aspnetcore-10.0)，访问 2026-09-29） | 常规产物可免 eval（general knowledge） | **中** |
| 供应链 | NuGet + Dependabot；Bzs.Blazor 零三方 UI 依赖 | npm 面更大（审计工具成熟，general knowledge） | **中** |
| 生态/招聘/AI 辅助 | 社区较小，0.x 自研库破坏性变更自担 | 极大社区、参考实现多 | **低**（单人内部工具） |

## 建议（judgement）

**重启 Blazor WASM + Bzs.Blazor**，理由按权重：契约类型直通、认证模型现成、组件库是自己的且活跃（0.7.1，2026-08-30 更新）、历史实现可考古、不引第二套工具链。React 的优势维度（首屏、生态、招聘）在"单人维护的内部管理工具"上权重最低。

**选 React 当且仅当**：① 当初剥离前端的真实原因是对 WASM 或 Bzs.Blazor 的否定（此为最重信号，commit 未记载，需你补充）；② 该 admin UI 计划走向对外产品、需要社区组件与协作扩展；③ 你明确想借项目进入 React 生态。

若选 Blazor：issue #31（仍 open）及其 18 个已完成子任务可作为考古地图与验收基线；若选 React：#31 应关闭（superseded）。

## 决议补充（2026-09-29，用户答复）

- **问题 1（剥离原因）**："写得太烂，史诗级的烂"——是对**实现质量**的否定，不是对 WASM/Blazor/Bzs.Blazor 技术栈的否定。本报告的条件性结论解除：**倾向成立，定案 Blazor WASM + Bzs.Blazor**。
- **问题 3（定位）**：纯内部运维。React 的生态/首屏/招聘维度权重进一步降低，结论加固。
- 问题 2（Bzs.Blazor 0.7.x 成熟度自评）保持开放；作者即用户，风险自控。
- **关键教训**：旧前端的病灶是产品级 UI 的范围膨胀（主题预载、i18n、注册、外部登录、consent、多 tab 会话同步——见 #31 的 22 条 user story），而非技术栈本身。重建时必须把范围钉死在"内部运维控制台"：用户、角色、权限拓扑、OIDC 客户端、OIDC scope 五块 + 登录，并沿用 ADR-0003/0004 的契约纪律（同源 Cookie、antiforgery、显式分页、ETag/If-Match、稳定错误码）。
