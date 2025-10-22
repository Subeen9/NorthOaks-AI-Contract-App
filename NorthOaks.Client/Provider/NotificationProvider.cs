using BlazorBootstrap;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NorthOaks.Client.Providers
{
    public class NotificationProvider
    {
        private HubConnection? _hubConnection;
        private int _unreadCount = 0;
        private readonly NavigationManager _navigation;
        private readonly ToastService _toast;
        private readonly IJSRuntime _js;  //  For reading user ID from localStorage

        private string? _currentUserId;   //  Tracks this client’s user ID

        public int UnreadCount => _unreadCount;
        public List<string> Messages { get; } = new();
        public event Action? OnChange;

        public NotificationProvider(NavigationManager navigation, ToastService toast, IJSRuntime js)
        {
            _navigation = navigation;
            _toast = toast;
            _js = js;
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(_navigation.ToAbsoluteUri("/hubs/notification"))
                .WithAutomaticReconnect()
                .Build();

            //  Receive structured notification payloads (message + senderId)
            _hubConnection.On<NotificationMessage>("ReceiveNotification", (payload) =>
            {
                // Skip showing if this client triggered it
                if (payload.UserId == _currentUserId)
                    return;

                Messages.Insert(0, payload.Message);
                _unreadCount++;
                _toast.Notify(new(ToastType.Info, payload.Message));
                OnChange?.Invoke();
            });

            await _hubConnection.StartAsync();

            //  Retrieve user ID from localStorage (set after login)
            _currentUserId = await _js.InvokeAsync<string>("localStorage.getItem", "userId");

            // Join this user's SignalR group
            if (!string.IsNullOrEmpty(_currentUserId))
                await _hubConnection.InvokeAsync("JoinUserGroup", _currentUserId);
        }

        public void ClearUnread()
        {
            _unreadCount = 0;
            OnChange?.Invoke();
        }
    }

    //  DTO for incoming notifications
    public class NotificationMessage
    {
        public string Message { get; set; } = string.Empty;
        public string? UserId { get; set; }
    }
}
