namespace OrderItemsFunction.Models;

public class OrderDetailsDto
{
    public int OrderId { get; set; }
    public DateTime OrderDate { get; set; }
    public decimal TotalAmount { get; set; }
    public List<ItemDto> Items { get; set; }
}
