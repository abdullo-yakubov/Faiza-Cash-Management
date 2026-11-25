using FaizaCash.Application;

namespace FaizaCash.Infrastructure.Services;

/// <summary>Кэш логотипа в памяти процесса: PdfReport — singleton, а логотип меняется редко (PUT/DELETE /api/settings/logo).</summary>
public sealed class OrganizationLogoCache : IOrganizationLogo
{
    private volatile byte[]? current;
    public byte[]? Current => current;
    public void Set(byte[]? image) => current = image is { Length: > 0 } ? image : null;
}
