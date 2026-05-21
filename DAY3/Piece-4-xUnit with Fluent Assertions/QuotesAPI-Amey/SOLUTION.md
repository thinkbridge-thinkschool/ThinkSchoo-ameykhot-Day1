# Day 3 – Piece 4: xUnit with Fluent Assertions

## What was built

**New test project:** `Quotes.Tests.Unit`  
**Stack:** xUnit 2.9 + FluentAssertions 7.0 + NSubstitute 5.3 + EF Core InMemory  
**Tests written:** 37 (well above the 20-test requirement)

### Test classes

| File | Tests | Covers |
|---|---|---|
| `CreateQuoteRequestValidatorTests.cs` | 11 | Every branch of `CreateQuoteRequestValidator` |
| `QuoteFactoryTests.cs` | 5 | `QuoteFactory.Create` — clock usage, field mapping, UTC kind |
| `CollectionTests.cs` | 14 | All `Collection` domain invariants (constructor, AddItem, RemoveItem, Rename) |
| `AuthTokenServiceTests.cs` | 7 | Full refresh-token lifecycle including reuse detection |

---

## 3 sample tests showing the pattern

### 1 — Validator: `[Theory]` with `[InlineData]` for every empty-input branch

```csharp
[Theory]
[InlineData("")]
[InlineData("   ")]
public void Validate_WhenAuthorEmptyOrWhitespace_ReturnsAuthorRequiredError(string author)
{
    // Arrange
    var sut = new CreateQuoteRequestValidator();
    var request = new CreateQuoteRequest { Author = author, Text = "Some text" };

    // Act
    var result = sut.Validate(request);

    // Assert
    result.IsValid.Should().BeFalse();
    result.Errors.Should()
        .Contain(e => e.PropertyName == "Author" && e.ErrorMessage == "Author is required");
}
```

### 2 — `QuoteFactory`: NSubstitute fake clock, FluentAssertions chained assertion

```csharp
[Fact]
public void Create_WhenNoTimestampProvided_UsesClockUtcNow()
{
    // Arrange
    var fixedNow = new DateTimeOffset(2026, 5, 19, 12, 30, 0, TimeSpan.Zero);
    var clock = Substitute.For<IClock>();
    clock.UtcNow.Returns(fixedNow);
    var sut = new QuoteFactory(clock);

    // Act
    var quote = sut.Create("Author", "Text");

    // Assert
    quote.CreatedAt.Should().Be(fixedNow.UtcDateTime);
}
```

### 3 — Auth: Refresh token reuse detection revokes the entire family

```csharp
[Fact]
public async Task RefreshAsync_WhenTokenReused_RevokesEntireFamilyAndReturnsReuseDetected()
{
    // Arrange
    var clock = Substitute.For<IClock>();
    clock.UtcNow.Returns(new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
    await using var db = NewDb();
    var user = await SeedUserAsync(db);
    var sut = BuildService(db, clock);

    var loginPair = await sut.IssueTokenPairAsync(user);
    var firstRefresh = await sut.RefreshAsync(loginPair.RefreshToken);
    firstRefresh.IsSuccess.Should().BeTrue();

    // Act — reuse the already-consumed original token
    var reuseResult = await sut.RefreshAsync(loginPair.RefreshToken);

    // Assert
    reuseResult.IsSuccess.Should().BeFalse();
    reuseResult.FailureReason.Should().Be(RefreshFailureReason.ReuseDetected);

    var childResult = await sut.RefreshAsync(firstRefresh.Tokens!.RefreshToken);
    childResult.FailureReason.Should().Be(RefreshFailureReason.RevokedToken);

    var activeTokens = await db.RefreshTokens.CountAsync(t => t.RevokedAt == null);
    activeTokens.Should().Be(0, "entire family must be revoked after reuse detection");
}
```

---

## Test run output — all 37 green

```
Test Run Successful.
Total tests: 37
     Passed: 37
 Total time: 10.9186 Seconds
```

