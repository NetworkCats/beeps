# Local development: `make setup` once on a fresh checkout, then `make dev`. `make help` lists every target.

COMPOSE := docker compose -f compose.dev.yml
PROJECT := src/Beeps
HOSTS := beeps.local media.beeps.local
CERT := dev/certs/beeps.local.pem
CERT_KEY := dev/certs/beeps.local-key.pem

.DEFAULT_GOAL := help
.PHONY: help setup install certs hosts up down logs dev watch-client watch-server build migrate migration search-rebuild flush-cache check

help: ## List the targets
	@awk 'BEGIN { FS = ":.*## " } /^[a-z-]+:.*## / { printf "  %-16s %s\n", $$1, $$2 }' $(MAKEFILE_LIST)

setup: install certs hosts up build migrate search-rebuild ## Everything a fresh checkout needs, once

install: ## Install npm packages and the dotnet tools
	npm ci
	dotnet tool restore

# Kestrel serves https://beeps.local and SeaweedFS https://media.beeps.local:8443 with this one certificate.
# dotnet watch refreshes the browser over its own wss://localhost, signed with the .NET development certificate.
certs: ## Trust the mkcert CA and the .NET dev certificate, and issue the local certificate (asks for passwords once)
	@command -v mkcert >/dev/null || { echo "mkcert is missing: brew install mkcert"; exit 1; }
	mkcert -install
	@test -f $(CERT) -a -f $(CERT_KEY) || mkcert -cert-file $(CERT) -key-file $(CERT_KEY) $(HOSTS)
	dotnet dev-certs https --trust

# The ::1 line matters: without it macOS asks mDNS for a .local name's IPv6 address and waits seconds for no answer.
hosts: ## Point beeps.local and media.beeps.local at this machine (asks for sudo)
	@if grep -Eq '^127\.0\.0\.1[[:space:]]+$(HOSTS)$$' /etc/hosts; then \
		echo "/etc/hosts already has $(HOSTS)"; \
	else \
		printf '127.0.0.1 %s\n::1 %s\n' "$(HOSTS)" "$(HOSTS)" | sudo tee -a /etc/hosts >/dev/null; \
	fi

# --wait counts a one-off container that finished as a failure, so the bucket is created after the wait.
up: ## Start PostgreSQL, Valkey, Meilisearch, SeaweedFS and MailHog
	@test -f $(CERT) || { echo "No certificate yet: run make certs"; exit 1; }
	$(COMPOSE) up -d --wait postgres valkey meilisearch seaweedfs mailhog
	$(COMPOSE) run --rm --quiet-pull seaweedfs-init

down: ## Stop the services (data is kept)
	$(COMPOSE) down

logs: ## Follow the services' logs
	$(COMPOSE) logs -f

dev: up ## Run the app at https://beeps.local, rebuilding client assets and the app on change
	@$(MAKE) --no-print-directory -j2 watch-client watch-server

watch-client:
	npm run watch

watch-server:
	dotnet watch --project $(PROJECT)

build: ## Build the client assets, then the app (it fingerprints them)
	npm run build
	dotnet build

migrate: ## Apply the database migrations
	dotnet ef database update --project $(PROJECT)

migration: ## Add a migration: make migration NAME=AddSomething
	@test -n "$(NAME)" || { echo "Usage: make migration NAME=AddSomething"; exit 1; }
	dotnet ef migrations add $(NAME) --project $(PROJECT) --output-dir Infrastructure/Data/Migrations

search-rebuild: ## Rebuild the Meilisearch indexes from PostgreSQL
	dotnet run --project $(PROJECT) -- search rebuild

flush-cache: ## Empty the local Valkey (after changing the database by hand; restart the app too)
	$(COMPOSE) exec valkey valkey-cli FLUSHDB

check: ## Everything CI runs: client build and tests, then dotnet build and tests
	npm run build
	npm test
	dotnet build
	dotnet test
