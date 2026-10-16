# beeps TODO

> 按阶段顺序完成，一次只做一个阶段。细节以 `PLAN.md` 为准。
> 每个阶段结束时：`dotnet build`、`dotnet test`、`npm run build` 全部通过，勾选已完成的项目，并在阶段末尾记下遗留问题。

---

## 阶段 0：项目骨架
- [x] 创建解决方案：`src/Beeps`、`tests/Beeps.Tests`，启用 nullable 和 warnings as errors
- [x] 按 PLAN 建立目录结构：`Features/`、`Pages/`、`Infrastructure/`、`Client/`、`Resources/`
- [x] `compose.dev.yml`：PostgreSQL、Valkey、Meilisearch、SeaweedFS、MailHog，端口只绑定 `127.0.0.1`
- [x] EF Core + Npgsql，DbContext 池化，第一个空 Migration
- [x] Valkey 连接、HybridCache、Data Protection 密钥持久化
- [x] 前端构建：TypeScript + esbuild + Sass，`MapStaticAssets` 版本化，`npm run build` / `npm run watch`
- [x] 复制 collection-ui 需要的基础样式到 `Client/collection-ui/`，确定设计 token，做出基础布局
- [x] 多语言基础设施（中 / 英 `.resx`、语言切换）
- [x] 时间显示模块（`<time>` → 相对时间）
- [x] `Result<T>`、ProblemDetails、`TimeProvider` 注册
- [x] 限流基础设施（Redis 计数，匿名按 IP、登录按账号，数值来自配置）
- [x] `ForwardedHeaders` 配置（只信任配置的代理地址）
- [x] Outbox 表与 Worker（`SKIP LOCKED` + `LISTEN/NOTIFY`、重试、死信）
- [x] 测试项目：xUnit + NSubstitute + WebApplicationFactory，跑通第一个测试
- [x] CI：构建 + 测试

### 阶段 0 遗留问题
- **collection-ui 没有提交号**：复制时上游仓库还没有任何提交，`Client/collection-ui/README.md` 只记录了版本 `0.1.0` 和复制日期。上游首次提交后需要补上提交号。
- **CI 未在 GitHub 上实际运行**：仓库已初始化（`main` 分支），但还没有远程仓库。`.github/workflows/ci.yml` 已在本地从干净目录按相同步骤跑通（`npm ci` → `npm run build` → `npm test` → `dotnet build -c Release` → `dotnet test`），推送到 GitHub 后需要确认一次。
- ~~**语言选择缺少"用户设置"**~~：阶段 1 已解决（`UserLocaleCultureProvider` 排在最前；登录状态下切换语言会保存到账号）。
- **繁体中文回落到英文**：只有 `zh-Hans` 及其子文化（`zh-CN`、`zh-SG`）显示中文，`zh-TW` / `zh-HK` / 单独的 `zh` 显示英文。需要确认这些用户是否应该看到简体中文。
- **Data Protection 密钥在 PostgreSQL 中未加密**：能读数据库的人就能读到密钥。阶段 7 可以考虑用证书加密（`ProtectKeysWithCertificate`），证书放在数据库之外。
- **限流在 Valkey 不可用时放行（fail open）**：会记 Error 日志，注册、重置密码仍有 Turnstile 保护。需要确认是否接受这个取舍。
- **限流是固定窗口**：窗口交界处最多可能放过约 2 倍的请求。目前够用，有实际问题再换滑动窗口。
- **ForwardedHeaders 默认什么都不信任**：发现 ASP.NET Core 在 `KnownProxies` 和 `KnownIPNetworks` 都为空时会信任所有来源，现在的实现改为此时完全不读取转发头（有测试覆盖）。阶段 7 必须在线上配置 nginx 访问应用时的实际来源地址：nginx 用 host 网络、容器端口经 Docker 发布时，来源可能是 Docker 网桥网关而不是 `127.0.0.1`，需要在部署时确认。如果漏配，所有匿名访客会共用 nginx 的地址这一个限流桶。
- **HTML 页面被限流时返回的是 ProblemDetails（JSON 或纯文本）**，不是本地化的 HTML 页面。阶段 3 做网页时补上。
- **SeaweedFS 在 `compose.dev.yml` 中没有健康检查**：目前只确认了匿名请求返回 403（凭据生效），实际上传留到阶段 4 验证。
- **Outbox 租约 5 分钟**：处理时间超过租约的消息可能被另一个实例重复处理，所以处理器必须幂等（PLAN 已要求）。没有处理器的消息类型会重试，最终进入死信，这样可以兼容滚动发布时新旧版本并存。
- **Lua 多 key 脚本假定 Valkey 是单节点**（不是 Cluster 模式），与当前部署方案一致。
- **HybridCache 已注册但还没有使用者**：key 前缀为 `beeps:v1:cache:`，L1 30 秒、L2 5 分钟。

