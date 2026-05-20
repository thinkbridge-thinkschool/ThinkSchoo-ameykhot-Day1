# AI Reflection

## Round 1 — Claude Code Refactoring

### What it got right:

Claude correctly identified `CalculateDiscount` as the monolithic method causing tight coupling. The proposed refactoring into an `IDiscountRule` interface with separate implementations was architecturally sound:

- **SubtotalDiscountRule**: Encapsulates threshold-based discounts ($500 → 10%, $200 → 5%)
- **LoyaltyDiscountRule**: Isolates loyalty logic (orders > 10 → 3%)
- **PromoCodeDiscountRule**: Separates promotional code handling (SAVE20, SAVE10)

Each class has a single responsibility and implements the same interface method: `Calculate(double subtotal, string? promoCode, int totalOrdersPlaced)`. The refactored `OrderService` now injects a `List<IDiscountRule>` and sums their outputs rather than embedding conditional logic.

### Where I pushed back / caught a problem:

**Avoided over-engineering**: The refactoring stayed minimal. Claude didn't introduce unnecessary abstractions like a `IDiscountRuleFactory` or `DiscountRuleResolver`. It didn't rename constants or move the `PromoCodes` class unnecessarily. The implementation was pragmatic: instantiate the rules in the constructor, call each one, aggregate results.

**Changed method visibility**: Originally `CalculateDiscount` was `static` and private. I changed it to an instance method so it could iterate over `_discountRules` (an instance field). This is the correct trade-off—the method must be non-static to access the rule list.

**No breaking changes to BuildOrder() or other methods**: The refactoring scope stayed narrow, exactly as requested.

### Risk identified:

If the `_discountRules` list is empty (due to misconfiguration), all discounts silently return zero. New code maintainers might add a new discount rule but forget to instantiate it in the constructor, and orders would silently lose that discount. A future guard or logging could mitigate this.

---

## Round 2 — Copilot Test Generation

### What it saved me:

When I added the three test method signatures as Copilot comments:
```csharp
// Test: CreateOrderAsync returns invalid when item quantity is negative
// Test: CreateOrderAsync returns invalid when CustomerId is zero
// Test: CalculateDiscount applies no discount when subtotal is below 200
```

Copilot **correctly recognized all three contexts** and generated implementations that:

1. **Negative quantity test**: Correctly used `Quantity = -1` and properly asserted `response.Success` is false with a message check for "valid items".
2. **CustomerId zero test**: Correctly passed `CustomerId = 0` and asserted failure with "CustomerId" in the message.
3. **Subtotal threshold test**: Recognized that after the refactoring, `CalculateDiscount` is now called internally and correctly tested it via `CreateOrderAsync` rather than trying to call a static method directly (which would have failed to compile).

### Where it was subtly wrong:

Initially, Copilot's CustomerId test used `Assert.False(response.Success)` without checking the message content—too loose. I tightened it to `Assert.Contains("CustomerId", response.Message)` to ensure the failure reason is specific, not just any validation error.

For the subtotal threshold test, Copilot didn't immediately seed a second customer with `TotalOrdersPlaced < 10` in the test fixtures. I added Customer ID 2 (Bob, 5 orders) to ensure the test would use a non-loyal customer and verify that orders below the $200 threshold get zero discount. Without this, the test would pass for the wrong reason (loyalty discount boosting the total).

---

## At 2 AM Debugging Prod

I would reach for **Copilot first** for this codebase because:

1. **It's inline in VS Code**: No tab switching, no copy-paste friction.
2. **It understands recent context**: After the Round 1 refactor, Copilot immediately knew the rules were now classes implementing `IDiscountRule`, not static methods.
3. **Fast, incremental fixes**: For a subtle bug like "discount not applying because a rule wasn't instantiated," inline suggestions are faster than explaining the full flow to Claude.

However, for **structural problems** (e.g., "the entire discount calculation is wrong" or "we need to rethink the rule composition"), I'd use **Claude Code** because:

1. **Better at big-picture reasoning**: It can trace through a complex refactoring and spot architectural issues.
2. **Handles longer context**: If I need to paste the entire OrderService, BuildOrder(), and three rule classes, Claude handles it better than inline Copilot.
3. **Less 2 AM cognitive load**: I can paste a problem, read a full analysis, and decide on a fix without jumping between tabs.

**Real scenario**: 2 AM, discount calculated as `0` across all orders. 
- **Copilot**: I'd ask "why is discount always 0?" → It would spot `_discountRules` is uninitialized (null reference) faster because the context is right there.
- **Claude**: If the issue was "the rules are calculating but totals are wrong," I'd paste the logic and ask "why does 3% loyalty + 5% subtotal not stack correctly?"

---

## Testing Verification

All 7 tests pass:
- ✅ `CreateOrderAsync_AppliesPromoAndLoyaltyDiscount` 
- ✅ `CreateOrderAsync_AllowsMissingShippingAddress` 
- ✅ `CreateOrderAsync_HandlesSingleValidItemWithoutOffByOne` 
- ✅ `CreateOrderAsync_ReturnsInvalidWhenItemQuantityIsNegative` (new)
- ✅ `CreateOrderAsync_ReturnsInvalidWhenCustomerIdIsZero` (new)
- ✅ `CreateOrderAsync_AppliesNoDiscountWhenSubtotalBelowThreshold` (new)
- ✅ `OrderControllerIntegrationTests` (1 test)

**No regressions**: Existing tests still pass. New discount rules compose correctly.

---

## Conclusion

This refactoring demonstrates a pragmatic use of AI at different stages:

1. **Claude Code** excels at **architectural decisions** and understanding trade-offs. It proposed the strategy pattern cleanly and avoided over-engineering.
2. **Copilot** excels at **tactical, context-aware code generation**. Once the direction was set, it generated correct test implementations without needing a full replay of the codebase.
3. **Human oversight** is essential: Pushing back on loose assertions, ensuring test data diversity (Customer 1 loyal, Customer 2 not), and verifying no regressions.

The resulting codebase is more maintainable: adding a "first-time customer 15% discount" now requires only a new class implementing `IDiscountRule`, not editing `OrderService`.
