using System.Text.Json;
using System.Net.Http.Headers;
using Ardalis.GuardClauses;
using Azure.Messaging.ServiceBus;
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
    private const string TopicName = "";
    private readonly IBasketService _basketService;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IOrderService _orderService;
    private string? _username = null;
    private readonly IBasketViewModelService _basketViewModelService;
    private readonly IAppLogger<CheckoutModel> _logger;
    private readonly IConfiguration _configuration;
    private readonly ServiceBusClient _serviceBusClient;
    private string _serviceBusConnectionString;
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
        _serviceBusConnectionString = _configuration["ServiceBusConnectionString"] ?? throw new InvalidOperationException();
        _serviceBusClient = new ServiceBusClient(_serviceBusConnectionString);
        functionUrl = _configuration["DeliveryOrderFunctionUrl"];
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
            await _orderService.CreateOrderAsync(BasketModel.Id, new Address("123 Main St.", "Kent", "OH", "United States", "44240"));
            // Calculate total amount by summing the price * quantity for each item
            var totalAmount = items.Sum(item => item.UnitPrice * item.Quantity);

            var orderDetailsDto = new OrderDetailsDto
            {
                OrderId = BasketModel.Id,
                OrderDate = DateTime.UtcNow,
                TotalAmount = totalAmount,
                Items = items.Select(item => new ItemDto
                {
                    ItemId = item.Id,
                    Quantity = item.Quantity
                }).ToList()
            };

            var orderDetailsJson = JsonSerializer.Serialize(orderDetailsDto);
            _logger.LogInformation("Serialized OrderDetailsDto: {OrderDetailsJson}", orderDetailsJson);
            await SendToServiceBusAsync(orderDetailsJson);
            await ProcessObjectSendingToDeliveryOrderAzureFunctionAsync(orderDetailsDto, items);
            await _basketService.DeleteBasketAsync(BasketModel.Id);
        }
        catch (EmptyBasketOnCheckoutException emptyBasketOnCheckoutException)
        {
            //Redirect to Empty Basket page
            _logger.LogWarning(emptyBasketOnCheckoutException.Message);
            return RedirectToPage("/Basket/Index");
        }

        return RedirectToPage("Success");
    }

    private async Task SendToServiceBusAsync(string orderDetailsJson)
    {
        ServiceBusSender sender = _serviceBusClient.CreateSender(TopicName);
        using ServiceBusMessageBatch messageBatch = await sender.CreateMessageBatchAsync();
        if (!messageBatch.TryAddMessage(new ServiceBusMessage(orderDetailsJson)))
        {
            throw new Exception($"The message is too large to fit in the batch.");
        }

        try
        {
            await sender.SendMessagesAsync(messageBatch);
        }
        finally
        {
            // Calling DisposeAsync on client types is required to ensure that network
            // resources and other unmanaged objects are properly cleaned up.
            await sender.DisposeAsync();
            await _serviceBusClient.DisposeAsync();
        }
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

    private async Task ProcessObjectSendingToDeliveryOrderAzureFunctionAsync(OrderDetailsDto order, IEnumerable<BasketItemViewModel> items)
    {
        _logger.LogInformation("Beginning process to send order details to Azure Function");

        using var httpClient = new HttpClient();
        // Check if Items is empty, if so assign a new list
        if (order.Items.Count == 0)
        {
            _logger.LogInformation("Order details do not contain any order items, assigning a new list");
            order.Items = new List<ItemDto>();
        }

        if (!string.IsNullOrEmpty(functionKey))
        {
            _logger.LogInformation("Adding function key to the function URL");
            functionUrl = functionUrl + (functionUrl.Contains("?") ? "&" : "?") + $"code={functionKey}";
        }

        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _logger.LogInformation("Sending order details to Azure Function at {FunctionUrl}", functionUrl);
        var response = await httpClient.PostAsJsonAsync(functionUrl, order, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(); // Read error content
            _logger.LogWarning("Failed to reserve order items. Status: {StatusCode}, Response: {ResponseContent}", response.StatusCode, errorContent);

            // Throw a more general HttpRequestException with detailed information
            throw new HttpRequestException($"Request to reserve order items failed with status code {response.StatusCode}. Response: {errorContent}");
        }

        _logger.LogInformation("Order details successfully sent to Azure Function");

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
}
