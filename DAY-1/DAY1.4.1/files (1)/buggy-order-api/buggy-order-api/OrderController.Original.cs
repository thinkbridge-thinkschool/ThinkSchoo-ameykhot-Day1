using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderApi.Controllers
{
    [ApiController]
    [Route("api/orders")]
    public class OrderController : ControllerBase
    {
        // ── POST /api/orders ─────────────────────────────────────────────────
        [HttpPost]
        public async Task<object> CreateOrder([FromBody] OrderRequest req)
        {
            // SMELL: DbContext instantiated directly, hardcoded connection string
            AppDbContext db = null;
            try
            {
                db = new AppDbContext("Server=localhost;Database=OrdersDb;Trusted_Connection=True;");
            }
            catch { }   // SMELL: empty catch #1 — hides connection failures completely

            // SMELL: null check after already using db above (if constructor threw, db is null here)
            if (req == null)
                return new { success = false, message = "Request is null" };

            if (req.CustomerId <= 0)
                return new { success = false, message = "CustomerId is required" };

            if (req.Items == null || req.Items.Count == 0)
                return new { success = false, message = "Items cannot be empty" };

            // SMELL: off-by-one bug — loop runs one index past the end
            // When i == req.Items.Count, req.Items[i] throws IndexOutOfRangeException
            // That exception is swallowed silently below, so validItems is always empty
            List<OrderItem> validItems = new List<OrderItem>();
            for (int i = 0; i <= req.Items.Count; i++)
            {
                var item = req.Items[i];
                if (item.Quantity > 0 && item.ProductId > 0)
                    validItems.Add(item);
            }

            // SMELL: synchronous EF call — blocks thread pool thread
            Customer customer = null;
            try
            {
                customer = db.Customers
                    .Where(c => c.Id == req.CustomerId)
                    .FirstOrDefault();   // sync query inside async method
            }
            catch { }   // SMELL: empty catch #2 — db being null / query failing is hidden

            if (customer == null)
                return new { success = false, message = "Customer not found" };

            var productIds = validItems.Select(x => x.ProductId).ToList();

            // SMELL: sync EF via .GetAwaiter().GetResult() inside async method
            List<Product> products = new List<Product>();
            try
            {
                products = db.Products
                    .Where(p => productIds.Contains(p.Id))
                    .ToListAsync()
                    .GetAwaiter()
                    .GetResult();
            }
            catch { }   // SMELL: empty catch #3 — product load failures silently skipped

            // Business logic, stock check, and total calc all inline in controller
            double total = 0;
            double discount = 0;
            List<string> outOfStock = new List<string>();

            foreach (var item in validItems)
            {
                var product = products.Where(p => p.Id == item.ProductId).FirstOrDefault();
                if (product == null) continue;

                if (product.Stock < item.Quantity)
                {
                    outOfStock.Add(product.Name);
                    continue;
                }

                total += product.Price * item.Quantity;
            }

            if (outOfStock.Count > 0)
                return new { success = false, message = "Items out of stock", items = outOfStock };

            // SMELL: magic numbers for discount tiers — no named constants
            if (total > 500)
                discount = total * 0.10;
            else if (total > 200)
                discount = total * 0.05;

            // SMELL: loyalty discount also magic numbers
            if (customer.TotalOrdersPlaced > 10)
                discount += total * 0.03;

            // SMELL: promo codes as raw string literals — repeated if ever used elsewhere
            if (req.PromoCode == "SAVE20")
                discount += total * 0.20;
            else if (req.PromoCode == "SAVE10")
                discount += total * 0.10;

            double finalTotal = total - discount;

            // SMELL: null dereference bug — ShippingAddress is optional in the model
            // but .Trim() is called unconditionally; throws NullReferenceException
            string shippingAddr = req.ShippingAddress.Trim().ToUpper();

            // SMELL: entity construction scattered inline rather than in a factory/service
            var order = new Order();
            order.CustomerId  = req.CustomerId;
            order.CreatedAt   = DateTime.Now;    // SMELL: DateTime.Now instead of UtcNow
            order.Status      = "pending";        // SMELL: raw string instead of enum
            order.SubTotal    = total;
            order.Discount    = discount;
            order.Total       = finalTotal;
            order.ShippingAddress = shippingAddr;
            order.Notes       = req.Notes;

            List<OrderLineItem> orderLines = new List<OrderLineItem>();
            foreach (var item in validItems)
            {
                var product = products.FirstOrDefault(p => p.Id == item.ProductId);
                if (product == null) continue;

                var line = new OrderLineItem();
                line.ProductId   = item.ProductId;
                line.ProductName = product.Name;
                line.Quantity    = item.Quantity;
                line.UnitPrice   = product.Price;
                line.LineTotal   = product.Price * item.Quantity;
                orderLines.Add(line);
            }

            order.Items = orderLines;

            // Deduct stock inline
            foreach (var item in validItems)
            {
                var product = products.FirstOrDefault(p => p.Id == item.ProductId);
                if (product == null) continue;
                product.Stock = product.Stock - item.Quantity;
            }

            // SMELL: sync SaveChanges inside async method
            try
            {
                db.Orders.Add(order);
                db.Products.UpdateRange(products);
                db.SaveChanges();
            }
            catch { }   // SMELL: empty catch #4 — save failures are invisible

            // Customer stat update also sync, also no error handling
            customer.TotalOrdersPlaced = customer.TotalOrdersPlaced + 1;
            customer.LastOrderDate = DateTime.Now;
            db.SaveChanges();   // another sync call

            // SMELL: blocking Thread.Sleep inside async method (simulates "sending email")
            try
            {
                var msg = "Dear " + customer.FirstName + ", order #" + order.Id + " placed.";
                System.Threading.Thread.Sleep(200);   // blocks a thread-pool thread for 200ms
                Console.WriteLine("Email sent: " + msg);
            }
            catch { }

            // SMELL: anonymous object response — no typed contract, Swagger shows nothing
            var responseLines = new List<object>();
            foreach (var line in orderLines)
            {
                responseLines.Add(new
                {
                    productId   = line.ProductId,
                    productName = line.ProductName,
                    quantity    = line.Quantity,
                    unitPrice   = line.UnitPrice,
                    lineTotal   = line.LineTotal
                });
            }

            return new
            {
                success         = true,
                orderId         = order.Id,
                customerId      = order.CustomerId,
                status          = order.Status,
                subTotal        = order.SubTotal,
                discount        = order.Discount,
                total           = order.Total,
                shippingAddress = order.ShippingAddress,
                items           = responseLines,
                createdAt       = order.CreatedAt
            };
        }

        // ── GET /api/orders/{id} ──────────────────────────────────────────────
        [HttpGet("{id}")]
        public async Task<object> GetOrder(int id)
        {
            AppDbContext db = null;
            try
            {
                db = new AppDbContext("Server=localhost;Database=OrdersDb;Trusted_Connection=True;");
            }
            catch { }

            Order order = null;
            try
            {
                // SMELL: sync FirstOrDefault inside async, no CancellationToken
                order = db.Orders
                    .Include(o => o.Items)
                    .Where(o => o.Id == id)
                    .FirstOrDefault();
            }
            catch { }

            if (order == null)
                return new { success = false, message = "Order not found" };

            // SMELL: field names differ from CreateOrder response (qty vs quantity)
            var lines = new List<object>();
            foreach (var line in order.Items)
            {
                lines.Add(new
                {
                    productId   = line.ProductId,
                    productName = line.ProductName,
                    qty         = line.Quantity,    // inconsistent: CreateOrder used "quantity"
                    price       = line.UnitPrice    // inconsistent: CreateOrder used "unitPrice"
                });
            }

            return new
            {
                id         = order.Id,
                customer   = order.CustomerId,      // inconsistent: CreateOrder used "customerId"
                total      = order.Total,
                status     = order.Status,
                items      = lines
            };
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SMELL: All models, DbContext, and request types dumped in the same file
    // ─────────────────────────────────────────────────────────────────────────

    public class OrderRequest
    {
        public int CustomerId { get; set; }
        public List<OrderItem> Items { get; set; }
        public string PromoCode { get; set; }
        public string ShippingAddress { get; set; }  // optional — but treated as required below
        public string Notes { get; set; }
    }

    public class OrderItem
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
    }

    public class Order
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; }
        public double SubTotal { get; set; }
        public double Discount { get; set; }
        public double Total { get; set; }
        public string ShippingAddress { get; set; }
        public string Notes { get; set; }
        public List<OrderLineItem> Items { get; set; }
    }

    public class OrderLineItem
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public int Quantity { get; set; }
        public double UnitPrice { get; set; }
        public double LineTotal { get; set; }
    }

    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public double Price { get; set; }
        public int Stock { get; set; }
    }

    public class Customer
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public int TotalOrdersPlaced { get; set; }
        public DateTime? LastOrderDate { get; set; }
    }

    public class AppDbContext : DbContext
    {
        private readonly string _connectionString;

        // SMELL: constructor takes raw connection string — not injected via DI
        public AppDbContext(string connectionString)
        {
            _connectionString = connectionString;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlServer(_connectionString);

        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderLineItem> OrderLines { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Customer> Customers { get; set; }
    }
}
