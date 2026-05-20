namespace OrderApi.Services
{
    /// <summary>
    /// Applies loyalty discount for customers who have placed more than 10 orders.
    /// Loyal customers receive 3% discount on their orders.
    /// </summary>
    public class LoyaltyDiscountRule : IDiscountRule
    {
        private const int LoyaltyThreshold = 10;
        private const double LoyaltyDiscount = 0.03;

        public double Calculate(double subtotal, string? promoCode, int totalOrdersPlaced)
        {
            if (totalOrdersPlaced > LoyaltyThreshold)
            {
                return subtotal * LoyaltyDiscount;
            }

            return 0;
        }
    }
}
