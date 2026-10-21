# 上线清单

站长手动完成的步骤（PLAN 9.3）。每一步的做法见 [`deployment.md`](deployment.md)，括号里是对应小节。

## 账号和密钥

- [ ] Cloudflare：`beeps.sh` 接入，SSL/TLS **Full (strict)**、Always Use HTTPS、HSTS、最低 TLS 1.2（1）
- [ ] Cloudflare：签发 Origin CA 证书（`beeps.sh`、`*.beeps.sh`，15 年）（1）
- [ ] R2：Terraform state bucket 和它的 token（1）
- [ ] R2：媒体 bucket，绑定 `media.beeps.sh`，建只能读写它的 token（1）
- [ ] Cloudflare：给 `media.beeps.sh` 的响应加 `X-Content-Type-Options: nosniff`（Transform Rules）（1）
- [ ] R2（可选）：备份 bucket，30 天生命周期规则，只能写它的 token（1）
- [ ] Turnstile：为 `beeps.sh` 建 widget，拿到正式 site key 和 secret（1）
- [ ] Resend：验证发信域名（SPF、DKIM），创建 API key（2）
- [ ] OpenRouter：创建 API key，设置用量上限（2）
- [ ] Google Cloud Console、GitHub OAuth Apps（可选）：回调 `https://beeps.sh/oauth/google/callback`、`https://beeps.sh/oauth/github/callback`（2）
- [ ] Vultr：API key（2）
- [ ] 生成部署用 SSH 密钥、`POSTGRES_PASSWORD`、`MEILI_MASTER_KEY`、Data Protection 证书，另外妥善保存一份（3）
- [ ] GitHub：建 `production` 环境，填全部 secrets 和 variables；部署范围只放行 `main` 和 `v*` tag，保护 `main`，限制谁能建 `v*` tag（4）

## 服务器和第一次部署

- [ ] 推送第一个版本 tag（`v0.1.0`）：自动创建服务器、准备系统、第一次发布（5）
- [ ] Cloudflare DNS：`A beeps.sh → IP`（Vultr 后台的 IPv4），开启代理；不加 AAAA，不加不经代理的记录（5）
- [ ] 打开 https://beeps.sh，站长第一个注册，成为管理员；按需关闭开放注册（5）

## 上线后实测

- [ ] 推送一个新的版本 tag，确认自动部署成功、期间站点可用；在没合并到 `main` 的分支上打 tag，确认部署被拒绝
- [ ] 发一条带图片的 beep：R2 实际上传成功，`media.beeps.sh` 能打开，带 `nosniff`；删除后对象从 R2 消失
- [ ] 删除违规图片后，在 Cloudflare 按地址清除该图片的缓存（图片缓存一年）
- [ ] AI 审核：用真实的 OpenRouter key 发几条，`/admin/review` 和 `moderation_results` 里记录正确
- [ ] 登录状态下页面实时更新（SSE 经 nginx + Cloudflare），放置几分钟不断开
- [ ] 设置 → 登录设备里显示的是自己的真实 IP 和所在地
- [ ] 用真实账号走一遍 Google、GitHub 登录和关联（如果配置了）
- [ ] 通行密钥：iPhone、Android、Mac Touch ID、Windows Hello、1Password 各添加和登录一次
- [ ] 注册和重置密码收到 Resend 发出的邮件
- [ ] `sudo systemctl start beeps-backup`，确认备份写到服务器和备份 bucket
- [ ] `sudo beeps-deploy rollback` 再回滚回来，确认两个颜色都能接收流量
