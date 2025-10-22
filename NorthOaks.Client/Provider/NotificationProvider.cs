using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NorthOaks.Client.Providers
{
    public class NotificationProvider : IAsyncDisposable
    {
        private HubConnection? _hubConnection;
        private string? _currentUserId;
        private bool _isInitialized = false;
        private readonly NavigationManager _navigation;

        private int _unreadCount = 0;
        public int UnreadCount => _unreadCount;
        public List<string> Messages { get; } = new();

        public event Action<string>? OnMessageReceived;
        public event Action? OnCountChanged;

        public NotificationProvider(NavigationManager navigation)
        {
            _navigation = navigation;
        }

        // Called from MainLayout after login
        public async Task InitializeAsync(string userId)
        {
            // Guard: don’t reinitialize or start with no user
            if (_isInitialized || string.IsNullOrWhiteSpace(userId))
                return;

            _currentUserId = userId;

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(_navigation.ToAbsoluteUri("/hubs/notification"))
                .WithAutomaticReconnect()
                .Build();

            // When backend pushes a notification
            _hubConnection.On<NotificationMessage>("ReceiveNotification", (payload) =>
            {
                if (payload == null)
                    return;

                // Skip own actions
                if (!string.IsNullOrEmpty(payload.UserId) && payload.UserId == _currentUserId)
                    return;

                Messages.Insert(0, payload.Message);
                _unreadCount++;

                // Raise events to UI
                OnMessageReceived?.Invoke(payload.Message);
                OnCountChanged?.Invoke();
            });

            try
            {
                await _hubConnection.StartAsync();
                await _hubConnection.InvokeAsync("JoinUserGroup", _currentUserId);
                Console.WriteLine($"✅ Joined SignalR notification group for user {_currentUserId}");
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌ Failed to connect NotificationHub: {ex.Message}");
            }
        }

        // Clears unread count when bell is opened
        public void ClearUnread()
        {
            _unreadCount = 0;
            OnCountChanged?.Invoke();
        }

        // Gracefully disconnect when app is disposed or user logs out
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_hubConnection != null)
                {
                    await _hubConnection.StopAsync();
                    await _hubConnection.DisposeAsync();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"⚠️ Error disposing NotificationProvider: {ex.Message}");
            }

            _isInitialized = false;
            _hubConnection = null;
        }
    }

    // === Payload type from backend ===
    public class NotificationMessage
    {
        public string Message { get; set; } = string.Empty;
        public string? UserId { get; set; }  // senderId from backend
    }
}
