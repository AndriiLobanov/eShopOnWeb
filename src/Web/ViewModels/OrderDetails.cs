using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Web.Pages.Basket;

namespace Microsoft.eShopWeb.Web.ViewModels;
public class OrderDetails
{
    public int OrderId { get; set; } // Add if an Order ID is needed for identification
    public List<BasketItemViewModel> Items { get; set; } = new List<BasketItemViewModel>();
    public DateTime OrderDate { get; set; } // Optional: could represent when the order was placed
    public decimal TotalAmount { get; set; } // Optional: calculate based on item prices and quantities
}

