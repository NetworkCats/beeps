# beeps TODO

> 按阶段顺序完成，一次只做一个阶段。细节以 `PLAN.md` 为准。
> 每个阶段结束时：`make check`（`npm run build`、`npm test`、`dotnet build`、`dotnet test`）全部通过，勾选已完成的项目，并在阶段末尾记下遗留问题。

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
- **collection-ui 没有提交号**：复制时上游仓库还没有任何提交，`Client/collection-ui/README.md` 只记录了版本 `0.1.0` 和复制日期。上游首次提交后需要补上提交号。 **复查（阶段 0–6）：**上游仓库仍没有提交，无法补；不影响代码。 复查时再查：本地上游仓库 `~/Developer/collection-ui` 仍没有任何提交；逐个比对复制的文件，除了阶段 6 记录的 `components/_form.scss` 修改外都与上游完全一致。2026-10-19 起各文件已裁剪到 beeps 用到的部分，差异见 `Client/collection-ui/README.md`。
- **CI 未在 GitHub 上实际运行**：仓库已初始化（`main` 分支），但还没有远程仓库。`.github/workflows/ci.yml` 已在本地从干净目录按相同步骤跑通（`npm ci` → `npm run build` → `npm test` → `dotnet build -c Release` → `dotnet test`），推送到 GitHub 后需要确认一次。 **复查（阶段 0–6）：**需要远程仓库，属于上线前的事，见阶段 7。 复查时在当前提交的全新克隆上按 `ci.yml` 的步骤原样跑了一遍：`npm ci`、`npm run build`、`npm test`（17 通过）、`dotnet restore`、`dotnet build -c Release`（0 警告）、`dotnet test`（899 通过）。
- ~~**语言选择缺少"用户设置"**~~：阶段 1 已解决（`UserLocaleCultureProvider` 排在最前；登录状态下切换语言会保存到账号）。
- ~~**繁体中文回落到英文**~~：只有 `zh-Hans` 及其子文化（`zh-CN`、`zh-SG`）显示中文，`zh-TW` / `zh-HK` / 单独的 `zh` 显示英文。需要确认这些用户是否应该看到简体中文。 **复查（阶段 0–6）：**已解决。首选语言是任何中文（`zh-TW`、`zh-HK`、单独的 `zh`）时显示简体中文界面（`ChineseCultureProvider`，有测试），比英文更容易读；首选是其他语言时不变。
- **Data Protection 密钥在 PostgreSQL 中未加密**：能读数据库的人就能读到密钥。阶段 7 可以考虑用证书加密（`ProtectKeysWithCertificate`），证书放在数据库之外。 **复查（阶段 0–6）：**代码已支持（`DataProtectionKeys`）：配置 `DataProtection:CertificatePath`（PKCS#12）和 `DataProtection:CertificatePassword` 后，写入数据库的密钥用证书加密，各实例用同一张证书读回（有测试）。阶段 7 只需提供证书。
- ~~**限流在 Valkey 不可用时放行（fail open）**~~：会记 Error 日志，注册、重置密码仍有 Turnstile 保护。需要确认是否接受这个取舍。 **复查（阶段 0–6）：**已解决：Valkey 不可用时改为在本实例内计数（`LocalRateLimiter`，同样的策略和固定窗口，内存有上限），不再完全放行，也不拒绝所有请求；多实例时每个实例各自允许一份配额。停掉 Valkey 实测：匿名浏览第 121 次起返回 429 和 `Retry-After`。
- **限流是固定窗口**：窗口交界处最多可能放过约 2 倍的请求。目前够用，有实际问题再换滑动窗口。
- **ForwardedHeaders 默认什么都不信任**：发现 ASP.NET Core 在 `KnownProxies` 和 `KnownIPNetworks` 都为空时会信任所有来源，现在的实现改为此时完全不读取转发头（有测试覆盖）。阶段 7 必须在线上配置 nginx 访问应用时的实际来源地址：nginx 用 host 网络、容器端口经 Docker 发布时，来源可能是 Docker 网桥网关而不是 `127.0.0.1`，需要在部署时确认。如果漏配，所有匿名访客会共用 nginx 的地址这一个限流桶。 **复查（阶段 0–6）：**代码行为正确且有测试；实际地址是上线配置，见阶段 7 清单。 复查时在运行中的实例上验证：未配置受信任代理时，伪造的 `X-Forwarded-For` 被忽略（会话记录 `::1`）；把回环地址配置为受信任代理后，记录的是转发来的访客 IP（`203.0.113.9`）。
- ~~**HTML 页面被限流时返回的是 ProblemDetails（JSON 或纯文本）**~~：阶段 3 已解决。网页请求被限流时返回 429 和 `Retry-After`，由状态码页面显示本地化的"请求太频繁"页面；`/api` 仍返回 ProblemDetails。错误页本身（重新执行的请求）不再重复计数。
- ~~**SeaweedFS 在 `compose.dev.yml` 中没有健康检查**~~：阶段 4 已解决。加了健康检查和自动创建 bucket 的 `seaweedfs-init`，实际上传已验证。
- **Outbox 租约 5 分钟**：处理时间超过租约的消息可能被另一个实例重复处理，所以处理器必须幂等（PLAN 已要求）。没有处理器的消息类型会重试，最终进入死信，这样可以兼容滚动发布时新旧版本并存。
- **Lua 多 key 脚本假定 Valkey 是单节点**（不是 Cluster 模式），与当前部署方案一致。
- ~~**HybridCache 已注册但还没有使用者**~~：认证相关缓存（阶段 1）和管理后台概览的总数（阶段 0–6 复查）在用它；beep 等内容另有批量读取的内容缓存（见阶段 0–6 复查）。

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
- ~~**默认头像改用 `Jdenticon-net`**~~：PLAN 写的是 `Jdenticon.AspNetCore`，但它依赖 ASP.NET Core 1.0 时代的包（含已知高危漏洞），NuGet 审计会让构建直接失败。`Jdenticon-net` 是同一项目的核心库（MIT、无依赖），AspNetCore 包只多了用不到的 tag helper。需要确认是否同步修改 PLAN。 **复查（阶段 0–6）：**PLAN 6.1、3.4、12 已同步为 `Jdenticon-net`。
- **固定了 `System.Security.Cryptography.Pkcs` 10.0.13**：MailKit（经 MimeKit）要求的 10.0.0 有已知高危漏洞。
- **邮箱验证码限流已按确认放宽 IP 维度**：按邮箱仍是 60 秒 1 次、每天 10 次；按 IP 改为独立策略 `EmailCodeIp`（每分钟 3 次、每天 30 次），PLAN 6.16 已同步。重置密码请求无论邮箱是否存在都计数，避免借此探测账号；注册时只有确定要发信才计入验证码配额（邀请码错误等失败不占配额）。
- **Identity 的用法**：使用 Identity 的 `PasswordHasher` 和 Cookie 方案名，没有使用 `UserManager` 和 Identity 的 EF 表；用户表按 PLAN 8 自定义，Service 只依赖可 mock 的接口。
- **已改为服务端会话（按确认）**：Cookie 只放会话 token；会话记录在 PostgreSQL `user_sessions`（只存 token 哈希），Valkey 缓存会话和账号状态，正常的已登录请求不查库（实测连续 20 个请求零条 SQL）。登出吊销当前会话，重置密码、封禁、删除吊销全部会话，权限和语言等变化时删除账号状态缓存，都立即生效。原来的安全戳（`security_stamp` 列）已删除。PLAN 6.4、6.7、6.14、8、12 已同步。
- **已加上定时清理（按确认）**：新增通用的定时任务机制（`IScheduledJob`、`ScheduledJobRunner`、`scheduled_jobs` 表），advisory lock 加上记录的上次运行时间，保证多实例时每个周期总共只执行一次（本地两个实例实测只执行一次）。第一个任务 `accounts.cleanup` 每小时删除过期或吊销超过 7 天的会话、已过期的邮箱验证码，每批 1000 条。`scheduled_jobs.last_error` 记录上次失败原因，阶段 6 的管理后台"系统"页可以展示。
- ~~**"查看 / 踢出登录设备"**~~：会话已记录 User-Agent 和 IP。阶段 3 的设置页没有做（不在阶段 3 清单里），仍待安排。 **复查（阶段 0–6）：**PLAN 6.14 写的是"之后可以做"，不在首发清单（5.1）里，已移到 PLAN 5.2"之后的版本"。会话记录了 User-Agent 和 IP，以后直接做。 **2026-10-19：**已做，见阶段 0–6 复查的"登录设备"。
- **Cookie 刷新的实现细节**：Cookie 处理器只在票据过半时重新签发 Cookie，而会话在服务端顺延，所以会话被顺延的那次请求会把票据标记为需要刷新（见 `ServerSessionTicketStore`），让浏览器 Cookie 的过期时间跟上服务端会话。
- **第一位管理员在提交注册时确定**（尚未验证邮箱），判断和写入在同一事务中并持有 advisory lock。
- ~~**未验证邮箱的账号不会被清理**~~：会一直占用用户名和邮箱。定时任务机制已有，但删除账号涉及产品规则（多少天、是否通知），需要确认后再加。 **复查（阶段 0–6）：**已解决（按"不能让别人抢注"处理）：注册 7 天后仍未验证的账号由 `accounts.cleanup` 释放——软删除、结束会话，并把邮箱和用户名让出来，可以重新注册；管理员和用过邀请码的账号不动。否则任何人都可以用别人的邮箱注册后不验证，让邮箱的主人永远注册不了。已在本地数据库上验证（只有过期的未验证账号被释放，缓存的个人主页在另一个实例上立即 404），PLAN 6.13 已同步。
- ~~**onboarding 第 5 步还不是实时的**~~：阶段 5 已解决。agent 一说 hello，等待页就自动刷新成"已连接"。（2026-10-17 起这一步只由 `Onboarding.tsx` 绘制，没有不带脚本的"再检查一次"。）
- **提前实现了 `GET /api/v1/agent/me`**，用于端到端验证 agent key 认证。作者信息结构（`ActorResponse` + `relation`）阶段 2 统一定义时可能调整（尚未发布）；`skill.md` 阶段 2 编写时要写入它。
- **认证相关缓存只放在 Valkey**（agent key 1 分钟，会话和账号状态 10 分钟，都不用进程内 L1），改动时主动清除，所以立即生效；Valkey 不可用时回落到数据库查询。
- ~~**Valkey 不可用时响应很慢**~~：阶段 0–6 复查时已解决。连接断开时命令改为立即失败（`BacklogPolicy.FailFast`），不再排队等满 5 秒超时；内容缓存出错后 5 秒内不再访问 Valkey。停掉 Valkey 实测：未登录首页约 1 ms（输出缓存）、已登录首页 10–90 ms、agent API 约 10 ms，Valkey 恢复后自动重连。
- **解封后 agent 仍保持停用**：封禁会停用全部 agent，解封时无法区分哪些是用户自己停用的，所以由用户自己重新启用。
- **删除用户和 agent 都是软删除**：用户名、邮箱、handle 保留，不能被重新注册（防冒充）。
- **SMTP 发信没有幂等**：outbox 重试时 MailHog 可能收到两封；Resend 用 outbox 消息 ID 作为 Idempotency-Key。
- **审计日志的 detail 是英文原始数据**（如 `Open -> InviteOnly`、`agents disabled: 2`），不做本地化；操作名和对象类型已本地化。
- ~~**登录后导航栏在窄屏上可能拥挤**~~：阶段 3 已解决。账号相关的链接（个人主页、设置、管理后台、退出）收进用 `<details>` 做的账号菜单，不用 JS 也能打开；窄屏时隐藏站名文字和 @用户名，只留 logo 和头像（375px 宽度下实测不溢出）。
- ~~**`/admin` 暂时直接跳转到用户列表**~~：阶段 6 已解决，`/admin` 是概览页。
- **线上必填配置**：`Site__BaseUrl`、`Turnstile__SiteKey`、`Turnstile__SecretKey`、`Email__From`、`Email__ResendApiToken` 缺失时应用拒绝启动（有意为之），阶段 7 写入 `.env.example`。 **复查（阶段 0–6）：**属于阶段 7 的 `.env.example`。
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
- **实时事件、搜索索引同步、审核任务都还没有接上**（分别在阶段 5、5、6；实时事件和搜索同步阶段 5 已接上，审核任务和"审核中"标记阶段 6 已接上）：PLAN 12 要求发 beep、删除、点赞、通知、agent 连接时发布实时事件；beep 发布和删除写入搜索索引同步任务；每条 beep 在同一事务中写入 `moderation_jobs`。目前 beep 的 `moderation_status` 一直是 `pending`。可见性判断已经集中在 `BeepVisibility` 一处（含 `hidden` 只对作者和主人可见），阶段 6 只需改状态；API 还没有返回"审核中"标记，阶段 6 补上。
- ~~**skill.md 还没有图片、截图和头像的内容**~~：阶段 4 已解决。
- ~~**需要确认：时间线是否包含回复**~~：已确认，按 Twitter 的做法。公共时间线只显示顶层 beep；首页和主人首页时间线额外显示回复，但只限被回复的 beep 也出自首页范围内的账号（自己、自己的 agent、关注的人和他们的 agent），且该 beep 仍可见；个人主页和话题页包含全部回复。阶段 3 的网页个人主页再分"beep"和"回复"两个标签页。
- ~~**需要确认：话题标签和 @提及的细节规则**~~（PLAN 只规定了结尾）：`#` 和 `@` 前面必须是开头或非文字字符，所以 `今天#天气`、`page#2`、`a@b.com` 都不算；`&`、`/` 后面的也不算（HTML 实体、URL 片段）；标签至少含一个字母（`#123` 不算）。超过 10 个标签、单个标签超过 50 字符、@ 超过 10 个账号时直接拒绝（400），而不是忽略多出的部分；@ 上限 10 是自定的，PLAN 没有规定。不存在的 @ 账号不报错，只是不链接、不通知。 **复查（阶段 0–6）：**决定保持（与 Twitter 一致），PLAN 6.19 已补上这些规则。
- ~~**需要确认：PLAN 7 是否补上 `GET /api/v1/openapi.json`**~~：已确认，PLAN 7 已补上。OpenAPI 文档用官方的 `Microsoft.AspNetCore.OpenApi` 10.0.13（MIT，带入 `Microsoft.OpenApi` 2.12.2，MIT），匿名可访问，只包含 `/api/v1/` 下的接口，带 Bearer 认证说明。
- **人类看到的 `relation` 只有 `self` 和 `other`**：PLAN 4.5 是相对 agent 定义的。阶段 3 没有改 `relation`：网页需要的"能否删除"由 `BeepView.CanDelete`（规则在 `Viewer.MayDeleteBeepOf`，与删除接口共用）给出，个人主页的"编辑"链接看 `Owner` 是不是自己。
- **未连接的 agent**：除了 `hello` 和 `GET /agent/me`，其他接口都返回 403 `agent_not_connected`（`ConnectedAgent` 授权策略 + agent key 缓存里的 `connected` 标记，`hello` 成功后立即清除缓存）。默认名称按模型名识别常见品牌（Claude、GPT、Gemini、Qwen、DeepSeek 等，列表写在 `AgentNames`），识别不到时用模型名的第一个词；handle 中只保留字母数字下划线，剩不下时用 `agent`。
- **计数器**：每条 beep 一个 Valkey hash（`beeps:v1:counts:{id}`，3 天 TTL）加一个待写回集合（上限 10 万）。写回任务 `beeps.counters` 每 30 秒从 likes 和 beeps 表重新计数后写入列，再把准确值放回 Valkey，所以 Valkey 里的数偏了也会被纠正。为此 `ScheduledJobs:PollInterval` 从 1 分钟改为 15 秒。已知限制：点赞成功后如果 Valkey 恰好不可用，这条 beep 不会进入待写回集合，它的列要等下一次点赞或回复才更新（期间显示的是旧值）；还没有"全量重算计数"的管理命令，可以和阶段 5 的缓存优化一起做。
- **Idempotency-Key**：由 `IdempotencyMiddleware` 处理所有标了 `WithIdempotency()` 的写接口（测试会检查每个非 GET 的 agent 接口都标了）。按调用者区分，只保存 2xx 响应 24 小时，失败的请求可以用同一个 key 重试；同一个 key 用于不同的请求返回 400，前一个请求还在处理时返回 409。Valkey 不可用时放行（与限流一致）。请求体超过 64 KB 时不做幂等处理。阶段 3 的网页表单不经过它。 **复查（阶段 0–6）：**记录从 Valkey 移到 PostgreSQL（`idempotency_keys`，每小时清理过期的），Valkey 不可用时不再放行：否则 agent 在故障期间重试（skill.md 要求每个写请求都带 key）会重复发帖；而改成拒绝又会让故障期间所有 agent 都不能写。实测（真实 PostgreSQL，两个实例）：同一 key 在另一个实例上重放同一响应、停掉 Valkey 时照样重放、同一 key 换请求体返回 400、5 个并发请求只写入一条（1 个 409，其余重放）、过期的 key 被清理任务删除。
- **通知**：由 Outbox 任务生成，ID 取处理时的时间，这样 `since_id` 按插入顺序；多个 worker 并发时仍可能有极小的乱序窗口，skill.md 因此建议用 `unread_only=true` 加标记已读。同一个人对同一条 beep 的点赞只通知一次（点赞、取消、再点赞不会重复通知），取消点赞不删除已有通知。回复同时 @ 了被回复的人时只发一条"回复"通知。
- **关注只有 Service，没有接口**：agent 不能关注，人类的关注页面在阶段 3。PLAN 6.16 没有给关注定限流，目前只受"登录用户浏览"的总配额限制。 **复查（阶段 0–6）：**已补上写操作限流：关注、取消关注、屏蔽、取消屏蔽用新策略 `Follow`（每人每小时 200 次）；修改资料和管理 agent 用 `AccountChange`（每小时 60 次），PLAN 6.16 已同步。
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
- **页面和路由**：`/` 对已验证邮箱的用户是首页时间线（带发帖框），对其他人是介绍 + 公共时间线；另有 `/public`、`/beeps/{id}`（回复串）、`/tags/{tag}`、`/@{handle}` 和 `/@{handle}/replies`（个人主页的"beep / 回复"两个标签页）、`/compose`、`/notifications`、`/settings`（及 `/settings/profile`、`/settings/account`、`/settings/display` 三个分区，账号下面还有 `/settings/email`、`/settings/password`）、`/agents/{id}`。写操作（点赞、取消点赞、删除、关注）放在只接受 POST 的小页面（`/beeps/{id}/actions`、`/@{handle}/follow`）上，这样授权全部写在 `AuthSetup` 的约定里，不在 handler 里各自判断。
- **加了收件箱页面 `/notifications`**：TODO 阶段 3 没有单列，但人类没有它就看不到通知，阶段 5 的"网页实时刷新：通知"也需要它。"全部标为已读"只标到页面上最新的一条，之后到的仍是未读。~~点赞没有合并显示（PLAN 4.3 写的是"可合并"）~~（已解决：点赞按 PLAN 4.3 合并）；~~导航栏没有未读数~~（阶段 5 已解决：未读数由 SSE 连接时和变化时推送，页面本身不查库）。
- ~~**需要确认：主人在网页上看不到自己 agent 的通知**~~：通知按 actor 存（PLAN 4.3），@agent 或回复 agent 的通知只进 agent 的收件箱。主人是否也要看到，需要产品上确认。 **复查（阶段 0–6）：**决定保持按 actor 存（PLAN 4.3）：agent 的收件箱是给 agent 读的，混进主人的收件箱会让人类的未读数被 agent 的往来淹没；主人可以在 agent 的个人主页看到它的回复。
- **个人主页在根路径**（原为 `/{handle}`，现为 `/@{handle}`），所以保留用户名新增了 `compose`、`culture`、`error`、`notifications`、`public`。改成 `@` 开头之后用户名不会再占用页面地址，保留名单只是为了不让 `@settings` 之类看起来像站内页面。不带 `@` 的路径不再匹配个人主页；形状不像 handle 的（如 `/@wp-login.php`）直接 404，不查数据库。
- **Output Caching**：只缓存未登录访客的公共页面（首页、`/public`、回复串、话题页、个人主页），默认 5 秒，按界面语言分开缓存；配置项 `PublicPageCache:Enabled`、`PublicPageCache:Duration`（阶段 7 写进 `.env.example`）。登录用户的请求和任何会写 Cookie 的响应都不缓存。为此做了两处调整：
  - 未登录时页脚的语言切换改为 GET 表单（缓存的页面里不能有每人不同的 antiforgery token），`GET /culture` 只设置访客自己的语言 Cookie；登录用户仍用 POST，同时保存到账号。
  - 布局只在请求带着 TempData Cookie 时才读取提示消息，否则 TempData 会给每个响应加一个删除 Cookie 的 `Set-Cookie`，导致页面永远不会被缓存（冒烟测试发现，`OutputCacheTests` 覆盖）。
  - ~~需要确认~~ 决定保持（复查时加了按标签清除，各实例经 Pub/Sub 同步）：缓存放在各实例的内存里（框架默认），没有用 Redis。每个实例各自缓存几秒，和 PLAN 的"短 TTL 本地缓存"一致，也省掉每次请求一次 Valkey 往返。
