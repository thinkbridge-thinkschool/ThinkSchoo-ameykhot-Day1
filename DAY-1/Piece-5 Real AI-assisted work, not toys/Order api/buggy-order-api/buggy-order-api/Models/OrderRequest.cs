namespace OrderApi.Models
{
    public class OrderRequest
    {
        public int CustomerId { get; set; }
        public List<OrderItem> Items { get; set; } = new();
        public string? PromoCode { get; set; }
        public string? ShippingAddress { get; set; }
        public string? Notes { get; set; }
    }
}
