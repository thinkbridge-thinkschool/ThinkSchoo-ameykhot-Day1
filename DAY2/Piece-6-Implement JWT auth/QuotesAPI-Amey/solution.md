=== Login Endpoint ===

```csharp
[HttpPost("/api/auth/login")]
public async Task<IActionResult> Login(LoginRequest request)
{
    var user = await _context.Users
        .FirstOrDefaultAsync(u => u.Email == request.Email);

    if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        return Unauthorized("Invalid email or password");

    var token = GenerateJwtToken(user);

    return Ok(new
    {
        access_token = token,
        refresh_token = Guid.NewGuid().ToString(),
        expires_in = 900
    });
}
```

=== Curl 1 - No Token ===

Command:

```bash
curl -X POST http://localhost:5000/api/quotes \
  -H "Content-Type: application/json" \
  -d '{"author":"test","text":"hello"}'
```

Response:

```text
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer
```

=== Curl 2 - Valid Token ===

Command:

```bash
curl -X POST http://localhost:5000/api/quotes \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..." \
  -d '{"author":"test","text":"hello secure"}'
```

Response:

```text
HTTP/1.1 201 Created
Location: /api/quotes/3
```

=== Curl 3 - Expired Token ===

Command:

```bash
curl -X POST http://localhost:5000/api/quotes \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...(expired)" \
  -d '{"author":"test","text":"hello secure"}'
```

Response:

```text
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer error="invalid_token", error_description="The token expired at '05/20/2026 11:22:46'"
```
