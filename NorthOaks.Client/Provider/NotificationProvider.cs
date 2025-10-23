using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using NorthOaks.Shared.Model.Notifications;
using System.Net.Http.Json;

namespace NorthOaks.Client.Providers
{
    public class NotificationProvider : IAsyncDisposable
    {
        private HubConnection? _hubConnection;
        private string? _currentUserName;   // username (sub)
        private bool _isInitialized = false;
        private readonly NavigationManager _navigation;
        private readonly HttpClient _http;

        private int _unreadCount = 0;
        public int UnreadCount => _unreadCount;
        public List<NotificationDto> Messages { get; } = new();

        public event Action<string>? OnMessageReceived;
        public event Action? OnCountChanged;

        public NotificationProvider(NavigationManager navigation, HttpClient http)
        {
            _navigation = navigation;
            _http = http;
        }

        public async Task InitializeAsync(string userName)
        {
            if (_isInitialized || string.IsNullOrWhiteSpace(userName))
                return;

            _currentUserName = userName.Trim().ToLower();

            // === 1️⃣ Load unread notifications from API ===
            try
            {
                var offline = await _http.GetFromJsonAsync<List<NotificationDto>>("api/notifications/unread");
                if (offline != null && offline.Any())
                {
                    Messages.Clear();
                    Messages.AddRange(offline);
                    _unreadCount = offline.Count;
                    OnCountChanged?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Failed to load offline notifications: {ex.Message}");
            }

            // === 2️⃣ Set up SignalR connection ===
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(_navigation.ToAbsoluteUri("/hubs/notification"))
                .WithAutomaticReconnect()
                .Build();

            // Handle automatic rejoin after reconnect
            _hubConnection.Reconnected += async (connectionId) =>
            {
                if (!string.IsNullOrEmpty(_currentUserName))
                {
                    Console.WriteLine($" Reconnected — rejoining group: {_currentUserName}");
                    await _hubConnection.InvokeAsync("JoinUserGroup", _currentUserName);
                }
            };

            // Handle incoming notifications
            _hubConnection.On<NotificationMessage>("ReceiveNotification", (payload) =>
            {
                Console.WriteLine($"[DEBUG FRONTEND] payload.UserId = '{payload?.UserId}', currentUser = '{_currentUserName}'");

                if (payload == null) return;
                if (payload.UserId?.Trim().ToLower() == _currentUserName) return; // skip self

                Console.WriteLine($"📨 Notification received: {payload.Message}");

                var notification = new NotificationDto
                {
                    Message = payload.Message,
                    CreatedAt = payload.CreatedAt ?? DateTime.UtcNow,
                    IsRead = false
                };

                Messages.Insert(0, notification);
                _unreadCount++;

                OnMessageReceived?.Invoke(payload.Message);
                OnCountChanged?.Invoke();
            });

            // === 3️⃣ Connect and join SignalR group ===
            try
            {
                await _hubConnection.StartAsync();
                await _hubConnection.InvokeAsync("JoinUserGroup", _currentUserName);
                Console.WriteLine($" Connected and joined group: {_currentUserName}");
                await Task.Delay(300); // small delay ensures stable join
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($" Failed to connect NotificationHub: {ex.Message}");
                Console.WriteLine(" Continuing in offline mode (API polling only).");
            }
        }

        // === 4️⃣ Mark all notifications as read ===
        public async Task ClearUnread()
        {
            try
            {
                var response = await _http.PostAsync("api/notifications/mark-read", null);
                if (response.IsSuccessStatusCode)
                {
                    foreach (var msg in Messages)
                        msg.IsRead = true;
                    _unreadCount = 0;
                    OnCountChanged?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Failed to mark notifications as read: {ex.Message}");
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_hubConnection != null)
            {
                try
                {
                    await _hubConnection.StopAsync();
                    await _hubConnection.DisposeAsync();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($" Error disposing NotificationProvider: {ex.Message}");
                }
            }

            _isInitialized = false;
            _hubConnection = null;
        }
    }

    public class NotificationMessage
    {
        public string Message { get; set; } = string.Empty;
        public string? UserId { get; set; }   // username/sub
        public DateTime? CreatedAt { get; set; }
        public bool IsRead { get; set; }
    }
}
