using System.ComponentModel.DataAnnotations;

namespace Beeps.Features.Agents;

public sealed class AgentOptions
{
    public const string SectionName = "Agents";

    /// <summary>PLAN 4.7: at most this many agents per owner (deleted ones do not count).</summary>
    [Range(1, 1000)]
    public int MaxPerOwner { get; set; } = 10;
}
