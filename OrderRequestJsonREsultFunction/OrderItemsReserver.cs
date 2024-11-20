using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using OrderRequestJsonResultFunction.Models;

namespace OrderRequestJsonResultFunction
{
    public class OrderItemsReserver
    {
        private const string _blobContainerClientName = "orderrequestcontainer";
        private const string TopicName = "orderitemreserver";
        private const string SubscriptionName = "eShopWebFunction";
        private readonly ServiceBusClient _serviceBusClient;
        private string serviceBusConnectionString;
        private readonly ILogger<OrderItemsReserver> _logger;

        public OrderItemsReserver(ILogger<OrderItemsReserver> logger)
        {
            serviceBusConnectionString = Environment.GetEnvironmentVariable("ServiceBusConnectionString") ?? "Endpoint=sb://sbforeshopbwebapp.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=Flyc98YlQsIZVOTMXQQkJ1tCnzWyi10bx+ASbHIoBOg=";
            _serviceBusClient = new ServiceBusClient(serviceBusConnectionString);
            _logger = logger;
        }

        [Function("OrderItemsReserver")]
        public async Task<IActionResult> Run(
            [ServiceBusTrigger(TopicName, SubscriptionName, Connection = "ServiceBusConnectionString")]  string message)
        {
            // var serviceBusProcessor = _serviceBusClient.CreateProcessor(TopicName, SubscriptionName, new ServiceBusProcessorOptions());

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

                // Retrieve the Blob Storage connection string
                var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
                var blobServiceClient = new BlobServiceClient(connectionString);
                var containerClient = blobServiceClient.GetBlobContainerClient(_blobContainerClientName);

                // Ensure the container exists
                await containerClient.CreateIfNotExistsAsync();
                
                // Create a blob with a unique name
                var blobClient = containerClient.GetBlobClient($"order-{orderDetails.OrderId}.json");

                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(orderJson)))
                {
                    await blobClient.UploadAsync(stream, overwrite: true);
                }

                // serviceBusProcessor.ProcessMessageAsync += MessageHandler;
                // serviceBusProcessor.ProcessErrorAsync += ErrorHandler;
                
                // start processing
                //await serviceBusProcessor.StartProcessingAsync();

                // stop processing
                // await serviceBusProcessor.StopProcessingAsync();
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
        
        // handle received messages
        private async Task MessageHandler(ProcessMessageEventArgs args)
        {
            var body = args.Message.Body.ToString();
            Console.WriteLine($"Received: {body} from subscription: {TopicName}");

            // complete the message. messages is deleted from the subscription. 
            await args.CompleteMessageAsync(args.Message);
        }

        // handle any errors when receiving messages
        private Task ErrorHandler(ProcessErrorEventArgs args)
        {
            Console.WriteLine(args.Exception.ToString());
            return Task.CompletedTask;
        }
    }
}
