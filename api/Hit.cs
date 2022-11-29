namespace Adamijak.HitCounter;

public class Hit
{
    public string? Id { get; set; }
    public string? SiteId { get; set; }
    public long HitCount { get; set; }
}

public class HitRequest
{
    public string? Fingerprint { get; set; }
}

public class HitResponse
{
    public int HitCount { get; set; }
}