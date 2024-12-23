using Newtonsoft.Json;

namespace DeliveryOrderFunction.Models;

public class OrderDetailsCosmos
{
    [JsonProperty("id")]
    public string Id { get; set; }
    public string OrderId { get; set; }
    public string ShippingAddress { get; set; }
    public List<OrderItemCosmos> Items { get; set; }
    public decimal FinalPrice { get; set; }
    public string? PartitionKey { get; set; } = null;
}
