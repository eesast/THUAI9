using CommunityToolkit.Mvvm.ComponentModel;

namespace THUAI9_Avalonia.Models
{
    /// <summary>
    /// 顶层摘要区中的队伍概览。
    /// </summary>
    public partial class TeamOverviewItem : ObservableObject
    {
        [ObservableProperty]
        private int teamId;

        [ObservableProperty]
        private int score;

        [ObservableProperty]
        private int material;

        [ObservableProperty]
        private int computePower;

        [ObservableProperty]
        private int factoryHp;

        public string Header => $"队伍 {TeamId}";

        public string SummaryText => $"得分 {Score} · 原料 {Material} · 算力 {ComputePower} · 工厂血量 {FactoryHp}";

        partial void OnTeamIdChanged(int value) => OnPropertyChanged(nameof(Header));
        partial void OnScoreChanged(int value) => OnPropertyChanged(nameof(SummaryText));
        partial void OnMaterialChanged(int value) => OnPropertyChanged(nameof(SummaryText));
        partial void OnComputePowerChanged(int value) => OnPropertyChanged(nameof(SummaryText));
        partial void OnFactoryHpChanged(int value) => OnPropertyChanged(nameof(SummaryText));
    }
}
