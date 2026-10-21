# 线上部署

beeps 线上跑在一台 Vultr High Performance AMD（2 vCPU / 2 GB，洛杉矶）的 Debian 13 上，全部服务都是 Docker 容器（PLAN 9）。三部分配置各管一层：

| 层 | 位置 | 怎么运行 |
|---|---|---|
| 服务器本身 | `deploy/terraform/`（Vultr 实例、Vultr 防火墙；state 存在 R2） | 第一个版本 tag 的「Deploy」自动创建；之后的改动用「Infrastructure」手动触发 |
| 服务器上的系统和服务 | `deploy/ansible/`、`compose.prod.yml` | GitHub Actions「Deploy」：第一次（或勾选 provision）运行 `site.yml`，其余运行 `deploy.yml` |
| 应用发布 | `Dockerfile`、`deploy/ansible/roles/beeps/files/beeps-deploy` | 推送版本 tag（`v1.2.3`）自动触发「Deploy」 |

Cloudflare（DNS、证书、R2、Turnstile）不在 Terraform 里，按下文在后台手动配置。

## 请求链路

```
访客 ─HTTPS─▶ Cloudflare ─HTTPS（Origin CA）─▶ Vultr 防火墙（22、443 对所有来源开放）
     ─▶ UFW（443 只放行 Cloudflare 的 IPv4 网段，22 放行所有来源）
     ─▶ nginx（host 网络，监听 443）─HTTP─▶ app-blue 172.30.0.11:8080 或 app-green 172.30.0.12:8080
                                                  └─▶ postgres / valkey / meilisearch（beeps 内部网络，不对外）
```

- 服务器只有 IPv4（Vultr 上关闭了 IPv6，内核里也关闭了）；访客照样可以经 IPv6 访问 Cloudflare。
- Vultr 防火墙只决定哪些端口能到服务器（22、443、ICMP），不设来源白名单；443 的白名单只在 UFW 里，由 `beeps-cloudflare-ips` 每天从 Cloudflare 官方列表更新，同一份列表同时写进 nginx 的 `set_real_ip_from`。
- nginx 用 host 网络，UFW 对它生效；没有任何容器发布端口（Docker 发布的端口会绕过 UFW）。
- nginx 经 beeps 网络的网关 `172.30.0.1` 访问应用，应用只信任这个地址发来的 `X-Forwarded-For`（`ForwardedHeaders__KnownProxies__0`），日志、限流、登录设备里都是访客真实 IP。
- `/healthz` 只在服务器内部可用，nginx 对外返回 404。

## 蓝绿部署

发布一个版本就是给 `main` 上的提交打 tag 并推送：

```sh
git tag v1.2.3 && git push origin v1.2.3
```

「Deploy」先确认 tag 指向的提交在 `main` 上（没合并的分支上的 tag 会被拒绝），再依次运行 CI（`ci.yml`）、构建镜像推到 GHCR（`ghcr.io/<owner>/beeps:v1.2.3`，部署时按 `@sha256:…` 引用）、用 Ansible 写入配置并在服务器上执行 `beeps-deploy deploy <镜像>`。推送到 `main` 只运行 CI，不部署。

1. 确保 PostgreSQL、Valkey、Meilisearch 在运行；第一次部署时用 master key 创建应用专用的 Meilisearch key（`search.env`）。
2. 用新镜像里的 migration bundle（`/app/efbundle`）执行数据库迁移，此时旧版本仍在接收流量。第一次部署后再从 PostgreSQL 建立搜索索引。
3. 用新镜像启动空闲的颜色（blue 或 green），等 `/healthz` 通过（最多 180 秒）。不通过就停掉它，流量留在旧版本，部署失败。
4. 改写 `nginx/upstream.conf` 指向新颜色，`nginx -t` 通过后 `nginx -s reload`：进行中的请求在旧 worker 上完成，新请求进入新版本。
5. 等 15 秒让旧版本处理完手上的请求，再停掉旧颜色（30 秒优雅退出，打开的 SSE 连接收到关闭后由浏览器带 `Last-Event-ID` 重连到新版本）。停掉的容器和镜像保留，用于回滚。

整个过程不中断服务（本地用同样的 Ansible 在 Debian 13 容器里实测：两次切换和一次回滚期间 4 个并发客户端持续请求，没有一个失败）。

