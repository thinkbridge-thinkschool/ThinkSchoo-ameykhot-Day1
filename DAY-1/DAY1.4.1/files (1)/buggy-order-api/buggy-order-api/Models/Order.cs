namespace OrderApi.Models
{
    public class Order
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; } = string.Empty;
        public double SubTotal { get; set; }
        public double Discount { get; set; }
        public double Total { get; set; }
        public string ShippingAddress { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public List<OrderLineItem> Items { get; set; } = new();
    }
}
