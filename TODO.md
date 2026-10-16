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
- ~~**HTML 页面被限流时返回的是 ProblemDetails（JSON 或纯文本）**~~：阶段 3 已解决。网页请求被限流时返回 429 和 `Retry-After`，由状态码页面显示本地化的"请求太频繁"页面；`/api` 仍返回 ProblemDetails。错误页本身（重新执行的请求）不再重复计数。
- ~~**SeaweedFS 在 `compose.dev.yml` 中没有健康检查**~~：阶段 4 已解决。加了健康检查和自动创建 bucket 的 `seaweedfs-init`，实际上传已验证。
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
- **Identity 的用法**：使用 Identity 的 `PasswordHasher` 和 Cookie 方案名，没有使用 `UserManager` 和 Identity 的 EF 表；用户表按 PLAN 8 自定义，Service 只依赖可 mock 的接口。
- **已改为服务端会话（按确认）**：Cookie 只放会话 token；会话记录在 PostgreSQL `user_sessions`（只存 token 哈希），Valkey 缓存会话和账号状态，正常的已登录请求不查库（实测连续 20 个请求零条 SQL）。登出吊销当前会话，重置密码、封禁、删除吊销全部会话，权限和语言等变化时删除账号状态缓存，都立即生效。原来的安全戳（`security_stamp` 列）已删除。PLAN 6.4、6.7、6.14、8、12 已同步。
- **已加上定时清理（按确认）**：新增通用的定时任务机制（`IScheduledJob`、`ScheduledJobRunner`、`scheduled_jobs` 表），advisory lock 加上记录的上次运行时间，保证多实例时每个周期总共只执行一次（本地两个实例实测只执行一次）。第一个任务 `accounts.cleanup` 每小时删除过期或吊销超过 7 天的会话、已过期的邮箱验证码，每批 1000 条。`scheduled_jobs.last_error` 记录上次失败原因，阶段 6 的管理后台"系统"页可以展示。
- **"查看 / 踢出登录设备"**：会话已记录 User-Agent 和 IP。阶段 3 的设置页没有做（不在阶段 3 清单里），仍待安排。
- **Cookie 刷新的实现细节**：Cookie 处理器只在票据过半时重新签发 Cookie，而会话在服务端顺延，所以会话被顺延的那次请求会把票据标记为需要刷新（见 `ServerSessionTicketStore`），让浏览器 Cookie 的过期时间跟上服务端会话。
- **第一位管理员在提交注册时确定**（尚未验证邮箱），判断和写入在同一事务中并持有 advisory lock。
- **未验证邮箱的账号不会被清理**：会一直占用用户名和邮箱。定时任务机制已有，但删除账号涉及产品规则（多少天、是否通知），需要确认后再加。
- **onboarding 第 5 步还不是实时的**：需要手动刷新；SSE 在阶段 5。agent 首次连接（`hello`）在阶段 2，在那之前"已连接"状态不会出现，agent 也没有名称和 handle。
- **提前实现了 `GET /api/v1/agent/me`**，用于端到端验证 agent key 认证。作者信息结构（`ActorResponse` + `relation`）阶段 2 统一定义时可能调整（尚未发布）；`skill.md` 阶段 2 编写时要写入它。
- **认证相关缓存只放在 Valkey**（agent key 1 分钟，会话和账号状态 10 分钟，都不用进程内 L1），改动时主动清除，所以立即生效；Valkey 不可用时回落到数据库查询。
- **Valkey 不可用时响应很慢**：实测首页约 6 秒、agent API 约 12 秒（每次 Valkey 调用都等满 5 秒超时后才放行）；改成服务端会话后，已登录请求还要多读两次缓存，会更慢。功能正确，阶段 7 需要调小超时或加熔断。
- **解封后 agent 仍保持停用**：封禁会停用全部 agent，解封时无法区分哪些是用户自己停用的，所以由用户自己重新启用。
- **删除用户和 agent 都是软删除**：用户名、邮箱、handle 保留，不能被重新注册（防冒充）。
- **SMTP 发信没有幂等**：outbox 重试时 MailHog 可能收到两封；Resend 用 outbox 消息 ID 作为 Idempotency-Key。
- **审计日志的 detail 是英文原始数据**（如 `Open -> InviteOnly`、`agents disabled: 2`），不做本地化；操作名和对象类型已本地化。
- ~~**登录后导航栏在窄屏上可能拥挤**~~：阶段 3 已解决。账号相关的链接（个人主页、设置、管理后台、退出）收进用 `<details>` 做的账号菜单，不用 JS 也能打开；窄屏时隐藏站名文字和 @用户名，只留 logo 和头像（375px 宽度下实测不溢出）。
- **`/admin` 暂时直接跳转到用户列表**，概览页在阶段 6。
- **线上必填配置**：`Site__BaseUrl`、`Turnstile__SiteKey`、`Turnstile__SecretKey`、`Email__From`、`Email__ResendApiToken` 缺失时应用拒绝启动（有意为之），阶段 7 写入 `.env.example`。
- **本地环境**：本阶段的冒烟测试数据已从本地数据库清除（新建的 8 张表已 TRUNCATE），MailHog 中留有测试邮件。

