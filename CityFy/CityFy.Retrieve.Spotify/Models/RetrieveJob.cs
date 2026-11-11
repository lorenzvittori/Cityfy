namespace CityFy.Retrieve.Spotify.Models
{
    // Unit of work handed from the controller (or a scheduler) to the background worker.
    // It carries only the user id: the worker obtains a fresh access token from the linked account.
    public sealed record RetrieveJob(string JobId, string UserId);
}
