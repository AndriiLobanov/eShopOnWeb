using System.Configuration;
using System.Net.Http.Headers;
using System.Text.Json;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Web.FunctionModels;
using Microsoft.eShopWeb.Web.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Basket;

[Authorize]
public class CheckoutModel : PageModel
{
    private readonly IBasketService _basketService;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IOrderService _orderService;
    private string? _username = null;
    private readonly IBasketViewModelService _basketViewModelService;
    private readonly IAppLogger<CheckoutModel> _logger;
    private readonly IConfiguration _configuration;
    private string functionUrl;
    private string functionKey;
    public CheckoutModel(IBasketService basketService,
        IBasketViewModelService basketViewModelService,
        SignInManager<ApplicationUser> signInManager,
        IOrderService orderService,
        IAppLogger<CheckoutModel> logger,
        IConfiguration configuration)
    {
        _basketService = basketService;
        _signInManager = signInManager;
        _orderService = orderService;
        _basketViewModelService = basketViewModelService;
        _logger = logger;
        _configuration = configuration;
        functionUrl = _configuration["OrderItemsReserverUrl"];
        functionKey = _configuration["FunctionKey"];
    }

    public BasketViewModel BasketModel { get; set; } = new BasketViewModel();

    public async Task OnGet()
    {
        await SetBasketModelAsync();
    }

    public async Task<IActionResult> OnPost(IEnumerable<BasketItemViewModel> items)
    {
        try
        {
            await SetBasketModelAsync();

            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var updateModel = items.ToDictionary(b => b.Id.ToString(), b => b.Quantity);
            await _basketService.SetQuantities(BasketModel.Id, updateModel);
            await _orderService.CreateOrderAsync(BasketModel.Id,
                new Address("123 Main St.", "Kent", "OH", "United States", "44240"));
            
            var order = await _orderService.GetOrderAsync(BasketModel.Id);
            await ProcessObjectSendingToAzureFunctionAsync(order, items);
            await _basketService.DeleteBasketAsync(BasketModel.Id);
        }
        catch (EmptyBasketOnCheckoutException emptyBasketOnCheckoutException)
        {
            //Redirect to Empty Basket page
            _logger.LogWarning(emptyBasketOnCheckoutException.Message);
            return RedirectToPage("/Basket/Index");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex.Message);
            return RedirectToPage("/Basket/Index");
        }
        return RedirectToPage("Success");
    }

    private async Task SetBasketModelAsync()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        if (_signInManager.IsSignedIn(HttpContext.User))
        {
            BasketModel = await _basketViewModelService.GetOrCreateBasketForUser(User.Identity.Name);
        }
        else
        {
            GetOrSetBasketCookieAndUserName();
            BasketModel = await _basketViewModelService.GetOrCreateBasketForUser(_username!);
        }
    }

    private void GetOrSetBasketCookieAndUserName()
    {
        if (Request.Cookies.ContainsKey(Constants.BASKET_COOKIENAME))
        {
            _username = Request.Cookies[Constants.BASKET_COOKIENAME];
        }
        if (_username != null) return;

        _username = Guid.NewGuid().ToString();
        var cookieOptions = new CookieOptions();
        cookieOptions.Expires = DateTime.Today.AddYears(10);
        Response.Cookies.Append(Constants.BASKET_COOKIENAME, _username, cookieOptions);
    }

    private async Task ProcessObjectSendingToAzureFunctionAsync(Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order order, IEnumerable<BasketItemViewModel> items)
    { 
        using  var httpClient = new HttpClient();
        var orderDetails = new OrderDetailsCosmos
        {
            Id = Guid.NewGuid().ToString(),
            FinalPrice = order.Total(),
            OrderId = order.Id.ToString(),
            ShippingAddress = string.Join(',', order.ShipToAddress.City, order.ShipToAddress.Country,
                order.ShipToAddress.State, order.ShipToAddress.Street),
            Items = order.OrderItems.Select(x => new OrderItemCosmos
            {
                ItemName = x.ItemOrdered.ProductName,
                Units = x.Units,
                UnitPrice = x.UnitPrice
            }).ToList() // Convert the IEnumerable to List
        };

        // Check if Items is empty, if so assign a new list
        if (orderDetails.Items.Count == 0)
        {
            orderDetails.Items = new List<OrderItemCosmos>();
        }

        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await httpClient.PostAsJsonAsync(functionUrl, orderDetails, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });;

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(); // Read error content
            _logger.LogWarning("Failed to reserve order items. Status: {StatusCode}, Response: {ResponseContent}", response.StatusCode, errorContent);
    
            // Throw a more general HttpRequestException with detailed information
            throw new HttpRequestException($"Request to reserve order items failed with status code {response.StatusCode}. Response: {errorContent}");
        }
    }
}
