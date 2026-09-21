namespace SmartX.Ingest.Seeding;

public sealed class SeederOptions
{
    public const string SectionName = "SmartX:Seeder";

    ///Off unless explicitly enabled. Production never runs the seeder.
    public bool Enabled { get; set; }

    ///Number of simulated sites.
    public int Sites { get; set; } = 4;

    ///Number of simulated devices per site.
    public int DevicesPerSite { get; set; } = 50;

    ///Publish interval per device, in milliseconds.
    public int PublishIntervalMs { get; set; } = 1000;

    ///Seed for the PRNG, so a load run is reproducible.
    public int RandomSeed { get; set; } = 7312;
}
