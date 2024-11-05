using Newtonsoft.Json;

namespace OrderItemsToCosmos.Models;

public class OrderDetailsCosmos
{
    [JsonProperty("id")] // This is important for Cosmos DB
    public string Id { get; set; }
    public string OrderId { get; set; }
    public string ShippingAddress { get; set; }
    public List<OrderItemCosmos> Items { get; set; }
    public decimal FinalPrice { get; set; }
    public string? PartitionKey { get; set; } = null;
}
