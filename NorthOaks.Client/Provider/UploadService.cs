namespace NorthOaks.Client.Providers
{
    public static class UploadService
    {
        public static event Action? UploadRequested;

        public static void RaiseUpload()
        {
            UploadRequested?.Invoke();
        }
    }
}