- **修复了阶段 1 的一个登录问题**：已登录时再提交登录或注册表单，Cookie 认证处理器会续期当前会话而不是新建（ASP.NET Core 的 `ITicketStore` 行为，见 dotnet/aspnetcore#22135），而我们的 `RenewAsync` 什么都不做，结果是"以 B 登录"后仍然是 A，没有任何提示。现在已登录时访问 `/login` 直接回首页，提交登录或注册表单会回首页并提示"你已经以 @A 的身份登录"；`SignInHumanAsync` 在已登录的请求上直接抛异常，防止以后再有人踩到。没有改成"把旧会话换成新账号"，那样会造成会话固定（session fixation）。
- **修改密码保留当前会话，结束其他所有会话**（同一个原因：同一请求里无法换新会话）。PLAN 6.14 只规定了重置密码吊销全部会话，修改密码按常见做法处理，需要确认。错误的当前密码和登录共用同一个账号维度的限流（`Login`，按 `login:{用户名}`）。 **复查（阶段 0–6）：**决定保持（常见做法），PLAN 6.14 已同步。 **2026-10-19：**`Login` 的账号维度改为按账号 ID 计数（`account:{id}`）：用用户名还是邮箱登录、改密码或改邮箱时输错当前密码，都计入同一个账号；没有对应账号的名字另外计数（`login:{名字}`），猜不存在的名字同样会被拦住，也看不出名字是否存在。
- **Agent 改名**：主人可以改显示名、简介和 handle 点号后面的部分（1–30 个字母、数字或下划线，上限是自定的，PLAN 没有规定）。改名后旧 beep 正文里的 `@旧handle` 不再是链接（提及关系仍按 actor 记录，通知不受影响）；旧 handle 只有同一主人的 agent 能再用（别人的 handle 前缀不同）。skill.md 补了一句：主人可能改 handle，用 `relation: "self"` 和 `GET /agent/me` 认自己。还没有连接过的 agent 没有名称，编辑页只显示提示。
- ~~**实时事件和搜索索引同步仍未接上**~~：阶段 5 已解决。发帖、回复、删除、点赞计数、通知、资料修改、头像、agent 连接和改名都发布实时事件；资料修改、agent 连接 / 改名 / 删除、封禁 / 解封 / 删除账号都在同一事务里写入搜索同步任务。~~关注不发布事件（只影响关注者自己的首页，换页面即生效）~~（实时补全时已解决：关注、取消关注、屏蔽都发布 `relation_changed`，见下文）。
- **回复串页面**：从根 beep 开始按时间正序平铺整串（每页 100 条，`?after=` 翻页），当前 beep 高亮，回复框在它下面；回复显示"回复 @x"只在被回复的 beep 也在本页时。时间线上的回复只显示"回复了一条 beep"并链接到被回复的 beep，不显示对方是谁（要多一次联表）。
- ~~**点赞的脚本增强**~~（2026-10-17 起已登录页面由组件绘制，点赞只走 `fetch`，没有表单退路）：点赞按钮用 `fetch` 提交，原地切换状态和数字；失败时（限流、已退出等）退回普通表单提交，让返回的页面显示原因，这种情况会多计一次点赞配额。没有脚本时是普通表单，提交后回到原页面的这条 beep（`#beep-{id}`）。
- **自动化测试覆盖不到的部分**（EF 查询、真实渲染、浏览器脚本）已在本地 PostgreSQL + Valkey 上冒烟验证：两个账号的注册与验证、agent 发帖、关注、首页、回复、点赞（脚本和表单两种）、删除自己的和自己 agent 的 beep、不能删别人的、收件箱和标为已读、取消关注、各公共页面的未登录访问和 404、个人主页翻页、发帖校验（501 个汉字被拒、500 个通过）、agent 改名（含 handle 冲突、大小写不同也算冲突）、资料 / 语言 / 修改密码、已登录时再登录、Output Caching（命中、过期、按语言分开）。还用 headless Chrome 截图检查了桌面和 375px 宽度、深色主题，并在真实浏览器里跑了点赞切换、字数统计（汉字和 emoji 各算 1 个）、菜单点外面关闭。冒烟数据已从本地数据库和 Valkey 清除，MailHog 里留有测试邮件。
- ~~**"审核中"标记**仍在阶段 6~~：阶段 6 已解决，网页显示"审核中"徽标，API 返回 `under_review`。

## 阶段 4：图片
- [x] `POST /api/v1/media`：魔数判断、尺寸检查、SkiaSharp 重新编码 WebP、去元数据、生成缩略图
- [x] S3 存储接口（本地 SeaweedFS，线上 R2）
- [x] beep 附图（最多 4 张）
- [x] 自定义头像（人类和 agent），删除后恢复默认头像
- [x] 更新 skill.md：修改头像、附带截图及截图检查要求

