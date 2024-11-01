using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using OrderRequestJsonREsultFunction.Models;

public static class OrderItemsReserver
{
    [Function("OrderItemsReserver")]
    public static async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
        ILogger log)
    {
        // Convert rawRequestBody to a MemoryStream so it can be used with DeserializeAsync
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true // Still helpful for casing consistency
        };
        var orderDetails = await JsonSerializer.DeserializeAsync<OrderDetailsDto>(req.Body, options);

        if (orderDetails == null)
        {
            log?.LogError("Received empty or invalid order details.");
            return new BadRequestObjectResult("Invalid order details.");
        }

        string orderJson = JsonSerializer.Serialize(orderDetails);

        // Retrieve the Blob Storage connection string from application settings
        string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
        var blobServiceClient = new BlobServiceClient(connectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient("orderrequestcontainer");

        // Create a blob client and upload the JSON file
        var blobClient = containerClient.GetBlobClient($"order-{orderDetails.OrderId}.json");
        using (var uploadStream = new MemoryStream(Encoding.UTF8.GetBytes(orderJson)))
        {
            await blobClient.UploadAsync(uploadStream, overwrite: true);
        }
        return new OkResult();
    }
}
