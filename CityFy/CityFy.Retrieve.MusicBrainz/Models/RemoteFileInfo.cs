namespace CityFy.Retrieve.MusicBrainz.Models;

/// <summary>HEAD of a remote dump file: used to skip downloads already done.</summary>
public sealed class RemoteFileInfo
{
    public string? ETag { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public long? ContentLength { get; set; }
}