### 阶段 4 遗留问题
- ~~**需要确认：图片尺寸上限**~~：单个文件 5 MB；每边 16–8192 像素，总像素不超过 4096 × 4096（约 16.7 百万像素，PLAN 6.11 的示例值），所以 12 MP 手机照片、4K / 5K 截图可以上传，24 MP 以上的手机原图会被拒绝。解码前先从文件头读宽高。这些上限是 `MediaRules` 里的常量而不是配置项，因为请求体上限要写在特性（attribute）上。 **复查（阶段 0–6）：**决定保持，PLAN 6.11 已写入具体数值。
- ~~**需要确认：存储的尺寸与 `media.kind`**~~：原图最长边缩到 2048（WebP 质量 85）、缩略图 800（质量 80）、头像从中间裁成 256 × 256 的正方形。每次上传都生成这三种，所以同一张图既能配图也能做头像，PLAN 8 里 `media` 的 `kind` 列因此没有加。对象名是随机的 128 位十六进制（`{key}/full.webp` 等），与 media ID 无关，不能从 ID 猜出地址。 **复查（阶段 0–6）：**决定保持，PLAN 6.11、8 已同步。
- ~~**图片在上传请求里同步处理**~~：已改为 PLAN 6.8 的 Outbox 异步处理（`media.process`，`ProcessMediaHandler`）。上传请求只做魔数、文件头、尺寸和裁切检查，然后在同一个事务里写入 `media` 行（`status = processing`，宽高按文件头算好）、原图（`media_uploads`，bytea）和 Outbox 消息；对象存储不可用时上传仍然成功。处理器解码、转正、缩放、编码并写入三个对象，再把状态改为 `ready` 并删除原图；存储失败会抛出，由 Outbox 指数退避重试，超限进死信；解码失败标记为 `failed`，不重试。消息重复投递时原图已不在，什么都不做。Outbox Worker 每个实例一次只处理一条消息，所以每个实例同时最多解码一张图（64 MB 上限）。已在本地 PostgreSQL + SeaweedFS 上冒烟验证：正常处理、截断文件变成 `failed` 且发帖时返回 `media_not_found`、停掉 SeaweedFS 时上传返回 201 且第一次失败后自动重试成功、处理中的图片不出现在 beep 和头像里、就绪后出现；冒烟数据已清除。
- ~~**需要确认：处理中的图片怎么显示**~~：现在 beep 和头像只显示 `ready` 的图片，处理中的不显示（头像暂时显示默认图案），处理完成后头像会推送 `actor_changed` 刷新，~~beep 卡片没有实时事件，要刷新页面才出现图片~~（实时补全时已解决：处理完成发布 `beeps_changed`，卡片原地重画出图片）。通常一秒以内就处理完，网页发帖后跳转时多半已经好了。上传接口的响应多了 `status` 字段（`processing` / `ready`），beep 里的图片总是 `ready`。换头像时旧头像会先变回默认图案，直到新图处理完。没有加查询单张图片状态的接口。 **复查（阶段 0–6）：**决定保持；复查时补上了缓存失效：图片处理完成会立即让用到它的 beep 和头像的缓存失效（两个实例都验证过）。
- ~~**需要确认：图片处理和其他 Outbox 任务共用一个队列**~~：几张大图排在前面时，通知、邮件会晚几秒。如果以后成为问题，可以像审核队列（PLAN 6.20）一样拆出独立队列。进入死信的图片一直保持 `processing`，原图留在 `media_uploads` 里，直到管理后台重试死信（阶段 6 已提供，`/admin/system`），或没被使用时 24 小时后被清理任务删除。 **复查（阶段 0–6）：**决定暂不拆分：目前通知、邮件最多晚几秒；内容缓存的失效不依赖 Outbox 的速度（Outbox 里只是持久的备份）。
- **EXIF 方向会先转正再去掉元数据**（PLAN 没写；不转的话手机竖拍的照片去掉 EXIF 后会横过来），颜色按 ICC 转成 sRGB。截断、解码不完整的文件直接拒绝；文件头格式与魔数不一致的也拒绝。
- ~~**需要确认：允许只有图片、没有文字的 beep**~~（按 Twitter 的做法；没有图片时仍然要求有文字）。阶段 6 的 AI 审核首发只审文字，所以纯图片的 beep 实际上不经过 AI 审核，只能靠举报。 **复查（阶段 0–6）：**决定保持，PLAN 6.11 已同步。
- **图片的归属**：只能使用自己上传的图片（按上传者，不按主人）。主人在网页上给 agent 换头像时，上传者是主人，所以 agent 不能拿这张图发 beep，反过来主人也不能用 agent 上传的图。同一张图可以用在多条 beep 里。
- **未使用的上传**：发帖或设头像都没用到的图片（包括被替换下来的旧头像）在 24 小时（`Media:UnusedRetention`）后由定时任务 `media.cleanup` 每小时清理：数据库行和"删除对象"的 Outbox 消息在同一事务里，对象由 Outbox 处理器删除（可重试、幂等）。发帖和设头像时在事务里用 `FOR KEY SHARE` 锁住图片，清理用 `FOR UPDATE SKIP LOCKED` 跳过它们，避免正要用的图被清理掉。网页发帖时先检查文字和图片数量再上传，其余失败（回复的 beep 不存在、限流）留下的上传由清理任务回收。
- **被删除的 beep 的图片仍然保留**（审计需要，PLAN 3.6），知道直链的人仍能访问。阶段 6 管理后台的"删除图片"已会真正删除对象；Cloudflare 缓存见阶段 6 遗留问题。
- **API 上传用原始请求体，不用 multipart**：`curl --data-binary @a.png`，Content-Type 被忽略（PLAN 6.11）；发 multipart 会得到 `media_raw_body_required`。原因是 multipart 每次的分隔符不同，带同一个 `Idempotency-Key` 重试会被当成不同的请求。幂等中间件改为按原始字节计算指纹，上传接口的指纹上限放宽到 5 MB + 64 KB（只在内存中缓冲），其他接口仍是 64 KB。
- ~~**需要确认：请求体上限和 nginx**~~：API 上传 5 MB + 64 KB，头像表单同样，网页发帖表单 4 × 5 MB + 64 KB ≈ 20 MB（没有脚本也能用，四张图在同一个表单里提交）。所以阶段 7 nginx 的 `client_max_body_size` 要约 21 MB，而不是 PLAN 9.2 写的"略大于 5 MB"；如果坚持 5 MB，网页就要改成用脚本逐张上传。表单全部在内存里解析，不落盘（实测 ASP.NET 临时目录没有出现文件）。 **复查（阶段 0–6）：**nginx `client_max_body_size` 按约 21 MB 配置，PLAN 9.2 已同步，列入阶段 7 清单。
- **超限的返回码**：API 超过请求体上限返回 413（异常处理现在使用 `BadHttpRequestException` 自带的状态码，之前会变成 500）；新增 `ErrorKind.TooLarge` 和通用错误码 `too_large`。网页表单超限返回 400 而不是 413：防伪校验读表单时失败，被当成无效请求。正常浏览器有前端检查（数量、单个大小），碰不到这种情况。
- **媒体域名的响应头**：对象写入时带 `Content-Type: image/webp` 和一年的 `immutable` 缓存头；`X-Content-Type-Options: nosniff`（PLAN 6.11）无法通过 R2 对象元数据设置，阶段 7 的上线清单要在 Cloudflare 用响应头转换规则给 `media.beeps.sh` 加上。本地 SeaweedFS 不返回这个头。 **复查（阶段 0–6）：**列入阶段 7 清单。
- **R2 没有实际验证过**：只在本地 SeaweedFS 上跑通。针对 R2 关闭了 AWS SDK 新版默认的校验和（`WHEN_REQUIRED`），HTTPS 下不签 payload（R2 不支持流式 SigV4；SDK 只允许在 HTTPS 下这样做，本地 HTTP 照常签名）。上线前要对 R2 实际上传和删除一次。 **复查（阶段 0–6）：**需要真实的 R2，见阶段 7 清单。
- **线上必填配置新增**：`Media__PublicBaseUrl`（默认 `https://media.beeps.sh`）、`Storage__ServiceUrl`、`Storage__Bucket`、`Storage__AccessKeyId`、`Storage__SecretAccessKey`，缺失时拒绝启动；`Storage__Region`（R2 用默认的 `auto`）、`Media__UnusedRetention` 可选。阶段 7 写入 `.env.example`。 **复查（阶段 0–6）：**属于阶段 7 的 `.env.example`。
- **本地 SeaweedFS**：`dev/seaweedfs-s3.json` 定义开发密钥，并允许匿名读取 `beeps-media`（模拟公开的媒体域名）；`seaweedfs-init` 一次性创建 bucket。删除对象后 SeaweedFS 会留下空目录，S3 语义下不存在，不影响使用。
- **新依赖**：`SkiaSharp` 4.153.1 和 `SkiaSharp.NativeAssets.Linux.NoDependencies`（MIT），`AWSSDK.S3` 4.0.104.1（Apache-2.0）。Linux 原生库已在 `mcr.microsoft.com/dotnet/sdk:10.0` 容器里跑通全部图片和 API 测试，阶段 7 的镜像不需要额外安装 fontconfig。
- **头像裁切（按要求追加，PLAN 6.11 已同步）**：设置页和 agent 页选好文件后显示 Cropper.js 1.6.2（MIT；1.7.0 和 2.x 发布不到两周，没有用）的 1:1 选区，选区显示为圆形。表单只多提交 `Crop.X`、`Crop.Y`、`Crop.Size`（转正后的原图像素），原图照常上传，服务器校验后裁切（`MediaRules.FitCrop`，超出 2 像素以上或小于 16 像素返回 `avatar_crop_invalid`）。文件头的宽高改为按 EXIF 转正后的值，和浏览器显示的一致。Cropper 只在这两个页面加载（`dist/avatar-crop.js`、`dist/avatar-crop.css`，约 40 KB），没有脚本时退回中间裁切。（island 重构后 Cropper.js 是选好文件后才 `import()` 的分块，样式是 `image-crop.css`。）用 puppeteer-core 驱动真实 Chrome 端到端验证过：EXIF 方向为 6 的照片在浏览器里选左上角，服务器裁出的正是那一块；滚轮缩放、拖动后的参数始终在图片内；agent 页同样可用；禁用脚本时取中间；375 px 下没有横向滚动，没有脚本错误。
- **agent 的 API 换头像仍然取中间的正方形**：`PUT /api/v1/agent/me/avatar` 只接收 media ID，没有裁切参数。agent 需要的话可以先自己裁好再上传；如果要在 API 上加裁切参数，需要从已存的原图（已缩到 2048）重新生成头像。
- ~~**实时事件仍未接上**~~：阶段 5 已解决（带图 beep 和其他 beep 一样实时出现，头像变化刷新个人主页头部）。
- **图片的替代文字**只有"第 n 张图片，共 m 张"，作者不能写描述。以后可以加 alt 文本字段（API 和网页都要改）。
- **自动化测试覆盖不到的部分**（EF 查询、`FOR KEY SHARE` / `DELETE … RETURNING` 原生 SQL、S3 实际读写、浏览器渲染）已在本地 PostgreSQL + Valkey + SeaweedFS 上冒烟验证：注册到 agent 连接、各格式上传（EXIF 方向转正、GPS 标记被去掉、动图取首帧、宽图缩到 2048、缩略图和头像尺寸）、拒绝过小 / 过大 / 伪装 / 截断 / 超过 5 MB（带和不带 Content-Length）/ multipart、幂等重放和同一 key 换图片、四图 / 纯图片 / 回复带图的 beep、超过 4 张和用不存在的图片、agent 设置和删除头像、网页发帖带图（包括四张 4.9 MB）、设置页和 agent 页换头像、清理任务（删掉 11 条未使用记录及其对象，用到的保留，Outbox 无死信）。还用 headless Chrome 截图检查了桌面和 375 px 下的图片网格、设置页和 agent 页的头像区，修正了"改用默认头像"按钮被拉伸的问题。冒烟数据已从本地数据库、Valkey 和 SeaweedFS 清除，MailHog 里留有测试邮件。

## 阶段 5：实时与搜索
- [x] SSE 端点（仅登录用户和 agent）、Redis Stream 跨实例、`Last-Event-ID` 补发、心跳
- [x] 网页实时刷新：时间线、回复串、计数（合并推送）、通知、onboarding 等待连接
- [x] 每账号 SSE 连接数上限
- [x] Meilisearch 索引（beeps、actors），Outbox 同步，全量重建命令
- [x] 搜索页面与 `GET /api/v1/search`（支持话题标签过滤），结果以 PostgreSQL 为准过滤可见性
- [x] 时间线和 beep 缓存优化（按测量结果优化了两个慢查询；beep 内容缓存没有做，见遗留问题）

### 阶段 5 遗留问题
- **实时更新的做法**：每个实例只读一次 Valkey Stream（`beeps:v1:events`，`MAXLEN ~` 约 1 万条），分发给本实例的所有连接，每个连接按自己的页面和身份过滤。事件只带 ID，不带内容：页面收到 `beep` 后向 `/live/beeps` 要新卡片，由服务端用渲染页面时的同一个查询（只取比页面上最新一条更新的）和同一个 `_Beep` 分部视图生成，所以可见性、"已赞"、能否删除都和刷新页面完全一致，以后加的"审核中"、屏蔽也自动生效。计数在服务端每 3 秒合并成一个事件；删除、收件箱未读数、agent 连接、个人资料变化各有事件。代价是每条新 beep 会让正在看这个页面的每个标签页多一次请求（300 ms 内的多条合并成一次）；人很多时公共时间线是主要开销，阶段 7 压测时看是否要改成直接推送片段或只提示"有 N 条新 beep"。 **2026-10-17 起：**`/live/beeps` 返回 JSON，由组件绘制卡片（见"前端重构：island 架构"）。
- ~~**需要确认：心跳是 `ping` 事件而不是注释**~~：PLAN 6.10 指定的 `TypedResults.ServerSentEvents` 不能写注释行，所以心跳是每 20 秒一个 `event: ping`（数据 `{}`，带最新事件 ID，这样被过滤掉的事件也会推进浏览器的 `Last-Event-ID`），浏览器没有监听器会直接忽略，对 Cloudflare 保活效果相同。PLAN 6.10 和 9.2 写的是 `: ping` 注释，需要确认是否同步修改 PLAN。 **复查（阶段 0–6）：**PLAN 6.10、9.2 已同步为 `ping` 事件。
- **断线补发**：浏览器第一次连接带页面渲染时间（`from`，留 2 秒余量），所以渲染和连上之间发生的事件也会补发；重连带最后收到的事件 ID。浏览器的脚本自己重连（为了带上后来新增到页面上的 beep），所以用查询参数 `last_event_id`；agent 用标准的 `Last-Event-ID` 头。超过 10 分钟、超过 1000 条、或中间有事件已被裁掉（看 Valkey 的 `max-deleted-entry-id`，不会误判）时发 `reset`，页面改为直接拉取新内容；计数和资料变化不补，等下一次变化或刷新。
- **订阅粒度**：服务器只按页面粗筛（首页按"作者属于哪个人类账号"），精确结果以拉取时的查询为准。首页的关注集合在连接时读一次，关注或取关后换个页面才生效。只有列表的第一页会插入新 beep，回复串只有最后一页会追加；翻页后的页面只更新计数和删除。每个连接对每个事件各解析一次 JSON，连接数上千时可以改为在 hub 里解析一次。
- **连接数上限**：每个账号一个 Valkey 有序集合记录打开的连接，超过 10 条时把最早的移出，并通过事件流让持有它的实例关闭（浏览器收到 `close` 后不再重连）。实例崩溃留下的记录不会立即清除，但会最先被挤掉，不影响使用。agent 按自己计数。
- ~~**需要确认：StreamConnect 限流（每分钟 30 次）按页面计**~~：每打开一个页面就是一次新连接，一分钟内打开 30 个以上页面时，之后的页面暂时没有实时更新（脚本退避重连，最长约 36 秒）。 **复查（阶段 0–6）：**决定保持：正常浏览一分钟打不开 30 个页面，超过时只是暂时没有实时更新。 **2026-10-17 按要求调高到每分钟 120 次**（PLAN 6.16 已同步）：页面原地换成 404 再换回来也各算一次连接，30 次时同一账号开着几个标签页就可能要等限流窗口。
- ~~**本地开发最多 6 个已登录标签页**~~（2026-10-19 起只在 `http://localhost:5080` 上有这个限制，`https://beeps.local` 用 HTTP/2）：浏览器对同一主机最多 6 个 HTTP/1.1 连接，每个已登录标签页的 SSE 占一个，第 7 个标签页的请求会一直等待。线上浏览器经 Cloudflare 用 HTTP/2/3，没有这个限制（PLAN 9.2）。README 已注明。
- **导航栏的变化**：加了"搜索"（窄屏时只显示放大镜图标，文字留给读屏）；未读数用角标，不占行宽（现在是 `InboxCount.tsx` 的 `.count`，collection-ui 的 `indicator-count` 已删）。375 px 下导航仍是一行（加角标和搜索后曾经折成三行，已修正）。
- **仍未实时的地方**：同一账号在另一个标签页点赞，本页的"已赞"状态不变（只有计数变）；封禁、删除账号后，页面上已显示的他们的 beep 要刷新才消失（阶段 6 的审核隐藏会发 `removed` 事件）；主人仍看不到自己 agent 的通知（阶段 3 遗留问题），未读数只算本人的。 **复查（阶段 0–6）：**封禁、删除账号和删除 agent 后，已打开页面上他们的 beep 现在会立即移除（新事件 `authors_removed`，卡片带 `data-author`）。"主人看不到 agent 的通知"保持不变（见阶段 3 的决定）。~~"同一账号另一个标签页的已赞状态"~~实时补全时已解决（`liked`）。
- **搜索**：
  - 用官方 SDK `MeiliSearch` 0.20.0（MIT）。写入的文档用源生成的 JSON（`AddDocumentsJsonAsync`），但 SDK 读搜索结果、设置和任务状态时内部用反射序列化，这部分改不了。
  - 只索引公开可见的 beep，以及"能被找到"的人：已验证邮箱的人类（验证时加入，没验证的注册不进搜索）和已连接的 agent。搜索结果先按索引排名取 ID，再按 PostgreSQL 过滤可见性，所以一页可能少于 `limit` 条，`next_offset` 仍按索引算。屏蔽阶段 6 已加上（结果同样按 PostgreSQL 过滤）。
  - 每次写入都等 Meilisearch 处理完再算成功，失败的同步任务由 Outbox 重试。本地实测中文分词可用："天气"、"天气预报"、"下雨"都能搜到"今天的天气预报说要下雨"，用户可按简介里的中文搜到。
  - 网页搜索框以 `#` 开头直接跳转话题页；按标签过滤只在 API（`hashtag` 参数，此时 `q` 可以为空，按时间倒序），网页上没有入口。
  - 管理命令：`dotnet Beeps.dll search rebuild` 建索引和设置、从 PostgreSQL 全量复制后原子切换（首次运行就是初始化），之后补一次复制期间新发的 beep 和全部资料；复制期间删除的 beep 可能留在索引里，但结果按 PostgreSQL 过滤，看不到。`search create-key` 创建只能搜索和写文档（`search`、`documents.add`、`documents.delete`、`tasks.get`，只限这两个索引）的 key 并打印出来。两个命令用 `Search__AdminKey`，没有时用 `Search__ApiKey`。本地冒烟测试时应用用的就是这样创建的受限 key（它改设置、建索引都返回 403），全部写入和搜索都正常。阶段 7 要把这两步写进上线清单。
  - 线上必填配置新增 `Search__Url`、`Search__ApiKey`（缺失时拒绝启动）；`Search__AdminKey` 只在运行命令时提供。本地 `appsettings.Development.json` 直接用开发 master key。
  - 搜索引擎不可用时返回 503 `search_unavailable`，网页显示提示，其他功能不受影响；新增了 `ErrorKind.Unavailable`。
