using CSJSGen.Models;

namespace CSJSGen.Services;

/// <summary>
/// Stores files the user attaches to a message and hands back a URL for each.
/// <para>
/// The spec asks for uploads to become URLs: an image's URL is sent as an image content part,
/// a document's is appended to the message text as a link. Both need somewhere stable to live,
/// which is what this provides.
/// </para>
/// </summary>
public sealed class AttachmentService
{
    private readonly string _root;

    public AttachmentService(string? root = null)
    {
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CSJSGen", "attachments");

        Directory.CreateDirectory(_root);
    }

    /// <summary>Largest file accepted. Beyond this the model would refuse it anyway.</summary>
    public const long MaxSizeBytes = 20 * 1024 * 1024;

    /// <summary>Copies a file into the store and returns its attachment record.</summary>
    public async Task<Attachment> AddAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(sourcePath);
        if (!info.Exists) throw new FileNotFoundException("The file no longer exists.", sourcePath);

        if (info.Length > MaxSizeBytes)
            throw new InvalidOperationException($"{info.Name} is {info.Length / (1024 * 1024)} MB; the limit is 20 MB.");

        // A per-attachment folder keeps the original file name usable without collisions.
        var id = Guid.NewGuid().ToString("n")[..12];
        var directory = Path.Combine(_root, id);
        Directory.CreateDirectory(directory);

        var target = Path.Combine(directory, info.Name);

        await using (var source = File.OpenRead(sourcePath))
        await using (var destination = File.Create(target))
        {
            await source.CopyToAsync(destination, cancellationToken);
        }

        return new Attachment(
            info.Name,
            new Uri(target).AbsoluteUri,
            GuessContentType(info.Extension),
            info.Length);
    }

    /// <summary>Stores bytes that arrived from a stream rather than a path.</summary>
    public async Task<Attachment> AddAsync(
        string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("n")[..12];
        var directory = Path.Combine(_root, id);
        Directory.CreateDirectory(directory);

        var target = Path.Combine(directory, fileName);

        await using (var destination = File.Create(target))
        {
            await content.CopyToAsync(destination, cancellationToken);
        }

        var info = new FileInfo(target);

        if (info.Length > MaxSizeBytes)
        {
            info.Delete();
            throw new InvalidOperationException($"{fileName} exceeds the 20 MB limit.");
        }

        return new Attachment(
            fileName,
            new Uri(target).AbsoluteUri,
            GuessContentType(Path.GetExtension(fileName)),
            info.Length);
    }

    public void Remove(Attachment attachment)
    {
        try
        {
            var path = new Uri(attachment.Url).LocalPath;
            var directory = Path.GetDirectoryName(path);
            if (directory is not null && directory.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // A leftover file is harmless; failing to delete should not surface to the user.
        }
    }

    /// <summary>File types offered in the picker, split the way the model treats them.</summary>
    public static IReadOnlyList<string> ImageExtensions { get; } =
        new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };

    public static IReadOnlyList<string> DocumentExtensions { get; } =
        new[] { ".txt", ".md", ".pdf", ".json", ".csv", ".xml", ".cs", ".csjs", ".js", ".ts", ".html", ".css", ".log" };

    private static string GuessContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".svg" => "image/svg+xml",

        ".pdf" => "application/pdf",
        ".json" => "application/json",
        ".xml" => "application/xml",
        ".csv" => "text/csv",
        ".md" => "text/markdown",
        ".txt" or ".log" => "text/plain",
        ".cs" or ".csjs" => "text/x-csharp",
        ".js" or ".ts" => "text/javascript",
        ".html" => "text/html",
        ".css" => "text/css",

        _ => "application/octet-stream",
    };
}
