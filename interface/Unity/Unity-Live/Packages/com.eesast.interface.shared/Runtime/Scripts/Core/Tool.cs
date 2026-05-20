using UnityEngine;

namespace THUAI9.Unity.Core
{
    /// <summary>
    /// THUAI9 坐标转换工具。
    /// 游戏坐标原点在左上角，地图默认 50x50，每格 1000 单位。
    /// Unity 世界坐标以左下为原点，因此需要翻转 Y 轴方向。
    /// </summary>
    public static class Tool
    {
        public const float CellSize = 1000f;
        public const int DefaultMapRows = 50;
        public const int DefaultMapCols = 50;

        public static int GetMapRows()
        {
            return CoreParam.map != null && CoreParam.map.Height > 0
                ? (int)CoreParam.map.Height
                : DefaultMapRows;
        }

        public static int GetMapCols()
        {
            return CoreParam.map != null && CoreParam.map.Width > 0
                ? (int)CoreParam.map.Width
                : DefaultMapCols;
        }

        public static Vector2 GameToUnity(float gameX, float gameY)
        {
            float rows = GetMapRows();
            return new Vector2(gameY / CellSize, rows - gameX / CellSize);
        }

        public static Vector2 GridToUnity(int gridX, int gridY)
        {
            float rows = GetMapRows();
            return new Vector2(gridY + 0.5f, rows - gridX - 0.5f);
        }

        public static Vector2Int GameToGrid(float gameX, float gameY)
        {
            return new Vector2Int(
                Mathf.Clamp(Mathf.FloorToInt(gameX / CellSize), 0, Mathf.Max(GetMapRows() - 1, 0)),
                Mathf.Clamp(Mathf.FloorToInt(gameY / CellSize), 0, Mathf.Max(GetMapCols() - 1, 0))
            );
        }

        public static Vector2 GridToGame(int gridX, int gridY)
        {
            return new Vector2(
                (gridX + 0.5f) * CellSize,
                (gridY + 0.5f) * CellSize
            );
        }

        public static Rect GetWorldRect()
        {
            return new Rect(0f, 0f, GetMapCols(), GetMapRows());
        }

        public static Vector3 GetWorldCenter(float z = 0f)
        {
            Rect rect = GetWorldRect();
            return new Vector3(rect.center.x, rect.center.y, z);
        }
    }
}
