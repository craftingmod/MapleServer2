﻿using Maple2Storage.Types;
using Maple2Storage.Types.Metadata;
using MapleServer2.Tools;
using ProtoBuf;

namespace MapleServer2.Data.Static;

public static class TrophyMetadataStorage
{
    private static readonly Dictionary<int, TrophyMetadata> Trophies = new();

    public static void Init()
    {
        using FileStream stream = MetadataHelper.GetFileStream(MetadataName.Trophy);
        List<TrophyMetadata> trophies = Serializer.Deserialize<List<TrophyMetadata>>(stream);
        foreach (TrophyMetadata trophy in trophies)
        {
            Trophies[trophy.Id] = trophy;
        }
    }

    public static IEnumerable<TrophyMetadata> GetTrophiesByType(string type)
        => Trophies.Values.Where(m => m.ConditionType == type);

    public static TrophyMetadata GetMetadata(int id) => Trophies.GetValueOrDefault(id);

    public static IEnumerable<TrophyMetadata> GetAll() => Trophies.Values;
}
