using CommunityToolkit.Mvvm.ComponentModel;
using Protobuf;

namespace THUAI9_Avalonia.ViewModels
{
    /// <summary>
    /// 角色视图模型。
    /// </summary>
    public partial class CharacterViewModel : ViewModelBase
    {
        [ObservableProperty]
        private long guid;

        [ObservableProperty]
        private long characterId;

        [ObservableProperty]
        private int teamId;

        [ObservableProperty]
        private string name = string.Empty;

        [ObservableProperty]
        private int hp;

        [ObservableProperty]
        private int maxHp;

        [ObservableProperty]
        private int posX;

        [ObservableProperty]
        private int posY;

        [ObservableProperty]
        private string activeState = string.Empty;

        [ObservableProperty]
        private CharacterType characterType;

        public string Coordinates => $"坐标 ({PosX / 1000.0:0.0},{PosY / 1000.0:0.0})";

        public string HealthText => $"生命值 {Hp}";

        public string PlayerText => $"玩家 P{CharacterId}";

        public bool ShowCoordinates => Hp > 0;

        partial void OnPosXChanged(int value) => OnPropertyChanged(nameof(Coordinates));
        partial void OnPosYChanged(int value) => OnPropertyChanged(nameof(Coordinates));
        partial void OnHpChanged(int value)
        {
            OnPropertyChanged(nameof(ShowCoordinates));
            OnPropertyChanged(nameof(HealthText));
        }
        partial void OnMaxHpChanged(int value) => OnPropertyChanged(nameof(HealthText));
        partial void OnCharacterIdChanged(long value) => OnPropertyChanged(nameof(PlayerText));
    }
}
