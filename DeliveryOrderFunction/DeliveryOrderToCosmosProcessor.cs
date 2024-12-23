using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using DeliveryOrderFunction.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
namespace DeliveryOrderFunction
{
    public class DeliveryOrderToCosmosProcessor
    {
        private readonly ILogger<DeliveryOrderToCosmosProcessor> _logger;
        private readonly string cosmosConnectionString;

        private readonly IConfiguration _configuration;
        private readonly CosmosClient _cosmosClient;
        private readonly string cosmosDatabaseName = "WebShobDelivery";
        private readonly string cosmosContainerName = "OrderData";

        private Container _cosmosContainer;
        private Database _cosmosDatabase;
        public DeliveryOrderToCosmosProcessor(ILogger<DeliveryOrderToCosmosProcessor> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            cosmosConnectionString = _configuration["COSMOS_CONNECTION_STRING"];

            _cosmosClient = new CosmosClient(cosmosConnectionString);
        }

        [Function("DeliveryOrder")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {
            _logger.LogInformation("Received new order details to be saved to CosmosDB");

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true // Still helpful for casing consistency
            };
            var order = await JsonSerializer.DeserializeAsync<OrderDetailsCosmos>(req.Body, options);

            if (order == null)
            {
                _logger.LogError("Invalid order details received.");
                return new BadRequestObjectResult("Invalid order details.");
            }

            string partitionKey = "PartitionKeyValue";
            
            try
            {
                _logger.LogInformation("Creating CosmosDB database and container if they don't exist");
                _cosmosDatabase = await _cosmosClient.CreateDatabaseIfNotExistsAsync(cosmosDatabaseName);
                _cosmosContainer = await _cosmosDatabase.CreateContainerIfNotExistsAsync(cosmosContainerName, "/PartitionKey");
                order.PartitionKey = partitionKey;
                _logger.LogInformation("Saving order details to CosmosDB");
                var response = await _cosmosContainer.CreateItemAsync(order, new PartitionKey(partitionKey));

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
                _logger.LogInformation("Order created successfully in CosmosDB");
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
