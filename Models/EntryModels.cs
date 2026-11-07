namespace LocalPasswordVault.Models
{
    /// <summary>列表展示用的条目摘要，只含明文名称。</summary>
    public sealed record EntrySummary(long Id, string Name);

    /// <summary>解密后的条目详情，只在内存与详情界面短暂存在。</summary>
    public sealed record EntryDetail(long Id, string Name, string Username, string Password, string Extra);
}
