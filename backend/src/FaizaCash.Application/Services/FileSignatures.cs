namespace FaizaCash.Application;

/// <summary>
/// Определение типа файла по сигнатуре (magic bytes), а не по расширению или заголовку клиента.
/// Подмена расширения (например, exe с именем scan.jpg) даёт null.
/// </summary>
public static class FileSignatures
{
    public const string Jpeg = "image/jpeg", Png = "image/png", WebP = "image/webp", Pdf = "application/pdf";
    public static readonly string[] Images = [Jpeg, Png, WebP];
    public static readonly string[] Documents = [Jpeg, Png, WebP, Pdf];

    public static string? Detect(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return Jpeg;
        if (head.Length >= 8 && head[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return Png;
        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8)) return WebP;
        if (head.Length >= 5 && head[..5].SequenceEqual("%PDF-"u8)) return Pdf;
        return null;
    }

    public static string Extension(string contentType) => contentType switch { Jpeg => ".jpg", Png => ".png", WebP => ".webp", Pdf => ".pdf", _ => "" };

    /// <summary>Безопасное отображаемое имя: только имя без пути, без управляющих символов, до 200 символов, с расширением по реальному типу.</summary>
    public static string SafeFileName(string? name, string contentType)
    {
        var raw = Path.GetFileName((name ?? "").Replace('\\', '/').Split('/').Last());
        var clean = new string(raw.Where(c => !char.IsControl(c) && c is not ('"' or '<' or '>' or '|' or ':' or '*' or '?')).ToArray()).Trim();
        var stem = Path.GetFileNameWithoutExtension(clean);
        if (string.IsNullOrWhiteSpace(stem)) stem = "document";
        if (stem.Length > 190) stem = stem[..190];
        return stem + Extension(contentType);
    }
}

/// <summary>Правила вложений первичных документов (контракт волны 1, п.2).</summary>
public static class AttachmentRules
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public const int MaxPerOwner = 10;
    public static readonly string[] OwnerTypes = ["operation", "receipt", "advance"];

    /// <summary>Владелец «закрыт» (вложения не добавляются и не удаляются): проведённый документ, принятая квитанция, закрытый аванс.</summary>
    public static bool IsLocked(string ownerType, string ownerStatus) => ownerType switch
    {
        "operation" => ownerStatus == "posted",
        "receipt" => ownerStatus is "accepted" or "cancelled",
        "advance" => ownerStatus == "closed",
        _ => true
    };

    /// <summary>
    /// Кто может добавлять и удалять вложения: бухгалтер — любые; кассир — к собственным кассовым документам;
    /// руководитель — к квитанциям представителей. Администратор и аудитор только просматривают.
    /// </summary>
    public static bool CanModify(string ownerType, bool accountant, bool cashier, bool manager, bool isAuthor) =>
        accountant || ownerType == "operation" && cashier && isAuthor || ownerType == "receipt" && manager;
}
