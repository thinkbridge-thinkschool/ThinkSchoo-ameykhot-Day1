# WHY: Rich Quote Entity vs Anemic Quote Entity

Moving `Quote` from an anemic model to a rich model gives us one reliable place to enforce business rules. In the old version, `Quote` was just data (`Author`, `Text`, `CreatedAt`). Any layer could create invalid quotes, and different endpoints could enforce different limits. That creates drift and production bugs.

In the rich version, quote creation must go through `Quote.Create(author, text)`. This makes invariants explicit and consistent: `Author` is 1-200 chars, `Text` is 1-1000 chars. If invalid, creation returns a domain error immediately, so the API can return a clear 422 response. We also prevent text mutation by exposing `Text` with `private set`; there is no public method to edit it. Finally, deletion is modeled as behavior (`SoftDelete`) rather than physical row removal, preserving history and avoiding accidental data loss.

Concrete bug the anemic model could ship:
A background import job bypasses API validation and inserts a quote with 1500 chars of text. The API endpoint may have accepted only 1000, but the model itself allowed anything. Later, UI rendering or downstream consumers break on unexpected length. With the rich model, the import path also calls `Quote.Create`, so oversized text is rejected at the domain boundary before persistence.
