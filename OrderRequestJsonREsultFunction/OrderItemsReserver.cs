using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using OrderDetails = Microsoft.eShopWeb.Web.ViewModels.OrderDetails;

public static class OrderItemsReserver
{
    [Function("OrderItemsReserver")]
    public static async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
        ILogger log)
    {
        // Deserialize request body to OrderDetails, which includes a list of BasketItemViewModel items
        var orderDetails = await JsonSerializer.DeserializeAsync<OrderDetails>(req.Body);

        // Convert order details to JSON (if needed)
        string orderJson = JsonSerializer.Serialize(orderDetails);


        // Retrieve the Blob Storage connection string from application settings
        string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
        var blobServiceClient = new BlobServiceClient(connectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient("orderrequestcontainer");

        // Create a blob client and upload the JSON file
        var blobClient = containerClient.GetBlobClient($"order-{orderDetails.OrderId}.json");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(orderJson)))
        {
            await blobClient.UploadAsync(stream, overwrite: true);
        }

        return new OkResult();
    }
}
