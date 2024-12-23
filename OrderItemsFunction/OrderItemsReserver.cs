using System;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OrderItemsFunction.Models;
namespace OrderItemsFunction
{
    public class OrderItemsReserver
    {
        private readonly ILogger<OrderItemsReserver> _logger;
        private readonly IConfiguration _configuration;
        private const string _blobContainerClientName = "orderrequestcontainer";
        private const string TopicName = "orders";
        private const string SubscriptionName = "lobanovSubscription";
        private readonly ServiceBusClient _serviceBusClient;
        private string serviceBusConnectionString;

        public OrderItemsReserver(ILogger<OrderItemsReserver> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            serviceBusConnectionString = _configuration["ServiceBusConnectionString"];
            _serviceBusClient = new ServiceBusClient(serviceBusConnectionString);
        }

        [Function(nameof(OrderItemsReserver))]
        public async Task<IActionResult> Run(
            [ServiceBusTrigger(TopicName, SubscriptionName, Connection = "ServiceBusConnectionString")]
            string message)
        {

            try
            {
                // Deserialize the incoming JSON string to OrderDetailsDto
                var orderDetails = JsonSerializer.Deserialize<OrderDetailsDto>(message, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (orderDetails == null)
                {
                    _logger.LogError("Invalid order details. Cannot process null order.");
                    return new BadRequestResult();
                }

                _logger.LogInformation("Deserialized OrderDetailsDto: OrderId={OrderId}, TotalAmount={TotalAmount}", orderDetails.OrderId, orderDetails.TotalAmount);

                // Serialize the object back into JSON for Blob Storage upload
                var orderJson = JsonSerializer.Serialize(orderDetails);

                // Upload logic with retry policy
                var success = await UploadToBlobWithRetryAsync(orderDetails);
                if (!success)
                {
                    await CallLogicAppIfBlobContainerFailedUpload(orderDetails);
                }
            }
            finally
            {
                // Calling DisposeAsync on client types is required to ensure that network
                // resources and other unmanaged objects are properly cleaned up.
                // await serviceBusProcessor.DisposeAsync();
                await _serviceBusClient.DisposeAsync();
            }

            return new OkResult();
        }

        private async Task<bool> UploadToBlobWithRetryAsync(OrderDetailsDto orderDetails)
        {
            const int maxRetries = 3;
            int retryCount = 0;
            bool isUploaded = false;

            while (retryCount < maxRetries && !isUploaded)
            {
                try
                {
                    // throw new Exception("Oops, the blob container is not accessible");
                    // Upload logic here
                    var connectionString = _configuration["BlobStorageCs"];
                    var blobServiceClient = new BlobServiceClient(connectionString);
                    var containerClient = blobServiceClient.GetBlobContainerClient(_blobContainerClientName);

                    await containerClient.CreateIfNotExistsAsync();
                    var blobClient = containerClient.GetBlobClient($"order-{orderDetails.OrderId}.json");

                    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(orderDetails)));
                    await blobClient.UploadAsync(stream, overwrite: true);

                    isUploaded = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to upload blob. Retrying...");
                    retryCount++;
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }

            return isUploaded;
        }

        private async Task CallLogicAppIfBlobContainerFailedUpload(OrderDetailsDto orderDetails)
        {
            string logicAppUrl = _configuration["LogicAppWebhookUrl"];
            var payload = new
            {
                OrderId = orderDetails.OrderId,
                ItemId = orderDetails.Items.First().ItemId,
                Quantity = orderDetails.Items.First().Quantity
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, encoding: Encoding.UTF8, "application/json");
            using var httpClient = new HttpClient();
            var response = await httpClient.PostAsync(logicAppUrl, content);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation($"Successfully triggered Logic App for order {orderDetails.OrderId}");
            }
            else
            {
                _logger.LogError($"Failed to trigger Logic App. Status code: {response.StatusCode}");
            }
        }
    }
}