**迁移必须向后兼容**：迁移在旧版本还在运行时执行，所以每次迁移都要让旧版本继续能用（先加列、加表，等不再用到的那个版本下线后，再在下一次发布里删除）。

部署一次只跑一个（`concurrency: production`），后来的 tag 排队等前一个完成，不会打断进行中的部署。GitHub 只保留一个排队的运行：排队时又来一个，先排队的那个会被取消，部署的总是最新的 tag（Infrastructure 也在这个组里）。要重新部署旧版本，在 Actions → Deploy → Run workflow 里选那个 tag 运行（注意它不会撤销之后的数据库迁移）。

## 第一次上线

### 1. Cloudflare（手动）

1. 把 `beeps.sh` 加进 Cloudflare，SSL/TLS 模式设为 **Full (strict)**，开启 **Always Use HTTPS**、**HSTS**，最低 TLS 1.2。
2. **Origin CA 证书**：SSL/TLS → Origin Server → Create Certificate，主机名 `beeps.sh`、`*.beeps.sh`，有效期 15 年。证书存为 secret `ORIGIN_CERTIFICATE`，私钥存为 `ORIGIN_PRIVATE_KEY`（都是 PEM 原文）。
3. **R2**：
   - Terraform state 的 bucket（如 `beeps-terraform`），和一个只能读写它的 R2 API token（`TF_STATE_ACCESS_KEY_ID` / `TF_STATE_SECRET_ACCESS_KEY`）。
   - 媒体 bucket（如 `beeps-media`），绑定自定义域名 `media.beeps.sh`，再建一个只能读写它的 token（`STORAGE_*`）。
   - Rules → Transform Rules 给 `media.beeps.sh` 的响应加 `X-Content-Type-Options: nosniff`（R2 对象元数据设不了这个头）。
   - 可选：备份 bucket（如 `beeps-backups`），加一条 30 天后删除的生命周期规则，和只能读写它的 token（`BACKUP_*`）。
4. **Turnstile**：为 `beeps.sh` 建一个 widget，site key 和 secret 分别是 `TURNSTILE_SITE_KEY`、`TURNSTILE_SECRET_KEY`。
5. DNS 记录等服务器建好、知道 IP 之后再加（第 4 步）。

缓存不用另加规则：Cloudflare 默认按扩展名缓存，正好是 `/dist/` 下的 JS、CSS 和默认头像 SVG；HTML 和 API 不缓存（PLAN 9.2）。管理员专用的 chunk 带 `private`，不会被缓存。

### 2. 其他服务

- **Resend**：验证发信域名（SPF、DKIM），创建 API key（`RESEND_API_TOKEN`），`EMAIL_FROM` 用这个域名的地址。
- **OpenRouter**：创建 API key（`OPENROUTER_API_KEY`），设置用量上限。
- **Google / GitHub 登录**（可选）：回调地址 `https://beeps.sh/oauth/google/callback`、`https://beeps.sh/oauth/github/callback`。
- **Sentry**（可选）：建一个 ASP.NET Core 项目，把它的 DSN 存为 `SENTRY_DSN`。应用把没被捕获的异常和 Error 级别以上的日志发过去（不带 IP、Cookie、查询字符串），版本号就是 tag（`Beeps@1.2.3`）；不设就什么都不发。
- **Vultr**：Account → API 创建 API key（`VULTR_API_KEY`）。如果给它设了 IP 访问控制，要允许 GitHub Actions 的出口地址（Any IPv4）。

### 3. 生成密钥

```sh
# GitHub Actions 登录服务器用的 SSH 密钥：私钥 → secret DEPLOY_SSH_KEY，公钥写进 secret SSH_PUBLIC_KEYS
ssh-keygen -t ed25519 -N '' -C github-actions -f beeps-deploy

# 数据库密码和 Meilisearch master key（至少 16 位，不含 ; 和 '）
openssl rand -hex 32   # → POSTGRES_PASSWORD
openssl rand -hex 32   # → MEILI_MASTER_KEY

# 加密 Data Protection 密钥的证书（PLAN 6.4）：base64 → DATA_PROTECTION_CERTIFICATE，密码 → DATA_PROTECTION_CERTIFICATE_PASSWORD
openssl req -x509 -newkey rsa:3072 -nodes -days 36500 -subj /CN=beeps-dataprotection -keyout dp.key -out dp.pem
openssl pkcs12 -export -inkey dp.key -in dp.pem -out dataprotection.pfx -passout pass:"$(openssl rand -hex 24 | tee dp.password)"
base64 < dataprotection.pfx | tr -d '\n'
```

