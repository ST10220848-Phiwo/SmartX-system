namespace SmartX.Api.Features;


public sealed class SmartXFeatures
{
    public const string SectionName = "SmartX:Features";

    /// <summary>Deliverable 1. On.</summary>
    public bool TelemetryIngestion { get; set; } = true;

    /// <summary>Deliverable 2. Real-time command stream and history. Lands in a later PR.</summary>
    public bool CommandStream { get; set; }

    /// <summary>Deliverable 3. Network topology and mesh routing. Lands in the final PR.</summary>
    public bool MeshTopology { get; set; }

    /// <summary>Seeder control endpoints. Development only.</summary>
    public bool SeederControls { get; set; }
}
