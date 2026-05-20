# Solution Submission

## What You Submit - 4 Things

1. Updated Quote.cs
- Rich entity implemented with private setters.
- Static factory method `Create(author, text)` added.
- Invariants enforced:
  - Author: 1-200 chars
  - Text: 1-1000 chars
- Text is immutable after creation.
- Soft-delete behavior added with `IsDeleted` flag.

2. Updated QuoteController.cs (or equivalent quote endpoint flow)
- Quote creation now goes through `Quote.Create()`.
- Domain creation result is handled and mapped to API response.
- Invalid domain input returns a proper validation/domain error response.
- In this project structure, the equivalent flow is in endpoint handlers under `ServiceCollectionExtensions.cs`.

3. WHY.md
- Added a ~200-word explanation of why rich model is better than an anemic model.
- Includes a concrete bug scenario that an anemic model could ship and rich model prevents.

4. Git branch link
- Branch: `feature/rich-quote-entity`
- Repository: `https://github.com/thinkbridge-thinkschool/ThinkSchoo-ameykhot-Day1.git`
- PR link: `https://github.com/thinkbridge-thinkschool/ThinkSchoo-ameykhot-Day1/pull/new/feature/rich-quote-entity`

## Notes
- No additional git actions were performed in this step (no commit, no push).
