using System.Collections.Generic;
using Protobuf;
using THUAI9.Unity.Core;
using UnityEngine;

namespace THUAI9.Unity.Render
{
    /// <summary>
    /// Runtime metadata attached to visible world objects.
    /// Selection / inspector code reads this instead of depending on THUAI7/8 object rules.
    /// </summary>
    public class WorldObjectInfo : MonoBehaviour
    {
        private static readonly List<WorldObjectInfo> activeInfos = new List<WorldObjectInfo>();

        public static IReadOnlyList<WorldObjectInfo> ActiveInfos => activeInfos;

        public string objectType;
        public string title;
        [TextArea(3, 12)] public string detail;
        public long guid;
        public long teamId;
        public long playerId;
        public CharacterType characterType = CharacterType.NullCharacterType;
        public int gridX = -1;
        public int gridY = -1;
        public int gameX;
        public int gameY;
        public int hp;
        public int attackRange;
        public int observedMaxHp;
        public int lastSeenFrame;
        public float lastSeenRealtime;

        private void OnEnable()
        {
            if (!activeInfos.Contains(this))
            {
                activeInfos.Add(this);
            }
        }

        private void OnDisable()
        {
            activeInfos.Remove(this);
        }

        public void SetInfo(string type, string objectTitle, string objectDetail, long objectGuid = 0, long ownerTeamId = 0, int row = -1, int col = -1)
        {
            objectType = type;
            title = objectTitle;
            detail = objectDetail;
            guid = objectGuid;
            teamId = ownerTeamId;
            playerId = 0;
            characterType = CharacterType.NullCharacterType;
            gridX = row;
            gridY = col;
            gameX = -1;
            gameY = -1;
            hp = 0;
            attackRange = 0;
            lastSeenFrame = CoreParam.frameCount;
            lastSeenRealtime = Time.realtimeSinceStartup;
        }

        public void SetCharacterInfo(MessageOfCharacter character)
        {
            if (character == null)
            {
                return;
            }

            playerId = character.PlayerId;
            characterType = character.CharacterType;
            gameX = character.X;
            gameY = character.Y;
            hp = character.Hp;
            attackRange = character.CommonAttackRange;
        }

        public string BuildDisplayText()
        {
            string position = gridX >= 0 && gridY >= 0 ? $"\n坐标：({gridX}, {gridY})" : string.Empty;
            string team = teamId > 0 ? $"\n队伍：Team {teamId}" : string.Empty;
            string id = guid != 0 ? $"\nID：{guid}" : string.Empty;
            string frame = lastSeenFrame > 0 ? $"\n最后更新帧：{lastSeenFrame}" : string.Empty;
            return $"{title}{team}{id}{position}{frame}\n{detail}";
        }

        public bool TryGetBounds(out Bounds bounds)
        {
            bool initialized = false;
            bounds = new Bounds(transform.position, Vector3.one);

            foreach (SpriteRenderer spriteRenderer in GetComponentsInChildren<SpriteRenderer>())
            {
                if (spriteRenderer.gameObject.name.Contains("StatusBar"))
                {
                    continue;
                }

                if (!initialized)
                {
                    bounds = spriteRenderer.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(spriteRenderer.bounds);
                }
            }

            foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
            {
                if (renderer is SpriteRenderer || renderer.gameObject.name.Contains("StatusBar"))
                {
                    continue;
                }

                if (!initialized)
                {
                    bounds = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return initialized;
        }
    }
}