这几个文件（尤其是 `dataprotection.pfx` 和它的密码）另外妥善保存一份：证书丢了，数据库里的 Data Protection 密钥就解不开，所有人都会被登出、表单令牌失效。

### 4. GitHub：production 环境

Settings → Environments → New environment，名字 `production`（可以在这里加审批人）。

仓库是公开的，Actions 日志所有人都能看。每个步骤的日志里会列出它的环境变量：**Variables 原样显示，Secrets 显示成 `***`**。所以**任何 key 和 ID 都放 Secrets**，即使它本来就会出现在网页上（比如 Turnstile site key、OAuth client id）；Variables 只放域名、bucket 名这类设置，拿不准就放 Secrets。另外在 production 环境里设置：

- **Deployment branches and tags** 选 *Selected branches and tags*，只加分支 `main`（手动运行 Infrastructure）和 tag `v*`（Deploy）。这样别的分支即使改了 workflow，也拿不到这些 secrets。
- 在仓库的 Rules 里保护 `main`（只能经 PR 合并），并限制只有维护者能创建 `v*` tag：推一个 tag 就会部署。

**Secrets**

| 名字 | 内容 | 必需 |
|---|---|---|
| `VULTR_API_KEY` | Vultr API key | 是 |
| `CLOUDFLARE_ACCOUNT_ID` | Cloudflare 账户 ID（32 位，R2 页面右侧可见）。Terraform state、媒体和备份的 R2 地址 `https://<ID>.r2.cloudflarestorage.com` 都由它拼出，所以放在 secret 里，日志中会被打码 | 是 |
| `TF_STATE_ACCESS_KEY_ID`、`TF_STATE_SECRET_ACCESS_KEY` | 读写 Terraform state bucket 的 R2 token | 是 |
| `DEPLOY_SSH_KEY` | `beeps-deploy` 私钥 | 是 |
| `ORIGIN_CERTIFICATE`、`ORIGIN_PRIVATE_KEY` | Cloudflare Origin CA 证书和私钥（PEM） | 是 |
| `DATA_PROTECTION_CERTIFICATE`、`DATA_PROTECTION_CERTIFICATE_PASSWORD` | 上面生成的 PKCS#12（base64）和密码 | 是 |
| `POSTGRES_PASSWORD`、`MEILI_MASTER_KEY` | 上面生成的随机值 | 是 |
| `STORAGE_ACCESS_KEY_ID`、`STORAGE_SECRET_ACCESS_KEY` | 媒体 bucket 的 R2 token | 是 |
| `RESEND_API_TOKEN` | Resend API key | 是 |
| `TURNSTILE_SITE_KEY`、`TURNSTILE_SECRET_KEY` | Turnstile 的 site key 和 secret | 是 |
| `OPENROUTER_API_KEY` | OpenRouter key | 审核开着时 |
| `OAUTH_GOOGLE_CLIENT_ID`、`OAUTH_GOOGLE_CLIENT_SECRET`、`OAUTH_GITHUB_CLIENT_ID`、`OAUTH_GITHUB_CLIENT_SECRET` | OAuth 应用的 client id 和 secret | 否 |
| `SENTRY_DSN` | Sentry 项目的 DSN | 否 |
| `BACKUP_ACCESS_KEY_ID`、`BACKUP_SECRET_ACCESS_KEY` | 备份 bucket 的 R2 token | 否 |
| `SSH_PUBLIC_KEYS` | `beeps-deploy.pub` 的内容，再加上你自己的公钥，每行一个。公钥本身不算机密，但有人可以拿着它问一台服务器“接不接受这把 key”，扫一遍 Vultr 的地址段就能找到被 Cloudflare 藏起来的源站；你的个人公钥也能对上你的 GitHub 账号 | 是 |
| `TF_STATE_BUCKET` | Terraform state 的 bucket 名，如 `beeps-terraform` | 是 |
| `BACKUP_BUCKET` | 备份 bucket 名，如 `beeps-backups`（和媒体在同一个 Cloudflare 账户） | 否 |
| `APP_SETTINGS` | 其他应用配置，每行一个 `KEY=VALUE`（见 `.env.example`）。放 secret：限流阈值之类的值不该公开，也免得以后有人顺手往里写 key | 否 |

