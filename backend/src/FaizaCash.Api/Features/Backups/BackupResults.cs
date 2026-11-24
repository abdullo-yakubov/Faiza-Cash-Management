namespace FaizaCash.Api;

/// <summary>Перевод исключений BackupService в коды HTTP с записью неудачи в аудит.</summary>
internal static class BackupResults
{
internal static async Task<IResult> BackupAction<T>(Func<Task<T>> run, Func<string,Task> onFailure)
{
    try { return Results.Ok(await run()); }
    catch (ArgumentException ex) { await onFailure(ex.Message); return Results.Problem(ex.Message, statusCode: 400); }
    catch (FileNotFoundException ex) { await onFailure(ex.Message); return Results.Problem(ex.Message, statusCode: 404); }
    catch (InvalidDataException ex) { await onFailure(ex.Message); return Results.Problem(ex.Message, statusCode: 422); }
    catch (InvalidOperationException ex) { await onFailure(ex.Message); return Results.Problem(ex.Message, statusCode: 503); }
}
}
