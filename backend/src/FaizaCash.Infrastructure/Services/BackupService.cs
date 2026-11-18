using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FaizaCash.Infrastructure.Services;

public sealed record BackupInfo(string Id, DateTime CreatedAt, long SizeBytes, string Sha256, string Status, string? Error);
public sealed record BackupManifest(int FormatVersion, string Id, DateTime CreatedAt, long SizeBytes, string Sha256, string[] Migrations);
public sealed record BackupStatus(DateTime? LastAttemptAt, DateTime? LastSuccessfulAt, string Status, string? Error, double IntervalHours, int RetentionDays);

public sealed class BackupService(IConfiguration configuration, IHostEnvironment environment, ILogger<BackupService> logger)
{
    private readonly string root = Path.GetFullPath(configuration["Backups:Directory"] ?? Path.Combine(environment.ContentRootPath, ".runtime", "backups"));
    private readonly string? pgBin = configuration["Backups:PostgresBin"];
    private readonly string connectionString = configuration.GetConnectionString("CashDb") ?? throw new InvalidOperationException("CashDb is not configured.");
    private readonly SemaphoreSlim gate = new(1, 1);

    public double IntervalHours => Math.Clamp(configuration.GetValue<double>("Backups:IntervalHours"), 0, 8760);
    public int RetentionDays => Math.Clamp(configuration.GetValue("Backups:RetentionDays", 30), 1, 36500);
    private string StatusFile => Path.Combine(root, "status.json");

    public async Task<BackupStatus> StatusAsync(CancellationToken ct)
    {
        if (File.Exists(StatusFile))
        {
            var saved = JsonSerializer.Deserialize<BackupStatus>(await File.ReadAllTextAsync(StatusFile, ct));
            if (saved is not null)
            {
                return saved with { IntervalHours = IntervalHours, RetentionDays = RetentionDays };
            }
        }

        return new BackupStatus(null, null, "never", null, IntervalHours, RetentionDays);
    }

    public async Task<IReadOnlyList<BackupInfo>> ListAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(root);
        var archiveDir = Path.Combine(root, "archive");
        var files = new List<string>();
        files.AddRange(Directory.EnumerateFiles(root, "cash-*.dump"));
        if (Directory.Exists(archiveDir))
        {
            files.AddRange(Directory.EnumerateFiles(archiveDir, "cash-*.dump"));
        }

        var result = new List<BackupInfo>();
        foreach (var file in files.OrderByDescending(File.GetCreationTimeUtc))
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            var id = Path.GetFileNameWithoutExtension(file);
            try
            {
                var manifest = await ReadManifest(file, ct);
                var status = info.Length == manifest.SizeBytes
                    ? (file.StartsWith(Path.Combine(root, "archive"), StringComparison.OrdinalIgnoreCase) ? "archived" : "available")
                    : "invalid";
                result.Add(new BackupInfo(id, manifest.CreatedAt, info.Length, manifest.Sha256, status, null));
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            {
                result.Add(new BackupInfo(id, info.CreationTimeUtc, info.Length, string.Empty, "unverified", "Нет корректного манифеста. Восстановление заблокировано."));
            }
        }

