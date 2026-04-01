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
        private long guid; // 全局唯一 ID

        [ObservableProperty]
        private long characterId; // 玩家 ID

        [ObservableProperty]
        private int teamId; // 队伍 ID

        [ObservableProperty]
        private string name = string.Empty; // 角色名称

        [ObservableProperty]
        private int hp; // 当前生命值

        [ObservableProperty]
        private int maxHp; // 最大生命值

        [ObservableProperty]
        private int posX; // X 坐标（游戏坐标）

        [ObservableProperty]
        private int posY; // Y 坐标（游戏坐标）

        [ObservableProperty]
        private string activeState = string.Empty; // 当前状态

        [ObservableProperty]
        private CharacterType characterType; // 角色类型

        /// <summary>
        /// 网格坐标显示文本。
        /// </summary>
        public string Coordinates => $"({PosX / 1000},{PosY / 1000})";

        /// <summary>
        /// 仅在角色存活时显示坐标。
        /// </summary>
        public bool ShowCoordinates => Hp > 0;

        partial void OnPosXChanged(int value) => OnPropertyChanged(nameof(Coordinates));
        partial void OnPosYChanged(int value) => OnPropertyChanged(nameof(Coordinates));
        partial void OnHpChanged(int value) => OnPropertyChanged(nameof(ShowCoordinates));
    }
}