Full detailed output:
```
  Passed Quotes.Tests.Unit.CreateQuoteRequestValidatorTests.Validate_WhenTextEmptyOrWhitespace_ReturnsTextRequiredError(text: "   ") [2 s]
  Passed Quotes.Tests.Unit.CreateQuoteRequestValidatorTests.Validate_WhenTextEmptyOrWhitespace_ReturnsTextRequiredError(text: "") [3 ms]
  Passed Quotes.Tests.Unit.CreateQuoteRequestValidatorTests.Validate_WhenTextExceeds2000Characters_ReturnsMaxLengthError [4 ms]
  Passed Quotes.Tests.Unit.CreateQuoteRequestValidatorTests.Validate_WhenAuthorIs256Characters_NoAuthorErrors [16 ms]
  Passed Quotes.Tests.Unit.CreateQuoteRequestValidatorTests.Validate_WhenAuthorEmptyOrWhitespace_ReturnsAuthorRequiredError(author: "   ") [3 ms]
  Passed Quotes.Tests.Unit.CreateQuoteRequestValidatorTests.Validate_WhenAuthorEmptyOrWhitespace_ReturnsAuthorRequiredError(author: "") [2 ms]
  Passed Quotes.Tests.Unit.CreateQuoteRequestValidatorTests.Validate_WithVariousValidInputs_Succeeds(author: "X", text: "Y") [1 ms]
  Passed Quotes.Tests.Unit.CreateQuoteRequestValidatorTests.Validate_WithVariousValidInputs_Succeeds(author: "Author Name", text: "Short text") [< 1 ms]
  Passed Quotes.Tests.Unit.CreateQuoteRequestValidatorTests.Validate_WithBothFieldsValid_ReturnsIsValidTrue [10 ms]
  Passed Quotes.Tests.Unit.CreateQuoteRequestValidatorTests.Validate_WhenTextIs2000Characters_NoTextErrors [2 ms]
  Passed Quotes.Tests.Unit.CreateQuoteRequestValidatorTests.Validate_WhenAuthorExceeds256Characters_ReturnsMaxLengthError [3 ms]
  Passed Quotes.Tests.Unit.CollectionTests.RemoveItem_WhenQuoteNotInCollection_ThrowsDomainException [67 ms]
  Passed Quotes.Tests.Unit.CollectionTests.Constructor_WhenNameExceeds80Characters_ThrowsDomainException [22 ms]
  Passed Quotes.Tests.Unit.QuoteFactoryTests.Create_WhenNoTimestampProvided_UsesClockUtcNow [3 s]
  Passed Quotes.Tests.Unit.CollectionTests.AddItem_WhenNewUniqueQuote_AddsItemAndIncreasesCount [8 ms]
  Passed Quotes.Tests.Unit.CollectionTests.Constructor_WhenNameIsValid_DoesNotThrow(name: "ABC") [3 ms]
  Passed Quotes.Tests.Unit.CollectionTests.Constructor_WhenNameIsValid_DoesNotThrow(name: "My Collection") [< 1 ms]
  Passed Quotes.Tests.Unit.QuoteFactoryTests.Create_SetsAuthorFromInput [7 ms]
  Passed Quotes.Tests.Unit.QuoteFactoryTests.Create_SetsTextFromInput [1 ms]
  Passed Quotes.Tests.Unit.CollectionTests.Rename_WhenNameIsTooShort_ThrowsDomainException [2 ms]
  Passed Quotes.Tests.Unit.QuoteFactoryTests.Create_WhenExplicitTimestampProvided_IgnoresClock [1 ms]
  Passed Quotes.Tests.Unit.CollectionTests.AddItem_WhenQuoteAlreadyPresent_ThrowsDomainException [2 ms]
  Passed Quotes.Tests.Unit.CollectionTests.RemoveItem_WhenQuoteExists_RemovesItFromItems [2 ms]
  Passed Quotes.Tests.Unit.CollectionTests.AddItem_WhenCollectionAlreadyHas50Items_ThrowsDomainException [2 ms]
  Passed Quotes.Tests.Unit.CollectionTests.Constructor_WhenNameTooShort_ThrowsDomainException(name: "  A  ") [3 ms]
  Passed Quotes.Tests.Unit.QuoteFactoryTests.Create_CreatedAtIsUtcKind [15 ms]
  Passed Quotes.Tests.Unit.CollectionTests.Constructor_WhenNameTooShort_ThrowsDomainException(name: "") [10 ms]
  Passed Quotes.Tests.Unit.CollectionTests.Constructor_WhenNameTooShort_ThrowsDomainException(name: "AB") [< 1 ms]
  Passed Quotes.Tests.Unit.CollectionTests.Rename_WhenNameIsValid_UpdatesTheName [< 1 ms]
  Passed Quotes.Tests.Unit.CollectionTests.Constructor_WhenNameIs80Characters_DoesNotThrow [1 ms]
  Passed Quotes.Tests.Unit.AuthTokenServiceTests.RefreshAsync_WhenTokenIsValid_ReturnsNewTokenPair [6 s]
  Passed Quotes.Tests.Unit.AuthTokenServiceTests.RefreshAsync_WhenTokenRevokedWithoutReplacement_ReturnsRevokedToken [187 ms]
  Passed Quotes.Tests.Unit.AuthTokenServiceTests.IssueTokenPairAsync_ReturnsTokenPairWithConfiguredExpiresIn [73 ms]
  Passed Quotes.Tests.Unit.AuthTokenServiceTests.RefreshAsync_WhenTokenNotInDatabase_ReturnsInvalidToken [4 ms]
  Passed Quotes.Tests.Unit.AuthTokenServiceTests.RefreshAsync_WhenTokenIsExpired_ReturnsExpiredToken [7 ms]
  Passed Quotes.Tests.Unit.AuthTokenServiceTests.RevokeAsync_WhenTokenExists_SetsRevokedAtToClockTime [34 ms]
  Passed Quotes.Tests.Unit.AuthTokenServiceTests.RefreshAsync_WhenTokenReused_RevokesEntireFamilyAndReturnsReuseDetected [76 ms]
```

---

## What I learned this session

Injecting a clock abstraction (`IClock`) instead of calling `DateTime.UtcNow` directly is not just about testability — it's about making time an explicit dependency. With NSubstitute, I can freeze time in any test with one line (`clock.UtcNow.Returns(fixedNow)`) and assert on exact timestamps without any flakiness.

The bigger click was `result.Should().Throw<DomainException>().WithMessage(...)` — FluentAssertions chaining reads like a sentence and collapses what would have been a try/catch block into a one-liner. When a test fails, the error message tells you exactly what was expected vs. received, not just "assertion failed".

---

## What would break this

1. **Remove the `family` column from the RefreshTokens table** — `RefreshAsync_WhenTokenReused_RevokesEntireFamilyAndReturnsReuseDetected` would fail because the family revocation query filters on `r.Family`.
2. **Change `ValidateName` to not trim** — `Constructor_WhenNameTooShort_ThrowsDomainException(name: "  A  ")` would pass instead of throwing, silently allowing single-character names hidden in whitespace.
3. **Swap `ExpiresAt <= now` to `<`** — `RefreshAsync_WhenTokenIsExpired_ReturnsExpiredToken` would miss the exact-boundary case and allow a token at its expiry instant.
4. **Remove the `ReplacedByToken` check in `RefreshAsync`** — a revoked token would always return `RevokedToken` instead of `ReuseDetected`, and no family revocation would happen.
