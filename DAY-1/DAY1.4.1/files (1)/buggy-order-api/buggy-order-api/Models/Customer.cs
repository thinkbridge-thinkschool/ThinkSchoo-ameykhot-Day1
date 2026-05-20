namespace OrderApi.Models
{
    public class Customer
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int TotalOrdersPlaced { get; set; }
        public DateTime? LastOrderDate { get; set; }
    }
}
