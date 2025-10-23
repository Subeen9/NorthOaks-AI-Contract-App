using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using NorthOaks.Client;
using NorthOaks.Client.Providers;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddScoped(sp =>
{
    var navigation = sp.GetRequiredService<NavigationManager>();
    var jsRuntime = sp.GetRequiredService<IJSRuntime>();

    var handler = new NorthOaks.Client.Providers.AuthMessageHandler(jsRuntime)
    {
        InnerHandler = new HttpClientHandler()
    };

    var client = new HttpClient(handler)
    {
        BaseAddress = new Uri(navigation.BaseUri)
    };

    return client;
});


builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();
builder.Services.AddAuthorizationCore();
builder.Services.AddBlazorBootstrap();

//Notification
builder.Services.AddSingleton<NotificationProvider>();

await builder.Build().RunAsync();