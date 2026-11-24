using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Cato;

/// Mirrors WhatsApp Desktop's Windows toasts into the notch. Polls the Action Center because
/// the NotificationChanged event needs package identity. Runs on the UI thread (awaits resume there).
static class ChatNotifications
{
    public static async void Start(Action<string, string> onMessage, Action onDenied)
    {
        try
        {
            var listener = UserNotificationListener.Current;
            if (await listener.RequestAccessAsync() != UserNotificationListenerAccessStatus.Allowed) { onDenied(); return; }
            var seen = new HashSet<uint>();
            var first = true; // the first pass only records what was already there
            while (true)
            {
                var all = await listener.GetNotificationsAsync(NotificationKinds.Toast);
                var fresh = all.Where(n => !seen.Contains(n.Id)).ToList();
                seen = all.Select(n => n.Id).ToHashSet(); // forget dismissed ones: the set cannot grow forever
                if (!first)
                    foreach (var n in fresh)
                    {
                        if (n.AppInfo?.AppUserModelId is not { } id || !id.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase)) continue;
                        var t = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements().Select(x => x.Text).ToArray();
                        if (t is { Length: > 0 }) onMessage(t[0], string.Join(" ", t.Skip(1)));
                    }
                first = false;
                await Task.Delay(1500);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException) { Debug.WriteLine(ex); onDenied(); }
    }
}
