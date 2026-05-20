using Microsoft.AspNetCore.Mvc;
using OrderApi.Models;
using OrderApi.Services;

namespace OrderApi.Controllers
{
    [ApiController]
    [Route("api/orders")]
    public class OrderController : ControllerBase
    {
        private readonly IOrderService _orderService;
        private readonly ILogger<OrderController> _logger;

        public OrderController(IOrderService orderService, ILogger<OrderController> logger)
        {
            _orderService = orderService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<ActionResult<OrderResponse>> CreateOrder([FromBody] OrderRequest request, CancellationToken cancellationToken)
        {
            var response = await _orderService.CreateOrderAsync(request, cancellationToken);

            if (!response.Success)
            {
                _logger.LogWarning("Order creation failed: {Message}", response.Message);
                return BadRequest(response);
            }

            return Ok(response);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<OrderResponse>> GetOrder(int id, CancellationToken cancellationToken)
        {
            var response = await _orderService.GetOrderAsync(id, cancellationToken);
            if (response is null)
            {
                return NotFound(new OrderResponse { Success = false, Message = "Order not found." });
            }

            return Ok(response);
        }
    }
}
