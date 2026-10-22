# 部署 beeps

[English](INSTALL.md) | 简体中文

> [!NOTE]
> 这是 [beeps.sh](https://beeps.sh) 自己的生产环境的部署方式。workflow、Terraform 和 Ansible 都只为这一套环境（Vultr、Cloudflare、Resend、OpenRouter）而写，并不是部署 beeps 的通用方案。要运行自己的实例，请按你自己的服务商和域名改写。

beeps 跑在一台 Linux 服务器上，所有服务都是 Docker 容器。具体工作全由 GitHub Actions 完成：准备好几个账号，把它们的密钥填进一个 GitHub 环境，再推送一个版本 tag 即可。

- **Deploy**（`.github/workflows/deploy.yml`）在 `main` 上的提交每打一个 `v*` tag 时运行：先跑 CI，再构建镜像推到 GHCR，最后用 Ansible 蓝绿部署，不中断服务。第一个 tag 还会用 Terraform 创建服务器并完成初始化。
- **Infrastructure**（`.github/workflows/infrastructure.yml`）手动运行，用 Terraform 修改服务器本身（套餐、防火墙）：先 `plan`，再 `apply`。
- **CI**（`.github/workflows/ci.yml`）在每次推送到 `main` 和每个 pull request 上运行，从不部署。

服务器是一台运行 Debian 13 的 Vultr 实例。Cloudflare 在它前面，提供 DNS、TLS、对象存储（R2）和 Turnstile，需要在后台手动配置。

下文的例子都用 `beeps.sh`，请换成你自己的域名。

## 1. 账号

### Cloudflare

1. 添加域名。在 SSL/TLS 里把模式设为 **Full (strict)**，打开 **Always Use HTTPS**、**HSTS**，最低 TLS 版本设为 1.2。
2. 在 SSL/TLS → Origin Server 为 `beeps.sh` 和 `*.beeps.sh` 创建 **Origin CA 证书**，保存证书和私钥（都是 PEM）。
3. 在 **R2** 里创建：
   - 存放 Terraform state 的 bucket（如 `beeps-terraform`），以及一个只能读写这个 bucket 的 API token；
   - 存放图片的 bucket（如 `beeps-media`），绑定自定义域名 `media.beeps.sh`，以及一个只能读写这个 bucket 的 API token；
   - 可选：存放备份的 bucket（如 `beeps-backups`），加一条 30 天后删除对象的生命周期规则，以及它自己的 token。
4. 在 Rules → Transform Rules 里给 `media.beeps.sh` 的响应加上 `X-Content-Type-Options: nosniff`。R2 无法按对象设置这个头。
5. 为 `beeps.sh` 创建一个 **Turnstile** widget。

DNS 记录等第一次部署完成、服务器建好之后再加。

### 其他服务

- **Vultr**：在 Account → API 创建 API key。如果按 IP 限制了访问，要允许所有 IPv4 地址，因为 GitHub 的 runner 没有固定地址。
- **Resend**：验证发信域名（SPF 和 DKIM），创建 API key。
- **OpenRouter**：为 AI 审核和翻译创建 API key，并设置用量上限。
- **Google 和 GitHub 登录**（可选）：创建 OAuth 应用，回调地址为 `https://beeps.sh/oauth/google/callback` 和 `https://beeps.sh/oauth/github/callback`。
- **Sentry**（可选）：创建一个 ASP.NET Core 项目，复制它的 DSN。

## 2. 生成密钥

```sh
# GitHub Actions 登录服务器用的 SSH 密钥
ssh-keygen -t ed25519 -N '' -C github-actions -f beeps-deploy

# 数据库密码和 Meilisearch master key
openssl rand -hex 32
openssl rand -hex 32

# 加密应用 Data Protection 密钥的证书，以及它的密码
openssl req -x509 -newkey rsa:3072 -nodes -days 36500 -subj /CN=beeps-dataprotection -keyout dp.key -out dp.pem
openssl pkcs12 -export -inkey dp.key -in dp.pem -out dataprotection.pfx -passout pass:"$(openssl rand -hex 24 | tee dp.password)"
base64 < dataprotection.pfx | tr -d '\n'
```

把 `dataprotection.pfx` 和它的密码另外妥善保存一份。丢了它们，应用就读不出已保存的 Data Protection 密钥：所有人都会被登出，已打开的表单也会失效。

## 3. GitHub 环境

在仓库的 Settings → Environments 里创建一个名为 **`production`** 的环境，可以在这里设置审批人。

在 **Deployment branches and tags** 里选择 *Selected branches and tags*，添加分支 `main`（供 Infrastructure 使用）和 tag 规则 `v*`（供 Deploy 使用）。这样其他分支即使改了 workflow 也拿不到这些 secrets。另外在仓库的 Rules 里保护 `main`，并限制谁能创建 `v*` tag，因为推送一个 tag 就会部署。

公开仓库的日志是公开的：variables 原样显示，secrets 显示为 `***`。所以**所有 key 和 ID 都放进 secrets**，即使它本来就是公开的，比如 Turnstile site key 或 OAuth client ID。variables 只放域名、bucket 名这类普通设置。拿不准时就放 secrets。

### Secrets

| 名称 | 内容 | 必需 |
|---|---|---|
| `VULTR_API_KEY` | Vultr API key | 是 |
| `CLOUDFLARE_ACCOUNT_ID` | Cloudflare 账户 ID（32 位十六进制，R2 页面上可以看到） | 是 |
| `TF_STATE_BUCKET` | Terraform state bucket 的名称 | 是 |
| `TF_STATE_ACCESS_KEY_ID`、`TF_STATE_SECRET_ACCESS_KEY` | Terraform state bucket 的 R2 token | 是 |
| `DEPLOY_SSH_KEY` | 私钥 `beeps-deploy` | 是 |
| `SSH_PUBLIC_KEYS` | `beeps-deploy.pub` 和你自己的公钥，每行一个 | 是 |
| `ORIGIN_CERTIFICATE`、`ORIGIN_PRIVATE_KEY` | Cloudflare Origin CA 证书和私钥（PEM） | 是 |
| `DATA_PROTECTION_CERTIFICATE`、`DATA_PROTECTION_CERTIFICATE_PASSWORD` | `dataprotection.pfx` 的 base64，以及 `dp.password` 的内容 | 是 |
| `POSTGRES_PASSWORD`、`MEILI_MASTER_KEY` | 上面生成的两个随机值（至少 16 位，不含 `;` 和 `'`） | 是 |
| `STORAGE_ACCESS_KEY_ID`、`STORAGE_SECRET_ACCESS_KEY` | 图片 bucket 的 R2 token | 是 |
| `RESEND_API_TOKEN` | Resend API key | 是 |
| `TURNSTILE_SITE_KEY`、`TURNSTILE_SECRET_KEY` | Turnstile 的 site key 和 secret key | 是 |
| `OPENROUTER_API_KEY` | OpenRouter API key | 开启审核或翻译时 |
| `OAUTH_GOOGLE_CLIENT_ID`、`OAUTH_GOOGLE_CLIENT_SECRET` | Google OAuth 应用 | 否 |
| `OAUTH_GITHUB_CLIENT_ID`、`OAUTH_GITHUB_CLIENT_SECRET` | GitHub OAuth 应用 | 否 |
| `SENTRY_DSN` | Sentry DSN | 否 |
| `BACKUP_BUCKET` | 备份 bucket 的名称，与图片在同一个 Cloudflare 账户 | 否 |
| `BACKUP_ACCESS_KEY_ID`、`BACKUP_SECRET_ACCESS_KEY` | 备份 bucket 的 R2 token | 否 |
| `APP_SETTINGS` | 其他应用配置，每行一个 `KEY=VALUE`（见 `.env.example`） | 否 |

`SSH_PUBLIC_KEYS` 也放在 secrets 里：拿着一把公钥就能问一台服务器接不接受它，从而找到被 Cloudflare 隐藏的源站地址。

### Variables

| 名称 | 例子 | 必需 |
|---|---|---|
| `SITE_BASE_URL` | `https://beeps.sh` | 是 |
| `MEDIA_PUBLIC_BASE_URL` | `https://media.beeps.sh` | 是 |
| `STORAGE_BUCKET` | `beeps-media` | 是 |
| `SERVER_NAME` | `beeps.sh`（默认） | 否 |
| `EMAIL_FROM` | `beeps <noreply@beeps.sh>` | 否 |
| `MODERATION_ENABLED` | `true`（默认）或 `false` | 否 |
| `TRANSLATION_ENABLED` | `true`（默认）或 `false` | 否 |
| `VULTR_REGION` | `lax`（默认）。只在创建服务器时生效 | 否 |

每个值对应哪项应用配置见 `.env.example`，映射关系在 `deploy/ansible/group_vars/all.yml`。除 PEM 证书外，任何值都不能含单引号或换行。Ansible 在做任何改动之前会先检查这些。

## 4. 第一次部署

给 `main` 上的一个提交打 tag 并推送：

```sh
git tag v0.1.0 && git push origin v0.1.0
```

仓库里没有别的 `v*` tag 时，Deploy 会把这次当作第一次发布：用 Terraform 创建服务器，完成初始化（系统升级、Docker、防火墙、PostgreSQL、Valkey、Meilisearch、nginx），执行数据库迁移，建立搜索索引，然后启动应用。如果失败了，修好原因后重新运行 workflow 即可，每一步都可以安全地重复执行。

然后：

1. 在 Vultr 后台找到实例 `beeps-production` 的 IPv4 地址。在 Cloudflare DNS 里添加 `A beeps.sh → <这个地址>`，并**开启**代理（橙色云朵）。不要加 AAAA 记录，因为服务器没有 IPv6；也不要加任何不经代理、指向这个地址的记录。
2. 打开你的站点。**第一个注册的账号自动成为管理员。** 需要的话，在管理后台关闭开放注册。

## 5. 发布

之后 `main` 上的每个新版本 tag 都会部署：

```sh
git tag v1.2.3 && git push origin v1.2.3
```

打在不在 `main` 上的提交上的 tag 会被拒绝。部署一次只运行一个，新的 tag 会等进行中的部署完成。要重新部署旧版本，在那个 tag 上手动运行 Deploy（Actions → Deploy → Run workflow）。这不会撤销之后的数据库迁移。

改过的 secret 或 variable 在下一次部署时生效。对 `compose.prod.yml` 和 nginx 配置的改动也随下一次部署上线。改了服务器自身的配置（`deploy/ansible/roles/base`、`docker` 或 `firewall`）之后，在最新的 tag 上手动运行 Deploy，并勾选 **provision**。

要修改服务器本身，先用 `plan` 运行 **Infrastructure**，看过计划后再用 `apply` 运行。地区和主机名在创建服务器时就固定了。Terraform 不允许销毁这台服务器，所以要改其中任何一个，都得另建一台服务器并迁移数据。

## 6. 服务器上

用 `SSH_PUBLIC_KEYS` 里的密钥登录：`ssh deploy@<地址>`。

```sh
sudo beeps-deploy status               # 哪个颜色在接收流量，各自运行哪个镜像
sudo beeps-deploy rollback             # 切回上一个版本（数据库迁移不回滚）
sudo beeps-deploy run search rebuild   # 用当前版本运行一次性命令
sudo systemctl start beeps-backup      # 立即备份一次
```

**备份**：每晚 UTC 3:30 左右，`pg_dump` 写入 `/srv/beeps/backups`，保留 7 天。设置了 `BACKUP_*` secrets 时，每份备份还会复制到备份 bucket 的 `postgres/` 下；没有设置时，唯一的备份和数据库在同一台服务器上。恢复方法：

```sh
cd /srv/beeps
compose="sudo docker compose -f compose.prod.yml --env-file release.env"
$compose stop app-blue app-green
sudo cat backups/beeps-<时间>.dump | $compose exec -T postgres pg_restore -U beeps -d beeps --clean --if-exists
$compose exec valkey valkey-cli FLUSHDB
$compose start "app-$(sudo cat state/active-colour)"
sudo beeps-deploy run search rebuild
```

**删除图片后**：图片会被缓存一年。从 R2 删除之后，要在 Cloudflare 里按地址清除它的缓存。