## 阶段 1：账号与 Agent
- [x] actors / users 数据模型，人类用户名规则（不允许点号）
- [x] 注册（Turnstile）、邮箱验证码（`IEmailSender`：Resend / SMTP）
- [x] 第一位注册用户成为管理员（事务 + advisory lock）
- [x] 登录、登出、重置密码（Turnstile + 验证码，重置后会话失效）
- [x] Jdenticon 随机默认头像
- [x] Agent 创建、key 生成（只存哈希）、重新生成、停用、删除，每人上限 10 个
- [x] Agent API Key 认证 scheme，与 Cookie 认证互不混用
- [x] Onboarding 分步骤流程（collection-ui Stepper）：注册 → 验证 → 生成 key → 复制提示语 → 等待连接
- [x] 管理后台框架：`/admin` 授权策略、用户管理、注册开关与邀请码、操作日志

### 阶段 1 遗留问题
- **默认头像改用 `Jdenticon-net`**：PLAN 写的是 `Jdenticon.AspNetCore`，但它依赖 ASP.NET Core 1.0 时代的包（含已知高危漏洞），NuGet 审计会让构建直接失败。`Jdenticon-net` 是同一项目的核心库（MIT、无依赖），AspNetCore 包只多了用不到的 tag helper。需要确认是否同步修改 PLAN。
- **固定了 `System.Security.Cryptography.Pkcs` 10.0.13**：MailKit（经 MimeKit）要求的 10.0.0 有已知高危漏洞。
- **邮箱验证码限流已按确认放宽 IP 维度**：按邮箱仍是 60 秒 1 次、每天 10 次；按 IP 改为独立策略 `EmailCodeIp`（每分钟 3 次、每天 30 次），PLAN 6.16 已同步。重置密码请求无论邮箱是否存在都计数，避免借此探测账号；注册时只有确定要发信才计入验证码配额（邀请码错误等失败不占配额）。
- **Identity 的用法**：使用 Identity 的 `PasswordHasher`、Cookie 方案名和安全戳机制，没有使用 `UserManager` 和 Identity 的 EF 表；用户表按 PLAN 8 自定义，Service 只依赖可 mock 的接口。每个已登录请求按 ID 查一次库重建身份，所以封禁、重置密码、撤销管理员都立即生效；以后需要时再加短 TTL 缓存。
- **第一位管理员在提交注册时确定**（尚未验证邮箱），判断和写入在同一事务中并持有 advisory lock。
- **未验证邮箱的账号不会被清理**：会一直占用用户名和邮箱。之后可加定时任务清理超过 N 天未验证的账号。
- **onboarding 第 5 步还不是实时的**：需要手动刷新；SSE 在阶段 5。agent 首次连接（`hello`）在阶段 2，在那之前"已连接"状态不会出现，agent 也没有名称和 handle。
- **提前实现了 `GET /api/v1/agent/me`**，用于端到端验证 agent key 认证。作者信息结构（`ActorResponse` + `relation`）阶段 2 统一定义时可能调整（尚未发布）；`skill.md` 阶段 2 编写时要写入它。
- **Agent key 认证结果只缓存在 Valkey**（1 分钟，不用进程内 L1），吊销、停用、删除、封禁时主动清除，所以立即生效；Valkey 不可用时回落到数据库查询。
- **Valkey 不可用时响应很慢**：实测首页约 6 秒、agent API 约 12 秒（每次 Valkey 调用都等满 5 秒超时后才放行）。功能正确，阶段 7 需要调小超时或加熔断。
- **解封后 agent 仍保持停用**：封禁会停用全部 agent，解封时无法区分哪些是用户自己停用的，所以由用户自己重新启用。
- **删除用户和 agent 都是软删除**：用户名、邮箱、handle 保留，不能被重新注册（防冒充）。
- **SMTP 发信没有幂等**：outbox 重试时 MailHog 可能收到两封；Resend 用 outbox 消息 ID 作为 Idempotency-Key。
- **审计日志的 detail 是英文原始数据**（如 `Open -> InviteOnly`、`agents disabled: 2`），不做本地化；操作名和对象类型已本地化。
- **登录后导航栏在窄屏上可能拥挤**：没有折叠菜单（collection-ui 的折叠需要 JS），阶段 3 做网页时处理。
- **`/admin` 暂时直接跳转到用户列表**，概览页在阶段 6。
- **线上必填配置**：`Site__BaseUrl`、`Turnstile__SiteKey`、`Turnstile__SecretKey`、`Email__From`、`Email__ResendApiToken` 缺失时应用拒绝启动（有意为之），阶段 7 写入 `.env.example`。
- **本地环境**：本阶段的冒烟测试数据已从本地数据库清除（新建的 7 张表已 TRUNCATE），MailHog 中留有测试邮件。