**Variables**（只有不是 key、也不是 ID 的设置；key 和 ID 无论是否公开都放 Secrets）

| 名字 | 例子 | 必需 |
|---|---|---|
| `VULTR_REGION` | `lax`（默认，洛杉矶；默认值本来就写在公开的 Terraform 代码里）。只在创建服务器时生效 | 否 |
| `SERVER_NAME` | `beeps.sh`（默认） | 否 |
| `SITE_BASE_URL` | `https://beeps.sh` | 是 |
| `MEDIA_PUBLIC_BASE_URL` | `https://media.beeps.sh` | 是 |
| `STORAGE_BUCKET` | `beeps-media` | 是 |
| `EMAIL_FROM` | `beeps <noreply@beeps.sh>` | 否 |
| `MODERATION_ENABLED` | `true`（默认）或 `false` | 否 |

每个值最终写到服务器的哪个配置项，见 `.env.example` 和 `deploy/ansible/group_vars/all.yml`。值里不能有单引号和换行（PEM 证书除外，它们写成文件），Ansible 会检查。

### 5. 第一次部署

打第一个版本 tag 并推送：

```sh
git tag v0.1.0 && git push origin v0.1.0
```

仓库里还没有别的 `v*` tag，「Deploy」就把这次当作第一次：

1. `terraform apply` 创建服务器（Vultr 实例和防火墙）。
2. 运行 `site.yml`：等服务器开机、cloud-init 跑完，然后系统升级、性能调优、Docker、UFW、Cloudflare IP 列表，写入配置，启动 PostgreSQL / Valkey / Meilisearch / nginx。
3. 第一次发布：迁移、创建搜索 key、建立索引、启动 app-blue。

服务器地址每次部署都从 Terraform 的输出读取（在日志里打码：它是 Cloudflare 隐藏的源站地址），不用另外配置。服务器的 SSH 主机密钥不校验也不记录，服务器重建后不用改任何东西。第一次失败了，修好后在 Actions 里重新运行即可：仍然只有这一个 tag，还是按第一次处理，`terraform apply` 和 `site.yml` 重复运行都没有副作用。

部署完成后：

1. 在 Vultr 后台找到实例 `beeps-production` 的 IPv4，在 Cloudflare DNS 加 `A beeps.sh → 这个 IP`，**开启代理**（橙色云朵）。不要加 AAAA 记录（服务器没有 IPv6），也不要加任何不经代理、指向这个 IP 的记录。在这之前站点从外面打不开，部署最后那一步站点检查只会给出警告。
2. 打开 https://beeps.sh，**第一个注册的账号自动成为管理员**，再按需关闭开放注册。

不开 Vultr 的自动备份，数据库靠每晚的 `pg_dump`（见下文“备份”）。实例设了 `prevent_destroy`，Terraform 不会删除它。之后要改服务器本身（套餐、防火墙），用 Actions → Infrastructure：先 `plan` 看一遍，再 `apply`。地区（`VULTR_REGION`）和主机名只在创建时生效：改了它们 Terraform 要换一台新服务器，`prevent_destroy` 会让 plan 直接报错；真要换，得另建服务器再迁移数据。

## 日常操作

推送版本 tag 即发布。改了服务器本身的配置（`roles/base`、`roles/docker`、`roles/firewall`）时，在最新的 tag 上手动运行一次 Deploy 并勾选 provision；改了 `compose.prod.yml`、nginx 配置或 GitHub 里的配置项，下一次普通部署就会带上。注意：改了 PostgreSQL、Valkey、Meilisearch 或 nginx 在 `compose.prod.yml` 里的定义，部署时它们会重建，有几秒中断；应用本身的改动始终是蓝绿切换。

