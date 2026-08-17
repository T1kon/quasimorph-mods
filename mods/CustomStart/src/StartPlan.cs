using System;
using System.Collections.Generic;

namespace CustomStart;

[Serializable]
public sealed class StartPlan
{
    public string GeneratorVersion { get; set; } = Plugin.Version;

    public string Profile { get; set; } = string.Empty;

    public int Seed { get; set; }

    public int ElapsedDays { get; set; }

    public DateTime TargetDate { get; set; }

    public string ConfigSource { get; set; } = string.Empty;

    public string GameAssemblySha256 { get; set; } = string.Empty;

    public bool StationTransfersEnabled { get; set; }

    public List<string> HelpedFactions { get; set; } = new();

    public List<string> RivalFactions { get; set; } = new();

    public List<FactionPlan> Factions { get; set; } = new();

    public List<StationTransferPlan> StationTransfers { get; set; } = new();

    public Dictionary<string, int> StationPower { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, float> StationPendingTech { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<MercenaryGrant> Mercenaries { get; set; } = new();

    public List<string> Classes { get; set; } = new();

    public List<string> MagnumUpgrades { get; set; } = new();

    public List<string> ProductionRecipeUnlocks { get; set; } = new();

    public List<ItemGrant> Items { get; set; } = new();

    public List<string> Warnings { get; set; } = new();

    public bool Applied { get; set; }

    public string Error { get; set; } = string.Empty;
}

[Serializable]
public sealed class FactionPlan
{
    public string Id { get; set; } = string.Empty;

    public int TechLevel { get; set; }

    public float TechExp { get; set; }

    public float PlayerReputation { get; set; }

    public int PlayerTradePoints { get; set; }

    public int Power { get; set; }

    public double ResearchRate { get; set; }

    public double EconomyOffset { get; set; }
}

[Serializable]
public sealed class StationTransferPlan
{
    public string StationId { get; set; } = string.Empty;

    public string PreviousOwnerId { get; set; } = string.Empty;

    public string NewOwnerId { get; set; } = string.Empty;

    public int DaysAgo { get; set; }

    public bool PlayerInfluenced { get; set; }
}

[Serializable]
public sealed class MercenaryGrant
{
    public string ProfileId { get; set; } = string.Empty;

    public string AgentName { get; set; } = string.Empty;
}

[Serializable]
public sealed class ItemGrant
{
    public string ItemId { get; set; } = string.Empty;

    public int Count { get; set; } = 1;

    public bool FullStacks { get; set; }

    public string SourceFactionId { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string AcquisitionBasis { get; set; } = string.Empty;

    public string SelectionGroup { get; set; } = string.Empty;

    public double SelectionScore { get; set; }
}
