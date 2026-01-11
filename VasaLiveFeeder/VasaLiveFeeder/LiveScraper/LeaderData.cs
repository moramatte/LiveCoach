namespace VasaLiveFeeder.LiveScraper;

/// <summary>
/// Represents the leader's progress data from a live race timing page.
/// </summary>
public class LeaderData
{
    /// <summary>
    /// Distance the leader has covered in kilometers.
    /// </summary>
    public double DistanceKm { get; set; }
    
    /// <summary>
    /// Time elapsed when the leader reached this distance checkpoint.
    /// Null if time information is not available.
    /// </summary>
    public TimeSpan? ElapsedTime { get; set; }
    
    public LeaderData(double distanceKm, TimeSpan? elapsedTime = null)
    {
        DistanceKm = distanceKm;
        ElapsedTime = elapsedTime;
    }
}