登录服务器：`ssh deploy@<IP>`（只能用 `SSH_PUBLIC_KEYS` 里的密钥，不能用密码，不能用 root）。

```sh
sudo beeps-deploy status                # 哪个颜色在接收流量，各自运行哪个镜像
sudo beeps-deploy rollback              # 切回上一个版本（同样不中断；不回滚数据库迁移）
sudo beeps-deploy run search rebuild    # 用当前版本运行一次性命令（search 命令自动带上 master key）
cd /srv/beeps && sudo docker compose -f compose.prod.yml --env-file release.env logs -f app-blue nginx
sudo systemctl start beeps-backup       # 立即备份一次
# 总耗时最多的 20 条语句（pg_stat_statements）
cd /srv/beeps && sudo docker compose -f compose.prod.yml --env-file release.env exec postgres psql -U beeps -d beeps \
    -c 'SELECT calls, round(total_exec_time) AS total_ms, round(mean_exec_time::numeric, 2) AS mean_ms, left(query, 80) AS query FROM pg_stat_statements ORDER BY total_exec_time DESC LIMIT 20'
```

**备份**：每天 UTC 3:30 左右 `pg_dump` 到 `/srv/beeps/backups`，保留 7 天；配置了 `BACKUP_*` 时同时上传到备份 bucket 的 `postgres/` 下。没有 Vultr 的整机备份，不配 `BACKUP_*` 时唯一的备份和数据库在同一台机器上，服务器坏了就一起没了。恢复（`backups/` 只有 root 能读，所以用 `sudo cat` 读出；Valkey 里缓存的会话、账号状态和待写回的计数属于恢复前的数据，要一并清空）：

```sh
cd /srv/beeps
compose="sudo docker compose -f compose.prod.yml --env-file release.env"
$compose stop app-blue app-green
sudo cat backups/beeps-<时间>.dump | $compose exec -T postgres pg_restore -U beeps -d beeps --clean --if-exists
$compose exec valkey valkey-cli FLUSHDB
$compose start "app-$(sudo cat state/active-colour)"
sudo beeps-deploy run search rebuild
```

**更换密钥**：改 GitHub 里的 secret 后运行一次 Deploy。`POSTGRES_PASSWORD` 会同步到数据库；换了 `MEILI_MASTER_KEY` 后旧的应用 key 失效，部署时自动创建新 key。新的值在部署开始时就生效，到切换到新版本之前（一两分钟）旧版本还用着旧的：换数据库密码时，旧版本新开的数据库连接会失败（已经开着的不受影响）；换 master key 时，旧版本的搜索会失败。所以挑访问少的时候换。Origin CA 证书换了 nginx 会重新加载。`DATA_PROTECTION_CERTIFICATE` 不要随意更换（见上文）。

**删除违规图片后**：图片带一年的 `immutable` 缓存头，R2 删除后 Cloudflare 边缘可能还在提供，要在 Cloudflare 后台按地址清除缓存。

## 不经 GitHub 手动运行

需要 Terraform 1.10+ 和 Ansible（`pipx install --include-deps ansible==14.5.0`），把上面的 secrets 和 variables 以同名环境变量导出：

```sh
cd deploy/terraform
cp backend.hcl.example backend.hcl   # 填 bucket 和 endpoint
AWS_ACCESS_KEY_ID=… AWS_SECRET_ACCESS_KEY=… terraform init -backend-config=backend.hcl
VULTR_API_KEY=… TF_VAR_ssh_public_keys="$(cat ~/.ssh/id_ed25519.pub beeps-deploy.pub)" terraform plan

cd ../ansible
export DEPLOY_HOST=$(terraform -chdir=../terraform output -raw ipv4_address)
ansible-playbook site.yml                                    # 准备服务器，不发布
BEEPS_IMAGE=ghcr.io/<owner>/beeps@sha256:… GHCR_USER=… GHCR_TOKEN=… ansible-playbook deploy.yml
```

## 性能调优

都在 Ansible 里，`site.yml` 应用（`roles/base/files/sysctl.conf` 有逐项说明）：

