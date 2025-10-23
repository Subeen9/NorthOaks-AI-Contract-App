using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using NorthOaks.Shared.Model.Notifications; // <-- Uses shared DTO
using System.Net.Http.Json;

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
        public List<NotificationDto> Messages { get; } = new();

        public event Action<string>? OnMessageReceived;
        public event Action? OnCountChanged;

        public NotificationProvider(NavigationManager navigation)
        {
            _navigation = navigation;
        }

        // Initialize notifications for current user
        public async Task InitializeAsync(string userId)
        {
            if (_isInitialized || string.IsNullOrWhiteSpace(userId))
                return;

            _currentUserId = userId;

            // === 1️⃣ Load unread notifications from API (offline support)
            try
            {
                using var http = new HttpClient { BaseAddress = new Uri(_navigation.BaseUri) };
                var offline = await http.GetFromJsonAsync<List<NotificationDto>>($"api/notifications/unread/{userId}");

                if (offline != null && offline.Any())
                {
                    Messages.Clear();
                    foreach (var n in offline)
                        Messages.Insert(0, n);

                    _unreadCount = offline.Count;
                    OnCountChanged?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to load offline notifications: {ex.Message}");
            }

            // === 2️⃣ Set up SignalR live notifications
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(_navigation.ToAbsoluteUri("/hubs/notification"))
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<NotificationMessage>("ReceiveNotification", (payload) =>
            {
                Console.WriteLine("--- NOTIFICATION RECEIVED ---");
                Console.WriteLine($"Payload UserID (from server): '{payload?.UserId}'");
                Console.WriteLine($"Client UserID (from MainLayout): '{_currentUserId}'");
                // --- END DEBUGGING CODE ---
                if (payload == null || payload.UserId == _currentUserId)
                    return;

                var notification = new NotificationDto
                {
                    Message = payload.Message,
                    TargetUserId = int.TryParse(payload.UserId, out var uid) ? uid : 0,
                    CreatedAt = DateTime.UtcNow,
                    IsRead = false
                };

                Messages.Insert(0, notification);
                _unreadCount++;

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

        // === 3️⃣ Mark all notifications as read (called from MainLayout bell)
        public async Task ClearUnread()
        {
            if (string.IsNullOrEmpty(_currentUserId)) return;

            try
            {
                using var http = new HttpClient { BaseAddress = new Uri(_navigation.BaseUri) };
                await http.PostAsync($"api/notifications/mark-read/{_currentUserId}", null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to mark notifications as read: {ex.Message}");
            }

            foreach (var msg in Messages)
                msg.IsRead = true;

            _unreadCount = 0;
            OnCountChanged?.Invoke();
        }

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

    // Payload from SignalR hub
    public class NotificationMessage
    {
        public string Message { get; set; } = string.Empty;
        public string? UserId { get; set; }
    }
}
