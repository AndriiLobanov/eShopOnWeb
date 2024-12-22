namespace Microsoft.eShopWeb.Web.FunctionModels;

public class OrderDetailsCosmos
{
    public string Id { get; set; }
    public string OrderId { get; set; }
    public string ShippingAddress { get; set; }
    public List<OrderItemCosmos> Items { get; set; }
    public decimal FinalPrice { get; set; }
}
