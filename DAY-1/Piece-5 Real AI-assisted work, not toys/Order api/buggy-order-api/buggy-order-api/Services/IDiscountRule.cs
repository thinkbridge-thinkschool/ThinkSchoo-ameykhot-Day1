namespace OrderApi.Services
{
    /// <summary>
    /// Represents a discount calculation strategy.
    /// Each implementation calculates a specific type of discount.
    /// </summary>
    public interface IDiscountRule
    {
        /// <summary>
        /// Calculate the discount amount based on the given parameters.
        /// </summary>
        /// <param name="subtotal">The order subtotal before discount.</param>
        /// <param name="promoCode">Optional promotional code.</param>
        /// <param name="totalOrdersPlaced">Total number of orders the customer has placed.</param>
        /// <returns>The discount amount.</returns>
        double Calculate(double subtotal, string? promoCode, int totalOrdersPlaced);
    }
}
