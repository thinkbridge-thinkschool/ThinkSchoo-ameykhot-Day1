# My Prompt to the AI

> This is the exact prompt I gave to Claude to generate the original `OrderController.cs`.

---

Write me a deliberately bad `OrderController.cs` for an ASP.NET Core 10 web API project.

Requirements:

- Approximately 300 lines total
- A single giant `POST /api/orders` action that mixes all of the following inline with no separation:
  - Input validation
  - Business logic (discount tiers, loyalty bonus, promo codes)
  - Stock checking
  - EF Core data access (instantiate DbContext directly inside the method)
  - HTTP response building
- Exactly four empty `catch { }` blocks that swallow exceptions silently
- Synchronous EF calls (`.FirstOrDefault()` and `.GetAwaiter().GetResult()`) inside an `async Task` method
- One more sync `SaveChanges()` outside of try/catch
- Return type `Task<object>` with anonymous objects instead of typed responses
- A hardcoded SQL Server connection string inside the controller
- Zero XML docs, zero tests
- Two subtle runtime bugs:
  1. An off-by-one error in the item validation loop (`<=` instead of `<`)
  2. A null reference dereference on `ShippingAddress` which is optional but never null-checked
- Extra smells for realism:
  - Magic numbers (0.10, 0.05, 0.03, 500, 200)
  - Raw string literals for promo codes and order status
  - `DateTime.Now` instead of `DateTime.UtcNow`
  - `Thread.Sleep(200)` inside the async method simulating email sending
  - All model classes, DbContext, and request/response types in the same file as the controller
  - Inconsistent field names between the POST and GET responses

Do NOT add comments explaining what is wrong. The code should look like something a junior developer wrote under deadline pressure two years ago.
