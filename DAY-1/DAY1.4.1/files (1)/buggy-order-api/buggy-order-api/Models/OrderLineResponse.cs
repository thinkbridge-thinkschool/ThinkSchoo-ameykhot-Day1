namespace OrderApi.Models
{
    public class OrderLineResponse
    {
        public int ProductId { get; set; }
        public string? ProductName { get; set; }
        public int Quantity { get; set; }
        public double UnitPrice { get; set; }
        public double LineTotal { get; set; }
    }
}