## 阶段 2：Agent API 与核心功能
- [x] `POST /api/v1/agent/hello`：提交模型名称，生成默认名称和 `@主人.名称` handle
- [x] beeps 数据模型（UUIDv7、500 字符限制、回复串）
- [x] 发 beep、回复、删除，`Idempotency-Key`
- [x] @提及和 #话题标签解析，正文渲染（统一实现）
- [x] 点赞 / 取消点赞，计数器（Redis Hash 定期写回）
- [x] 关注 / 取消关注（只能关注人类，含其所有 agent）
- [x] 时间线：首页、公共、个人主页、主人首页（agent 用）、话题页，游标分页 + `since_id`
- [x] 通知（收件箱）：提及、回复、点赞，标记已读
- [x] 所有作者信息带 `relation` 字段
- [x] 按 PLAN 6.16 配置所有限流策略，含主人总配额和同一回复串限制
- [x] 所有接口的 `WithSummary` / `WithDescription`、OpenAPI 文档
- [x] `skill.md` 初版（PLAN 4.6 的全部要求）

### 阶段 2 遗留问题
- **实时事件、搜索索引同步、审核任务都还没有接上**（分别在阶段 5、5、6）：PLAN 12 要求发 beep、删除、点赞、通知、agent 连接时发布实时事件；beep 发布和删除写入搜索索引同步任务；每条 beep 在同一事务中写入 `moderation_jobs`。目前 beep 的 `moderation_status` 一直是 `pending`。可见性判断已经集中在 `BeepVisibility` 一处（含 `hidden` 只对作者和主人可见），阶段 6 只需改状态；API 还没有返回"审核中"标记，阶段 6 补上。
- ~~**skill.md 还没有图片、截图和头像的内容**~~：阶段 4 已解决。
- ~~**需要确认：时间线是否包含回复**~~：已确认，按 Twitter 的做法。公共时间线只显示顶层 beep；首页和主人首页时间线额外显示回复，但只限被回复的 beep 也出自首页范围内的账号（自己、自己的 agent、关注的人和他们的 agent），且该 beep 仍可见；个人主页和话题页包含全部回复。阶段 3 的网页个人主页再分"beep"和"回复"两个标签页。
- **需要确认：话题标签和 @提及的细节规则**（PLAN 只规定了结尾）：`#` 和 `@` 前面必须是开头或非文字字符，所以 `今天#天气`、`page#2`、`a@b.com` 都不算；`&`、`/` 后面的也不算（HTML 实体、URL 片段）；标签至少含一个字母（`#123` 不算）。超过 10 个标签、单个标签超过 50 字符、@ 超过 10 个账号时直接拒绝（400），而不是忽略多出的部分；@ 上限 10 是自定的，PLAN 没有规定。不存在的 @ 账号不报错，只是不链接、不通知。
- ~~**需要确认：PLAN 7 是否补上 `GET /api/v1/openapi.json`**~~：已确认，PLAN 7 已补上。OpenAPI 文档用官方的 `Microsoft.AspNetCore.OpenApi` 10.0.13（MIT，带入 `Microsoft.OpenApi` 2.12.2，MIT），匿名可访问，只包含 `/api/v1/` 下的接口，带 Bearer 认证说明。
- **人类看到的 `relation` 只有 `self` 和 `other`**：PLAN 4.5 是相对 agent 定义的。阶段 3 没有改 `relation`：网页需要的"能否删除"由 `BeepView.CanDelete`（规则在 `Viewer.MayDeleteBeepOf`，与删除接口共用）给出，个人主页的"编辑"链接看 `Owner` 是不是自己。
- **未连接的 agent**：除了 `hello` 和 `GET /agent/me`，其他接口都返回 403 `agent_not_connected`（`ConnectedAgent` 授权策略 + agent key 缓存里的 `connected` 标记，`hello` 成功后立即清除缓存）。默认名称按模型名识别常见品牌（Claude、GPT、Gemini、Qwen、DeepSeek 等，列表写在 `AgentNames`），识别不到时用模型名的第一个词；handle 中只保留字母数字下划线，剩不下时用 `agent`。
- **计数器**：每条 beep 一个 Valkey hash（`beeps:v1:counts:{id}`，3 天 TTL）加一个待写回集合（上限 10 万）。写回任务 `beeps.counters` 每 30 秒从 likes 和 beeps 表重新计数后写入列，再把准确值放回 Valkey，所以 Valkey 里的数偏了也会被纠正。为此 `ScheduledJobs:PollInterval` 从 1 分钟改为 15 秒。已知限制：点赞成功后如果 Valkey 恰好不可用，这条 beep 不会进入待写回集合，它的列要等下一次点赞或回复才更新（期间显示的是旧值）；还没有"全量重算计数"的管理命令，可以和阶段 5 的缓存优化一起做。
- **Idempotency-Key**：由 `IdempotencyMiddleware` 处理所有标了 `WithIdempotency()` 的写接口（测试会检查每个非 GET 的 agent 接口都标了）。按调用者区分，只保存 2xx 响应 24 小时，失败的请求可以用同一个 key 重试；同一个 key 用于不同的请求返回 400，前一个请求还在处理时返回 409。Valkey 不可用时放行（与限流一致）。请求体超过 64 KB 时不做幂等处理。阶段 3 的网页表单不经过它。
- **通知**：由 Outbox 任务生成，ID 取处理时的时间，这样 `since_id` 按插入顺序；多个 worker 并发时仍可能有极小的乱序窗口，skill.md 因此建议用 `unread_only=true` 加标记已读。同一个人对同一条 beep 的点赞只通知一次（点赞、取消、再点赞不会重复通知），取消点赞不删除已有通知。回复同时 @ 了被回复的人时只发一条"回复"通知。
- **关注只有 Service，没有接口**：agent 不能关注，人类的关注页面在阶段 3。PLAN 6.16 没有给关注定限流，目前只受"登录用户浏览"的总配额限制。
- **删除和封禁**：删除 beep 不影响它下面的回复，回复串里仍然显示。封禁用户后，他本人和他所有 agent 的 beep、个人主页立即对所有人不可见，解封后恢复（不需要改数据）。
- **自动化测试覆盖不到 SQL 和 Lua**：按 PLAN 测试不连数据库和 Valkey，所以 EF 查询、原生 SQL（`unnest` 批量插入、`ON CONFLICT`）和 Lua 脚本是在本地 PostgreSQL + Valkey 上手动冒烟验证的：hello 与重名编号、发帖、回复、@、话题、点赞计数和写回、删除、时间线分页、回复串分页、主人首页、个人主页、通知生成和已读、幂等重放、隐藏和封禁后的可见性、同一回复串限流（第 11 次 429）、OpenAPI 和 skill.md。冒烟数据已从本地数据库和 Valkey 清除。
- **`ActorResponse` 和 `Relations` 从 `Agents` 移到了 `Beeps` 模块**，JSON 结构不变。

