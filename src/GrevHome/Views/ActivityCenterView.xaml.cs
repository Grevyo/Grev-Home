using System.Windows;
using System.Windows.Controls;
using GrevHome.Notifications;
using GrevHome.Transfers;

namespace GrevHome.Views;

public partial class ActivityCenterView : UserControl
{
    public event EventHandler? BackRequested;
    public event EventHandler? MarkAllNotificationsReadRequested;
    public event Action<string>? NotificationReadRequested;
    public event Action<string>? TransferCancelRequested;
    public event Action<string>? TransferRetryRequested;
    public event EventHandler? ClearFinishedTransfersRequested;

    public ActivityCenterView()
    {
        InitializeComponent();
        SetData(NotificationSnapshot.Empty, null, TransferSnapshot.Empty);
    }

    public void SetData(
        NotificationSnapshot notifications,
        string? grevId,
        TransferSnapshot transfers)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(transfers);

        NotificationSummaryText.Text = notifications.Items.Count == 0
            ? "No notifications yet."
            : notifications.UnreadCount == 0
                ? $"{notifications.Items.Count} recent notification{(notifications.Items.Count == 1 ? string.Empty : "s")} • all read"
                : $"{notifications.UnreadCount} unread notification{(notifications.UnreadCount == 1 ? string.Empty : "s")}";
        MarkAllReadButton.IsEnabled = notifications.UnreadCount > 0 && !string.IsNullOrWhiteSpace(grevId);

        NotificationsPanel.Children.Clear();
        if (notifications.Items.Count == 0)
        {
            NotificationsPanel.Children.Add(CreateEmptyText("System, app and transfer messages will appear here."));
        }
        else
        {
            foreach (var notification in notifications.Items.Take(8))
            {
                NotificationsPanel.Children.Add(CreateNotificationCard(notification, grevId));
            }
        }

        var terminalCount = transfers.Items.Count(item =>
            item.State is TransferState.Completed or TransferState.Failed or TransferState.Cancelled);
        TransferSummaryText.Text = transfers.Items.Count == 0
            ? "No transfers yet."
            : $"{transfers.ActiveCount} active • {transfers.QueuedCount} queued • {transfers.FailedCount} failed";
        ClearFinishedButton.IsEnabled = terminalCount > 0;