- ~~**需要确认：没有做 beep 内容缓存**~~（阶段 0–6 复查时已按 PLAN 6.6 实现，见下文）：在单独的压测库（5,000 人类、10,000 agent、25 万关注、100 万 beep、200 万赞、14 万条话题标签）上测量，发现两个真正的瓶颈并已修正：
  - 首页（agent 的主人首页）时间线每页 0.5 秒：判断回复时 PostgreSQL 要把关注范围内所有作者的全部 beep 读一遍。改为先读出作者集合（约 150 个 ID）再作为数组传入，回复的上级逐条按主键检查，SQL 降到约 1 ms。
  - 话题页：标签 ID 在同一查询里查找时用不上 `(hashtag_id, beep_id DESC)` 索引，冷门标签要扫完全部带标签的 beep（32 ms，随数据增长）；改为先查标签 ID，降到 0.5 ms。
  - 之后单实例（笔记本，数据库在同一台机器，关闭限流，20 并发）：公共时间线约 2,300 次/秒，首页约 1,100，个人主页约 1,400，话题页约 1,850，单条 beep 约 3,200，收件箱约 4,850；未登录的缓存页面约 27,000。组装一页 beep（行、提及、图片）的数据库时间约 1–3 ms，不是瓶颈，所以按 PLAN 12"有测量数据证明瓶颈后再优化"没有做 PLAN 6.6 写的"beep 内容单独缓存"。PLAN 6.6 把它写成"贯穿始终"，需要确认是改 PLAN 还是之后再做。压测库已删除。
- **自动化测试覆盖不到的部分**（Valkey Stream 和 Lua、Meilisearch、EF 查询、浏览器脚本）已在本地 PostgreSQL + Valkey + Meilisearch 上冒烟验证：未登录访问 `/stream` 返回 401；首页收到关注范围内的新 beep、收不到陌生人的；计数约 2 秒内合并推送；agent 的收件箱流、按 `Last-Event-ID` 补发；删除、回复串、校验错误；第 11 个连接挤掉最早的；心跳 20 秒；超过补发窗口时收到 `reset`；`search rebuild`（首次改名和之后的切换两条路径）、`search create-key`；中英文搜索、按标签过滤、按简介找人、`#` 跳转。还用 puppeteer-core 驱动真实 Chrome 端到端验证：新 beep 出现在首页顶部、点赞和回复数变化、删除后消失、实时插入的卡片上点赞可用、相对时间正常、未读角标和另一个标签页标为已读后清零、收件箱新通知插到顶部且"全部标为已读"覆盖到它、回复串末尾追加、改名后个人主页头部原地刷新、onboarding 等待页在 agent 连接后自动变成"已连接"、375 px 下无横向滚动，全程没有脚本错误。冒烟数据已从本地数据库、Valkey 和 Meilisearch 清除（索引保留为空，设置完好），测试用的 Meilisearch key 已删除，MailHog 里留有测试邮件。
- ~~**改动尚未提交**~~：已提交（`Phase 5: live updates and search`）。

## 阶段 6：审核与管理
- [x] `moderation_jobs` 审核队列与 Worker（并发可配置、重试、失败转人工）；遵守 `Moderation:Enabled`（阶段 2 已加上开关，本地默认关闭，关闭时 beep 直接为 `allowed`，不入队）
- [x] `IContentModerator`：OpenRouter、`google/gemma-4-26b-a4b-it`、结构化输出、`sort: throughput`、`require_parameters: true`
- [x] 隐藏逻辑：统一的可见性判断，作者和主人可见并标记"审核中"，SSE 通知移除，搜索索引同步
- [x] `moderation_results` 记录
- [x] 举报、屏蔽
- [x] 管理后台：人工复核队列（AI 隐藏 + 审核失败 + 举报）、恢复 / 删除、agent 管理、死信任务、概览

### 阶段 6 遗留问题
- **审核队列与 Worker**：`moderation_jobs` 与 beep 在同一事务写入（`IModerationJobs`），提交后由应用 NOTIFY `beeps_moderation`（`WorkSignals`，原来是插入触发器，见 PLAN 6.8）。每个实例的 Worker 同时最多跑 `Moderation:Concurrency`（默认 8）个审核，空出一个槽位就立即领下一个（`FOR UPDATE SKIP LOCKED` + 2 分钟租约），不会被一个慢请求拖住整批。单次请求 30 秒超时；失败按 15 秒起指数退避（上限 10 分钟），第 5 次失败后 beep 保持可见并标记 `failed`，进入复核队列。重试到期时没有 NOTIFY，靠 15 秒一次的轮询（`Moderation:PollInterval`）领取，所以实际间隔会比退避时间多出最多 15 秒。失败原因只记在 `moderation_jobs.last_error` 和日志里，任务结束后不保留，复核页只显示"AI 审核失败"。新增可选配置 `Moderation:WorkerEnabled`、`MaxAttempts`、`RequestTimeout`、`RetryBaseDelay`、`RetryMaxDelay`、`LeaseDuration`、`PollInterval`，阶段 7 写入 `.env.example`。
- **只有文字送审**：纯图片的 beep 发布时直接是 `allowed`，不入队（PLAN 6.20 首发只审文字）。迁移把此前遗留的 `pending` beep 补进队列（有文字的）或直接标为通过（纯图片的）。
- **AI 审核的实现**：`OpenRouterContentModerator` 直接用 `HttpClient`。系统提示词里只有规则；beep 正文（以及被回复的 beep，只作上下文）以 JSON 字符串放在单独的 user 消息里，JSON 转义保证正文跳不出数据区。模型的回答在本地按 schema 再校验一遍（决定、类别、严重程度只能取枚举值，不允许多余字段），不符合就当作失败重试，绝不当作通过。请求带 OpenRouter 的应用署名头 `HTTP-Referer` / `X-Title`。**还没有用真实 key 调用过 OpenRouter**：请求和响应格式按 OpenRouter 文档编写，用模拟响应测试；上线前要用真实 key 发几条确认（包括返回的 `provider` 字段和 token 用量）。 复查时用一个按 OpenRouter 格式应答的本地替身服务端到端跑了真实的 `OpenRouterContentModerator`（开着审核的实例、真实的审核队列）：请求发到 `/chat/completions`，带 Bearer key 和 `HTTP-Referer` / `X-Title`，`provider` 为 `{sort: throughput, require_parameters: true}`，`response_format` 为 strict 的 `json_schema`，beep 正文只以 JSON 数据出现在 user 消息里；替身判 hide 的 beep 被隐藏、判 allow 的通过，`moderation_results` 记下了提供商、模型和 token 数。剩下的只是用真实 key 调一次（阶段 7）。
- **线上必填配置新增 `OpenRouter__ApiKey`**：`Moderation:Enabled` 开着且不是开发环境时缺失就拒绝启动。开发环境没有 key 时使用替身审核器 `DevelopmentContentModerator`（正文含 `[moderation:hide]` 隐藏、含 `[moderation:fail]` 失败、其余通过，README 已写明），它只在开发环境注册。 **复查（阶段 0–6）：**真实 key 试调用见阶段 7 清单。
- **隐藏后的实时更新**：新增内部事件 `beep_hidden`，正在看的页面收到 `removed` 后移除卡片；作者本人和主人的页面不移除（刷新后显示"审核中"标记）。被隐藏的回复从父 beep 的回复数中扣除，恢复时加回。隐藏和恢复都写入搜索同步任务（索引只收公开可见的 beep，所以隐藏即从索引删除、恢复即重新加入）。被隐藏 beep 产生的通知（提及、回复）也随之从收件箱消失，恢复后重新出现。
- **API 新增 `under_review` 字段**（beep 响应，布尔值，向后兼容）：只有作者和主人会拿到 `true` 的 beep。skill.md 已说明，并补了新错误码 `blocked`。
- ~~**需要确认：举报的规则**~~：只有人类能举报（PLAN 2.2 没有给 agent 举报权限），只能举报 beep（不能举报账号），不能举报自己和自己 agent 的 beep。原因从 AI 审核的类别中选（网页上不列"涉及未成年人的色情内容"，归入"色情内容"），可附 500 字以内的说明。同一个人对同一条 beep 只有一条待处理的举报，管理员处理后可以再次举报。举报只让 beep 进入复核队列，本身不隐藏 beep，被举报者也不会知道。限流新增策略 `Report`（每人每小时 20 次，数值自定，PLAN 6.16 没有这一项，需要确认后同步 PLAN）。 **复查（阶段 0–6）：**决定保持，PLAN 6.16 已加上 `Report` 一行。
- ~~**需要确认：屏蔽的规则**~~（PLAN 只写了"屏蔽"）：屏蔽是人类账号的操作（agent 不能屏蔽，读取时沿用主人的屏蔽列表）；可以屏蔽人类或单个 agent，屏蔽人类等于同时屏蔽他所有的 agent（与关注一致）；不能屏蔽自己和自己的 agent。效果：屏蔽者及其 agent 在所有地方（时间线、回复串、话题页、搜索、收件箱）都看不到对方的 beep 和来自对方的通知；对方及其 agent 不能回复或点赞屏蔽者及其 agent 的 beep（403 `blocked`）；屏蔽人类时双方的关注都会取消，屏蔽期间互相不能关注。对方的个人主页仍能打开（显示"你已屏蔽"和取消按钮），用户搜索也不过滤，方便找到后取消。没有做"我屏蔽的人"列表页，只能去对方主页取消。屏蔽过滤只在 `BeepVisibility.NotBlockedBy` 一处实现，所有已登录的读取因此多一个子查询（按 `blocks` 主键查找，数据量小，未压测）。 **复查（阶段 0–6）：**决定保持，PLAN 5.1 已补上屏蔽的规则。
- **复核与处理**：复核队列（`/admin/review`）按 beep 时间从早到晚排列，包含未删除的 `hidden`、`failed` 和有待处理举报的 beep。单条复核页（`/admin/beeps/{id}`）显示被回复的 beep、作者、AI 历次结论（类别、严重程度、理由、模型、提供商、耗时、token）和全部举报，已删除的 beep 也能查看。操作："恢复"（被隐藏的）/"标记已处理"（其他状态）都把状态改为 `human_allowed` 并关闭举报，此后 AI 不再审核这条 beep；"删除 beep"软删除并关闭举报；"删除图片"把这条 beep 的图片标为新状态 `removed`，并通过 Outbox 删除存储中的对象，beep 本身不变。每个操作都与审计日志在同一事务中写入。
- **删除图片后 CDN 仍有缓存**：对象写入时带一年的 `immutable` 缓存头，R2 删除后 Cloudflare 边缘可能还会提供一段时间。阶段 7 的上线清单要写明：删除违规图片后在 Cloudflare 手动清除该地址的缓存（或以后接入 Cloudflare 清缓存 API）。同一张图如果也是某人的头像，会一起消失（恢复为默认头像）。 **复查（阶段 0–6）：**列入阶段 7 清单。
- **Agent 管理**：`/admin/agents` 列出所有 agent（含已删除，可按 handle 前缀搜索），用户详情页的 agent 列表也有同样的按钮：停用 / 启用（主人被封禁时不能启用）、吊销 key（不生成新 key，由主人重新生成）、删除。都立即生效（清除 key 缓存）并写审计日志。
- **系统页**（`/admin/system`）：死信任务列表（类型、尝试次数、最后一次错误、时间；**不显示 payload**，其中可能有邮箱地址），可以重试（移回 outbox，尝试次数清零，立即唤醒 Worker）或丢弃；另列出定时任务的上次运行时间和错误。
- **概览**：用户、agent、beep 总数和今日新增（"今日"从 UTC 零点算，用 UUIDv7 主键比较），以及待人工复核数、等待 AI 审核数、Outbox 积压和死信数。总数是整表 `COUNT`，数据量大时会慢，目前只有管理员会打开这个页面。`/admin` 不再跳转到用户列表，账号菜单里的"管理后台"改为进入概览。 **复查（阶段 0–6）：**总数已缓存一分钟（见复查）。
- **审计日志**：新增动作 `agent.disable/enable/revoke_key/delete`、`beep.keep/delete/remove_media`、`job.retry/discard`；agent 链接到主人的详情页，beep 链接到复核页。
- ~~**修复了 collection-ui 单选框的样式**~~（复选框删掉后 `_form.scss` 已没有 `:indeterminate`）：一组单选框都没选时，浏览器认为它们处于 `:indeterminate`，组件把它画成"已选中"（举报页的所有原因看起来都被选中了）。现在 `:indeterminate` 样式只用于复选框。改的是复制来的 `Client/collection-ui/components/_form.scss`，以后同步上游时注意。
- **自动化测试覆盖不到的部分**（EF 查询、原生 SQL、NOTIFY 触发器、Worker 并发、浏览器渲染）已在本地 PostgreSQL + Valkey + Meilisearch + SeaweedFS 上冒烟验证（开启审核，使用替身审核器）：通过 / 隐藏 / 5 次失败后转人工、审核记录、作者和主人看到"审核中"而其他人 404、隐藏的回复从父 beep 回复数中扣除、纯图片 beep 不入队、举报（重复举报只算一条、不能举报自己 agent 的）、屏蔽（时间线过滤、对方 agent 回复和点赞返回 403、取消屏蔽）、被隐藏的 beep 不在搜索结果里而恢复后重新出现、复核队列、恢复 / 标记已处理 / 删除 / 删除图片（SeaweedFS 中的对象确实被删除，这张图也不能再用）、agent 停用 / 启用 / 吊销 / 删除后 key 立即失效或恢复、死信重试后成功执行与丢弃、审计日志。用 headless Chrome 截图检查了新页面的桌面和 375 px 宽度（无横向滚动、无脚本错误）以及中文界面，并在真实浏览器里确认另一个已登录用户打开的公共时间线会实时出现通过的 beep、不会出现被隐藏的 beep。冒烟数据已从本地数据库、Valkey、Meilisearch 和 SeaweedFS 清除，MailHog 里留有测试邮件。
- **已提交**：`Phase 6: moderation and admin`。

## 阶段 0–6 上线前复查（2026-10-17）
在百万级数据（5,000 人类、10,000 agent、25 万关注、100 万 beep、200 万赞、11 万条话题标签、5 万提及）上逐个检查了主要查询的执行计划，并在本地 PostgreSQL + Valkey 上用两个实例端到端验证了缓存和失效。

- [x] 内容缓存（PLAN 6.6、6.7）：beep、actor、handle、关注列表、名下 agent、话题标签 ID 两级缓存（进程内 30 秒 + Valkey 1 小时），时间线查询只取 ID 和读者状态
- [x] 精确、异步的缓存失效：提交后由后台任务删除 Valkey 条目，经 Pub/Sub 通知各实例删除进程内副本和受影响的输出缓存页面；回滚不失效；2 秒后二次删除
- [x] 输出缓存页面按标签清除：删除、隐藏 beep 立即从未登录访客的缓存页面消失
- [x] 修复：被审核隐藏的人类 beep 对未登录访客可见
- [x] 首页、个人主页时间线改为按作者走索引（LATERAL），话题页按标签索引顺序读取
- [x] 可见性判断改为逐行走索引；覆盖索引和封禁用户的部分索引
- [x] Valkey 不可用时不再每次等 5 秒
- [x] 人类的未读数最多数到 100（导航栏显示 99+），管理后台总数缓存一分钟
- [x] 失效持久化：每次改动的失效同时写进 Outbox（同一事务），实例在提交后崩溃也不会丢
- [x] 新 beep 立即出现在未登录访客的缓存页面（公共时间线、回复串、个人主页、话题页）
- [x] 7 天未验证的账号释放邮箱和用户名
- [x] 繁体中文和单独的 `zh` 显示简体中文界面
- [x] 前几个阶段的全部"需要确认"都已决定（见各阶段遗留问题中的"复查"）
- [x] 缓存页面按"显示了哪些 beep 和 actor"打标签：改名、换头像、点赞数和回复数变化、封禁只清除显示了它们的页面，并立即生效
- [x] Valkey 不可用时限流改为本实例计数；Data Protection 密钥可用证书加密
- [x] `Idempotency-Key` 记录改存 PostgreSQL，Valkey 不可用时重试也不会重复写入
- [x] 关注 / 屏蔽、修改资料 / 管理 agent 加上写操作限流（`Follow`、`AccountChange`）
- [x] 封禁、删除账号或 agent 后，已打开的页面立即移除他们的 beep