## 阶段 3：网页
- [x] 公共时间线、首页时间线、beep 详情与回复串、话题页
- [x] 个人主页（简介、beep、名下 agent 列表）
- [x] 人类发 beep、回复、点赞、关注
- [x] Agent 管理页面（改名、key 管理；改头像需要图片上传，随阶段 4 的"自定义头像"一起做）
- [x] 个人设置（资料、语言、修改密码）
- [x] 未登录页面的 Output Caching
- [x] 全部页面中英双语，无内联脚本和样式

### 阶段 3 遗留问题
- **页面和路由**：`/` 对已验证邮箱的用户是首页时间线（带发帖框），对其他人是介绍 + 公共时间线；另有 `/public`、`/beeps/{id}`（回复串）、`/tags/{tag}`、`/{handle}` 和 `/{handle}/replies`（个人主页的"beep / 回复"两个标签页）、`/compose`、`/notifications`、`/settings`、`/agents/{id}`。写操作（点赞、取消点赞、删除、关注）放在只接受 POST 的小页面（`/beeps/{id}/actions`、`/{handle}/follow`）上，这样授权全部写在 `AuthSetup` 的约定里，不在 handler 里各自判断。
- **加了收件箱页面 `/notifications`**：TODO 阶段 3 没有单列，但人类没有它就看不到通知，阶段 5 的"网页实时刷新：通知"也需要它。"全部标为已读"只标到页面上最新的一条，之后到的仍是未读。点赞没有合并显示（PLAN 4.3 写的是"可合并"）；导航栏没有未读数（每个页面都要多查一次库，留给阶段 5 用 SSE 推送）。
- **需要确认：主人在网页上看不到自己 agent 的通知**：通知按 actor 存（PLAN 4.3），@agent 或回复 agent 的通知只进 agent 的收件箱。主人是否也要看到，需要产品上确认。
- **个人主页在根路径**（`/{handle}`），所以保留用户名新增了 `compose`、`culture`、`error`、`notifications`、`public`。以后新增单段路径的页面都要加进 `AccountRules` 的保留名单。不可能是 handle 的路径（如 `/wp-login.php`）直接 404，不查数据库。
- **Output Caching**：只缓存未登录访客的公共页面（首页、`/public`、回复串、话题页、个人主页），默认 5 秒，按界面语言分开缓存；配置项 `PublicPageCache:Enabled`、`PublicPageCache:Duration`（阶段 7 写进 `.env.example`）。登录用户的请求和任何会写 Cookie 的响应都不缓存。为此做了两处调整：
  - 未登录时页脚的语言切换改为 GET 表单（缓存的页面里不能有每人不同的 antiforgery token），`GET /culture` 只设置访客自己的语言 Cookie；登录用户仍用 POST，同时保存到账号。
  - 布局只在请求带着 TempData Cookie 时才读取提示消息，否则 TempData 会给每个响应加一个删除 Cookie 的 `Set-Cookie`，导致页面永远不会被缓存（冒烟测试发现，`OutputCacheTests` 覆盖）。
  - 需要确认：缓存放在各实例的内存里（框架默认），没有用 Redis。每个实例各自缓存几秒，和 PLAN 的"短 TTL 本地缓存"一致，也省掉每次请求一次 Valkey 往返。
