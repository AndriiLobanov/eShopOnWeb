using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace OrderItemsToCosmos
{
    public class OrderEshopItemToCosmos
    {
        private readonly ILogger<OrderEshopItemToCosmos> _logger;

        public OrderEshopItemToCosmos(ILogger<OrderEshopItemToCosmos> logger)
        {
            _logger = logger;
        }

        [Function("OrderEshopItemToCosmos")]
        public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");
            return new OkObjectResult("Welcome to Azure Functions!");
        }
    }
}
