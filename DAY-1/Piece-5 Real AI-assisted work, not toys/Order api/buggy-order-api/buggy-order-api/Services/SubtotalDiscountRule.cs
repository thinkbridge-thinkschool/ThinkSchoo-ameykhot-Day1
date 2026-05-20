namespace OrderApi.Services
{
    /// <summary>
    /// Applies discount based on order subtotal thresholds.
    /// - Orders over $500: 10% discount
    /// - Orders over $200: 5% discount
    /// </summary>
    public class SubtotalDiscountRule : IDiscountRule
    {
        private const double HighOrderThreshold = 500;
        private const double MediumOrderThreshold = 200;
        private const double HighOrderDiscount = 0.10;
        private const double MediumOrderDiscount = 0.05;

        public double Calculate(double subtotal, string? promoCode, int totalOrdersPlaced)
        {
            if (subtotal > HighOrderThreshold)
            {
                return subtotal * HighOrderDiscount;
            }

            if (subtotal > MediumOrderThreshold)
            {
                return subtotal * MediumOrderDiscount;
            }

            return 0;
        }
    }
}
