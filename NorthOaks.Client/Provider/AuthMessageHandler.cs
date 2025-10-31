using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace NorthOaks.Client.Providers
{
    public class AuthMessageHandler : DelegatingHandler
    {
        private readonly IJSRuntime _jsRuntime;

        public AuthMessageHandler(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                var token = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "authToken");

                if (!string.IsNullOrWhiteSpace(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
            }
            catch
            {
                // ignore any localStorage errors (first run etc.)
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
