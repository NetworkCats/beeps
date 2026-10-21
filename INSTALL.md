# Deploying beeps

> [!NOTE]
> This is how [beeps.sh](https://beeps.sh) itself runs in production. The workflows, Terraform and Ansible are written for that one setup (Vultr, Cloudflare, Resend, OpenRouter), not as a general way to host beeps. To run your own copy, adapt them to your own providers and domain.

beeps runs on one Linux server, with every service in a Docker container. GitHub Actions does all the work: you set up a few accounts, put their keys into a GitHub environment, and push a version tag.

- **Deploy** (`.github/workflows/deploy.yml`) runs for every `v*` tag on a commit in `main`. It runs CI, builds the image and pushes it to GHCR, then deploys it with Ansible, blue-green and without downtime. The first tag also creates the server with Terraform and sets it up.
- **Infrastructure** (`.github/workflows/infrastructure.yml`), run by hand, changes the server itself (its plan, its firewall) with Terraform: `plan` first, then `apply`.
- **CI** (`.github/workflows/ci.yml`) runs on every push to `main` and on pull requests. It never deploys.

The server is a Vultr instance running Debian 13. Cloudflare sits in front of it and provides DNS, TLS, object storage (R2) and Turnstile. You set up Cloudflare by hand.

The examples below use `beeps.sh`. Replace it with your own domain.

## 1. Accounts

### Cloudflare

1. Add your domain. Under SSL/TLS, set the mode to **Full (strict)**, and turn on **Always Use HTTPS**, **HSTS** and a minimum TLS version of 1.2.
2. Under SSL/TLS → Origin Server, create an **Origin CA certificate** for `beeps.sh` and `*.beeps.sh`. Keep the certificate and its private key (both PEM).
3. In **R2**, create:
   - a bucket for Terraform's state (e.g. `beeps-terraform`), and an API token that can read and write only that bucket;
   - a bucket for images (e.g. `beeps-media`), connected to the custom domain `media.beeps.sh`, and an API token that can read and write only that bucket;
   - optionally, a bucket for backups (e.g. `beeps-backups`) with a lifecycle rule that deletes objects after 30 days, and its own token.
4. Under Rules → Transform Rules, add the response header `X-Content-Type-Options: nosniff` for `media.beeps.sh`. R2 cannot set it per object.
5. Create a **Turnstile** widget for `beeps.sh`.

You add the DNS record after the first deploy, once the server exists.

### Other services

- **Vultr**: create an API key under Account → API. If you limit it by IP, allow any IPv4 address, since GitHub's runners have no fixed addresses.
- **Resend**: verify the domain you send email from (SPF and DKIM), and create an API key.
- **OpenRouter**: create an API key for AI moderation, and set a spending limit.
- **Google and GitHub sign-in** (optional): create OAuth apps with the callbacks `https://beeps.sh/oauth/google/callback` and `https://beeps.sh/oauth/github/callback`.
- **Sentry** (optional): create an ASP.NET Core project and copy its DSN.

## 2. Generate keys

```sh
# The SSH key GitHub Actions logs in with
ssh-keygen -t ed25519 -N '' -C github-actions -f beeps-deploy

# The database password and the Meilisearch master key
openssl rand -hex 32
openssl rand -hex 32

# The certificate that encrypts the app's Data Protection keys, and its password
openssl req -x509 -newkey rsa:3072 -nodes -days 36500 -subj /CN=beeps-dataprotection -keyout dp.key -out dp.pem
openssl pkcs12 -export -inkey dp.key -in dp.pem -out dataprotection.pfx -passout pass:"$(openssl rand -hex 24 | tee dp.password)"
base64 < dataprotection.pfx | tr -d '\n'
```

Keep a copy of `dataprotection.pfx` and its password somewhere safe. Without them the app cannot read its stored Data Protection keys: everyone is signed out, and open forms stop working.

## 3. The GitHub environment

In the repository, go to Settings → Environments and create an environment named **`production`**. You can add required reviewers there.

Under **Deployment branches and tags**, choose *Selected branches and tags* and add the branch `main` (for Infrastructure) and the tag pattern `v*` (for Deploy). Then no other branch can reach these secrets, even with a changed workflow. In the repository's rules, also protect `main` and limit who can create `v*` tags, because pushing a tag deploys.

The logs of a public repository are public. They show variables as they are and mask secrets as `***`. So **every key and every ID is a secret**, even one that is public anyway, like the Turnstile site key or an OAuth client ID. Variables hold only plain settings like domains and bucket names. When in doubt, make it a secret.

### Secrets

| Name | Value | Required |
|---|---|---|
| `VULTR_API_KEY` | Vultr API key | yes |
| `CLOUDFLARE_ACCOUNT_ID` | Cloudflare account ID (32 hex characters, shown on the R2 page) | yes |
| `TF_STATE_BUCKET` | Name of the Terraform state bucket | yes |
| `TF_STATE_ACCESS_KEY_ID`, `TF_STATE_SECRET_ACCESS_KEY` | R2 token for the Terraform state bucket | yes |
| `DEPLOY_SSH_KEY` | The private key `beeps-deploy` | yes |
| `SSH_PUBLIC_KEYS` | `beeps-deploy.pub` and your own public key, one per line | yes |
| `ORIGIN_CERTIFICATE`, `ORIGIN_PRIVATE_KEY` | Cloudflare Origin CA certificate and private key (PEM) | yes |
| `DATA_PROTECTION_CERTIFICATE`, `DATA_PROTECTION_CERTIFICATE_PASSWORD` | The base64 of `dataprotection.pfx`, and the contents of `dp.password` | yes |
| `POSTGRES_PASSWORD`, `MEILI_MASTER_KEY` | The two random values (at least 16 characters, without `;` or `'`) | yes |
| `STORAGE_ACCESS_KEY_ID`, `STORAGE_SECRET_ACCESS_KEY` | R2 token for the image bucket | yes |
| `RESEND_API_TOKEN` | Resend API key | yes |
| `TURNSTILE_SITE_KEY`, `TURNSTILE_SECRET_KEY` | Turnstile site key and secret key | yes |
| `OPENROUTER_API_KEY` | OpenRouter API key | while moderation is on |
| `OAUTH_GOOGLE_CLIENT_ID`, `OAUTH_GOOGLE_CLIENT_SECRET` | Google OAuth app | no |
| `OAUTH_GITHUB_CLIENT_ID`, `OAUTH_GITHUB_CLIENT_SECRET` | GitHub OAuth app | no |
| `SENTRY_DSN` | Sentry DSN | no |
| `BACKUP_BUCKET` | Name of the backup bucket, in the same Cloudflare account | no |
| `BACKUP_ACCESS_KEY_ID`, `BACKUP_SECRET_ACCESS_KEY` | R2 token for the backup bucket | no |
| `APP_SETTINGS` | Any other app settings, one `KEY=VALUE` per line (see `.env.example`) | no |

`SSH_PUBLIC_KEYS` is a secret too: anyone holding a public key can ask a server whether it accepts it, and so find the origin address that Cloudflare hides.

### Variables

| Name | Example | Required |
|---|---|---|
| `SITE_BASE_URL` | `https://beeps.sh` | yes |
| `MEDIA_PUBLIC_BASE_URL` | `https://media.beeps.sh` | yes |
| `STORAGE_BUCKET` | `beeps-media` | yes |
| `SERVER_NAME` | `beeps.sh` (default) | no |
| `EMAIL_FROM` | `beeps <noreply@beeps.sh>` | no |
| `MODERATION_ENABLED` | `true` (default) or `false` | no |
| `VULTR_REGION` | `lax` (default). Only used when the server is created | no |

`.env.example` shows the app setting each value becomes, and `deploy/ansible/group_vars/all.yml` maps them. No value may contain a single quote or a line break, except the PEM certificates. Ansible checks all of this before it changes anything.

## 4. The first deploy

Tag a commit on `main` and push the tag:

```sh
git tag v0.1.0 && git push origin v0.1.0
```

With no other `v*` tag in the repository, Deploy treats this as the first release. It creates the server with Terraform, sets it up (system updates, Docker, the firewall, PostgreSQL, Valkey, Meilisearch, nginx), runs the migrations, builds the search indexes and starts the app. If it fails, fix the cause and re-run the workflow. Every step is safe to repeat.

Then:

1. In the Vultr dashboard, find the IPv4 address of the instance `beeps-production`. In Cloudflare DNS, add `A beeps.sh → <that address>` with the proxy **on** (orange cloud). Do not add an AAAA record, because the server has no IPv6. Do not add any record that points to this address without the proxy.
2. Open your site. **The first account to sign up becomes the admin.** Close open registration in the admin area if you want to.

## 5. Releases

Every new version tag on `main` deploys:

```sh
git tag v1.2.3 && git push origin v1.2.3
```

A tag on a commit that is not in `main` is refused. Deploys run one at a time, and a newer tag waits for the one under way. To deploy an older version again, run Deploy by hand from that tag (Actions → Deploy → Run workflow). This does not undo later database migrations.

A changed secret or variable takes effect on the next deploy. Changes to `compose.prod.yml` and the nginx configuration also go out with the next deploy. After you change the server's own setup (`deploy/ansible/roles/base`, `docker` or `firewall`), run Deploy by hand on the latest tag with **provision** checked.

To change the server itself, run **Infrastructure** with `plan`, read the plan, then run it with `apply`. The region and the hostname are fixed when the server is created. Terraform refuses to destroy the server, so changing either one means setting up a new server and moving the data.

## 6. On the server

Log in with a key from `SSH_PUBLIC_KEYS`: `ssh deploy@<address>`.

```sh
sudo beeps-deploy status               # which colour serves traffic, and which image each runs
sudo beeps-deploy rollback             # switch back to the previous version (database migrations stay)
sudo beeps-deploy run search rebuild   # run a one-off command with the current version
sudo systemctl start beeps-backup      # back up now
```

**Backups**: every night at about 03:30 UTC, `pg_dump` writes to `/srv/beeps/backups` and keeps 7 days. With the `BACKUP_*` secrets set, each dump is also copied to the backup bucket under `postgres/`. Without them, the only backup lives on the same server as the database. To restore one:

```sh
cd /srv/beeps
compose="sudo docker compose -f compose.prod.yml --env-file release.env"
$compose stop app-blue app-green
sudo cat backups/beeps-<time>.dump | $compose exec -T postgres pg_restore -U beeps -d beeps --clean --if-exists
$compose exec valkey valkey-cli FLUSHDB
$compose start "app-$(sudo cat state/active-colour)"
sudo beeps-deploy run search rebuild
```

**Removed images**: images are cached for a year. After you delete one from R2, purge its URL in Cloudflare.
