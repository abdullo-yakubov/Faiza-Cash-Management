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

public sealed record BackupInfo(string Id, DateTime CreatedAt, long SizeBytes, string Sha256, string Status, string? Error, bool Encrypted = false);
/// <summary>Business totals captured in the same snapshot as pg_dump; compared after an isolated restore.</summary>
public sealed record BackupReconciliation(long PostedOperations, string SettlementTotal, string SettlementPaid, string AllocationTotal, int ForeignKeys, int InvalidForeignKeys, Dictionary<string, string> CashboxBalances)
{
    public bool? Matched { get; init; }
    public bool SameAs(BackupReconciliation other) =>
        PostedOperations == other.PostedOperations && SettlementTotal == other.SettlementTotal && SettlementPaid == other.SettlementPaid &&
        AllocationTotal == other.AllocationTotal && ForeignKeys == other.ForeignKeys && InvalidForeignKeys == 0 && other.InvalidForeignKeys == 0 &&
        CashboxBalances.Count == other.CashboxBalances.Count && CashboxBalances.All(x => other.CashboxBalances.TryGetValue(x.Key, out var value) && value == x.Value);
}
/// <summary>Format 3 = format 2 + AES-256-GCM encrypted file (D-09); Sha256 and SizeBytes describe the stored (encrypted) file.</summary>
public sealed record BackupManifest(int FormatVersion, string Id, DateTime CreatedAt, long SizeBytes, string Sha256, string[] Migrations, BackupReconciliation? Reconciliation = null, bool Encrypted = false, string? KeyId = null);
public sealed record RestoreTestResult(string Id, DateTime CreatedAt, long SizeBytes, string Sha256, string Status, long DurationMs, BackupReconciliation? Reconciliation);
public sealed record BackupStatus(DateTime? LastAttemptAt, DateTime? LastSuccessfulAt, string Status, string? Error, double IntervalHours, int RetentionDays);

public sealed class BackupService(IConfiguration configuration, IHostEnvironment environment, ILogger<BackupService> logger)
{
    private readonly string root = Path.GetFullPath(configuration["Backups:Directory"] ?? Path.Combine(environment.ContentRootPath, ".runtime", "backups"));
    private readonly string? pgBin = configuration["Backups:PostgresBin"];
    private readonly string? restoreConnectionString = configuration.GetConnectionString("BackupRestore");
    private readonly string connectionString = configuration.GetConnectionString("CashDb") ?? throw new InvalidOperationException("CashDb is not configured.");
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly byte[]? encryptionKey = BackupCrypto.ParseKey(configuration["Backups:EncryptionKey"]);

    public bool EncryptionEnabled => encryptionKey is not null;

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
                result.Add(new BackupInfo(id, manifest.CreatedAt, info.Length, manifest.Sha256, status, null, manifest.Encrypted));
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

            // Export one repeatable-read snapshot: migrations, reconciliation totals and pg_dump all see the same data.
            string[] migrations;
            BackupReconciliation reconciliation;
            await using (var snapshotConnection = new NpgsqlConnection(connectionString))
            {
                await snapshotConnection.OpenAsync(ct);
                await using var snapshotTx = await snapshotConnection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
                await using var export = new NpgsqlCommand("SELECT pg_export_snapshot()", snapshotConnection, snapshotTx);
                var snapshot = (string)(await export.ExecuteScalarAsync(ct))!;
                migrations = await ReadMigrations(snapshotConnection, snapshotTx, ct);
                reconciliation = await ReadReconciliation(snapshotConnection, snapshotTx, ct);
                if (encryptionKey is null)
                {
                    var pgDumpArgs = new string[] { "--no-password", "--format=custom", "--snapshot=" + snapshot, "--file", file };
                    await RunAsync("pg_dump", pgDumpArgs, ct);
                }
                else
                {
                    // D-09: the plain dump never touches the disk; pg_dump stdout is encrypted straight into the file.
                    var pgDumpArgs = new string[] { "--no-password", "--format=custom", "--snapshot=" + snapshot };
                    try
                    {
                        await using var target = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                        await RunAsync("pg_dump", pgDumpArgs, ct, stdout: (stream, token) => BackupCrypto.EncryptAsync(stream, target, encryptionKey, BackupCrypto.DefaultChunk, token));
                    }
                    catch
                    {
                        File.Delete(file);
                        throw;
                    }
                }
                await snapshotTx.CommitAsync(ct);
            }

            await ListContents(file, encryptionKey is not null, ct);

