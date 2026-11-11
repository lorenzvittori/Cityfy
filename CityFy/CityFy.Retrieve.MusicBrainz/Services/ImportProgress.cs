namespace CityFy.Retrieve.MusicBrainz.Services;

/// <summary>Current step of a running import, exposed by GET api/retrieve/status (admin panel).</summary>
/// <param name="Phase">Download, Estrazione, Lettura tabelle, Scrittura database.</param>
/// <param name="Percent">0-100 of the WHOLE phase, null when it cannot be measured (extraction).</param>
/// <param name="Detail">e.g. "mbdump.tar.bz2 (1/2) · 3,1 / 7,2 GB", "url (9/9)".</param>
public sealed record ImportProgressSnapshot(string Phase, double? Percent, string? Detail, DateTime UpdatedAt);

/// <summary>
/// Progress of the MusicBrainz import (singleton). Writers are the steps of the import, the reader
/// is the status endpoint: a single reference swap, no locking needed.
/// </summary>
public sealed class ImportProgress
{
    private volatile ImportProgressSnapshot? _current;

    public ImportProgressSnapshot? Current => _current;

    public void Report(string phase, double? percent, string? detail) =>
        _current = new ImportProgressSnapshot(
            phase,
            percent.HasValue ? Math.Round(Math.Clamp(percent.Value, 0, 100), 1) : null,
            detail,
            DateTime.UtcNow);

    public void Clear() => _current = null;
}

/// <summary>IProgress that runs the callback synchronously (Progress&lt;T&gt; would post to the thread pool).</summary>
public sealed class ActionProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
