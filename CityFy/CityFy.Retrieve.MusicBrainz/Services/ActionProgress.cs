namespace CityFy.Retrieve.MusicBrainz.Services;

/// <summary>IProgress that runs the callback synchronously (Progress&lt;T&gt; would post to the thread pool).</summary>
public sealed class ActionProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