        TransfersPanel.Children.Clear();
        if (transfers.Items.Count == 0)
        {
            TransfersPanel.Children.Add(CreateEmptyText("App downloads and other Grev Home transfers will appear here."));
        }
        else
        {
            foreach (var transfer in transfers.Items.Take(12))
            {
                TransfersPanel.Children.Add(CreateTransferCard(transfer));
            }
        }
    }

    public void ShowStatus(string message)
    {
        ActivityStatusText.Text = message;
        ActivityStatusText.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    public void FocusInitial() => BackButton.Focus();

    private UIElement CreateNotificationCard(GrevNotification notification, string? grevId)
    {
        var isRead = string.IsNullOrWhiteSpace(grevId) || NotificationService.IsReadBy(notification, grevId);
        var border = CreateCardBorder();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = $"{FormatSeverity(notification.Severity)} • {notification.Source} • {FormatTime(notification.CreatedAtUtc)}",
            FontSize = 12,
            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush")
        });
        text.Children.Add(new TextBlock
        {
            Text = notification.Title,
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 17,
            FontWeight = isRead ? FontWeights.Normal : FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        text.Children.Add(new TextBlock
        {
            Text = notification.Message,
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 13,
            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        grid.Children.Add(text);

        if (!isRead)
        {
            var markRead = new Button
            {
                Content = "Mark read",
                Tag = notification.Id,
                Width = 110,
                Height = 38,
                Margin = new Thickness(16, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            markRead.Click += NotificationMarkRead_Click;
            Grid.SetColumn(markRead, 1);
            grid.Children.Add(markRead);
        }

        border.Child = grid;
        return border;
    }

    private UIElement CreateTransferCard(TransferItem transfer)
    {
        var border = CreateCardBorder();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = transfer.DisplayName,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        text.Children.Add(new TextBlock
        {
            Text = BuildTransferDetail(transfer),
            Margin = new Thickness(0, 5, 0, 0),
            FontSize = 13,
            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(transfer.ErrorMessage))
        {
            text.Children.Add(new TextBlock
            {
                Text = transfer.ErrorMessage,
                Margin = new Thickness(0, 5, 0, 0),
                FontSize = 12,
                Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush"),
                TextWrapping = TextWrapping.Wrap
            });
        }
        grid.Children.Add(text);

        Button? action = transfer.State switch
        {
            TransferState.Downloading or TransferState.Queued => new Button
            {
                Content = "Cancel",
                Tag = transfer.Id,
                Width = 100,
                Height = 38,
                Margin = new Thickness(16, 0, 0, 0)
            },
            TransferState.Failed or TransferState.Cancelled => new Button
            {
                Content = "Retry",
                Tag = transfer.Id,
                Width = 100,
                Height = 38,
                Margin = new Thickness(16, 0, 0, 0)
            },
            _ => null
        };

        if (action is not null)
        {
            action.VerticalAlignment = VerticalAlignment.Center;
            if (transfer.State is TransferState.Downloading or TransferState.Queued)
            {
                action.Click += TransferCancel_Click;
            }
            else
            {
                action.Click += TransferRetry_Click;
            }
            Grid.SetColumn(action, 1);
            grid.Children.Add(action);
        }

        border.Child = grid;
        return border;
    }

    private Border CreateCardBorder() => new()
    {
        Padding = new Thickness(14),
        Margin = new Thickness(0, 0, 0, 8),
        CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1),
        BorderBrush = (System.Windows.Media.Brush)FindResource("MutedBrush"),
        Opacity = 0.96
    };

    private TextBlock CreateEmptyText(string message) => new()
    {
        Text = message,
        Margin = new Thickness(0, 6, 0, 4),
        FontSize = 13,
        Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
        TextWrapping = TextWrapping.Wrap
    };

    private static string BuildTransferDetail(TransferItem transfer)
    {
        var state = transfer.State switch
        {
            TransferState.Queued => "Queued",
            TransferState.Downloading => "Downloading",
            TransferState.Completed => "Completed",
            TransferState.Failed => "Failed",
            TransferState.Cancelled => "Cancelled",
            _ => transfer.State.ToString()
        };

        if (transfer.TotalBytes is > 0)
        {
            var percent = Math.Clamp((double)transfer.BytesReceived / transfer.TotalBytes.Value * 100d, 0d, 100d);
            return $"{state} • {percent:0}% • {FormatBytes(transfer.BytesReceived)} / {FormatBytes(transfer.TotalBytes.Value)}";
        }

        return transfer.BytesReceived > 0
            ? $"{state} • {FormatBytes(transfer.BytesReceived)} received"
            : state;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L)
        {
            return $"{bytes / (1024d * 1024d * 1024d):0.##} GB";
        }
        if (bytes >= 1024L * 1024L)
        {
            return $"{bytes / (1024d * 1024d):0.##} MB";
        }
        if (bytes >= 1024L)
        {
            return $"{bytes / 1024d:0.##} KB";
        }
        return $"{bytes} B";
    }

    private static string FormatSeverity(NotificationSeverity severity) => severity switch
    {
        NotificationSeverity.Success => "SUCCESS",
        NotificationSeverity.Warning => "WARNING",
        NotificationSeverity.Error => "ERROR",
        _ => "INFO"
    };

    private static string FormatTime(DateTimeOffset value) => value.ToLocalTime().ToString("d MMM HH:mm");

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    private void MarkAllRead_Click(object sender, RoutedEventArgs e) => MarkAllNotificationsReadRequested?.Invoke(this, EventArgs.Empty);
    private void ClearFinished_Click(object sender, RoutedEventArgs e) => ClearFinishedTransfersRequested?.Invoke(this, EventArgs.Empty);

    private void NotificationMarkRead_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            NotificationReadRequested?.Invoke(id);
        }
    }

    private void TransferCancel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            TransferCancelRequested?.Invoke(id);
        }
    }

    private void TransferRetry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            TransferRetryRequested?.Invoke(id);
        }
    }
}
