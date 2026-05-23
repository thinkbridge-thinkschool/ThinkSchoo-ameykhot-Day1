# Day 5 – Piece 2: Container Image from `dotnet publish` (No Dockerfile)

## What this piece proves

.NET 10 ships built-in container image generation. No Dockerfile, no `FROM`, no multi-stage build needed for the common case. One `dotnet publish` command produces a runnable OCI image and loads it directly into the local Docker daemon.

---

## 1. csproj Container Properties

Added to `QuotesApi.csproj`:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <ContainerRepository>quotes-api</ContainerRepository>
  <ContainerImageTag>0.1.0</ContainerImageTag>
  <ContainerBaseImage>mcr.microsoft.com/dotnet/aspnet:10.0</ContainerBaseImage>
</PropertyGroup>
```

> **Note on Alpine:** The task template suggests `aspnet:10.0-alpine` for smaller images. In practice, `SQLitePCLRaw` (used by EF Core SQLite) ships a `glibc`-linked native `e_sqlite3.so` that fails to load on Alpine's `musl libc` with `fcntl64: symbol not found`. Switched to the Debian-based `aspnet:10.0` image which uses `glibc` and works out of the box. For a production Alpine image, you would replace `SQLitePCLRaw.bundle_green` with a musl-compatible bundle or switch the database provider.

---

## 2. `dotnet publish` Output

```
dotnet publish --os linux --arch x64 -t:PublishContainer
```

```
Determining projects to restore...
All projects are up-to-date for restore.
QuotesApi -> ...\bin\Release\net10.0\linux-x64\QuotesApi.dll
QuotesApi -> ...\bin\Release\net10.0\linux-x64\publish\
Building image 'quotes-api' with tags '0.1.0' on top of base image 'mcr.microsoft.com/dotnet/aspnet:10.0'.
Pushed image 'quotes-api:0.1.0' to local registry via 'docker'.
```

No Dockerfile. The .NET SDK:
1. Compiled a self-contained linux/amd64 binary
2. Pulled the base image from MCR
3. Layered the app on top
4. Tagged and loaded it into the local Docker daemon

---

## 3. `docker run` Output

```
docker run -d --name quotes-api-test -p 8080:8080 \
  -e ConnectionStrings__DefaultConnection="Data Source=/tmp/quotes.db" \
  -e Jwt__Key="ThinkSchoolDay2JwtSigningKey-UseAtLeast32Chars" \
  -e EntraId__TenantId="00000000-0000-0000-0000-000000000000" \
  -e EntraId__ClientId="00000000-0000-0000-0000-000000000001" \
  -e KeyVault__Url="" \
  quotes-api:0.1.0
```

Container startup logs:

```
[07:02:02 INF] [] Program: Applying database schema...
[07:02:06 INF] [] Program: Seeded default user: user@test.com
[07:02:06 INF] [] Program: Migrations applied successfully
[07:02:06 INF] [] Microsoft.Hosting.Lifetime: Now listening on: http://[::]:8080
[07:02:06 INF] [] Microsoft.Hosting.Lifetime: Application started. Press Ctrl+C to shut down.
[07:02:06 INF] [] Microsoft.Hosting.Lifetime: Hosting environment: Production
[07:02:06 INF] [] Microsoft.Hosting.Lifetime: Content root path: /app
```

`docker ps` confirms container is running:

```
CONTAINER ID   IMAGE              COMMAND                  CREATED          STATUS          PORTS                    NAMES
e2e6e4589671   quotes-api:0.1.0   "dotnet /app/QuotesA…"   15 seconds ago   Up 14 seconds   0.0.0.0:8080->8080/tcp   quotes-api-test
```

---

## 4. `curl` to Health Endpoint

```bash
curl http://localhost:8080/health
```

Response:

```json
{"status":"healthy","timestamp":"2026-05-23T07:02:30.3441315+00:00"}
```

**This is the real app** — the same EF Core migrations ran, the default user was seeded, and the API is listening. The health endpoint was added to `Program.cs`:

```csharp
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));
```

---

## What I learned

**`dotnet publish` IS a container build tool.** The SDK's `Microsoft.NET.Build.Containers` targets (built into .NET 8+) do everything a multi-stage Dockerfile does — compile, publish, layer, tag — with zero Docker syntax. The only Docker knowledge needed is the base image name and the `docker run` command.

**Alpine + glibc-linked natives don't mix.** `SQLitePCLRaw` ships a precompiled `e_sqlite3.so` linked against `glibc`. Alpine uses `musl libc`, so `fcntl64` is missing and the load fails with a `DllNotFoundException` at runtime. The fix is either: use a `glibc`-based base image (`aspnet:10.0`), or replace the SQLite provider with a musl-compatible one. This is a real production concern — many NuGet packages with native binaries silently break on Alpine.

**The image size trade-off is real.** `aspnet:10.0-alpine` is ~100 MB vs `aspnet:10.0` at ~220 MB. For production, fixing Alpine compatibility is worth the effort. For a learning exercise, switching to the Debian image lets you focus on the container build workflow rather than musl debugging.

---

## Repository

- **Branch:** `day5/cloud-deployment-observability`
- **Folder:** `DAY5/Piece-2-Container image from dotnet publish/QuotesAPI-Amey/`
