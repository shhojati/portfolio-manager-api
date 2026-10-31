# Chandoo (چندو)

A public, Persian price site: daily prices of currencies, gold and coins, metals, crypto, funds, TSE stocks,
certificates and indices, with daily change and a 30-day trend. Pages are rendered on the server (ASP.NET Core
Razor Pages, .NET 10) so search engines can index them.

Chandoo has no database. It reads everything from the Portfolio Manager API (the project in the parent folder) through
`GET /api/board`, signed in as an API client, and caches each page for five minutes.

| Page | What it shows |
|---|---|
| `/` | Dollar, 18 karat gold and the TSE index, a short table per main category, FAQ |
| `/prices/{category}` | Every asset of a category (`currency`, `gold-coin`, `metal`, `crypto`, `fund`, `stock`, `certificate`, `index`), 50 per page |
| `/sources` | Where each category's prices come from and how often they're fetched |
| `/search?q=` | Search results with prices (noindex) |
| `/robots.txt`, `/sitemap.xml` | For search engines |

## Run locally

Start the API first (from the parent folder, `dotnet run --launch-profile http`, on port 5131), then:

```bash
cd chandoo
dotnet run --launch-profile http
```

The site is at http://localhost:5132. In Development it signs in to the local API as the `api-client` user from the
API's `appsettings.Development.json`.

## Configuration

Set these in `appsettings.json`, or as environment variables (`Api__BaseUrl` etc.) in `.env` with Docker.

| Setting | Meaning |
|---|---|
| `Api:BaseUrl` | The API's address, e.g. `https://portfolio-api.shahabdev.ir` |
| `Api:Username`, `Api:Password` | An API client account on the API. The site doesn't start without them. |
| `Api:Timeout` | How long one API call may take (default 20 s). After that the page says prices are unavailable. |
| `Landing:SiteUrl` | The site's public address, e.g. `https://chandoo.ir`, for canonical links and the sitemap |
| `Landing:BazaarUrl`, `Landing:WebAppUrl` | The app banner's buttons. The banner is hidden while both are empty. |

## Publish

1. **Create the API client.** In the API's backoffice, go to Users → New user, choose role "API client", and pick a
   username (e.g. `chandoo`) and a long password. API clients can only read.
2. **Copy this folder to the server**, e.g. `/opt/chandoo`. The server must be able to reach the API's address.
3. **Configure:** `cp .env.example .env`, fill in `Api__Password`, `Landing__SiteUrl` and the banner links, then
   `chmod 600 .env`.
4. **Start:** `docker compose up -d --build`. The site listens on `127.0.0.1:8081`. Check it with
   `curl -I http://127.0.0.1:8081/`, which should answer 200.
5. **Put HTTPS in front.** With Caddy:

   ```
   chandoo.ir, www.chandoo.ir {
       reverse_proxy 127.0.0.1:8081
   }
   ```

6. **Point DNS** for the domain at the server, then submit `https://<domain>/sitemap.xml` in Google Search Console.

To update: copy the new files over and run `docker compose up -d --build` again, or let GitHub do it (below).

### Automatic deploys from GitHub

`.github/workflows/deploy-chandoo.yml` deploys the site whenever a push to master changes `chandoo/`. You can also run
it by hand from the Actions tab. It copies `chandoo/` to `/opt/chandoo` with rsync (never touching `.env`), runs
`docker compose up -d --build`, and fails if the site doesn't answer 200 within a minute.

Chandoo runs on the same VPS as the API, so the workflow uses the API deploy's repository secrets (`VPS_HOST`,
`VPS_USER`, `VPS_SSH_KEY`, `VPS_KNOWN_HOSTS`; see `DEPLOYMENT.md`). On the server it needs `/opt/chandoo/.env`
(steps 1–3 above), `rsync`, and `/opt/chandoo` owned by the `VPS_USER` user. If Chandoo ever moves to another server,
give the workflow secrets of its own.

Without Docker: `dotnet publish -c Release -o publish`, then run `dotnet publish/Chandoo.dll` with the same settings as
environment variables and `ASPNETCORE_URLS=http://127.0.0.1:8081`.

## Notes

- The API client's rate limit is 120 requests a minute. Thanks to the cache, a page costs at most one to five API calls
  every five minutes, and a search one call per query every two minutes.
- If the API can't be reached, pages answer 503 with a short "prices are temporarily unavailable" message. Those
  answers aren't cached.
- `/sources` lists the API's sync schedules as written in `Pages/Sources.cshtml.cs`. Update it when a schedule in the
  API's `appsettings.json` changes.
