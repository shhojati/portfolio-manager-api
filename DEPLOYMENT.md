# Deploying Portfolio Manager API on an Ubuntu VPS (migrating from Liara)

This document explains how to run this service on an Ubuntu VPS with Docker, how to move the existing
data off Liara, and how every setting and environment variable works. It is written so that a person
or an AI agent can follow it step by step, and can answer questions like "how do I change the rate
limit?" or "why can't I log in?" from it.

- [1. Quick facts](#1-quick-facts)
- [2. How the service works](#2-how-the-service-works)
- [3. Configuration and environment variables](#3-configuration-and-environment-variables)
- [4. Docker files explained](#4-docker-files-explained)
- [5. Prepare the VPS](#5-prepare-the-vps)
- [6. Deploy the application](#6-deploy-the-application)
- [7. HTTPS reverse proxy](#7-https-reverse-proxy)
- [8. Migrate from Liara (cutover)](#8-migrate-from-liara-cutover)
- [9. Verify the deployment](#9-verify-the-deployment)
- [10. Day-to-day operations](#10-day-to-day-operations)
- [11. VPS located in Iran](#11-vps-located-in-iran)
- [12. Troubleshooting](#12-troubleshooting)
- [13. Rules for agents](#13-rules-for-agents)

---

## 1. Quick facts

| Item | Value |
|---|---|
| Application | ASP.NET Core (.NET 10) web API + static backoffice UI, single process |
| Database | SQLite file `portfolio.db` |
| Container port | `8080` (HTTP only; HTTPS is done by the reverse proxy) |
| Host port | `127.0.0.1:8080` (change with `HOST_PORT`); never exposed publicly |
| Persistent data in container | `/app/data` (database + data protection keys) |
| Persistent data on host | `./data` next to `docker-compose.yml` (recommended location: `/opt/portfolio-manager/data`) |
| Secrets file | `.env` next to `docker-compose.yml` (template: `.env.example`), `chmod 600`, never committed |
| Backoffice UI | `https://<DOMAIN>/` (redirects to `/login.html` when not signed in) |
| API docs | `https://<DOMAIN>/swagger` (if `Swagger__Enabled=true`) |
| Live feed | `wss://<DOMAIN>/ws` (WebSocket) |
| Timezone | `Asia/Tehran` (set in the image; the sync jobs use Tehran market hours) |
| Database migrations | Applied automatically on startup |
| Users | Created on startup from `Auth__Users__*` variables if they don't exist |

Placeholders used in this document:

| Placeholder | Meaning |
|---|---|
| `<DOMAIN>` | Public domain name of the service, e.g. `api.example.com` |
| `<VPS_IP>` | Public IP address of the VPS |
| `<REPO_URL>` | Git URL of this repository |
| `<SSH_USER>` | Non-root user you log in with on the VPS |

---

## 2. How the service works

```
Internet ──HTTPS──▶ Caddy / nginx (ports 80, 443)
                        │  adds X-Forwarded-For / X-Forwarded-Proto
                        ▼
                 127.0.0.1:8080 (host)
                        │
                        ▼
              Docker container "api" (:8080)
                ├─ REST API          /api/...
                ├─ Backoffice UI     /, /login.html (from wwwroot)
                ├─ WebSocket         /ws
                └─ Background jobs   TSETMC, Bitpin, tgju price sync (outbound HTTPS)
                        │
                        ▼
              /app/data  ◀── bind mount ──▶  ./data on the host
                ├─ portfolio.db (+ -wal, -shm)
                └─ keys/  (data protection keys)
```

### Authentication

| Who | How they authenticate | What they can do |
|---|---|---|
| **Admin** (role `Admin`) | Signs in on `/login.html`; gets an HttpOnly, Secure, SameSite=Strict cookie `pm.session` | Everything: backoffice, all API endpoints including writes, user management |
| **API client** (role `ApiClient`) | `POST /api/auth/token` with username/password → bearer access token (1 h) + refresh token (14 days); `POST /api/auth/refresh` renews | Read-only API access; cannot sign in to the backoffice |
| **Anonymous** | Nothing | Only `GET /api/assets/search?q=...` and `GET /api/prices/latest?identifiers=...` (max 100 identifiers) |

Everything else (other `/api/*` endpoints, `/ws`) returns `401` without authentication and `403` for
the wrong role. The WebSocket accepts the cookie, or a token as `?access_token=<token>` (browsers can't
send headers on WebSockets).

Changing or resetting a user's password signs out their other sessions and invalidates their refresh
tokens. Access tokens already issued stay valid until they expire (at most 1 hour).

### Rate limiting

Fixed window, per minute by default. Over the limit the API answers `429 Too Many Requests` with a
`Retry-After` header.

| Budget | Applies to | Keyed by | Default |
|---|---|---|---|
| `ApiClient` | Signed-in API clients | User | 120 / min |
| `Admin` | Signed-in admins (the UI makes several calls per page) | User | 600 / min |
| `Anonymous` | Requests without credentials (public endpoints and rejected requests share it) | Client IP | 30 / min |
| `Auth` | Login, token, refresh, change password (password guessing protection) | Client IP | 10 / min |

Per-IP limits depend on the app seeing the **real client IP**. Behind the reverse proxy that only works
because `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (set in the Dockerfile) makes the app trust the
proxy's `X-Forwarded-For` header. This is safe **only** if the container port is reachable from the
proxy alone, which is why it is bound to `127.0.0.1`. See [section 13](#13-rules-for-agents).

### Data protection keys

ASP.NET Core encrypts the session cookie and API tokens with keys stored in `/app/data/keys`
(`DataProtection__KeysPath`). If that folder is lost or not persisted, every admin is signed out and
every API token/refresh token becomes invalid (clients must request new tokens). Nothing else breaks.
Keep it in backups together with the database.

### Background price sync

| Job | Source (outbound HTTPS) | Schedule (defaults) |
|---|---|---|
| TSETMC (stocks, ETFs, ETF NAVs) | `cdn.tsetmc.com` | Hourly, Sat–Wed 08:45–13:00 Tehran; NAVs until 18:30 |
| Bitpin (crypto) | `api.bitpin.ir` | Every 15 min, 24/7 |
| tgju (currencies, gold, coins, metals) | `call1`–`call4.tgju.org` | Every 15 min |

The VPS must be able to reach these hosts. Some Iranian services block or throttle foreign IPs; check
with `curl` before migrating (see [section 5.6](#56-check-outbound-access-to-the-price-sources)).

---

## 3. Configuration and environment variables

### 3.1 How configuration is loaded

ASP.NET Core merges configuration from these sources; **later sources override earlier ones**:

1. `appsettings.json` (committed; defaults for production)
2. `appsettings.{Environment}.json` (e.g. `appsettings.Development.json`; only loaded when
   `ASPNETCORE_ENVIRONMENT` matches; **production never loads the Development file**)
3. Environment variables (from the Dockerfile `ENV`, then from `.env` via `docker-compose.yml`)
4. Command-line arguments (not used here)

So: defaults live in `appsettings.json`; anything server-specific or secret goes in `.env`.

Inside Docker, variables from `.env` (compose `env_file`) override the image's `ENV` values with the
same name.

### 3.2 Naming rules

- A section path like `Auth:Users:0:Username` in JSON becomes `Auth__Users__0__Username` as an
  environment variable: **two underscores** replace each `:`.
- Arrays use numeric indexes: `Cors__AllowedOrigins__0`, `Cors__AllowedOrigins__1`, ...
- Names are case-insensitive, but keep the casing shown here.
- **Array gotcha:** environment variables override array entries *by index*; they cannot remove
  entries defined in `appsettings.json`. Example: `Tsetmc:TradingDays` has 5 entries in
  `appsettings.json`; setting only `Tsetmc__TradingDays__0=Sunday` changes the first entry and keeps
  the other four. Arrays that are empty in `appsettings.json` (`Auth:Users`, `Cors:AllowedOrigins`)
  don't have this problem.
- Value formats:
  - Durations (`TimeSpan`): `hh:mm:ss` or `d.hh:mm:ss`, e.g. `00:15:00` (15 min), `14.00:00:00` (14 days)
  - Times of day (`TimeOnly`): `HH:mm`, e.g. `08:45`
  - Booleans: `true` / `false`
- In `.env`, write values **without quotes** and without spaces around `=`. Passwords from
  `openssl rand -base64 24` are safe to paste as is.

### 3.3 Variables set by the Docker image (Dockerfile)

You normally don't change these; they are listed so you know they exist. Override in `.env` only if
you know why.

| Variable | Value in image | Purpose |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Loads production behavior: secure cookies, no Development config |
| `ASPNETCORE_HTTP_PORTS` | `8080` | Port the app listens on inside the container |
| `TZ` | `Asia/Tehran` | Container timezone (logs, local times) |
| `ConnectionStrings__Default` | `Data Source=/app/data/portfolio.db` | SQLite database location (on the persistent volume) |
| `DataProtection__KeysPath` | `/app/data/keys` | Where cookie/token encryption keys are stored (on the persistent volume) |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` | Trust `X-Forwarded-For` / `X-Forwarded-Proto` from the reverse proxy (real client IP, HTTPS detection) |

### 3.4 Variables you set in `.env`

**Required on a fresh database** (users are created only if they don't exist yet):

| Variable | Example | Notes |
|---|---|---|
| `Auth__Users__0__Username` | `admin` | Letters, digits, `.`, `_`, `-`; case-insensitive |
| `Auth__Users__0__Password` | *(generated)* | Min. 10 characters. Empty or too short → user is not created, error in log |
| `Auth__Users__0__Role` | `Admin` | `Admin` or `ApiClient` (exact spelling) |
| `Auth__Users__1__Username` | `api-client` | |
| `Auth__Users__1__Password` | *(generated)* | |
| `Auth__Users__1__Role` | `ApiClient` | |

More users: continue with index `2`, `3`, ... Seeding never changes an existing user: changing a
password in `.env` later has **no effect**; change passwords in the backoffice (Users page or the key
icon next to your name). After the first successful start you may delete the password lines from
`.env`. If you migrate the existing Liara database ([section 8](#8-migrate-from-liara-cutover)), the
users already exist and these variables are not needed at all.

**Optional overrides** (defaults come from `appsettings.json`):

| Variable | Default | Purpose |
|---|---|---|
| `HOST_PORT` | `8080` | Host port (on 127.0.0.1) the reverse proxy forwards to. Read by `docker-compose.yml`, not by the app |
| `RateLimiting__Enabled` | `true` | Turn all rate limiting off/on |
| `RateLimiting__Anonymous__PermitLimit` | `30` | Requests per window per IP without credentials |
| `RateLimiting__Anonymous__Window` | `00:01:00` | Window length |
| `RateLimiting__ApiClient__PermitLimit` | `120` | Per API-client user |
| `RateLimiting__Admin__PermitLimit` | `600` | Per admin user |
| `RateLimiting__Auth__PermitLimit` | `10` | Password checks per IP |
| `Auth__SessionLifetime` | `08:00:00` | Admin session length (sliding: renewed while in use) |
| `Auth__AccessTokenLifetime` | `01:00:00` | API access token lifetime |
| `Auth__RefreshTokenLifetime` | `14.00:00:00` | API refresh token lifetime |
| `Cors__AllowedOrigins__0` | *(none)* | Browser apps on **other origins** allowed to call the API, e.g. `https://portfolio.example.com`. Not needed for server-to-server calls or the built-in backoffice |
| `Swagger__Enabled` | `true` | Serve API docs at `/swagger` (docs only; calls still need auth) |
| `AllowedHosts` | `*` | Restrict accepted `Host` headers, e.g. `api.example.com` |
| `Tsetmc__Enabled` / `Bitpin__Enabled` / `Tgju__Enabled` | `true` | Turn a price sync job off |
| `Tsetmc__Interval`, `Bitpin__Interval`, `Tgju__Interval` | `01:00:00`, `00:15:00`, `00:15:00` | Sync frequency |
| `Tsetmc__MarketOpen` / `Tsetmc__MarketClose` | `08:45` / `13:00` | TSETMC active hours (Tehran) |
| `Tsetmc__SyncEtfNav`, `Tsetmc__NavInterval`, `Tsetmc__NavClose`, `Tsetmc__NavConcurrency` | `true`, `01:00:00`, `18:30`, `4` | ETF NAV sync |
| `Tsetmc__RunOnStartup` | `true` | Run one TSETMC sync at startup even outside market hours |
| `Tgju__MaxQuoteAge` | `7.00:00:00` | Ignore tgju quotes older than this |
| `Logging__LogLevel__Default` | `Information` | Log verbosity (`Debug`, `Information`, `Warning`, `Error`) |

After editing `.env`, apply it with `docker compose up -d` (this recreates the container; a plain
`restart` does **not** re-read `.env`).

### 3.5 Example `.env`

```ini
Auth__Users__0__Username=admin
Auth__Users__0__Password=PASTE_GENERATED_PASSWORD_1
Auth__Users__0__Role=Admin

Auth__Users__1__Username=api-client
Auth__Users__1__Password=PASTE_GENERATED_PASSWORD_2
Auth__Users__1__Role=ApiClient

# Optional
#RateLimiting__Anonymous__PermitLimit=60
#Cors__AllowedOrigins__0=https://portfolio.example.com
```

The committed `.env.example` contains the same template with all optional settings commented out.

---

## 4. Docker files explained

| File | Used for | Notes |
|---|---|---|
| `Dockerfile` | Building from source anywhere with internet access | Two stages: `sdk:10.0` restores NuGet packages and publishes; `aspnet:10.0` runs it. Needs `nuget.org` and `mcr.microsoft.com` |
| `Dockerfile.liara` | Runtime-only image from an already published `publish/` folder | Created because Liara's builders can't reach nuget.org. Useful on a VPS that can't reach nuget.org either ([section 11](#11-vps-located-in-iran)) |
| `docker-compose.yml` | Running the container on the VPS | See below |
| `.env.example` | Template for `.env` | Committed, no secrets |
| `.dockerignore` | Keeps files out of the build context | Excludes `.env`, `data/`, `*.db`, `bin/`, `obj/`, `.git/` so secrets and the database never end up in an image |
| `liara.json`, `.github/workflows/deploy-liara.yml` | Liara deployment | Not used on the VPS. Disable the workflow after migrating ([section 8](#8-migrate-from-liara-cutover)) |

### 4.1 `docker-compose.yml`, line by line

```yaml
name: portfolio-manager            # Compose project name → container "portfolio-manager-api-1"

services:
  api:
    build:
      context: .                   # Build from the repository root
      dockerfile: Dockerfile       # Full source build (switch to Dockerfile.liara if nuget.org is blocked)
    image: portfolio-manager:latest  # Tag for built images; also lets you run an image loaded with docker load
    restart: unless-stopped        # Start on boot / after crashes, unless you stopped it manually
    env_file: .env                 # Secrets and overrides (section 3.4)
    ports:
      - "127.0.0.1:${HOST_PORT:-8080}:8080"   # Only reachable from the VPS itself (the reverse proxy)
    volumes:
      - ./data:/app/data           # Database + keys survive rebuilds and container recreation
```

Important details:

- **Port binding to `127.0.0.1`** is a security requirement, not a convenience. Docker publishes
  ports by editing iptables directly, which **bypasses `ufw`**. A mapping like `"8080:8080"` would
  expose the app to the internet even with ufw blocking 8080, and then anyone could fake
  `X-Forwarded-For` to evade per-IP rate limits and could log in over plain HTTP.
- **`./data` is a bind mount.** It is created on first start and owned by root (the container runs
  as root because Liara's disk required it). Use `sudo` to read or back it up.
- **`env_file` vs `${...}`:** variables in `.env` are passed into the container (`env_file`), and Compose
  also reads `.env` to fill `${HOST_PORT}` in the YAML itself.

---

## 5. Prepare the VPS

Tested target: Ubuntu 22.04 / 24.04 LTS, 1 vCPU, 1 GB RAM minimum (2 GB recommended if you build
the image on the VPS), 10 GB disk.

### 5.1 DNS

Create an `A` record `<DOMAIN>` → `<VPS_IP>` (and `AAAA` if the VPS has IPv6). Do this early: the
HTTPS certificate can only be issued once DNS points at the VPS. If you are migrating, lower the TTL
of the existing record to 300 seconds a day before the cutover.

### 5.2 Basic hardening

```bash
sudo apt update && sudo apt upgrade -y
sudo apt install -y unattended-upgrades sqlite3 git curl
sudo dpkg-reconfigure -plow unattended-upgrades
```

Use SSH keys and disable password logins (`PasswordAuthentication no` in `/etc/ssh/sshd_config`,
then `sudo systemctl restart ssh`) once you have confirmed key login works.

### 5.3 Firewall

```bash
sudo ufw allow OpenSSH
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw enable
```

Do **not** open 8080. (Remember: ufw does not protect Docker-published ports; the `127.0.0.1`
binding does.)

### 5.4 Install Docker Engine and the Compose plugin

Official Docker repository (from docs.docker.com):

```bash
sudo apt-get install -y ca-certificates curl
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "${UBUNTU_CODENAME:-$VERSION_CODENAME}") stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
```

Optional, to run `docker` without `sudo` (log out and back in afterwards):

```bash
sudo usermod -aG docker $USER
```

Check:

```bash
docker --version && docker compose version
```

If `download.docker.com` is unreachable (common from Iran), install from Ubuntu's own repository
instead: `sudo apt install -y docker.io docker-compose-v2` (package names on Ubuntu 24.04; older
releases may only have `docker-compose`, the v1 tool, whose command is `docker-compose`).

### 5.5 Log rotation for Docker

Docker keeps container logs forever by default. Limit them in `/etc/docker/daemon.json`:

```json
{
  "log-driver": "json-file",
  "log-opts": { "max-size": "20m", "max-file": "5" }
}
```

```bash
sudo systemctl restart docker
```

### 5.6 Check outbound access to the price sources

```bash
curl -sS -o /dev/null -w "tsetmc %{http_code}\n" -A "Mozilla/5.0" https://cdn.tsetmc.com/api/MarketData/GetMarketOverview/1
curl -sS -o /dev/null -w "bitpin %{http_code}\n" https://api.bitpin.ir/
curl -sS -o /dev/null -w "tgju   %{http_code}\n" https://call1.tgju.org/ajax.json
```

Any `200`–`4xx` answer means the host is reachable; timeouts or connection errors mean that price
source will not sync from this VPS (the API still works, but those prices stop updating).

---

## 6. Deploy the application

### 6.1 Get the code

```bash
sudo mkdir -p /opt/portfolio-manager
sudo chown $USER: /opt/portfolio-manager
git clone <REPO_URL> /opt/portfolio-manager
cd /opt/portfolio-manager
```

### 6.2 Create `.env`

```bash
cp .env.example .env
chmod 600 .env
openssl rand -base64 24   # run twice: one password for admin, one for api-client
nano .env                 # paste the passwords into Auth__Users__0__Password and Auth__Users__1__Password
```

Skip the passwords if you will restore the Liara database (users already exist), but keep the file:
`docker-compose.yml` requires `.env` to exist.

### 6.3 (Migration only) restore the data first

If you are moving from Liara, put the downloaded `portfolio.db` and `keys/` into
`/opt/portfolio-manager/data/` **before** the first start. See [section 8](#8-migrate-from-liara-cutover).

### 6.4 Build and start

```bash
docker compose up -d --build
docker compose logs -f api
```

Expected log lines on first start (fresh database):

- EF Core applying migrations (creates `data/portfolio.db`)
- `Created Admin user admin` and `Created ApiClient user api-client`
- `Now listening on: http://[::]:8080`
- Price sync messages from TSETMC/Bitpin/tgju a little later

Warning to act on: `No admin user exists, so nobody can sign in to the backoffice` → the `Auth__Users__*`
variables are missing or invalid. Fix `.env`, then `docker compose up -d`.

Local check on the VPS (before the proxy exists):

```bash
curl -i http://127.0.0.1:8080/              # expect: 302 Location: login.html
curl -s "http://127.0.0.1:8080/api/assets/search?q=btc" | head -c 300
```

---

## 7. HTTPS reverse proxy

HTTPS is **mandatory**: in production the session cookie has the `Secure` flag, so browsers drop it
on plain HTTP and the admin login appears to "do nothing". Choose one proxy.

### 7.1 Option A: Caddy (recommended, automatic certificates)

```bash
sudo apt install -y caddy
```

(On Ubuntu releases without a `caddy` package, use the official repository from caddyserver.com/docs/install.)

`/etc/caddy/Caddyfile`:

```caddy
<DOMAIN> {
    encode gzip
    reverse_proxy 127.0.0.1:8080
}
```

```bash
sudo systemctl reload caddy
```

Caddy obtains and renews a Let's Encrypt certificate automatically, redirects HTTP to HTTPS, proxies
WebSockets and sets `X-Forwarded-For` / `X-Forwarded-Proto` by default. Nothing else is needed.

### 7.2 Option B: nginx + certbot

```bash
sudo apt install -y nginx certbot python3-certbot-nginx
```

`/etc/nginx/sites-available/portfolio-manager`:

```nginx
map $http_upgrade $connection_upgrade {
    default upgrade;
    ''      close;
}

server {
    listen 80;
    server_name <DOMAIN>;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;

        # Real client information for the app (rate limiting, HTTPS detection)
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # WebSocket (/ws)
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection $connection_upgrade;
        proxy_read_timeout 1h;
    }
}
```

```bash
sudo ln -s /etc/nginx/sites-available/portfolio-manager /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx
sudo certbot --nginx -d <DOMAIN> --redirect
```

Certbot adds the `listen 443 ssl` block and installs a renewal timer.

### 7.3 If you use a CDN in front (e.g. Cloudflare, ArvanCloud)

The proxy then sees the CDN's IP, not the visitor's, and every visitor would share one per-IP rate
limit. Configure the proxy to take the client IP from the CDN's header (Caddy: `trusted_proxies` +
`client_ip_headers`; nginx: `real_ip_header` + `set_real_ip_from` with the CDN's IP ranges), so the
`X-Forwarded-For` it sends to the app contains the real client IP.

---

## 8. Migrate from Liara (cutover)

What must move: the contents of the Liara disk `data` (mounted at `/app/data`):
`portfolio.db`, `portfolio.db-wal`, `portfolio.db-shm` (may not exist) and the `keys/` folder.

- Moving `portfolio.db` keeps all assets, prices **and users** (with their current passwords).
- Moving `keys/` keeps admins signed in and keeps existing API tokens working. Without it, admins
  sign in again and API clients request new tokens; nothing else is affected.

### 8.1 Steps

1. **Prepare the VPS** fully (sections 5–7) and test it with a fresh database if you like. Then
   remove that test data: `docker compose down && sudo rm -rf data`.
2. **Stop the Liara app** so the database stops changing (Liara console → app → turn off / scale to
   zero). Stopping also flushes the SQLite WAL into `portfolio.db`.
3. **Download the disk contents.** Liara offers disk backups and file access for disks in its console;
   check Liara's current documentation for the exact method (download a disk backup, or connect to
   the disk via its file-transfer access). You need every file listed above.
4. **Copy them to the VPS:**
   ```bash
   scp -r ./liara-data/* <SSH_USER>@<VPS_IP>:/tmp/pm-data/
   ```
   On the VPS:
   ```bash
   sudo mkdir -p /opt/portfolio-manager/data
   sudo cp -a /tmp/pm-data/. /opt/portfolio-manager/data/
   sudo sqlite3 /opt/portfolio-manager/data/portfolio.db "PRAGMA integrity_check;"   # expect: ok
   ```
5. **Start the app:** `docker compose up -d --build`, then check the logs (section 6.4). There should be
   no `Created ... user` lines (users exist) and no "No admin user exists" warning.
6. **Switch DNS** to the VPS (if the domain was pointing at Liara) or give API consumers the new URL
   (if they used the `*.liara.run` address).
7. **Verify** (section 9), then **disable Liara deploys**: delete or disable
   `.github/workflows/deploy-liara.yml`. Otherwise every push to `master` still deploys to Liara, and
   a second copy of the service would run with its own, diverging database.
8. Keep the Liara app stopped (not deleted) for a few days as a fallback, then delete it.

### 8.2 Rollback

Start the Liara app again and point DNS back. Data written on the VPS after the cutover would need to
be copied back the same way (stop VPS app, copy `data/` to the Liara disk).

---

## 9. Verify the deployment

Run from your own computer:

```bash
# 1. HTTPS works and the UI redirects to the login page
curl -sI https://<DOMAIN>/ | grep -iE "^HTTP|^location"
# expect: status 302 (HTTP/1.1 or HTTP/2) and location: login.html

# 2. Public endpoints work anonymously
curl -s "https://<DOMAIN>/api/assets/search?q=btc" | head -c 200

# 3. Protected endpoints require authentication
curl -s -o /dev/null -w "%{http_code}\n" https://<DOMAIN>/api/assets
# expect: 401

# 4. API client token flow
curl -s -X POST https://<DOMAIN>/api/auth/token \
  -H "Content-Type: application/json" \
  -d '{"username":"api-client","password":"<API_CLIENT_PASSWORD>"}'
# expect: {"tokenType":"Bearer","accessToken":"...","expiresIn":3600,"refreshToken":"..."}

curl -s -o /dev/null -w "%{http_code}\n" -H "Authorization: Bearer <ACCESS_TOKEN>" https://<DOMAIN>/api/assets/1
# expect: 200

# 5. The container port is NOT reachable from the internet
curl -s -m 5 -o /dev/null -w "%{http_code}\n" http://<VPS_IP>:8080/ || echo "not reachable (good)"
```

In a browser:

1. Open `https://<DOMAIN>/` → login page → sign in as admin → assets list loads.
2. The sidebar shows a green **Live** dot (WebSocket through the proxy works).
3. Open an asset → chart and price table load.

Rate limit sanity check (from one machine; uses up your anonymous budget for a minute):

```bash
for i in $(seq 1 35); do curl -s -o /dev/null -w "%{http_code}\n" "https://<DOMAIN>/api/assets/search?q=a"; done | sort | uniq -c
# expect about 30 x 200 and 5 x 429
```

If *all* requests from different machines hit the limit together, the app is not seeing real client
IPs (section 12).

---

## 10. Day-to-day operations

All commands run in `/opt/portfolio-manager`.

| Task | Command / action |
|---|---|
| Status | `docker compose ps` |
| Logs (follow) | `docker compose logs -f api` |
| Logs (last hour) | `docker compose logs --since 1h api` |
| Restart | `docker compose restart api` (does not re-read `.env`) |
| Apply `.env` changes | `docker compose up -d` |
| Stop / start | `docker compose stop` / `docker compose start` |
| Update to latest code | `git pull && docker compose up -d --build` (back up first; migrations run automatically) |
| Free disk from old images | `docker image prune -f` |
| Change a password | Backoffice → Users → key icon (or the key icon next to your name for your own) |
| Add an API client | Backoffice → Users → New user → role "API client" |
| Change a rate limit | Set e.g. `RateLimiting__Anonymous__PermitLimit=60` in `.env`, then `docker compose up -d` |
| Allow a browser app on another domain | Add `Cors__AllowedOrigins__0=https://app.example.com` to `.env`, then `docker compose up -d` |
| Locked out (no admin can log in) | See below |

### 10.1 Backups

SQLite must not be copied while it is being written. `sqlite3 .backup` makes a consistent copy
while the app runs:

`/usr/local/bin/portfolio-backup.sh`:

```bash
#!/bin/sh
set -e
DATA=/opt/portfolio-manager/data
DEST=/var/backups/portfolio-manager
STAMP=$(date +%F-%H%M)
mkdir -p "$DEST"
sqlite3 "$DATA/portfolio.db" ".backup '$DEST/portfolio-$STAMP.db'"
tar -czf "$DEST/keys-$STAMP.tar.gz" -C "$DATA" keys
find "$DEST" -type f -mtime +14 -delete   # keep two weeks
```

```bash
sudo chmod +x /usr/local/bin/portfolio-backup.sh
echo "30 3 * * * root /usr/local/bin/portfolio-backup.sh" | sudo tee /etc/cron.d/portfolio-backup
```

Copy `/var/backups/portfolio-manager` off the server regularly (another machine or object storage);
a backup on the same disk doesn't survive losing the VPS.

### 10.2 Restore

```bash
cd /opt/portfolio-manager
docker compose stop
sudo rm -f data/portfolio.db-wal data/portfolio.db-shm
sudo cp /var/backups/portfolio-manager/portfolio-<STAMP>.db data/portfolio.db
sudo tar -xzf /var/backups/portfolio-manager/keys-<STAMP>.tar.gz -C data   # optional
docker compose start
```

### 10.3 Locked out of the backoffice

Seeding only creates users that don't exist, so add a **new** admin with a new username:

1. In `.env` add (use an index not used yet, e.g. `2`):
   ```ini
   Auth__Users__2__Username=admin2
   Auth__Users__2__Password=<new generated password>
   Auth__Users__2__Role=Admin
   ```
2. `docker compose up -d`, check the log for `Created Admin user admin2`.
3. Sign in as `admin2`, reset the old admin's password on the Users page (or delete it), then remove
   the lines from `.env`.

---

## 11. VPS located in Iran

Hosting inside Iran may be needed for reliable access to TSETMC, but several build-time services can
be blocked from Iranian IPs: `nuget.org` (package restore), `mcr.microsoft.com` (.NET base images),
`download.docker.com`, and sometimes Docker Hub. Running the built image needs none of them; only
building does. Options, simplest first:

**A. Build the image elsewhere and copy it** (works without any registry access on the VPS):

```bash
# On a machine with normal internet access, in the repository:
docker build -t portfolio-manager:latest .
docker save portfolio-manager:latest | gzip > portfolio-manager.tar.gz
scp portfolio-manager.tar.gz <SSH_USER>@<VPS_IP>:/tmp/

# On the VPS:
gunzip -c /tmp/portfolio-manager.tar.gz | docker load
cd /opt/portfolio-manager && docker compose up -d --no-build
```

The image name `portfolio-manager:latest` matches `image:` in `docker-compose.yml`, so
`--no-build` runs the loaded image. Repeat for every update. (This can be automated in GitHub Actions:
build, `docker save`, `scp` to the VPS, `docker load`, `docker compose up -d --no-build`.)

**B. Publish elsewhere, build the runtime image on the VPS** (needs only `mcr.microsoft.com` for the
`aspnet:10.0` base image, the same approach Liara used):

```bash
# On a machine with .NET 10 SDK:
dotnet publish PortfolioManager.Api.csproj -c Release -r linux-x64 --no-self-contained -o publish -p:UseAppHost=false
rsync -a publish/ <SSH_USER>@<VPS_IP>:/opt/portfolio-manager/publish/
```

Then set `dockerfile: Dockerfile.liara` in `docker-compose.yml` and run `docker compose up -d --build`.

**C. Registry mirrors.** Iranian providers publish Docker Hub mirrors (configured as
`"registry-mirrors"` in `/etc/docker/daemon.json`), but these normally only mirror Docker Hub, not
`mcr.microsoft.com`, so they don't help with the .NET base images. Prefer A.

Also check that Let's Encrypt can validate your domain (the HTTP-01 challenge comes from outside
Iran). If it can't, use a certificate from your provider or a CDN in front ([section 7.3](#73-if-you-use-a-cdn-in-front-eg-cloudflare-arvancloud)).

---

## 12. Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Login form accepts the password but you stay on the login page | Site is served over HTTP, so the `Secure` session cookie is dropped | Serve via HTTPS (section 7); check that the proxy sends `X-Forwarded-Proto` |
| Login says "Invalid username or password" for a user from `.env` | User existed before (seeding doesn't change existing users), or the password in `.env` differs from the one created first | Reset the password in the backoffice, or create a new admin (section 10.3) |
| Log: `No admin user exists...` | `Auth__Users__*` missing, wrong role spelling, or password under 10 characters | Fix `.env`, `docker compose up -d`, check the log for `Not creating user ...` errors |
| Everyone gets `429` at the same time | App sees the proxy/CDN IP instead of the client IP | Check `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (`docker compose exec api printenv | grep FORWARDED`); configure the proxy/CDN real-IP settings (section 7.3) |
| Rate limits can be bypassed | Port 8080 reachable from the internet; clients can fake `X-Forwarded-For` | Bind to `127.0.0.1` only (section 4.1); verify with section 9 step 5 |
| Sidebar shows "Offline — retrying" | WebSocket not proxied | nginx: add the `Upgrade`/`Connection` headers (section 7.2) |
| All admins were logged out and API tokens stopped working after a redeploy | Keys folder not persisted or deleted | Make sure `./data` is mounted and `data/keys` survives; restore keys from backup |
| `dotnet restore` hangs or times out during `docker compose build` | `nuget.org` unreachable | Section 11, option A or B |
| `docker compose up` fails with "env file .env not found" | `.env` missing | `cp .env.example .env` |
| Container restarts in a loop | Look at `docker compose logs api` | Common: unreadable/corrupt database (restore backup), invalid value format in `.env` (e.g. `Window=60` instead of `00:01:00`) |
| `SQLite Error: database is locked` or corrupt after a manual copy | Database copied while running | Stop the app before copying, or use `sqlite3 .backup` (section 10.1) |
| No new stock prices | Outside market hours (Sat–Wed 08:45–13:00 Tehran), or TSETMC unreachable from the VPS | Check the time; test with section 5.6 |
| `401` from the API with a token that worked before | Access token expired (1 h), user deleted, or keys lost | Call `/api/auth/refresh` with the refresh token, or request a new token |
| CORS error in a browser app on another domain | Origin not allowed | Add `Cors__AllowedOrigins__0=https://that-app.example.com` |

Useful inspection commands:

```bash
docker compose exec api printenv | sort                     # effective environment in the container
docker compose exec api ls -la /app/data /app/data/keys      # persistent files present?
sudo sqlite3 data/portfolio.db "SELECT Username, Role, LastLoginAt FROM Users;"
sudo sqlite3 data/portfolio.db "SELECT COUNT(*) FROM Assets; SELECT MAX(Date) FROM Prices;"
```

---

## 13. Rules for agents

When operating this deployment on someone's behalf:

1. **Never commit or print `.env` contents or passwords.** `.env` is git-ignored and must stay
   `chmod 600`. Refer to secrets by variable name.
2. **Never publish the container port publicly.** Keep `127.0.0.1:` in the `ports:` mapping. Exposing
   it lets clients spoof their IP (forwarded headers are trusted) and bypass HTTPS.
3. **Never delete `data/`** (database and keys). Back it up before updates, migrations or restores.
4. **Don't copy `portfolio.db` while the app runs**; stop it or use `sqlite3 .backup`.
5. **Changing a password in `.env` doesn't change an existing user.** Use the backoffice, or create a
   new user with a new username.
6. **`docker compose restart` does not apply `.env` changes**; use `docker compose up -d`.
7. **Turn off Liara deployments** (`.github/workflows/deploy-liara.yml`) once the VPS is live, or two
   diverging copies of the service will run.
8. **Prefer configuration over code changes.** Rate limits, lifetimes, CORS origins, Swagger and sync
   schedules are all settable through environment variables (section 3.4).
9. **Admin role = full access, ApiClient = read-only.** Give API consumers `ApiClient` accounts only.
