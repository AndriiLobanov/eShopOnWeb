using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OrderItemsToCosmos.Models;
namespace OrderItemsToCosmos
{
    public class OrderEshopItemToCosmos
    {
        private static readonly string cosmosEndpointString = Environment.GetEnvironmentVariable("CosmosEndpointString");
        private static readonly string cosmosKeyString = Environment.GetEnvironmentVariable("CosmosKeyString");

        private readonly ILogger<OrderEshopItemToCosmos> _logger;
        private readonly IConfiguration _configuration;
        private readonly CosmosClient _cosmosClient;
        private readonly string cosmosDatabaseName = "EshopWebLobData";
        private readonly string cosmosContainerName = "OrderData";

        private Container _cosmosContainer;
        private Database _cosmosDatabase;
        public OrderEshopItemToCosmos(ILogger<OrderEshopItemToCosmos> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _cosmosClient = new CosmosClient(cosmosEndpointString, cosmosKeyString);
        }

        [Function("OrderEshopItemToCosmos")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true // Still helpful for casing consistency
            };
            var order = await JsonSerializer.DeserializeAsync<OrderDetailsCosmos>(req.Body, options);

            if (order == null)
            {
                _logger.LogError("Invalid order details.");
                return new BadRequestObjectResult("Invalid order details.");
            }
            order.PartitionKey = "PartitionKeyValue"; 
            try
            {
                _cosmosDatabase =  await _cosmosClient.CreateDatabaseIfNotExistsAsync(cosmosDatabaseName);
                _cosmosContainer = await _cosmosDatabase.CreateContainerIfNotExistsAsync(cosmosContainerName, "/PartitionKey");

                var response = await _cosmosContainer.CreateItemAsync(order, new PartitionKey(order.PartitionKey));
        
                if (response is null &&
                    response.StatusCode != HttpStatusCode.OK && 
                    response.StatusCode != HttpStatusCode.Created && 
                    response.StatusCode != HttpStatusCode.Accepted)
                {
                    _logger.LogError("Failed to create order in CosmosDB.");
                    return new ObjectResult("Failed to create order in CosmosDB.") 
                    { 
                        StatusCode = (int)HttpStatusCode.InternalServerError 
                    };
                }

                // Return a success message with the created order
                return new OkObjectResult(new
                {
                    message = "Order created successfully.",
                    orderId = order.OrderId,
                    statusCode = response.StatusCode
                });
            }
            catch (CosmosException ex)
            {
                _logger.LogError($"CosmosDB error: {ex.Message}");
                // Handle CosmosDB-specific exceptions and return appropriate status
                return new ObjectResult($"CosmosDB error: {ex.Message}") 
                { 
                    StatusCode = (int)ex.StatusCode 
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"An unexpected error occurred: {ex.Message}");
                // Handle any other unexpected exceptions
                return new ObjectResult($"An unexpected error occurred: {ex.Message}") 
                { 
                    StatusCode = (int)HttpStatusCode.InternalServerError 
                };
            }
        }
    }
}
