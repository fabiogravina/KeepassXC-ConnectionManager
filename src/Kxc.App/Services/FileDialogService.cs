using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Kxc.App.Resources;

namespace Kxc.App.Services;

public interface IFileDialogService
{
    Task<string?> PickDatabaseAsync();

    Task<string?> PickKeyFileAsync();

    Task<string?> PickExecutableAsync();
}

/// <summary>Selezione dei file con il selettore del sistema.</summary>
internal sealed class FileDialogService : IFileDialogService
{
    public TopLevel? Owner { get; set; }

    public Task<string?> PickDatabaseAsync() => PickAsync(
        Strings.FilePicker_DatabaseTitle,
        new FilePickerFileType(Strings.FilePicker_KdbxFilter) { Patterns = ["*.kdbx"] });

    public Task<string?> PickKeyFileAsync() => PickAsync(
        Strings.FilePicker_KeyFileTitle,
        new FilePickerFileType(Strings.FilePicker_AllFiles) { Patterns = ["*"] });

    public Task<string?> PickExecutableAsync() => PickAsync(
        Strings.FilePicker_CliTitle,
        new FilePickerFileType("keepassxc-cli") { Patterns = OperatingSystem.IsWindows() ? ["keepassxc-cli.exe"] : ["keepassxc-cli*"] });

    private async Task<string?> PickAsync(string title, FilePickerFileType filter)
    {
        if (Owner is null)
        {
            return null;
        }

        var files = await Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [filter, new FilePickerFileType(Strings.FilePicker_AllFiles) { Patterns = ["*"] }],
        });

        return files.Count == 1 ? files[0].TryGetLocalPath() : null;
    }
}
