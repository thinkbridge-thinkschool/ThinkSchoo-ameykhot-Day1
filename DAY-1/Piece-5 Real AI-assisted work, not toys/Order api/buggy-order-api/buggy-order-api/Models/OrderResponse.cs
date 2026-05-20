namespace OrderApi.Models
{
    public class OrderResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public int? OrderId { get; set; }
        public int CustomerId { get; set; }
        public string? Status { get; set; }
        public double SubTotal { get; set; }
        public double Discount { get; set; }
        public double Total { get; set; }
        public string? ShippingAddress { get; set; }
        public List<OrderLineResponse> Items { get; set; } = new();
        public DateTime CreatedAt { get; set; }

        public static OrderResponse Invalid(string message, List<string>? invalidItems = null)
        {
            var response = new OrderResponse
            {
                Success = false,
                Message = message,
                Items = invalidItems?.Select(item => new OrderLineResponse { ProductName = item }).ToList() ?? new List<OrderLineResponse>()
            };

            return response;
        }

        public static OrderResponse FromOrder(Order order)
        {
            return new OrderResponse
            {
                Success = true,
                OrderId = order.Id,
                CustomerId = order.CustomerId,
                Status = order.Status,
                SubTotal = order.SubTotal,
                Discount = order.Discount,
                Total = order.Total,
                ShippingAddress = order.ShippingAddress,
                Items = order.Items.Select(item => new OrderLineResponse
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    LineTotal = item.LineTotal
                }).ToList(),
                CreatedAt = order.CreatedAt
            };
        }
    }
}
