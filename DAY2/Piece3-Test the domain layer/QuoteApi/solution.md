# Day 2 - Piece 3: Test the Domain Layer

## Goal
Test `Collection` aggregate invariants as pure domain tests (no DbContext, no fixtures, no setup methods).

## What was implemented
- Created `Tests.Domain` project.
- Added test packages: `xUnit`, `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`, `FluentAssertions`.
- Added project reference to `QuotesApi`.
- Implemented six invariant tests for `Collection` aggregate.

## Test class

```csharp
using FluentAssertions;
using QuotesApi.Models;

namespace Tests.Domain;

public class CollectionInvariantTests
{
	[Fact]
	public void Empty_name_throws()
	{
		Action act = () => new Collection(" ", 1);
		act.Should().Throw<DomainException>();
	}

	[Fact]
	public void Name_over_80_chars_throws()
	{
		Action act = () => new Collection(new string('a', 81), 1);
		act.Should().Throw<DomainException>();
	}

	[Fact]
	public void Fifty_first_item_throws()
	{
		var collection = new Collection("Favorites", 1);
		Enumerable.Range(1, 50).ToList().ForEach(collection.AddItem);
		Action act = () => collection.AddItem(51);
		act.Should().Throw<DomainException>();
	}

	[Fact]
	public void Duplicate_quote_id_throws()
	{
		var collection = new Collection("Favorites", 1);
		collection.AddItem(42);
		Action act = () => collection.AddItem(42);
		act.Should().Throw<DomainException>();
	}

	[Fact]
	public void Removing_non_existent_item_throws()
	{
		var collection = new Collection("Favorites", 1);
		Action act = () => collection.RemoveItem(999);
		act.Should().Throw<DomainException>();
	}

	[Fact]
	public void Add_then_remove_leaves_zero_items()
	{
		var collection = new Collection("Favorites", 1);
		collection.AddItem(7);
		collection.RemoveItem(7);
		collection.Items.Should().BeEmpty();
	}
}
```

## Test output (all pass)

Command:

```bash
dotnet test "Tests.Domain/Tests.Domain.csproj" --no-build --nologo -v minimal
```

Output:

```text
[xUnit.net 00:00:00.01] xUnit.net VSTest Adapter v3.1.4+50e68bbb8b (64-bit .NET 10.0.8)
[xUnit.net 00:00:00.44]   Discovering: Tests.Domain
[xUnit.net 00:00:00.65]   Discovered:  Tests.Domain
[xUnit.net 00:00:00.77]   Starting:    Tests.Domain
[xUnit.net 00:00:01.08]   Finished:    Tests.Domain
  Tests.Domain test net10.0 succeeded (4.6s)

Test summary: total: 6, failed: 0, succeeded: 6, skipped: 0, duration: 4.4s
Build succeeded in 6.6s
```