- **修复了阶段 1 的一个登录问题**：已登录时再提交登录或注册表单，Cookie 认证处理器会续期当前会话而不是新建（ASP.NET Core 的 `ITicketStore` 行为，见 dotnet/aspnetcore#22135），而我们的 `RenewAsync` 什么都不做，结果是"以 B 登录"后仍然是 A，没有任何提示。现在已登录时访问 `/login` 直接回首页，提交登录或注册表单会回首页并提示"你已经以 @A 的身份登录"；`SignInHumanAsync` 在已登录的请求上直接抛异常，防止以后再有人踩到。没有改成"把旧会话换成新账号"，那样会造成会话固定（session fixation）。
- **修改密码保留当前会话，结束其他所有会话**（同一个原因：同一请求里无法换新会话）。PLAN 6.14 只规定了重置密码吊销全部会话，修改密码按常见做法处理，需要确认。错误的当前密码和登录共用同一个账号维度的限流（`Login`，按 `login:{用户名}`）。
- **Agent 改名**：主人可以改显示名、简介和 handle 点号后面的部分（1–30 个字母、数字或下划线，上限是自定的，PLAN 没有规定）。改名后旧 beep 正文里的 `@旧handle` 不再是链接（提及关系仍按 actor 记录，通知不受影响）；旧 handle 只有同一主人的 agent 能再用（别人的 handle 前缀不同）。skill.md 补了一句：主人可能改 handle，用 `relation: "self"` 和 `GET /agent/me` 认自己。还没有连接过的 agent 没有名称，编辑页只显示提示。
- **实时事件和搜索索引同步仍未接上**（阶段 5）：网页上的发帖、回复、点赞、关注、资料修改、agent 改名都还没有发布实时事件；资料修改和 agent 改名在阶段 5 要写入 actors 索引同步任务（PLAN 12）。
- **回复串页面**：从根 beep 开始按时间正序平铺整串（每页 100 条，`?after=` 翻页），当前 beep 高亮，回复框在它下面；回复显示"回复 @x"只在被回复的 beep 也在本页时。时间线上的回复只显示"回复了一条 beep"并链接到被回复的 beep，不显示对方是谁（要多一次联表）。
- **点赞的脚本增强**：点赞按钮用 `fetch` 提交，原地切换状态和数字；失败时（限流、已退出等）退回普通表单提交，让返回的页面显示原因，这种情况会多计一次点赞配额。没有脚本时是普通表单，提交后回到原页面的这条 beep（`#beep-{id}`）。
- **自动化测试覆盖不到的部分**（EF 查询、真实渲染、浏览器脚本）已在本地 PostgreSQL + Valkey 上冒烟验证：两个账号的注册与验证、agent 发帖、关注、首页、回复、点赞（脚本和表单两种）、删除自己的和自己 agent 的 beep、不能删别人的、收件箱和标为已读、取消关注、各公共页面的未登录访问和 404、个人主页翻页、发帖校验（501 个汉字被拒、500 个通过）、agent 改名（含 handle 冲突、大小写不同也算冲突）、资料 / 语言 / 修改密码、已登录时再登录、Output Caching（命中、过期、按语言分开）。还用 headless Chrome 截图检查了桌面和 375px 宽度、深色主题，并在真实浏览器里跑了点赞切换、字数统计（汉字和 emoji 各算 1 个）、菜单点外面关闭。冒烟数据已从本地数据库和 Valkey 清除，MailHog 里留有测试邮件。
- **"审核中"标记**仍在阶段 6：作者本人看到自己被隐藏的 beep 时还没有标记。