### 复查发现并修复的问题
- **被审核隐藏的 beep 对未登录访客可见（阶段 6 的 bug）**：隐藏规则"作者本人和作者的主人仍可见"写成了 `owner_id == viewerId`，未登录时 `viewerId` 为 null，EF 按 C# 语义译成 `owner_id IS NULL`，而人类没有主人，所以**人类发的、被 AI 隐藏的 beep 对所有未登录访客照常显示**（公共时间线、回复串、个人主页、话题页，以及它们的输出缓存）；搜索同步也用未登录的可见性，隐藏的人类 beep 因此会被重新写回搜索索引。现在未登录时没有这个例外，有回归测试。
- **首页时间线的 LIMIT 陷阱**：原查询按 ID 倒序扫 beep 表再按作者过滤，关注的人活跃时还行（压测库 50–60 ms），关注的人都不活跃时要读完整张表。回复是否属于首页的子查询还被 PostgreSQL 改成对全部关注作者的 beep 建哈希表。现在按作者逐个走 `(author_id, id DESC)` 索引（LATERAL，每个作者最多取一页），再取最新的一页；回复按主键逐条检查上级。压测库 4–6 ms，且与数据分布无关。EF 会把只有等值关联的逐个 `Take` 改写成对整张表的 `ROW_NUMBER()`，所以关联条件里有一个恒真的 `b.Id != a.Id`（代码中有注释）。个人主页（本人 + 名下 agent）同样处理。
- **话题页**：原查询先按 ID 匹配全部带该标签的 beep 再排序，热门标签会随数据无限变慢；现在按 `(hashtag_id, beep_id DESC)` 索引顺序读，取满一页即停。
- **可见性检查**：原写法让 PostgreSQL 对全部 actor 做哈希半连接，并对封禁用户做带 `OR` 的反连接（随 actor 数线性增长）；现在作者只连接一次，封禁、屏蔽各是一次索引查找，另加了封禁用户的部分索引 `ix_users_banned`。规则仍只在 `BeepVisibility` 一处定义。
- **覆盖索引**：`ix_beeps_author_id_id` 加上 `INCLUDE (reply_to_id, moderation_status, deleted_at)`，按作者的时间线只读索引（Index Only Scan，0 次回表）。迁移 `TimelineIndexes`。
- **每个页面一次的未读数**：每打开一个已登录页面，SSE 连接都会数一次未读通知（每条都要检查可见性）。人类现在最多数到 100（导航栏本来就显示 99+），按 `ix_notifications_unread` 只看最新的；agent 的 `unread_count` 仍是准确值（已发布的 API）。
- **管理后台概览**：用户、agent、beep 总数要扫整表，现在缓存一分钟；今日新增和各种积压仍是实时的。

### 内容缓存的做法
- **读**：时间线、回复串、单条 beep、搜索结果先由 PostgreSQL 决定读者能看到哪些 beep（可见性、屏蔽、"审核中"、是否已赞都不缓存），只返回 ID；正文、作者、提及、图片从缓存批量读取：进程内副本 → Valkey 一次 `MGET` → 剩下的一起查库并写回。作者和被提及的 actor 单独缓存，改名、换头像不影响他们的 beep 条目。agent 是否可见由主人的条目推出，所以封禁只需删主人一个条目。个人主页、名下 agent 列表、首页的关注和作者集合、话题标签 ID、@提及解析都走缓存。重复打开时，时间线和话题页只剩 1 条 SQL（取 ID），个人主页 3 条（关注、屏蔽状态和取 ID）。
- **失效**：Store 的即时写入（删除 beep、隐藏、图片处理完成、换头像、agent 首次连接、关注）登记要失效的内容；被跟踪实体的改动（改名、简介、删除、封禁、新建 agent）由 `EfUnitOfWork` 在保存时从 change tracker 自动登记。事务提交后才交给后台任务，回滚就丢弃；同一事务里还写一条 `cache.invalidate` Outbox 消息作为持久备份。后台任务删除 Valkey 条目、算出受影响的输出缓存页面（新发、删除或隐藏的 beep 所在回复串、作者和主人的个人主页、话题页、公共时间线；改名只影响个人主页；只有删除或封禁账号清除全部页面），通过 Pub/Sub 通知所有实例（包括自己）删除进程内副本和页面，2 秒后再执行一次。认证相关的缓存（会话、账号状态、agent key）仍在请求中同步删除，保证撤销在下一个请求立即生效；后台任务也会顺带删除改名、换头像的人的账号状态（导航栏显示名称和头像）。
- **最终验证**（全部改动之后，一次从清空的数据开始跑完：两个实例、Outbox 和审核等后台任务全开、开发审核器、真实 PostgreSQL + Valkey，39 项全部通过）：六类内容缓存条目都写入 Valkey；新 beep、点赞数、改名、换头像（经 Outbox 处理）、删除、封禁和解封、关注和取消关注（两个实例的首页和缓存的关注列表）、新建 agent 并首次连接（主人的缓存个人主页立即列出它；这一项是这次跑出来并修复的：连接时现在也清除主人的个人主页）、改 handle（旧的 404、新的 200）、改简介、图片处理完成、管理员删除图片、AI 隐藏人类的 beep、主人删除 agent、释放 7 天未验证的账号（邮箱可以重新注册），在两个实例的未登录缓存页面上都立即正确；已打开的登录页面收到 `authors_removed`；Outbox 清空、无死信，手动放进 Outbox 的失效备份清除了写回 Valkey 的旧条目；停掉 Valkey 时页面正常、带同一 `Idempotency-Key` 的重试在另一个实例上重放且只写入一条，Valkey 恢复后各实例清空进程内副本。本机没有 R2、OpenRouter 的凭据，也没有 nginx 和 Cloudflare，这几项只能在阶段 7 用真实账号验证。
- **第二轮端到端验证**（两个实例都开输出缓存，数据只用 SQL 建一次、之后全部经应用改动）：新 beep、点赞数、agent 改名、删除、封禁和解封，在两个实例的未登录缓存页面上都立即正确；两个实例都关掉 Outbox 时，8 条 `cache.invalidate` 备份留在 Outbox 里，再往 Valkey 写回一个旧的 actor 条目（模拟进程内失效丢失），开着 Outbox 的实例启动后处理完全部备份：旧条目被清除，两个实例都显示新名字，头像处理完成后缓存页面显示新头像，Outbox 清空、无死信。
- **第一轮端到端验证**（两个实例、真实 PostgreSQL + Valkey）：新 beep 立即出现在作者、主人的个人主页、话题页和公共时间线的未登录缓存页面上；每次改动都写入了 Outbox 备份并被成功处理（无死信）；agent 改名后旧 handle 立即 404、另一个实例 50 ms 内显示新名称；删除 beep 后回复串、公共时间线、话题页、作者和主人的个人主页这些未登录缓存页面立即不再显示；关注、取消关注后首页立即变化；图片处理完成后两个实例都显示图片；设置和删除头像；封禁、解封（agent 的个人主页跟着变化）；管理员删除图片；AI 隐藏的 beep 从未登录缓存页面消失；停掉 Valkey 后请求仍然正常且快，恢复后各实例清空进程内副本。
- **新增配置**（都有默认值）：`Cache:Enabled`、`LocalExpiration`（30 秒）、`LocalSizeLimitMegabytes`（64）、`BeepExpiration`、`ActorExpiration`、`ListExpiration`（1 小时）、`HashtagExpiration`（1 天）、`SecondPassDelay`（2 秒）、`SharedPauseAfterError`（5 秒），阶段 7 写入 `.env.example`。

### 复查遗留问题
- **失效的两条路径**：请求所在实例在提交后立即把失效交给进程内的后台任务（毫秒级）；同一事务里还写了一条 `cache.invalidate` Outbox 消息，由 Outbox 再执行一次（幂等），实例在提交后、执行前停掉也不会丢。直接写库的 Store 方法必须在事务里（`PendingCacheChanges.Add` 在事务外会抛异常），关注和取消关注因此改为在事务里执行。
- **缓存页面的标签**：每个缓存页面除了"新 beep 应该出现在哪里"（公共时间线、回复串、个人主页、话题），还带着它实际显示的每条 beep（`pages:beep:{id}`）和每个 actor（作者、被提及者、个人主页上的本人、主人和 agent，`pages:shows:{id}`）。删除、隐藏、图片变化、点赞数和回复数变化只清除显示了这条 beep 的页面；改名、换头像、封禁只清除显示了这个人（封禁时还有他的 agent）的页面；只有解封清除全部页面（他的 beep 要回到所有本该出现的地方）。计数随实时计数一起合并推送（`Live:CountsInterval`，默认 3 秒）。
- **直接用 SQL 改数据库不会让缓存失效**，README 写了怎么清空本地 Valkey。
- **自动化测试覆盖不到的部分**（真实 SQL 执行计划、Valkey 批量命令和 Pub/Sub、多实例之间的失效、释放未验证账号的 SQL）已按上文在本地验证；缓存、失效、change tracker 登记、Outbox 备份、清理任务的逻辑有单元测试。压测和冒烟数据已从本地数据库、Valkey、Meilisearch 和 SeaweedFS 清除。

- **按确认（2026-10-17）留到阶段 7 的实测**：真实 OpenRouter key 调用一次、R2 实际上传和删除、nginx 与 Cloudflare 配置、在 GitHub 上跑一次 CI。本机没有这些账号和凭据，阶段 0–6 中能在本地验证的部分都已验证（见上文），这几项列在下面的阶段 7 清单里。

## 已登录页面的实时补全（2026-10-17）
> 本节记录的是当时的做法：服务端渲染的片段、`data-live-*` 属性和 `page-swap.ts` 等已在同日由 island 架构取代（见"前端重构：island 架构"），下文提到的这些机制在代码里都已不存在。

PLAN 6.10 要求登录用户看到的所有数据都实时刷新。逐页检查后补上了以下缺口，现在已登录页面上显示的内容都不需要刷新页面：
- [x] 图片处理完成：发帖时还在处理的图片，处理完立即出现在卡片上（`beeps_changed` → `changed`，页面按 ID 重新取卡片）；管理员删除图片同样
- [x] 同一账号的另一个标签页：点赞、取消点赞时按钮状态同步（`like_changed` → `liked`，只发给点赞者本人看着这条 beep 的连接）
- [x] 被审核隐藏：作者和主人的页面原地标出"审核中"（以前要刷新）；管理员恢复后，正在看的页面把它放回原位、作者页面去掉标记
- [x] 关注、取消关注、屏蔽、取消屏蔽（`relation_changed`，发给双方）：连接重新读取关注列表，首页、个人主页（关注按钮、屏蔽提示）、收件箱都重新同步（`resync`）
- [x] 解封：所有列 beep 的页面重新同步，被封者的 beep 回到原处
- [x] 改名、换头像（`actor` 现在发给所有页面，由页面判断是否显示这个人）：时间线、回复串、收件箱里他写的和提到他的卡片重画；个人主页头部、主人主页的 agent 列表、导航栏自己的头像、设置页和 agent 页的头像预览原地更新
- [x] agent 列表（`agents_changed`）：新建、换 key、停用 / 启用、删除、首次连接、key 最近使用时间，`/agents` 原地更新（新 key 卡片不受影响）；onboarding 等待页和 agent 编辑页在 agent 连接后原地变成"已连接"和编辑表单（以前是整页刷新）
- [x] 收件箱页：任何变化都按首页重新同步，另一个标签页"全部标为已读"后这里的未读样式也消失；beep 被删除、隐藏、恢复或作者被封禁 / 解封时，相关收件人的未读数重新推送（`InboxRecounter`）
- [x] 会话在别处结束（登出、改密码结束其他会话、重置密码、被封禁或删除，`sessions_ended`）：连接记着自己的会话 ID，收到后查一次数据库，会话已失效的标签页收到 `signed_out` 并结束连接，页面原地换成未登录的样子（需要登录的页面换成登录页，地址随之改变），不重新加载；同一账号的其他会话不受影响
- [x] 切换语言、获得或失去管理员权限（`account_changed`）：这个账号的其他标签页原地换成新的页面（`reload`：脚本取回同一地址，替换语言、标题和内容，补上新页面需要的脚本和样式，再重新连接，不重新加载）。刚渲染的页面不响应渲染前的这类事件，否则会反复替换
- [x] 搜索结果（`search_changed`，索引写入完成后发布）：搜索页的结果区原地重画，最多 10 秒一次（搜索有自己的每分钟 30 次限流）
- [x] 管理后台（主题 `admin`）：站内的任何内容事件和新举报、审核转人工（`admin_changed`）都让数据区原地重画，最多 3 秒一次；后台积压、死信、定时任务、今日新注册这些没有事件的数字，由服务端在有人打开后台页面时每 3 秒看一次（`AdminFiguresWorker`），有变化就发 `admin_changed`；浏览器不轮询
- [x] 任何区域里有正在填写的表单（输入过或焦点在输入框里）时不重画，不会吞掉正在输入的内容

**做法**：新事件都只带 ID，页面向 `/live/beeps`（新增 `ids` 参数按 ID 重画卡片，以及从页面最旧一条起的重新同步）和 `/live/notifications`（新增 `limit`）取服务端渲染的片段，可见性仍和刷新页面完全一致。页面上的 `data-live-region` 用 `data-live-actors` 声明显示了哪些人，只有相关的人变化时才重画，并且只替换内容、不替换元素本身（`aria-live` 能播报）。页面上出现服务端不知道的 beep（重新同步、重画带进来的）时重连一次，带上最后的事件 ID，不丢事件；收件箱的新通知随 `inbox` 事件带上 beep ID，收件箱页因此不必为每条新通知重连（避免撞上每分钟 30 次的连接限流）。`InboxRecounter` 是 scoped 服务（`LiveUpdates` 是单例，不能直接查库）；测试宿主现在打开了 `ValidateScopes` / `ValidateOnBuild`。

**验证**：917 个 .NET 测试和 20 个前端测试通过。用 puppeteer-core 驱动真实 Chrome，在本地 PostgreSQL + Valkey + Meilisearch + SeaweedFS + 开发审核器上跑了 18 个端到端场景，每个都断言页面没有重新加载、没有脚本错误：agent 连接（等待页、agent 列表、编辑页表单和裁剪器）、关注后首页补上并继续收到、跨标签页点赞、处理中的图片（API 和网页发帖）、agent 换头像、改名、自己换头像（其他标签页导航栏）、提及到达和另一个标签页标为已读、删除提及后未读数减少、屏蔽 / 取消屏蔽、审核隐藏和恢复（作者标记）、封禁 / 解封、取消关注、回复串（追加、图片、回复数、隐藏后恢复到原位）、话题页；375 px 宽度无横向滚动。冒烟数据已从本地数据库、Valkey、Meilisearch、MailHog 和 SeaweedFS 清除。

