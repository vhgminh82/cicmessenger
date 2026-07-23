using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Squiggle.Client;
using Squiggle.Client.Activities;
using Squiggle.Core.Chat.Activity;
using Squiggle.FileTransfer;

namespace Squiggle.UI.Services;

public class FileReceivedEventArgs : EventArgs
{
    public IBuddy Buddy { get; init; } = null!;
    public string FilePath { get; init; } = "";
    public string FileName { get; init; } = "";
}

public class FileTransferProgressEventArgs : EventArgs
{
    public IBuddy Buddy { get; init; } = null!;
    public string Message { get; init; } = "";
}

/// <summary>
/// Listens for incoming file offers on every chat session, independently of whether a chat
/// window happens to be open. Subscribing from the chat window alone was unreliable: the
/// window is created on a later UI tick, so an offer arriving in that gap was dropped and
/// the sender waited forever.
/// </summary>
public class FileTransferCoordinator
{
    readonly ConditionalWeakTable<IChat, object> _attached = new();

    public event EventHandler<FileReceivedEventArgs>? FileReceived;
    public event EventHandler<FileTransferProgressEventArgs>? Progress;

    public void Observe(IChatClient chatClient)
    {
        // Subscribe synchronously — no dispatcher hop — so no offer can slip through.
        chatClient.ChatStarted += (_, e) => Attach(e.Chat, e.Buddies);
    }

    public void Attach(IChat chat, IEnumerable<IBuddy>? buddies = null)
    {
        if (chat == null || _attached.TryGetValue(chat, out _))
            return;

        _attached.Add(chat, new object());
        chat.ActivityInvitationReceived += OnActivityInvitationReceived;
    }

    void OnActivityInvitationReceived(object? sender, ActivityInvitationReceivedEventArgs e)
    {
        if (e.ActivityId != SquiggleActivities.FileTransfer)
            return;

        var handler = new FileTransferActivity().FromInvite(e.Executor, e.Metadata);
        if (handler is not IFileTransferHandler transfer)
            return;

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var dialogService = App.Services.GetRequiredService<IDialogService>();
                var prompt = $"{e.Buddy.DisplayName} muốn gửi cho bạn file \"{transfer.Name}\" ({FormatSize(transfer.Size)}). Bạn có nhận không?";

                var answer = await dialogService.ShowMessageBoxAsync("CICMessenger", prompt, MessageBoxButton.YesNo);
                if (answer != MessageBoxResult.Yes)
                {
                    transfer.Cancel();
                    Report(e.Buddy, $"Đã từ chối nhận file: {transfer.Name}");
                    return;
                }

                var savePath = BuildDownloadPath(transfer.Name);

                transfer.TransferCompleted += (_, _) =>
                {
                    Report(e.Buddy, $"Đã nhận file: {savePath}");
                    Dispatcher.UIThread.Post(() => FileReceived?.Invoke(this, new FileReceivedEventArgs
                    {
                        Buddy = e.Buddy,
                        FilePath = savePath,
                        FileName = transfer.Name
                    }));
                };
                transfer.TransferCancelled += (_, _) => Report(e.Buddy, $"Đã hủy nhận file: {transfer.Name}");
                transfer.Error += (_, err) => Report(e.Buddy, $"Lỗi nhận file {transfer.Name}: {err.GetException().Message}");

                Report(e.Buddy, $"Đang nhận file: {transfer.Name}");
                transfer.Accept(savePath);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Handling incoming file offer failed");
            }
        });
    }

    void Report(IBuddy buddy, string message) =>
        Dispatcher.UIThread.Post(() => Progress?.Invoke(this,
            new FileTransferProgressEventArgs { Buddy = buddy, Message = message }));

    /// <summary>
    /// Picks a writable, non-colliding path inside the configured downloads folder. The
    /// name has already had any directory part stripped by FileInviteData.
    /// </summary>
    public static string BuildDownloadPath(string fileName)
    {
        var settings = App.Services.GetRequiredService<SettingsService>().Load();
        var folder = settings.GeneralSettings.DownloadsFolder;

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        Directory.CreateDirectory(folder);

        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "received_file";

        var candidate = Path.Combine(folder, safeName);
        var baseName = Path.GetFileNameWithoutExtension(safeName);
        var ext = Path.GetExtension(safeName);
        int counter = 1;
        while (File.Exists(candidate))
            candidate = Path.Combine(folder, $"{baseName} ({counter++}){ext}");

        return candidate;
    }

    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.#} {units[unit]}";
    }
}
