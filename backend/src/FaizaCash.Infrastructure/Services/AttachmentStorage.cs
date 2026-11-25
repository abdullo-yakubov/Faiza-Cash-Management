using Microsoft.Extensions.Configuration;

namespace FaizaCash.Infrastructure.Services;

/// <summary>
/// Файлы вложений на диске: каталог Attachments:Directory (по умолчанию .runtime/attachments),
/// имя файла — Guid вложения без расширения и без пользовательского имени (нет обхода путей).
/// </summary>
public sealed class AttachmentStorage(IConfiguration configuration)
{
    public string Directory { get; } = Path.GetFullPath(configuration["Attachments:Directory"] ?? ".runtime/attachments");

    private string PathOf(Guid id) => Path.Combine(Directory, id.ToString("N"));

    /// <summary>Атомарная запись: временный файл и переименование, чтобы частично записанный файл не был виден.</summary>
    public async Task SaveAsync(Guid id, byte[] content, CancellationToken ct)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var temp = PathOf(id) + ".tmp";
        await File.WriteAllBytesAsync(temp, content, ct);
        File.Move(temp, PathOf(id), overwrite: false);
    }

    public Stream? Open(Guid id) => File.Exists(PathOf(id)) ? new FileStream(PathOf(id), FileMode.Open, FileAccess.Read, FileShare.Read) : null;

    public void Delete(Guid id) { try { File.Delete(PathOf(id)); } catch (IOException) { } }

    /// <summary>Все файлы вложений (без временных) — для резервной копии и панели состояния.</summary>
    public IReadOnlyList<FileInfo> Files() => System.IO.Directory.Exists(Directory)
        ? new DirectoryInfo(Directory).GetFiles().Where(x => Guid.TryParseExact(x.Name, "N", out _)).OrderBy(x => x.Name, StringComparer.Ordinal).ToList()
        : [];

    public long TotalBytes() => Files().Sum(x => x.Length);
}