            var size = new FileInfo(file).Length;
            if (size == 0)
            {
                throw new InvalidDataException("Резервная копия пуста.");
            }

            var manifest = encryptionKey is null
                ? new BackupManifest(2, id, DateTime.UtcNow, size, await HashAsync(file, ct), migrations, reconciliation)
                : new BackupManifest(3, id, DateTime.UtcNow, size, await HashAsync(file, ct), migrations, reconciliation, true, BackupCrypto.KeyId(encryptionKey));
            await AtomicJson(file + ".json", manifest, ct);
            await SaveStatus(new BackupStatus(DateTime.UtcNow, manifest.CreatedAt, "success", null, IntervalHours, RetentionDays), ct);
            ArchiveExpired();
            logger.LogInformation("PostgreSQL backup created: {BackupId}, {SizeBytes} bytes, encrypted={Encrypted}", id, size, manifest.Encrypted);
            return new BackupInfo(id, manifest.CreatedAt, size, manifest.Sha256, "available", null, manifest.Encrypted);
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
        EnsureKeyFor(manifest);

        try { await ListContents(file, manifest.Encrypted, ct); }
        catch (ToolFailedException) { throw new InvalidDataException("Файл резервной копии повреждён: pg_restore не может прочитать его оглавление."); }
        logger.LogInformation("PostgreSQL backup verified: {BackupId}", id);
        return new BackupInfo(id, manifest.CreatedAt, manifest.SizeBytes, hash, "verified", null, manifest.Encrypted);
    }

    /// <summary>Runs pg_restore --list; an encrypted file is decrypted into pg_restore's stdin, never onto disk.</summary>
    private async Task ListContents(string file, bool encrypted, CancellationToken ct)
    {
        if (!encrypted)
        {
            await RunAsync("pg_restore", ["--list", file], ct);
            return;
        }
        await using var source = File.OpenRead(file);
        await RunAsync("pg_restore", ["--list"], ct, stdin: (stream, token) => BackupCrypto.DecryptAsync(source, stream, encryptionKey!, token));
    }

    private void EnsureKeyFor(BackupManifest manifest)
    {
        if (!manifest.Encrypted) return;
        if (encryptionKey is null) throw new InvalidOperationException("Копия зашифрована, а ключ Backups:EncryptionKey на сервере не задан.");
        if (!string.Equals(manifest.KeyId, BackupCrypto.KeyId(encryptionKey), StringComparison.Ordinal))
            throw new InvalidDataException("Копия зашифрована другим ключом: идентификатор ключа в манифесте не совпадает с ключом сервера.");
    }

    public bool RestoreTestConfigured => !string.IsNullOrWhiteSpace(restoreConnectionString);

    public async Task<RestoreTestResult> RestoreTestAsync(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(restoreConnectionString))
        {
            throw new InvalidOperationException("Изолированная тестовая база восстановления не сконфигурирована.");
        }

        var file = Resolve(id);
        var manifest = await ReadManifest(file, ct);
        var length = new FileInfo(file).Length;
        ValidateManifest(manifest, id, length, await HashAsync(file, ct));
        EnsureKeyFor(manifest);

        var destination = new NpgsqlConnectionStringBuilder(restoreConnectionString);
        var production = new NpgsqlConnectionStringBuilder(connectionString);
        var expectedHost = configuration["Backups:RestoreHost"];
        var expectedDatabase = configuration["Backups:RestoreDatabase"];
        if (string.IsNullOrWhiteSpace(destination.Database) ||
            string.IsNullOrWhiteSpace(expectedHost) || string.IsNullOrWhiteSpace(expectedDatabase) ||
            !string.Equals(destination.Host, expectedHost, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(destination.Database, expectedDatabase, StringComparison.Ordinal) ||
            string.Equals(destination.Database, production.Database, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(destination.Host, production.Host, StringComparison.OrdinalIgnoreCase) && destination.Port == production.Port)
        {
            throw new InvalidOperationException("Тестовое восстановление не совпадает с явно заданным изолированным сервером и базой данных.");
        }

        await gate.WaitAsync(ct);
        var started = Stopwatch.StartNew();
        var restoreStarted = false;
        try
        {
            await using (var conn = new NpgsqlConnection(destination.ConnectionString))
            {
                await conn.OpenAsync(ct);
                await using var preflight = new NpgsqlCommand("SELECT current_database(), current_user, current_setting('transaction_read_only'), EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public' AND c.relkind IN ('r','p','v','m','S','f'))", conn);
                await using var reader = await preflight.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct) || !string.Equals(reader.GetString(0), destination.Database, StringComparison.Ordinal) || reader.GetString(2) == "on" || reader.GetBoolean(3))
                {
                    throw new InvalidOperationException("Целевая база недоступна или не пуста; восстановление выполняется только в пустую одноразовую базу.");
                }
            }

            restoreStarted = true;
            var restoreArgs = new List<string> { "--no-password", "--exit-on-error", "--single-transaction", "--no-owner", "--no-privileges", "--dbname", destination.Database! };
            try
            {
                if (manifest.Encrypted)
                {
                    await using var source = File.OpenRead(file);
                    await RunAsync("pg_restore", restoreArgs, ct, destination, stdin: (stream, token) => BackupCrypto.DecryptAsync(source, stream, encryptionKey!, token));
                }
                else
                {
                    restoreArgs.Add(file);
                    await RunAsync("pg_restore", restoreArgs, ct, destination);
                }
            }
            catch (ToolFailedException)
            {
                throw new InvalidDataException("Файл резервной копии повреждён или не восстанавливается: pg_restore завершился с ошибкой. Рабочая база не затронута.");
            }

            BackupReconciliation? restored;
            await using (var conn = new NpgsqlConnection(destination.ConnectionString))
            {
                await conn.OpenAsync(ct);
                var restoredMigrations = await ReadMigrations(conn, null, ct);
                if (!manifest.Migrations.AsEnumerable().SequenceEqual(restoredMigrations))
                {
                    throw new InvalidDataException("Список миграций восстановленной базы не совпадает с манифестом резервной копии.");
                }
                restored = manifest.Reconciliation is null ? null : await ReadReconciliation(conn, null, ct);
            }

            if (manifest.Reconciliation is not null && !manifest.Reconciliation.SameAs(restored!))
            {
                throw new InvalidDataException("Сверка восстановленной базы не совпала с данными на момент копирования (остатки, долги, документы или внешние ключи).");
            }

            logger.LogInformation("PostgreSQL backup restored into isolated test database: {BackupId}", id);
            return new RestoreTestResult(id, manifest.CreatedAt, length, manifest.Sha256, "restored", started.ElapsedMilliseconds, restored is null ? null : restored with { Matched = true });
        }
        finally
        {
            try
            {
                if (restoreStarted) await ResetRestoreDatabase(destination, expectedDatabase!);
            }
            finally
            {
                gate.Release();
            }
        }
    }

    /// <summary>Empties the disposable restore database so the next restore-test can run. Never touches CashDb.</summary>
    private async Task ResetRestoreDatabase(NpgsqlConnectionStringBuilder destination, string expectedDatabase)
    {
        var production = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.Equals(destination.Database, production.Database, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(destination.Host, production.Host, StringComparison.OrdinalIgnoreCase) && destination.Port == production.Port)
        {
            throw new InvalidOperationException("Отказ очистки: строка подключения совпадает с рабочей базой.");
        }

        await using var conn = new NpgsqlConnection(destination.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);
        await using (var check = new NpgsqlCommand("SELECT current_database()", conn))
        {
            if (!string.Equals((string?)await check.ExecuteScalarAsync(CancellationToken.None), expectedDatabase, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Отказ очистки: подключение указывает не на изолированную базу восстановления.");
            }
        }

        await using var reset = new NpgsqlCommand("DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public;", conn);
        await reset.ExecuteNonQueryAsync(CancellationToken.None);
        logger.LogInformation("Isolated restore database emptied after test restore.");
    }

    private static async Task<BackupReconciliation> ReadReconciliation(NpgsqlConnection conn, NpgsqlTransaction? tx, CancellationToken ct)
    {
        const string totals = """
            SELECT (SELECT count(*) FROM "Operations" WHERE "Status"='posted'),
                   (SELECT COALESCE(SUM("TotalAmount"),0) FROM "Settlements"),
                   (SELECT COALESCE(SUM("PaidAmount"),0) FROM "Settlements"),
                   (SELECT COALESCE(SUM("Amount"),0) FROM "PaymentAllocations"),
                   (SELECT count(*) FROM pg_constraint c JOIN pg_namespace n ON n.oid=c.connamespace WHERE c.contype='f' AND n.nspname='public'),
                   (SELECT count(*) FROM pg_constraint c JOIN pg_namespace n ON n.oid=c.connamespace WHERE c.contype='f' AND n.nspname='public' AND NOT c.convalidated)
            """;
        const string balances = """
            SELECT c."Id", COALESCE(o."Amount",0) + COALESCE((SELECT SUM(CASE WHEN p."Type"='income' THEN p."Amount" ELSE -p."Amount" END)
                   FROM "Operations" p WHERE p."CashboxId"=c."Id" AND p."Status"='posted'),0)
            FROM "Cashboxes" c LEFT JOIN "Openings" o ON o."CashboxId"=c."Id" ORDER BY c."Id"
            """;
        static string Money(decimal value) => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        long posted; string settlementTotal, settlementPaid, allocations; int foreignKeys, invalidForeignKeys;
        await using (var command = new NpgsqlCommand(totals, conn, tx))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            posted = reader.GetInt64(0); settlementTotal = Money(reader.GetDecimal(1)); settlementPaid = Money(reader.GetDecimal(2));
            allocations = Money(reader.GetDecimal(3)); foreignKeys = (int)reader.GetInt64(4); invalidForeignKeys = (int)reader.GetInt64(5);
        }
        var cashboxes = new Dictionary<string, string>();
        await using (var command = new NpgsqlCommand(balances, conn, tx))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct)) cashboxes[reader.GetInt64(0).ToString(System.Globalization.CultureInfo.InvariantCulture)] = Money(reader.GetDecimal(1));
        }
        return new BackupReconciliation(posted, settlementTotal, settlementPaid, allocations, foreignKeys, invalidForeignKeys, cashboxes);
    }

    public static void ValidateManifest(BackupManifest manifest, string id, long length, string hash)
    {
        if (manifest.FormatVersion is not (1 or 2 or 3) || manifest.FormatVersion >= 2 && manifest.Reconciliation is null ||
            manifest.Encrypted != (manifest.FormatVersion == 3) || manifest.Encrypted && string.IsNullOrWhiteSpace(manifest.KeyId) || manifest.Id != id || manifest.SizeBytes <= 0 || manifest.SizeBytes != length || !string.Equals(manifest.Sha256, hash, StringComparison.OrdinalIgnoreCase) || manifest.Migrations is not { Length: > 0 })
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

    private static async Task<string[]> ReadMigrations(NpgsqlConnection conn, NpgsqlTransaction? tx, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"", conn, tx);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var ids = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            ids.Add(reader.GetString(0));
        }

        return ids.ToArray();
    }

    private async Task RunAsync(string tool, IEnumerable<string> args, CancellationToken ct, NpgsqlConnectionStringBuilder? databaseOverride = null,
        Func<Stream, CancellationToken, Task>? stdin = null, Func<Stream, CancellationToken, Task>? stdout = null)
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
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        var cs = databaseOverride ?? new NpgsqlConnectionStringBuilder(connectionString);
        psi.Environment["PGHOST"] = cs.Host;
        psi.Environment["PGPORT"] = cs.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        psi.Environment["PGUSER"] = cs.Username;
        psi.Environment["PGDATABASE"] = cs.Database;
        psi.Environment["PGPASSWORD"] = cs.Password;
        psi.Environment["PGCONNECT_TIMEOUT"] = "15";
        psi.Environment["PGSSLMODE"] = cs.SslMode.ToString().ToLowerInvariant().Replace("verifyca", "verify-ca").Replace("verifyfull", "verify-full");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить утилиту PostgreSQL.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException($"Утилита {tool} не найдена на сервере. Проверьте параметр Backups:PostgresBin.");
        }
        using var _ = process;
        // If any pipe handler fails (decryption error, disk error), stop the tool so the other pipes cannot block forever.
        async Task Guard(Task pipe)
        {
            try { await pipe; }
            catch
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw;
            }
        }
        async Task Feed()
        {
            try
            {
                await using var input = process.StandardInput.BaseStream;
                await stdin!(input, timeout.Token);
            }
            catch (IOException)
            {
                // The tool closed its input early; its exit code reports the failure.
            }
        }
        var output = Guard(stdout is null ? process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, timeout.Token) : stdout(process.StandardOutput.BaseStream, timeout.Token));
        var errors = Guard(process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token));
        var feeding = stdin is null ? Task.CompletedTask : Guard(Feed());

        try
        {
            await Task.WhenAll(feeding, output, errors);
            await process.WaitForExitAsync(timeout.Token);
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
            throw new ToolFailedException($"Утилита {tool} завершилась с кодом {process.ExitCode}.");
        }
    }
}

public sealed class ToolFailedException(string message) : InvalidOperationException(message);

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