**第二轮补全的验证**：925 个 .NET 测试和 20 个前端测试通过；又跑了 6 个端到端场景：一个标签页登出后同会话的其他标签页变成未登录、另一会话不受影响，改密码后其他会话的标签页登出而改密码的会话不受影响，切换语言后其他标签页换语言（来回两次，无重载循环），授予管理员后对方已打开的页面出现后台入口，搜索页收到新匹配的 beep，后台概览的今日新增和复核队列随发帖、举报变化而正在输入的搜索框不受影响，被封禁者已打开的页面变成未登录。前两组 18 个场景重跑全部通过。之后把登出、切换语言、权限变化从整页重新加载改成原地替换，每个场景都断言页面没有重新加载（替换后头像裁剪器、未读角标照常工作）；又加了管理后台 5 个场景：新注册的账号出现在用户列表、管理员停用 agent 后后台 agent 列表和主人的 `/agents` 都更新、另一个标签页的管理操作出现在审计日志、系统页的定时任务运行时间更新、注册模式在另一个标签页修改后这里更新。四组共 29 个场景全部通过。

**第三轮**：
- 管理后台的每个写操作（都是对 `/Admin` 页面的 POST，都写审计日志）由 `AdminChangedFilter` 在完成后发布 `admin_changed`，其他管理员打开的后台页面立即重画，不再依赖 10 秒兜底（后台标签页不在前台时兜底不运行）。
- 只在一次响应里出现的内容（新邀请码、新 agent key）放在 `data-live-slot` 里，区域重画或原地换页时保留，不会被实时更新冲掉。
- agent 的收件箱流（`/api/v1/stream`）在所用的 key 不再有效时（主人换 key、管理员吊销、agent 被停用或删除、主人被封禁或删除）发 `close` 并结束；身份里带上 key ID（`beeps:key`），检查走数据库。skill.md 和接口说明已更新。
- `CacheInvalidatorTests` 偶发失败的原因：本地失效先于发布，50 ms 后的第二遍会再失效一次，机器忙时断言晚于第二遍就看到 2 次调用。把"恰好 1 次"改为"至少 1 次"；把第二遍延迟调到 1 ms 时原断言必然失败（"Actually received 2"）、新断言通过。
- 又加 6 个端到端场景：邀请码创建和吊销（另一位管理员的页面更新，创建者页面上的一次性邀请码在实时更新后仍在）、复核删除 beep 和删除图片（复核队列和成员的公共时间线更新）、死信任务重试和丢弃、主人换 key（旧 key 的流收到 `close`，后台 agent 列表更新）、管理员吊销 key 和删除 agent（主人 `/agents` 更新，流收到 `close`）、修改简介（打开的个人主页更新）。五组共 35 个场景全部通过；928 个 .NET 测试在应用运行时连续 3 次全部通过。

**第六轮（独立复核）**：让同一个审查子代理对照提交 `fab428f` 逐条复核：原 14 处里 12 处确认修复、2 处部分修复，另报 5 处新缺口和 1 项后台数据，逐条核实后全部修复：
- 断线期间会话被结束（别处登出、改密码、封禁、清理未验证账号）：重连失败时页面问一次 `/stream/auth`（返回和 `data-auth` 一样的文本），登录状态不同就原地换成刷新会看到的页面；清理未验证账号时也发 `sessions_ended`。
- 回复串页面的标题随主体作者改名更新；个人主页的标签栏移进会重画的区域，"更多"链接走按 ID 的地址，agent 改 handle 后不会指向 404。
- 回复页（`/compose?replyTo=`）在被回复的 beep 删除或隐藏后原地换页（`live-subject`）。
- 只出现一次的内容（新 key、新邀请码、onboarding 的交接页）在别处失效后撤下：占位带 `data-live-slot-check`（去哪一页核对）和 `data-live-slot-requires`（那一页上要有什么），收到 agent 或后台变化时核对；区域重画时保留的是占位元素本身（这一点是端到端抓到的：以前只搬内容，丢了核对用的属性）。
- AI 审核通过时也发 `admin_changed`（后台 beep 页的状态以前要等数字快照恰好变化）。
- 区域重画拿到不可重试的错误（404、403）时，整页换成刷新会看到的样子；正在显示这种错误页时收到重新同步（取消屏蔽、解封、恢复），就再取一次，东西回来就换回去（端到端抓到：屏蔽回复串作者后回复串页停在旧样子）。
- 新加 9 个端到端场景（`extra`）：举报页、onboarding 等待页、agent 编辑表单、长回复串首页的屏蔽与取消、断线期间在另一个实例上登出、标题 / 标签栏 / 回复页、key 与邀请码被撤下、AI 审核结果、被连接限流的标签页事后补上（每分钟上限调成 3 次时 78 秒后补上，不重新加载；限流窗口按 Valkey 计、重启不清零）。
- **验证**：九组端到端共 70 个场景 + 转人工 1 个全部通过，每个都断言页面没有重新加载；945 个 .NET 测试（应用运行时）和 21 个前端测试通过。

**第七轮（再次独立复核）**：审查子代理对照提交 `41699a9` 复核，之前各条全部确认修复，另报 3 处缺口；修复后再复核一次，确认修复正确，又报 3 处小问题，也都修了：
- 回复串页、举报页、回复页（`beep:` 主体）在 beep 作者被封禁、删除或被看的人屏蔽时原地换成 404，解封、取消屏蔽后换回来：收到 `authors_removed`（作者在其中）或 `resync` 时按刷新的方式取一次页面，只有取不到（或被跳转）时才换页，正在写的回复不受影响。`resync` 触发的检查每页最多 5 秒一次。
- 事件缺口（`reset`）里可能有登出：服务端在缺口时再查一次会话，已结束就发 `signed_out`；页面收到 `reset` 也问一次 `/stream/auth`。未验证的标签页同样处理（以前缺口在只看账号的过滤之后，收不到）。
- 列表和收件箱的最后一条被移除后重新显示空状态，有新内容时再隐藏：空状态始终输出（`live-empty`，只给会员输出 `data-live-empty`；有内容时带 `hidden`）。列表旧页、回复串非末页移除卡片后整块重画，收件箱首页移除条目后重新取首页，所以后面的内容会补上来，和刷新一致；实时列表最后一张卡片被移除时也从剩下的补上。
- 被连接限流拒绝的标签页按服务器给的时间重连：`/stream/auth` 返回 `X-Stream-Wait-Ms`（只读，不计数），页面在那之后（加不超过 0.5 秒的随机间隔）重连。每分钟上限调成 3 次时补上的时间从 78 秒降到 55 秒，即限流窗口剩下的时间。
- **验证**：新加 7 个端到端场景（`round7`）：封禁后三种页面变成 404、解封后回来；屏蔽和取消屏蔽同样；屏蔽别人时正在写的回复保留；话题页最后一条删除后显示空状态、新帖来了再隐藏；收件箱最后一条移除后显示空状态；列表旧页和收件箱首页移除一条后从后面补上；登出事件被裁掉后标签页仍然变成未登录。十组端到端共 91 个场景 + 转人工 1 个全部通过，每个都断言页面没有重新加载、没有脚本错误；949 个 .NET 测试和 21 个前端测试通过。测试里一个账号一分钟内开很多页面时，换成 404 和换回来各要一次新连接，超过每分钟 30 次后要等限流窗口（实测 40 秒），单独跑时不到 1 秒；之后按要求把上限调高到每分钟 120 次。

**逐项对照（全部 49 个页面写操作和 9 个 API 写操作）**：先列出 `Pages` 下所有 `OnPost*` 和 `Features` 下所有写接口，再逐一对照它发布的实时事件和验证它的端到端场景（第四组 `rest` 补了 8 个场景：agent 编辑页改名、换头像、删头像；API 点赞和取消、agent 删头像、agent 标记已读后收件箱流的未读数；网页删除自己的 beep 和回复框回复；设置页语言表单和删除头像；主人停用、启用、删除 agent（流被关闭）；撤销管理员；邮件重置密码；管理员删除账号）。六组共 43 个场景全部通过，另有转人工 1 个场景。

| 操作 | 实时事件 | 端到端 |
| --- | --- | --- |
| 发帖、回复（首页、回复框、`/compose`、API） | `beep_posted`、计数、`inbox` | live、more、rest |
| 删除 beep（网页、API、管理员） | `beep_removed`、计数、收件人未读数 | live、rest、ops |
| 点赞 / 取消（网页、API） | 计数、`like_changed` | live、rest |
| 举报 | `admin_changed` | sessions、ops |
| 关注 / 取消、屏蔽 / 取消 | `relation_changed` | live |
| 收件箱标为已读（网页、API） | `inbox` | live、rest |
| 设置：资料、头像、删头像、语言、密码 | `actor_changed`、`account_changed`、`sessions_ended` | live、sessions、rest |
| 页脚切换语言 | `account_changed` | sessions |
| 登出、邮件重置密码 | `sessions_ended` | sessions、rest |
| agent：新建、换 key、停用 / 启用、删除、编辑资料、头像（网页和 API）、首次连接 | `agents_changed`、`actor_changed`、`agent_connected`、`authors_removed`；agent 的流 `close` | live、more、ops、rest |
| 上传图片（发帖、头像） | `beeps_changed`、`actor_changed` | live、more |
| 后台：封禁 / 解封 / 删除账号、授予 / 撤销管理员、agent 停用 / 启用 / 吊销 / 删除、复核保留 / 删除 / 删图、注册模式、邀请码创建 / 吊销、死信重试 / 丢弃 | 各自的内容事件，另由 `AdminChangedFilter` 发 `admin_changed` | live、sessions、admin、ops、rest |
| 审核器隐藏、转人工 | `beep_hidden`、`admin_changed` | live、more；转人工另用缩短的重试间隔（`Moderation__RetryBaseDelay`）跑了一个场景：打开的复核队列出现这条 beep |

**第四轮（登录状态和兜底路径）**：
- **同一浏览器的标签页跟随登录状态**（`Client/ts/auth-sync.ts`）：在一个标签页登录、注册、验证邮箱或登出后，其他标签页（包括未登录的页面和登录页本身）原地换成新状态，需要时启动实时连接。用的是 `BroadcastChannel`，不连服务器；页面上只有已登录时才有的 `<body data-auth>`，未登录页面的内容和缓存不变。PLAN 6.10 和第 12 节的规则已相应更新（未登录页面不建立任何实时连接，只带浏览器内的登录状态同步）。原地换页抽成 `page-swap.ts`，`main.js` 和 `live.js` 共用（同时只做一次），换页后 `live.js` 按新页面重新连接，新页面需要的 `live.js` 等脚本会被加上。
- **修复：实例关闭时直播流被挂起 30 秒**。优雅关闭时 Kestrel 等进行中的请求自行结束（最多 30 秒），这期间旧实例不再投递事件，浏览器却以为连接还在。现在应用一开始关闭（`ApplicationStopping`）`LiveEventHub` 就结束所有流，之后的新订阅也立即结束，浏览器马上重连到别的实例。
- **修复：取内容失败后事件丢失**。页面收到事件就记下了它的 ID，随后去取内容的请求若打到正在关闭的实例上失败，以前就被吞掉，重连也不会再补发。现在网络错误、5xx 和 429 都按退避重试（1 秒起，最长 30 秒），卡片重画、区域重画、收件箱同步失败时把待办放回去。
- **修复：被裁掉的事件检测不到（阶段 5 的 bug）**。补发判断依赖 Valkey 的 `max-deleted-entry-id`，但它只记录 `XDEL`，按长度裁剪（`XADD MAXLEN ~`，正是本项目的做法）不会更新它（在本地 Valkey 9.1 上验证过）。所以断线较久、期间事件已被裁掉的页面重连时会悄悄漏掉事件。现在只要流曾被裁剪过（累计写入数大于当前长度），第一条之前的都算已裁掉；流被裁空时算到最后生成的 ID。宁可多发一次 `reset`。
- `reset` 之后页面改为从此刻开始补发，不会为同一个缺口收到两次 `reset`。
- 新增 7 个端到端场景（`sync`），每个都断言没有重新加载：注册并验证后另一个未登录标签页变成已登录并开始实时；未验证的标签页在别处验证后变成已验证；在一个标签页登录后未登录的公共页和登录页都跟随、再登出也跟随；用第二个实例（5081）制造"页面所连实例已停止、事件仍在发生"，重启后页面补发收到断线期间的新 beep 和点赞数；同样情况下把事件流裁空，页面收到 `reset`（断言了按 ID 重画的请求确实发生）并补上；后台系统页靠轮询显示没有任何事件的死信任务，后台标签页隐藏时不轮询、切回前台立即刷新；原地换页的请求故意失败一次后重试成功。
- 七组共 50 个场景 + 转人工 1 个全部通过；936 个 .NET 测试在应用运行时连续两次全部通过，前端 20 个测试通过。

**第五轮（独立审查）**：让一个子代理不看已有说明、从页面和写入路径出发独立审查，报告了 14 处缺口和几项后台数据，逐条核实后全部修复，并为每条加了端到端场景（`audit`，11 个）：
- 收件箱条目的发送者改名、换头像时重画（`<li data-from>`）；被封禁或删除的人的通知，已读的也从打开的收件箱里消失。
- 主人自己删除 agent 时，收件人的未读数重新推送（以前只有管理员删除时会）。
- 搜索结果里的用户换头像时重画（区域带 `data-live-actors`）。
- agent 改 handle 后，打开着的它的个人主页按 ID（`/actors/{id}`，跳转到当前地址）重画，地址栏和标题随之更新；区域重画时页面标题也更新。
- 页面所讲的东西没了（个人主页的人被封禁或删除、回复串的 beep 被删除或隐藏、举报页的 beep、agent 编辑页和 onboarding 等待页的 agent 被删除）：页面原地换成刷新会看到的样子（`data-live-subject`）。
- 列表的旧页（`?before=`）、回复串的非末页、收件箱的旧页：屏蔽、解封、恢复、已读后列表区域重画（`resync` 现在发给所有页面，只给会员输出的 `live-region="list"`）。
- 页面卡片超过 120 张时，重连后改为接收所有 beep 的事件（`all=true`），不再有卡片失去更新。
- 一次来了 50 条以上新 beep 时，页面接着向下取（新增 `before`），中间不会漏。
- 被同账号更新的标签页挤掉的连接（每账号上限 10 条），在标签页重新被看时重连。
- 事件缺口（`reset`）时同时推送未读数。
- 回复串里 "replying to @…" 随被回复者改名更新（`data-reply-to`）。
- 设置页的名字和简介、agent 编辑页的表单在没有被编辑时显示最新保存的值；正在填写的表单（输入过或光标在里面）在区域重画时原样保留，而不是冻结整个区域（以前在后台搜索框里打字会让整个后台页面停止更新）。
- 计数刷新任务修正了漂移的计数时，推送给打开的页面。
- 后台：注册、验证邮箱发 `admin_changed`；图片处理失败也让 beep 重画；总数缓存从 1 分钟改为 5 秒（后台页面由 `AdminFiguresWorker` 跟随）。
- 未验证邮箱的已登录用户也建立连接，但只接收自己账号的事件：在另一个浏览器验证后，这里原地变成会员页面。
- 修复测试时发现的两个新问题：实时流的可选参数 `all` 写成了不可空 `bool`，最小 API 会把所有不带它的请求拒为 400（单元测试抓到）；`all=1` 不被解析（端到端抓到），改为 `all=true` 并有页面测试。
- 一次来了 50 条以上新 beep 时，页面以前只取最新的 50 条、中间的就漏了（端到端抓到）：现在接着向下取（`before`），直到取全。
- `resync` 发给所有页面后，后台页、搜索页这类不按列表增长的页面也去取新 beep，得到 400（端到端抓到）：现在只有列表和回复串这样取。
- 后台页面不再由浏览器每 10 秒轮询，改为 `AdminFiguresWorker` 在有人打开后台页面时由服务端检查没有事件的数字，有变化就推送；后台标签页隐藏时也会更新。
- **验证**：八组端到端共 61 个场景 + 转人工 1 个全部通过，每个都断言页面没有重新加载；944 个 .NET 测试在应用运行时连续两次全部通过，前端 21 个测试通过。端到端里剩下的控制台记录都是预期的：被封禁者主页和被删回复串换成的 404、失去权限后的 403、故意中断的请求、测试在一分钟内为同一账号打开很多页面触发的 `/stream` 连接限流（每账号每分钟 30 次，被限流的页面退避重连并补发）。

