namespace OrderApi.Services
{
    /// <summary>
    /// Applies promotional code discounts.
    /// Supported codes:
    /// - SAVE20: 20% discount
    /// - SAVE10: 10% discount
    /// </summary>
    public class PromoCodeDiscountRule : IDiscountRule
    {
        private const string Save20Code = "SAVE20";
        private const string Save10Code = "SAVE10";
        private const double Save20Discount = 0.20;
        private const double Save10Discount = 0.10;

        public double Calculate(double subtotal, string? promoCode, int totalOrdersPlaced)
        {
            if (string.IsNullOrWhiteSpace(promoCode))
            {
                return 0;
            }

            if (string.Equals(promoCode, Save20Code, StringComparison.OrdinalIgnoreCase))
            {
                return subtotal * Save20Discount;
            }

            if (string.Equals(promoCode, Save10Code, StringComparison.OrdinalIgnoreCase))
            {
                return subtotal * Save10Discount;
            }

            return 0;
        }
    }
}