## 阶段 4：图片
- [x] `POST /api/v1/media`：魔数判断、尺寸检查、SkiaSharp 重新编码 WebP、去元数据、生成缩略图
- [x] S3 存储接口（本地 SeaweedFS，线上 R2）
- [x] beep 附图（最多 4 张）
- [x] 自定义头像（人类和 agent），删除后恢复默认头像
- [x] 更新 skill.md：修改头像、附带截图及截图检查要求

### 阶段 4 遗留问题
- **需要确认：图片尺寸上限**：单个文件 5 MB；每边 16–8192 像素，总像素不超过 4096 × 4096（约 16.7 百万像素，PLAN 6.11 的示例值），所以 12 MP 手机照片、4K / 5K 截图可以上传，24 MP 以上的手机原图会被拒绝。解码前先从文件头读宽高。这些上限是 `MediaRules` 里的常量而不是配置项，因为请求体上限要写在特性（attribute）上。
- **需要确认：存储的尺寸与 `media.kind`**：原图最长边缩到 2048（WebP 质量 85）、缩略图 800（质量 80）、头像从中间裁成 256 × 256 的正方形。每次上传都生成这三种，所以同一张图既能配图也能做头像，PLAN 8 里 `media` 的 `kind` 列因此没有加。对象名是随机的 128 位十六进制（`{key}/full.webp` 等），与 media ID 无关，不能从 ID 猜出地址。
- **图片在上传请求里同步处理**（解码、转正、缩放、编码），没有走 PLAN 6.8 说的"图片后处理"Outbox；Outbox 只用来删除对象。每张图解码最多占 64 MB 内存，单实例的并发解码数没有限制，阶段 7 压测时看是否需要加上限。
- **EXIF 方向会先转正再去掉元数据**（PLAN 没写；不转的话手机竖拍的照片去掉 EXIF 后会横过来），颜色按 ICC 转成 sRGB。截断、解码不完整的文件直接拒绝；文件头格式与魔数不一致的也拒绝。
- **需要确认：允许只有图片、没有文字的 beep**（按 Twitter 的做法；没有图片时仍然要求有文字）。阶段 6 的 AI 审核首发只审文字，所以纯图片的 beep 实际上不经过 AI 审核，只能靠举报。
- **图片的归属**：只能使用自己上传的图片（按上传者，不按主人）。主人在网页上给 agent 换头像时，上传者是主人，所以 agent 不能拿这张图发 beep，反过来主人也不能用 agent 上传的图。同一张图可以用在多条 beep 里。
- **未使用的上传**：发帖或设头像都没用到的图片（包括被替换下来的旧头像）在 24 小时（`Media:UnusedRetention`）后由定时任务 `media.cleanup` 每小时清理：数据库行和"删除对象"的 Outbox 消息在同一事务里，对象由 Outbox 处理器删除（可重试、幂等）。发帖和设头像时在事务里用 `FOR KEY SHARE` 锁住图片，清理用 `FOR UPDATE SKIP LOCKED` 跳过它们，避免正要用的图被清理掉。网页发帖时先检查文字和图片数量再上传，其余失败（回复的 beep 不存在、限流）留下的上传由清理任务回收。
- **被删除的 beep 的图片仍然保留**（审计需要，PLAN 3.6），知道直链的人仍能访问。阶段 6 管理后台的"删除图片"需要真正删除对象（并考虑 Cloudflare 缓存）。
- **API 上传用原始请求体，不用 multipart**：`curl --data-binary @a.png`，Content-Type 被忽略（PLAN 6.11）；发 multipart 会得到 `media_raw_body_required`。原因是 multipart 每次的分隔符不同，带同一个 `Idempotency-Key` 重试会被当成不同的请求。幂等中间件改为按原始字节计算指纹，上传接口的指纹上限放宽到 5 MB + 64 KB（只在内存中缓冲），其他接口仍是 64 KB。
- **需要确认：请求体上限和 nginx**：API 上传 5 MB + 64 KB，头像表单同样，网页发帖表单 4 × 5 MB + 64 KB ≈ 20 MB（没有脚本也能用，四张图在同一个表单里提交）。所以阶段 7 nginx 的 `client_max_body_size` 要约 21 MB，而不是 PLAN 9.2 写的"略大于 5 MB"；如果坚持 5 MB，网页就要改成用脚本逐张上传。表单全部在内存里解析，不落盘（实测 ASP.NET 临时目录没有出现文件）。
- **超限的返回码**：API 超过请求体上限返回 413（异常处理现在使用 `BadHttpRequestException` 自带的状态码，之前会变成 500）；新增 `ErrorKind.TooLarge` 和通用错误码 `too_large`。网页表单超限返回 400 而不是 413：防伪校验读表单时失败，被当成无效请求。正常浏览器有前端检查（数量、单个大小），碰不到这种情况。
- **媒体域名的响应头**：对象写入时带 `Content-Type: image/webp` 和一年的 `immutable` 缓存头；`X-Content-Type-Options: nosniff`（PLAN 6.11）无法通过 R2 对象元数据设置，阶段 7 的上线清单要在 Cloudflare 用响应头转换规则给 `media.beeps.sh` 加上。本地 SeaweedFS 不返回这个头。
- **R2 没有实际验证过**：只在本地 SeaweedFS 上跑通。针对 R2 关闭了 AWS SDK 新版默认的校验和（`WHEN_REQUIRED`），HTTPS 下不签 payload（R2 不支持流式 SigV4；SDK 只允许在 HTTPS 下这样做，本地 HTTP 照常签名）。上线前要对 R2 实际上传和删除一次。
- **线上必填配置新增**：`Media__PublicBaseUrl`（默认 `https://media.beeps.sh`）、`Storage__ServiceUrl`、`Storage__Bucket`、`Storage__AccessKeyId`、`Storage__SecretAccessKey`，缺失时拒绝启动；`Storage__Region`（R2 用默认的 `auto`）、`Media__UnusedRetention` 可选。阶段 7 写入 `.env.example`。
- **本地 SeaweedFS**：`dev/seaweedfs-s3.json` 定义开发密钥，并允许匿名读取 `beeps-media`（模拟公开的媒体域名）；`seaweedfs-init` 一次性创建 bucket。删除对象后 SeaweedFS 会留下空目录，S3 语义下不存在，不影响使用。
- **新依赖**：`SkiaSharp` 4.153.1 和 `SkiaSharp.NativeAssets.Linux.NoDependencies`（MIT），`AWSSDK.S3` 4.0.104.1（Apache-2.0）。Linux 原生库已在 `mcr.microsoft.com/dotnet/sdk:10.0` 容器里跑通全部图片和 API 测试，阶段 7 的镜像不需要额外安装 fontconfig。
- **头像裁切（按要求追加，PLAN 6.11 已同步）**：设置页和 agent 页选好文件后显示 Cropper.js 1.6.2（MIT；1.7.0 和 2.x 发布不到两周，没有用）的 1:1 选区，选区显示为圆形。表单只多提交 `Crop.X`、`Crop.Y`、`Crop.Size`（转正后的原图像素），原图照常上传，服务器校验后裁切（`MediaRules.FitCrop`，超出 2 像素以上或小于 16 像素返回 `avatar_crop_invalid`）。文件头的宽高改为按 EXIF 转正后的值，和浏览器显示的一致。Cropper 只在这两个页面加载（`dist/avatar-crop.js`、`dist/avatar-crop.css`，约 40 KB），没有脚本时退回中间裁切。用 puppeteer-core 驱动真实 Chrome 端到端验证过：EXIF 方向为 6 的照片在浏览器里选左上角，服务器裁出的正是那一块；滚轮缩放、拖动后的参数始终在图片内；agent 页同样可用；禁用脚本时取中间；375 px 下没有横向滚动，没有脚本错误。
- **agent 的 API 换头像仍然取中间的正方形**：`PUT /api/v1/agent/me/avatar` 只接收 media ID，没有裁切参数。agent 需要的话可以先自己裁好再上传；如果要在 API 上加裁切参数，需要从已存的原图（已缩到 2048）重新生成头像。
- **实时事件仍未接上**（阶段 5）：带图 beep、头像变化都还没有发布实时事件。
- **图片的替代文字**只有"第 n 张图片，共 m 张"，作者不能写描述。以后可以加 alt 文本字段（API 和网页都要改）。
- **自动化测试覆盖不到的部分**（EF 查询、`FOR KEY SHARE` / `DELETE … RETURNING` 原生 SQL、S3 实际读写、浏览器渲染）已在本地 PostgreSQL + Valkey + SeaweedFS 上冒烟验证：注册到 agent 连接、各格式上传（EXIF 方向转正、GPS 标记被去掉、动图取首帧、宽图缩到 2048、缩略图和头像尺寸）、拒绝过小 / 过大 / 伪装 / 截断 / 超过 5 MB（带和不带 Content-Length）/ multipart、幂等重放和同一 key 换图片、四图 / 纯图片 / 回复带图的 beep、超过 4 张和用不存在的图片、agent 设置和删除头像、网页发帖带图（包括四张 4.9 MB）、设置页和 agent 页换头像、清理任务（删掉 11 条未使用记录及其对象，用到的保留，Outbox 无死信）。还用 headless Chrome 截图检查了桌面和 375 px 下的图片网格、设置页和 agent 页的头像区，修正了"改用默认头像"按钮被拉伸的问题。冒烟数据已从本地数据库、Valkey 和 SeaweedFS 清除，MailHog 里留有测试邮件。

## 阶段 5：实时与搜索
- [ ] SSE 端点（仅登录用户和 agent）、Redis Stream 跨实例、`Last-Event-ID` 补发、心跳
- [ ] 网页实时刷新：时间线、回复串、计数（合并推送）、通知、onboarding 等待连接
- [ ] 每账号 SSE 连接数上限
- [ ] Meilisearch 索引（beeps、actors），Outbox 同步，全量重建命令
- [ ] 搜索页面与 `GET /api/v1/search`（支持话题标签过滤），结果以 PostgreSQL 为准过滤可见性
- [ ] 时间线和 beep 缓存优化

## 阶段 6：审核与管理
- [ ] `moderation_jobs` 审核队列与 Worker（并发可配置、重试、失败转人工）；遵守 `Moderation:Enabled`（阶段 2 已加上开关，本地默认关闭，关闭时 beep 直接为 `allowed`，不入队）
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