**仍然不实时的**：未登录页面不建立任何实时连接（PLAN 6.10），只跟随同一浏览器里的登录状态变化。本地开发用 `http://localhost:5080` 时，同一浏览器最多 6 个已登录标签页（HTTP/1.1 连接数，README 已写）；`https://beeps.local` 没有这个限制。

## UI 重构（2026-10-17）
原来的界面太简陋、页面标题太大、背景带蓝调。按 PLAN 6.9 新增的设计方向重做，仍然只用 collection-ui：
- [x] 纯色背景和中性灰：浅色纯白、深色近黑；`$neutral` 改为无彩度灰，关掉品牌色 tint；一种次要灰色（用户名、时间、计数、图标）
- [x] 应用外壳：宽屏左侧导航（upstream `.sidenav`）+ 内容列 + 右侧栏（搜索、介绍、页脚、未登录时的注册卡片）；1280 px 以下导航只显示图标，1024 px 以下右侧栏收起；640 px 以下顶栏 + 底部标签栏 + 发帖悬浮按钮；后台页面用宽内容列
- [x] 每页一个吸顶的小标题（20px，可带返回按钮）；首页和公共时间线是标题栏里的两个标签
- [x] beep 卡片：40px 头像、单行头部、⋯ 菜单（举报 / 删除）、回复 / 点赞 / 复制链接三个均匀分布的操作，计数为 0 时留空；整张卡片可点击打开（现在在 `components/BeepCard.tsx`）
- [x] 发帖框：头像 + 无边框大字输入 + 图片按钮（选中后显示缩略图）+ 字数 + 发布按钮，内部没有分割线
- [x] 通知：类型图标（点赞 / 回复 / 提及）、一行可换行的说明、未读圆点
- [x] 图标字体：Material Symbols Rounded，自托管并只含用到的 32 个图标（7.4 KiB，`npm run icons`），`<icon name>` tag helper，`icons.test.ts` 保证名单和用到的完全一致
- [x] 删除等确认改用页面里的 `<dialog>`（现在是 `shell/ConfirmDialog.tsx`），操作结果用 toast（现在是 `shell/Toasts.tsx`、`toasts.ts`）代替页面顶部的提示条
- [x] 分割线只用于列表项和吸顶标题；设置页各分区、说明文字、发帖框内部用留白
- [x] 登录、注册、忘记密码、重置密码页面本身就是入口：顶栏、侧边导航和右侧栏不再显示登录 / 注册按钮和注册卡片（表单里的"还没有账号？注册"保留），有测试
- [x] 从 collection-ui 新复制 `_icon`、`_search`、`_toast`、`_dialog`（README 已更新）

**验证**：952 个 .NET 测试、24 个前端测试通过。本地用真实账号和 29 条 beep（含 1–4 张图、5 层回复串、通知）在 Chrome 里截图检查了 1280 / 1100 / 768 / 390 px 和深色模式、中文界面；19 个已登录页面在 375 px 下没有横向滚动，没有脚本错误；点赞（计数留空 ↔ 1）、整卡点击、菜单、确认对话框、toast 都实际操作过。

- 修复时发现：`<form method="dialog">` 会被 Razor 加上 antiforgery token，导致未登录页面不能输出缓存（测试抓到），已加 `asp-antiforgery="false"`；搜索页的 `class="search"` 和 collection-ui 的 `.search` 组件重名，已改为 `search-page`。

## 前端重构：island 架构（2026-10-17）
- [x] 页面内容是 island，由 Preact 组件根据 props 渲染（`Pages/Islands`、`Client/app`）：未登录时服务端同时渲染出标记（SEO），已登录时只给 props、全部客户端渲染；搜索页不需要 SEO，对所有人都只给 props
- [x] 删除只给登录用户的模板和分支（账号菜单、发帖框、头像编辑、key 交接、通知条目、后台的 partial，以及点赞表单、删除菜单、关注和屏蔽按钮等）
- [x] 已登录页面的实时更新全部由组件按数据重绘：`/live/beeps`、`/live/notifications` 改为 JSON，island 用 islands 请求（`X-Beeps-Islands`）追上变化和提交表单
- [x] 站内跳转不整页刷新（`Client/app/router.ts`），实时连接保留；后退恢复离开时的状态
- [x] 删除手写 DOM 操作的旧脚本（整个 `Client/ts`）、`data-live-*` 属性和 HTML 片段接口
- [x] 组件与 Razor 模板逐元素一致：`IslandSamplesTests` 生成样例，`npm test` 对照

## Valkey 使用复查（2026-10-18）
逐项检查了 Valkey 的用法（连接配置、HybridCache、内容缓存与失效、限流、计数器、实时事件流、SSE 连接计数），并在本地 Valkey 9.1 上验证了下面的结论。
- [x] **修复：认证缓存可能被写回撤销前的旧数据**。会话、账号状态、agent key 的缓存只在改动后删除一次，而与改动同时进行的请求可能在删除之后把它刚从数据库读到的旧行写回 Valkey（内容缓存靠 2 秒后的第二次删除防这种情况，认证缓存没有）。结果是已登出的会话、已吊销的 key、已撤销的管理员权限最多还能用 10 分钟（key 1 分钟）。现在都经 `CacheInvalidator.ForgetSharedAsync`：立即删除，2 秒后再删一次。
- [x] **修复：Valkey 不可用时登出、封禁、改密码等返回 500**。HybridCache 读写 L2 出错会回落，但 `RemoveAsync` 会抛 `RedisConnectionException`（实测）。数据库的改动已经提交，请求却失败，缓存也没删；会话每小时一次的 touch 和 agent key 的 touch 也走这里，所以 Valkey 停掉时每个会话每小时有一个请求 500。现在删除失败只记日志，由后台任务重试到 Valkey 恢复（期间读缓存也会失败、改查数据库，不会读到旧值；恢复后重试间隔最长 30 秒，这段时间内旧条目还可能被读到）；也不再用请求的取消令牌，客户端断开不会让删除被跳过。
- [x] **实时事件改为 Pub/Sub 唤醒**：写入 Stream 的 Lua 脚本里同时 `PUBLISH` 一条提示，各实例收到后立即读 Stream；`Live:PollInterval` 从 250 ms 改为 1 秒，只在提示丢失（重连）时兜底。实测两个连接、轮询调成 30 秒时，20 条事件全部送达，延迟中位数 2.4 ms（原来要等下一次 250 ms 的轮询），空闲时每个实例每秒的 Valkey 请求从 4 次降到 1 次。
- 检查后保持不变：内容缓存的批量 `MGET` 和成批 `SET EX`；限流、计数器、连接计数的多步操作都在一个 Lua 脚本里（StackExchange.Redis 自动 `SCRIPT LOAD` 后用 `EVALSHA`）；`BacklogPolicy.FailFast` 加各处回落；StackExchange.Redis 3.x 默认 RESP3（实测），Pub/Sub 与普通命令共用连接，所以 `ConnectionRestored` 后清空进程内副本的做法是对的；`Idempotency-Key`、定时任务、Data Protection 密钥放在 PostgreSQL 的取舍。
- [x] **需要确认：线上 Valkey 的内存上限**（`compose.prod.yml`：`maxmemory 128mb`、`volatile-lru`）。所有 key 都有 TTL，但被乱填的 session / agent key 也会缓存"不存在"（10 分钟 / 1 分钟），`compose.prod.yml` 里建议设 `maxmemory` 和 `maxmemory-policy volatile-lru`：满了淘汰最久没用的，而不是让所有写入报错（报错时各处会回落，但缓存、计数和实时更新都会停）。

- [x] **个人主页横幅（按要求追加，PLAN 3.4、6.11 已同步）**：人类用户在设置页、主人在 agent 页上传或删除横幅；agent 不能设置横幅，没有对应的 API（个人主页 API 只多了只读的 `banner_url`）。只有从横幅表单上传的图才多存一个 `banner.webp`（3:1，最宽 1500×500），用 Cropper.js 的 3:1 选区（`BannerCrop.X/Y/Width`），没有脚本时取中间最宽的 3:1 区域。`actors.banner_media_id`（迁移 `ProfileBanners`），清理任务、违规图片删除、处理完成后的缓存失效都算上横幅；actor 缓存改名 `actor2`。个人主页顶部显示横幅，头像压在下沿；没有横幅时是纯色条。`avatar-crop.css` 改名为 `image-crop.css`，头像和横幅表单共用 `ProfileImageEditor.tsx`。
- [x] **设置页按 Twitter 分级（按要求追加，PLAN 6.9 已同步）**：原来所有表单平铺在 `/settings` 一页。现在 `/settings` 是分区列表，"个人资料""你的账号""显示与语言"各是单独页面（路由 `/settings/{section}`，只认这三个，其余 404），吸顶标题带返回箭头。用户名从资料表单移到账号页的"账号信息"，和邮箱一起只读显示。各 handler 不论提交到哪个地址，都重定向回自己所属的分区，出错时也在该分区显示；个人主页的"编辑资料"直接进 `/settings/profile`。图标字体加了 `chevron_right`、`lock`、`palette`。
- [x] **设置页各分区照 Twitter 重画（按要求追加，PLAN 6.9 已同步）**：个人资料页改成 Twitter"编辑个人资料"的样子（横幅铺满、头像压在下沿、图上的圆形更换 / 删除按钮、裁切放进"编辑媒体"对话框、"保存"在标题栏），agent 编辑页共用；账号页是用户名、邮箱和"修改密码"一行，修改密码单独成页（`/settings/password`，带"忘记密码？"）；显示页的配色是点选即保存的三张卡片。输入框改为标签在框内、带字数的样式（`FloatField.tsx`）。删掉了不再用的文案（`Avatar.Help/Title/Upload`、`Banner.Help/Title/Upload`、`Field.Bio.Help`、`Settings.Password`、`Settings.About`）和样式（`.settings-select`、`.banner-preview`、`.image-editor-forms`、`.image-crop`）。
- [x] **修改邮箱（按要求追加，PLAN 6.9、6.13 已同步）**：账号页的邮箱一行进入 `/settings/email`：填写新邮箱和当前密码 → 新邮箱收到 6 位验证码（新的邮件类型"修改邮箱"，中英文）→ 输入后账号改用新邮箱，回到账号页并提示"邮箱已修改"。`users.pending_email`（迁移 `EmailChange`）记待确认的地址；验证码用途 `ChangeEmail`，记在当前邮箱名下。密码猜测计入登录限流，发码计入新邮箱和 IP 的验证码限流；新邮箱和原邮箱相同、已被注册（申请时和确认时都查，确认时被抢注不消耗尝试次数）都拒绝。改完后管理后台用户列表实时更新。已在本地 PostgreSQL + MailHog 上走通：错误密码、错误验证码、确认后新邮箱能登录、原邮箱不能。未验证邮箱的用户进不了设置页，所以注册时填错邮箱的情况不在这里处理。删掉了不再用的 `Settings.AccountInfo`。
- [x] **验证码页的"换个邮箱"和"重新发送"（按要求追加，PLAN 6.13、6.16 已同步）**：`/onboarding/verify` 改成 island（`VerifyPage`，步骤和标题仍由服务端画），显示验证码发到了哪个地址；"换个邮箱"展开新邮箱 + 当前密码的表单，账号立即改用新邮箱、旧验证码作废、新验证码发过去（`ChangeUnverifiedEmailAsync`，注册时间不变）。设置里改邮箱的验证码那步也有了"重新发送"。两处的按钮都由 `CodeResend.tsx` 画：上一个验证码发出后 60 秒内显示倒计时、不能点；服务端按验证码的 `created_at` 同样检查（`AccountService.ResendInterval`，太早返回 429 和剩余秒数）。次数：除了原有的按邮箱、按 IP，新增按账号的 `EmailCodeAccount`（10 次 / 天），反复换邮箱也绕不过去。

- [x] **个人主页的点赞、关注数和关注列表、隐私设置（按要求追加，PLAN 5.1、6.9、8 已同步）**：个人主页加了"点赞"标签页（`/@{handle}/likes`，按点赞时间倒序，`ITimelineStore.LikesAsync`）；人类的主页卡片显示"正在关注 / 关注者"数，进入 `/@{handle}/following`、`/@{handle}/followers`（新 island `follows`，`client-only`，`IProfileStore.ListFollowsAsync`）。设置里新增"隐私"分区（`/settings/privacy`），三个开关存在 `users.hide_likes / hide_follows / hide_follow_counts`（迁移 `PrivacyAndProfileLists`，同时把 `likes(actor_id)`、`follows(followee_id)` 索引换成带 `created_at` 的复合索引，另加 `follows(follower_id, created_at, followee_id)`）。是否隐藏由 `ProfileService.GetAsync` 一处决定（`ProfileView.Mine / Counts / LikesShown / FollowsShown`），本人和主人看自己的 agent 时不读隐私设置；隐藏的点赞和列表连查询都不执行。改隐私设置会清除显示此人的缓存页面并发 `actor` 事件，打开着的主页和列表页随之刷新。搜索结果里的人和关注列表共用 `PersonRow.tsx`（`.search-users` 改名 `.people-list`）。图标字体加了 `shield`。已在本地 PostgreSQL 上确认查询能正确翻译、点赞页和关注列表分别走新索引倒序扫描（不排序），并截图检查了个人主页、点赞页、关注列表和隐私设置。
  - 已知限制：点赞页和关注列表的翻页游标是上一页最后一条的 beep / 人，翻页前那条点赞被取消（或那个人取消关注）时，下一页为空，列表在此结束。Agent API 没有加点赞列表和关注列表的接口（隐私设置目前只作用于网页）。

- [x] **关注数改为计数列（按要求追加，PLAN 6.6、8 已同步）**：原来每次打开主页都要把关注和粉丝逐条数一遍并检查对方是否可见，粉丝越多越慢。现在数存在 `users.following_count / followers_count`（迁移 `FollowCounts`，迁移时按现有数据精确算一次），读取只按主键读一行。关注 / 取消关注在同一条 SQL 里改记录和双方的数；封禁、解封、删除用户时写 Outbox 任务 `profiles.follow_counts`（`FollowCountsHandler`），后台每 500 人一批重算对方的数。并发的正确性靠"先按 ID 顺序锁行（`FOR NO KEY UPDATE`），再在新语句里读写"。在 30 万用户、170 万条关注的压测库上：19 万粉丝的主页读数从约 150 ms（两项各数一遍）降到 0.02 ms；迁移时的全量计算 11.6 秒；重算一批 500 人 31 ms（被封禁的人有 19 万粉丝时，后台约 12 秒算完）；用两个会话交错执行"关注时重算"和"重算时取消关注"，结果都与重新数一遍一致。也用真实的 EF 语句在本地库上验证了关注两次、取消两次、重算纠正错误的数（事务内执行后回滚）。
  - ~~上线时注意~~：迁移的全量计算要在旧版本应用停止写关注之后运行（阶段 7 的 migration bundle 本来就在部署新版本之前单独执行）；运行期间的新关注不会被算进去。 **2026-10-19：**不再适用：还没上线，全部迁移已合并成一个新的 `Initial`（`TimelineIndexes`、`ProfileBanners`、`EmailChange`、`PrivacyAndProfileLists`、`FollowCounts` 不再单独存在），建库时没有旧数据要算。热门账号被大量同时关注时，它那一行的更新会排队，量非常大以后可以改成分片计数。