        return result;
    }

    public async Task<BackupInfo> CreateAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        DateTime? previousSuccess = null;
        try
        {
            Directory.CreateDirectory(root);
            previousSuccess = (await StatusAsync(ct)).LastSuccessfulAt;
            await SaveStatus(new BackupStatus(DateTime.UtcNow, previousSuccess, "running", null, IntervalHours, RetentionDays), ct);

            var id = "cash-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
            var file = Path.Combine(root, id + ".dump");
            var migrations = await ReadMigrations(connectionString, ct);

            var pgDumpArgs = new string[] { "--no-password", "--format=custom", "--file", file };
            await RunAsync("pg_dump", pgDumpArgs, ct);

            var currentMigrations = await ReadMigrations(connectionString, ct);
            if (!migrations.SequenceEqual(currentMigrations))
            {
                throw new InvalidDataException("Схема изменилась во время копирования. Повторите после завершения миграции.");
            }

            var pgRestoreArgs = new string[] { "--list", file };
            await RunAsync("pg_restore", pgRestoreArgs, ct);

            var size = new FileInfo(file).Length;
            if (size == 0)
            {
                throw new InvalidDataException("Резервная копия пуста.");
            }

            var manifest = new BackupManifest(1, id, DateTime.UtcNow, size, await HashAsync(file, ct), migrations);
            await AtomicJson(file + ".json", manifest, ct);
            await SaveStatus(new BackupStatus(DateTime.UtcNow, manifest.CreatedAt, "success", null, IntervalHours, RetentionDays), ct);
            ArchiveExpired();
            logger.LogInformation("PostgreSQL backup created: {BackupId}, {SizeBytes} bytes", id, size);
            return new BackupInfo(id, manifest.CreatedAt, size, manifest.Sha256, "available", null);
        }
        catch (Exception ex)
        {
            logger.LogError("Backup failed ({ErrorType}).", ex.GetType().Name);
            await SaveStatus(new BackupStatus(DateTime.UtcNow, previousSuccess, "failed", "Ошибка резервного копирования. Проверьте доступ к PostgreSQL и его утилитам на сервере.", IntervalHours, RetentionDays), CancellationToken.None);
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<BackupInfo> VerifyAsync(string id, CancellationToken ct)
    {
        var file = Resolve(id);
        var manifest = await ReadManifest(file, ct);
        var hash = await HashAsync(file, ct);
        ValidateManifest(manifest, id, new FileInfo(file).Length, hash);

        var verifyArgs = new string[] { "--list", file };
        await RunAsync("pg_restore", verifyArgs, ct);
        logger.LogInformation("PostgreSQL backup verified: {BackupId}", id);
        return new BackupInfo(id, manifest.CreatedAt, manifest.SizeBytes, hash, "verified", null);
    }

    public static void ValidateManifest(BackupManifest manifest, string id, long length, string hash)
    {
        if (manifest.FormatVersion != 1 || manifest.Id != id || manifest.SizeBytes <= 0 || manifest.SizeBytes != length || !string.Equals(manifest.Sha256, hash, StringComparison.OrdinalIgnoreCase) || manifest.Migrations is not { Length: > 0 })
        {
            throw new InvalidDataException("Контрольная сумма или манифест резервной копии не соответствует файлу.");
        }
    }

    private string Resolve(string id)
    {
        if (!Regex.IsMatch(id ?? string.Empty, "^cash-[a-zA-Z0-9-]+$", RegexOptions.CultureInvariant))
        {
            throw new ArgumentException("Некорректный идентификатор копии.");
        }

        var file = Path.Combine(root, id + ".dump");
        if (!File.Exists(file))
        {
            file = Path.Combine(root, "archive", id + ".dump");
        }

        if (!File.Exists(file))
        {
            throw new FileNotFoundException("Резервная копия не найдена.");
        }

        return file;
    }

    private static async Task<BackupManifest> ReadManifest(string file, CancellationToken ct) =>
        JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(file + ".json", ct)) ?? throw new InvalidDataException("Нет манифеста.");

    private static async Task<string> HashAsync(string file, CancellationToken ct)
    {
        await using var stream = File.OpenRead(file);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
    }

    private Task SaveStatus(BackupStatus status, CancellationToken ct) => AtomicJson(StatusFile, status, ct);

    private static async Task AtomicJson<T>(string file, T value, CancellationToken ct)
    {
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value), ct);
        File.Move(temporary, file, true);
    }

    private void ArchiveExpired()
    {
        var archive = Path.Combine(root, "archive");
        foreach (var file in Directory.EnumerateFiles(root, "cash-*.dump").Where(x => File.GetLastWriteTimeUtc(x) < DateTime.UtcNow.AddDays(-RetentionDays)))
        {
            if (!File.Exists(file + ".json"))
            {
                continue;
            }

            Directory.CreateDirectory(archive);
            var target = Path.Combine(archive, Path.GetFileName(file));
            if (File.Exists(target) || File.Exists(target + ".json"))
            {
                continue;
            }

            File.Move(file + ".json", target + ".json");
            File.Move(file, target);
        }
    }

    private static async Task<string[]> ReadMigrations(string connection, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connection);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var ids = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            ids.Add(reader.GetString(0));
        }

        return ids.ToArray();
    }

    private async Task RunAsync(string tool, IEnumerable<string> args, CancellationToken ct)
    {
        var executable = tool + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);
        if (!string.IsNullOrWhiteSpace(pgBin))
        {
            executable = Path.Combine(pgBin, executable);
        }

        var psi = new ProcessStartInfo(executable)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        var cs = new NpgsqlConnectionStringBuilder(connectionString);
        psi.Environment["PGHOST"] = cs.Host;
        psi.Environment["PGPORT"] = cs.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        psi.Environment["PGUSER"] = cs.Username;
        psi.Environment["PGDATABASE"] = cs.Database;
        psi.Environment["PGPASSWORD"] = cs.Password;
        psi.Environment["PGCONNECT_TIMEOUT"] = "15";
        psi.Environment["PGSSLMODE"] = cs.SslMode.ToString().ToLowerInvariant().Replace("verifyca", "verify-ca").Replace("verifyfull", "verify-full");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить утилиту PostgreSQL.");
        var stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
        var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Утилита {tool} завершилась с кодом {process.ExitCode}.");
        }
    }
}

public sealed class BackupScheduler(BackupService backups, ILogger<BackupScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (backups.IntervalHours <= 0)
            {
                continue;
            }

            try
            {
                var status = await backups.StatusAsync(stoppingToken);
                if (status.LastAttemptAt is null || DateTime.UtcNow - status.LastAttemptAt >= TimeSpan.FromHours(backups.IntervalHours))
                {
                    await backups.CreateAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError("Scheduled backup failed ({ErrorType}).", ex.GetType().Name);
            }
        }
    }
}