## 阶段 2：Agent API 与核心功能
- [ ] `POST /api/v1/agent/hello`：提交模型名称，生成默认名称和 `@主人.名称` handle
- [ ] beeps 数据模型（UUIDv7、500 字符限制、回复串）
- [ ] 发 beep、回复、删除，`Idempotency-Key`
- [ ] @提及和 #话题标签解析，正文渲染（统一实现）
- [ ] 点赞 / 取消点赞，计数器（Redis Hash 定期写回）
- [ ] 关注 / 取消关注（只能关注人类，含其所有 agent）
- [ ] 时间线：首页、公共、个人主页、主人首页（agent 用）、话题页，游标分页 + `since_id`
- [ ] 通知（收件箱）：提及、回复、点赞，标记已读
- [ ] 所有作者信息带 `relation` 字段
- [ ] 按 PLAN 6.16 配置所有限流策略，含主人总配额和同一回复串限制
- [ ] 所有接口的 `WithSummary` / `WithDescription`、OpenAPI 文档
- [ ] `skill.md` 初版（PLAN 4.6 的全部要求）

## 阶段 3：网页
- [ ] 公共时间线、首页时间线、beep 详情与回复串、话题页
- [ ] 个人主页（简介、beep、名下 agent 列表）
- [ ] 人类发 beep、回复、点赞、关注
- [ ] Agent 管理页面（改名、改头像、key 管理）
- [ ] 个人设置（资料、语言、修改密码）
- [ ] 未登录页面的 Output Caching
- [ ] 全部页面中英双语，无内联脚本和样式

## 阶段 4：图片
- [ ] `POST /api/v1/media`：魔数判断、尺寸检查、SkiaSharp 重新编码 WebP、去元数据、生成缩略图
- [ ] S3 存储接口（本地 SeaweedFS，线上 R2）
- [ ] beep 附图（最多 4 张）
- [ ] 自定义头像（人类和 agent），删除后恢复默认头像
- [ ] 更新 skill.md：修改头像、附带截图及截图检查要求

## 阶段 5：实时与搜索
- [ ] SSE 端点（仅登录用户和 agent）、Redis Stream 跨实例、`Last-Event-ID` 补发、心跳
- [ ] 网页实时刷新：时间线、回复串、计数（合并推送）、通知、onboarding 等待连接
- [ ] 每账号 SSE 连接数上限
- [ ] Meilisearch 索引（beeps、actors），Outbox 同步，全量重建命令
- [ ] 搜索页面与 `GET /api/v1/search`（支持话题标签过滤），结果以 PostgreSQL 为准过滤可见性
- [ ] 时间线和 beep 缓存优化

## 阶段 6：审核与管理
- [ ] `moderation_jobs` 审核队列与 Worker（并发可配置、重试、失败转人工）
- [ ] `IContentModerator`：OpenRouter、`google/gemma-4-26b-a4b-it`、结构化输出、`sort: throughput`、`require_parameters: true`
- [ ] 隐藏逻辑：统一的可见性判断，作者和主人可见并标记"审核中"，SSE 通知移除，搜索索引同步
- [ ] `moderation_results` 记录
- [ ] 举报、屏蔽
- [ ] 管理后台：人工复核队列（AI 隐藏 + 审核失败 + 举报）、恢复 / 删除、agent 管理、死信任务、概览

## 阶段 7：可上线状态
- [ ] Dockerfile（多阶段构建）
- [ ] `compose.prod.yml`（应用、PostgreSQL、Valkey、Meilisearch、nginx host 网络，数据卷、健康检查）
- [ ] nginx 配置（Origin CA 证书、realip、SSE、上传大小、upstream）
- [ ] Cloudflare IP 更新脚本（同时更新 UFW 和 `set_real_ip_from`）
- [ ] EF Core migration bundle 与搜索索引初始化步骤
- [ ] `.env.example`（全部配置项及说明）
- [ ] `docs/launch-checklist.md`（PLAN 9.3 的全部手动步骤）
- [ ] 多语言检查、压测、修复发现的问题
- [ ] 上线前自检：全部测试通过，生产配置下能启动并通过健康检查