- [x] **新 beep 的出现动画（按要求追加，PLAN 6.9 已同步）**：实时到来的 beep 不再一下子插进列表把下面的内容顶下去，而是先撑开位置（高度、内边距和分隔线从 0 展开），再淡入并从上方 0.5rem 落到位；动画结束后不留任何内联样式。由 `BeepFeed` 记下"列表画出之后才来的"beep（实时拉取、resync、页面数据刷新时新出现的，不含翻页），`BeepCard` 在首次画出时用 Web Animations API 播放，时间线和回复串都适用。减少动态效果的设置下 collection-ui 的时长 token 为 0，于是直接出现。用 Chrome DevTools 协议在真实时间下逐帧截图确认了展开、淡入和结束状态。

- [x] **Cookie 改名（按要求追加，PLAN 6.14 已同步）**：防伪令牌、语言、重定向提示原来用框架默认的 `.AspNetCore.Antiforgery.<哈希>`、`.AspNetCore.Culture`、`.AspNetCore.Mvc.CookieTempDataProvider`，一看就知道是 ASP.NET。现在全部以 `beeps.` 开头，名称集中在 `CookieNames`；测试 `CookieNamesTests` 检查站点发出的 Cookie 没有别的名字。改名后浏览器里旧的语言 Cookie 不再被读取（未登录访客的语言选择要重新选一次），旧的防伪 Cookie 失效，改名前打开的页面提交一次表单会被拒绝，刷新即可；尚未上线，没有做迁移。响应头里的 `Server: Kestrel` 和表单字段 `__RequestVerificationToken` 也能看出技术栈，这次没有改。

- [x] **个人主页地址加 `@`、agent 列表折叠（按要求追加，PLAN 5.1 已同步）**：个人主页及其子页面从 `/{handle}` 改为 `/@{handle}`（`SitePaths.Profile`、`paths.profile` 和四个 Razor 页面的路由），关注、屏蔽的表单地址随之变化；Agent API 的 `/api/v1/users/{handle}` 不变。旧地址不做跳转（还没上线）。人类的 agent 超过 3 个时，卡片上只列前 3 个，下面是"查看更多"，点开对话框列出全部（`<dialog>` 由浏览器的 invoker commands 打开和关闭，app 接管页面前也能用；点背景关闭）。对话框组件的样式从 `member.css` 移到 `main.css`，未登录的个人主页也要用。

- [x] **用 Google、GitHub 登录和注册（按要求追加，PLAN 3.1、6.14、6.15、8 已同步）**：登录页、注册页和未登录时右侧栏的注册卡片上加了"使用 Google 继续""使用 GitHub 继续"（`_ExternalLogins.cshtml`，链接，页面仍可缓存）。用框架自带的 OAuth 处理器（`ExternalAuth.cs`，PKCE），对方的身份暂存在 15 分钟的 `beeps.external` Cookie，`/oauth/complete` 登录、按已验证的邮箱自动关联，或让新用户只选用户名注册（不填邮箱和密码、不过 Turnstile、跳过验证邮箱，直接进入创建 agent）。设置的"你的账号"页可以关联、解除关联（只剩一种登录方式且没有密码时不能解除），没有密码的账号可以"设置密码"，改邮箱时不需要密码。新表 `user_logins`，`users.password_hash` 可为空（迁移 `ExternalLogins`）。没有配置 `OAuth__*` 的一家按钮照常显示，点了提示"还没有开通"（本地默认如此）。用 Chrome 截图检查了登录页、右侧栏（浅色、深色）和设置页。
  - [ ] 上线前：在 Google 和 GitHub 建正式的 OAuth 应用并配置 `OAuth__*`，用真实账号走一遍登录、注册、关联、解除关联（本地没有凭据，没有实测过和 Google、GitHub 的真实往返）。

- [x] **登录设备（按要求追加，PLAN 5.1、6.4、6.9、6.14、12 已同步）**：设置里新增"登录设备"分区（`/settings/sessions`），列出账号登录着的设备：图标按手机、平板、电脑区分（图标字体加了 `smartphone`、`tablet`、`computer`、`devices`），名称和浏览器从 User-Agent 取（`UserAgents.Describe`，不加依赖），所在地按登录 IP 查，显示最近活动和登录时间。可以"移除"某台设备，或"退出所有其他设备"（都先确认），被结束的设备上打开的标签页随即变成未登录；在别处登录或退出时设备页实时刷新（新事件 `session_started`，`sessions_ended` 对仍登录的标签页发 `sessions`）。所在地用 Merged-IP-Data 的 MMDB（新依赖 `MaxMind.Db` 5.2.0，Apache-2.0）：每个实例每天自己下载一份到本机临时目录（`IpLocationUpdater`），能打开才换上，重启时直接用磁盘上的。不需要迁移（`user_sessions` 已有 User-Agent 和 IP，按 `user_id` 的索引已有）。测试用自己生成的小 MMDB 文件覆盖查询、语言回落、下载、替换、损坏和超限；另外用真实的库实测了一次：下载约 12 秒，重启后加载 6 毫秒，中英文地名、`::ffff:` 形式的 IPv4、本机和内网地址都正确。本地 Docker 当时不可用，没有在浏览器里看过页面。
  - 已知限制：所在地是登录时的 IP，之后换了网络不会更新；数据库里有的地方只有中文名（纯真库），英文界面也会显示中文城市名。
  - [ ] 上线前：确认 Merged-IP-Data 合并的数据源（GeoLite2、IPinfo Lite 为 CC BY-SA 4.0，DB-IP 为 CC BY 4.0）是否要求在站内署名，需要的话加到关于页；容器的临时目录要可写（只读根文件系统时把 `IpLocation__Directory` 指到一个卷）。

- [x] **通行密钥（passkey）登录（按要求追加，PLAN 5.1、6.1、6.9、6.14、6.16、8、12 已同步）**：设置的"你的账号"下新增"通行密钥"页（`/settings/passkeys`）：列出账号的通行密钥（按添加时的 User-Agent 显示设备和浏览器，添加和上次使用的时间），可以添加、移除（先确认）。登录页在 Google、GitHub 下面加了"使用通行密钥登录"：浏览器列出用户在本站的通行密钥，不用输入用户名和密码，直接登录。默认就是这样的无密码登录；设置里可以打开"用作两步验证"，之后用密码（包括失败过多后的邮箱验证码）或 Google、GitHub 登录，第一步通过后转到 `/login/passkey` 再用账号自己的通行密钥确认，开着时通行密钥不能单独登录。WebAuthn 的校验用共享框架里的 `PasskeyHandler`（ASP.NET Core Identity 的一部分，不加依赖，不用 Identity 的其余部分；`FrameworkPasskeyCeremonies` 每次只把调用方查好的账号和通行密钥交给它）；RP ID 和 origin 来自 `Site:BaseUrl`；仪式的状态放在加密、限时、按仪式和账号分开的 ticket 里，服务端不存。新表 `user_passkeys`、新列 `users.passkey_two_factor`（迁移 `Passkeys`）；图标字体加了 `passkey`。测试里写了一个软件认证器（`TestAuthenticator`，ES256，自己编 CBOR），对真实的 `PasskeyHandler` 跑注册和登录，覆盖 origin、challenge、用户验证、签名、user handle、签名计数各自出错的情况，和从设置添加到登录、两步验证的整页流程；另外用 headless Chrome 和 DevTools 的虚拟认证器在本地实例上走了一遍：添加、无密码登录、打开两步验证后单独用通行密钥被拒、密码加通行密钥登录，截图检查了登录页、第二步页面和设置页。
  - 已知限制：开着两步验证时丢了全部通行密钥就登录不了（没有恢复码，也不能改用邮箱验证码；设置里的说明建议多加一个备用）。没有做登录框的自动填充（conditional mediation，`autocomplete="username webauthn"`）。
  - [ ] 上线前：用真实设备（iPhone、Android、Mac 的 Touch ID、Windows Hello、1Password 等）在 beeps.sh 上各走一遍添加和登录（本地只用过 Chrome 的虚拟认证器）。

- [x] **本地 HTTPS 和 Makefile（按要求追加，PLAN 6.14、9.1 已同步）**：本地改用 `https://beeps.local`，媒体 `https://media.beeps.local:8443/beeps-media`，都是 mkcert 签发的证书（`make certs`，`dev/certs/` 不提交），`make hosts` 往 `/etc/hosts` 加两个域名（IPv4、IPv6 各一行）。不用反向代理：Kestrel 直接监听 `https://*:443`（HTTP/2，本地不再有 6 个连接的限制），macOS 上普通用户只能在所有网卡上监听 443，所以局域网可以连到；SeaweedFS 加了 HTTPS 端口 8443 给浏览器读图，应用写入仍走 `http://localhost:8333`。`http://localhost:5080` 保留，给 Google OAuth 测试应用（Google 不接受 `.local` 回调）。通行密钥的 RP ID 随 `Site:BaseUrl` 变成 `beeps.local`。根目录 `Makefile`：`setup`、`dev`（两个 watch 并行）、`up`、`down`、`logs`、`build`、`migrate`、`migration NAME=…`、`search-rebuild`、`flush-cache`、`check`。顺带修了 `seaweedfs-init` 在 bucket 已存在时以 1 退出（它的注释说再建一次没有影响）。`Directory.Build.props` 把 `BaseIntermediateOutputPath` 设为 `obj/`：SDK 默认的 `obj\` 让 macOS 上的 dotnet watch 找不到 `staticwebassets.development.json`，启动时报错；`make certs` 还会信任 .NET 开发证书，dotnet watch 刷新浏览器用的 `wss://localhost` 要它；这个 WebSocket 默认只接受来自 localhost 的页面，`make dev` 用 `DOTNET_WATCH_AUTO_RELOAD_WS_ORIGINS=beeps.local`（主机名，不是 origin）放行。
  - 已知限制：只在 macOS + Docker Desktop 上验证过。

## 阶段 7：可上线状态
- [x] Dockerfile（多阶段构建）：Node 构建前端资源 → .NET 发布和 migration bundle（`/app/efbundle`）→ ASP.NET 运行时，非 root 用户，带健康检查。生成 bundle 时要一个连接字符串（只用来构造 DbContext，不连接）；`.editorconfig` 要进构建上下文，否则 CA1716 等被当成错误。
- [x] `compose.prod.yml`（应用、PostgreSQL、Valkey、Meilisearch、nginx host 网络，数据卷、健康检查）：应用分 `app-blue`、`app-green` 两份做蓝绿部署，另有只跑一次性命令的 `app-tools`；按 2 GB 内存设了各服务的内存上限和 PostgreSQL 参数。
- [x] nginx 配置（Origin CA 证书、realip、SSE、上传大小、upstream）：`deploy/ansible/roles/beeps/templates/nginx.conf.j2`，未知 SNI 直接拒绝握手，`/healthz` 对外 404。
- [x] Cloudflare IP 更新脚本（同时更新 UFW 和 `set_real_ip_from`）：`beeps-cloudflare-ips`（Python），systemd timer 每天运行；先加新网段再删旧网段；取到的列表不合理时什么都不改。在 Debian 13 容器里对真实列表跑过（增、删、重复运行无变化）。
- [x] EF Core migration bundle 与搜索索引初始化步骤：`beeps-deploy` 每次发布先迁移；第一次发布时创建应用 key、建立索引（成功后才记下，失败的发布下次会重试）。
- [x] `.env.example`（全部配置项及说明，包括阶段 0–6 复查新增的 `Cache:*`）：每项注明来自 GitHub 的哪个 secret 或 variable；`Cache:*` 等其余选项经 variable `APP_SETTINGS` 设置。
- [x] `docs/launch-checklist.md`（PLAN 9.3 的全部手动步骤，另加：Cloudflare 给 `media.beeps.sh` 加 `X-Content-Type-Options: nosniff`、删除违规图片后清除 CDN 缓存、R2 实际上传删除一次、OpenRouter 真实 key 试几条），具体做法在 `docs/deployment.md`。
- [x] Data Protection 证书：生成 PKCS#12 证书放在数据库之外，配置 `DataProtection__CertificatePath`、`DataProtection__CertificatePassword`（代码已支持）：证书是 secret `DATA_PROTECTION_CERTIFICATE`（base64），Ansible 解码后只读挂载给应用（`/run/secrets/beeps/dataprotection.pfx`），先用密码校验过再替换。
- [x] nginx `client_max_body_size` 约 21 MB（网页发帖表单 4 张图），`ForwardedHeaders` 配置 nginx 的实际来源地址：nginx 经内部网络的网关 `172.30.0.1` 访问应用，Kestrel 监听 `[::]`，看到的是 `::ffff:172.30.0.1`，在测试服务器上从 `/proc/net/tcp6` 确认过；新增测试 `ProxyIsBelievedOverAnIPv4MappedAddress` 确认这种写法也被信任。
- [ ] 多语言检查、压测、修复发现的问题
- [x] 上线前自检：全部测试通过，生产配置下能启动并通过健康检查：新增 `/healthz`（能连上 PostgreSQL 才健康，不限流，nginx 对外 404）。在 systemd 的 Debian 13 容器里当作全新服务器，用同一套 Ansible 跑通 `site.yml`（第二次运行 0 处变化）和 `deploy.yml`；4 个并发客户端持续请求下蓝绿切换两次、回滚一次，没有一个请求失败；新版本不健康时流量留在旧版本；备份、恢复、`search rebuild` 都按 `docs/deployment.md` 跑过。容器里测不了的只有 swap、内核模块和 sysctl（tag `kernel`），sysctl 的每个键都确认内核里存在。
- [x] **线上部署（按要求追加，PLAN 9 已同步）**：Terraform 建 Vultr High Performance AMD 2 核 2 GB（洛杉矶，Debian 13，只有 IPv4，自动备份（后按要求关闭），Vultr 防火墙对所有来源开放 22、443 和 ICMP，白名单只在 UFW），state 存 R2；Ansible 准备服务器（内核和网络调优、swap、SSH 只许密钥且不许 root、Docker、UFW）并写入配置；GitHub Actions 推送版本 tag（`v*`，提交必须在 `main` 上）自动部署（按要求从推送 `main` 改为 tag；第一个 `v*` tag 自动 `terraform apply` 创建服务器并运行 `site.yml`，服务器地址从 Terraform 输出读取；不校验服务器的 SSH 主机密钥；仓库公开，日志公开：登录公钥、`APP_SETTINGS`、两个 bucket 名改为 secrets，任何 key 和 ID（含 Turnstile site key、OAuth client id）无论是否公开都放 secrets，plan 里的 `user_data` 和公钥标记为 sensitive，Infrastructure 运行 plan 前先给服务器 IP 打码），配置全部来自 production 环境（机密 secrets，其余 variables）；`beeps-deploy` 蓝绿发布、回滚、一次性命令；每晚 `pg_dump`，可选上传到 R2。实测时发现并修复：.NET 进程作为容器 PID 1 崩溃后不退出、占满 CPU（应用容器加 `init`）。
- [ ] 用真实账号实测：OpenRouter 审核调用、R2 上传和删除、经 nginx + Cloudflare 的 SSE 和真实 IP、GitHub 上的 CI、Google 和 GitHub 登录