- **网络**：BBR + fq；更大的连接队列（`somaxconn` 65535、`tcp_max_syn_backlog` 8192、`netdev_max_backlog`）；最大 16 MB 的套接字缓冲；`tcp_notsent_lowat`（HTTP/2 优先级更准）；空闲后不回到慢启动（Cloudflare 的长连接）；TCP Fast Open；MTU 探测；`tcp_tw_reuse`、更宽的本地端口范围、更短的 FIN 超时；5 分钟起的 keepalive；conntrack 表 262144。大多数 `net.*` 按网络命名空间生效：管到用宿主机网络的 nginx（及它到应用的连接），管不到容器内部，所以 PostgreSQL 自己设 keepalive。`default_qdisc` 只对之后建立的队列生效，Ansible 应用时顺便把网卡换成 fq，不必重启。
- **内核和内存**：2 GB swap 文件，`swappiness` 10；`overcommit_memory=1`（Valkey 重写 AOF 要 fork）；更早、更小批量的脏页回写（PostgreSQL checkpoint 不卡）；透明大页改为 `madvise`（Valkey、PostgreSQL 都建议）；每进程文件句柄上限 1048576（系统总量 systemd 已调到最大，不再设）。
- **nginx**：`reuseport`、每 worker 8192 连接、`multi_accept`；到应用的 upstream keepalive；对 Cloudflare 的 keepalive 905 秒（长于 Cloudflare 的 900 秒，避免 520）；TLS 会话缓存；压缩 JSON / JS / CSS（SSE 不压缩，不缓冲）；代理缓冲 16 KB（默认 4 KB，登录时的 cookie 加跳转响应头超了会 502），多数页面不落临时文件。
- **PostgreSQL**（`compose.prod.yml`，逐项有说明）：
  - 内存：`shared_buffers` 256 MB；`effective_cache_size` 512 MB（数据文件的页缓存也算在容器 768 MB 上限里，能用作缓存的约一半）；`work_mem` 4 MB；`maintenance_work_mem` 64 MB。
  - 连接：`max_connections` 60，应用每个颜色连接池 25（`Maximum Pool Size`），部署时两个颜色重叠也够；每个后端最多缓存 200 条预备语句，连接越多内存越多。`idle_in_transaction_session_timeout` 5 分钟；服务端 TCP keepalive 5 分钟。
  - 规划和并行：`random_page_cost` 1.1、`effective_io_concurrency` 200（本地 NVMe）；`jit=off`；每个查询最多 1 个并行 worker（2 个 vCPU 还要跑应用）。
  - WAL：`wal_compression=lz4`、TOAST 也用 lz4；`checkpoint_timeout` 15 分钟、`max_wal_size` 1 GB。
  - autovacuum：2 个 worker；表变动 5% 就 vacuum / analyze（默认 20% / 10%）。
  - 观测：`pg_stat_statements`（Ansible 建扩展）、`track_io_timing`；日志记 500 ms 以上的语句、锁等待、溢出到磁盘的排序，不记参数（可能含邮箱和 token）。
- **内存上限**：PostgreSQL 768 MB、每个应用颜色 640 MB、Meilisearch 512 MB、Valkey 256 MB（`maxmemory` 128 MB，`volatile-lru`）、nginx 128 MB；部署时两个颜色短暂同时运行，swap 兜底。
- **Valkey**：只用 AOF（每秒刷盘，存着还没写回 PostgreSQL 的计数和实时事件流），关掉默认的 RDB 快照，少 fork，免得写时复制的内存碰到容器上限；惰性释放（Valkey 9 默认已开）。
- **Meilisearch**：索引用 1 个线程、256 MB；日志级别 WARN（INFO 会逐条记录每次搜索、健康检查和应用对每个写入任务的轮询）。
- **健康检查**：PostgreSQL、Valkey、Meilisearch 启动时每秒检查一次（`up --wait` 就绪即返回），之后 30 秒一次。
- **备份**：`pg_dump` 用 zstd 压缩，比默认的 gzip 快一倍，文件略小。
- **应用**：镜像用 ReadyToRun 预编译（`Dockerfile`），新颜色启动时不用和正在服务的颜色抢两个线程做 JIT，热点方法仍按运行数据重新编译。
- **Docker**：`local` 日志驱动（压缩、按大小轮转）、`live-restore`（重启 Docker 不停容器）、容器的文件句柄上限。应用容器带 `init`，进程崩溃时能正常退出。
