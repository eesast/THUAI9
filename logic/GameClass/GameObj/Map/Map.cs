using GameClass.GameObj.Areas;
using GameClass.MapGenerator;
using Preparation.Interface;
using Preparation.Utility;
using Preparation.Utility.Value;
using Preparation.Utility.Value.SafeValue;
using System;
using System.Collections.Generic;

namespace GameClass.GameObj.Map
{
    public partial class Map : IMap
    {
        private readonly Dictionary<GameObjType, LockedClassList<IGameObj>> gameObjDict;
        public Dictionary<GameObjType, LockedClassList<IGameObj>> GameObjDict => gameObjDict;
        private readonly uint height;
        public uint Height => height;
        private readonly uint width;
        public uint Width => width;
        public readonly PlaceType[,] protoGameMap;
        public PlaceType[,] ProtoGameMap => protoGameMap;

        private readonly MyTimer timer = new();
        public List<Factory> Factories { get; }
        public IMyTimer Timer => timer;
        private readonly long currentHomeNum = 0;
        public bool TeamExists(long teamID)
        {
            return teamID < currentHomeNum;
        }

        #region GetPlaceType
        public PlaceType GetPlaceType(IGameObj obj)
        {
            try
            {
                var (x, y) = GameData.PosGridToCellXY(obj.Position);
                return protoGameMap[x, y];
            }
            catch
            {
                return PlaceType.NULL_PLACE_TYPE;
            }
        }
        public PlaceType GetPlaceType(XY pos)
        {
            try
            {
                var (x, y) = GameData.PosGridToCellXY(pos);
                return protoGameMap[x, y];
            }
            catch
            {
                return PlaceType.NULL_PLACE_TYPE;
            }
        }
        #endregion

        public bool IsOutOfBound(IGameObj obj)
        {
            return obj.Position.x >= GameData.MapLength - obj.Radius
                || obj.Position.x <= obj.Radius
                || obj.Position.y >= GameData.MapLength - obj.Radius
                || obj.Position.y <= obj.Radius;
        }
        public IOutOfBound GetOutOfBound(XY pos)
        {
            return new OutOfBoundBlock(pos);
        }

        public Character? FindCharacterInID(long ID)
        {
            return (Character?)GameObjDict[GameObjType.CHARACTER].Find(gameObj => (ID == ((Character)gameObj).ID));
        }
        public Character? FindCharacterInPlayerID(long teamID, long playerID)
        {
            return (Character?)GameObjDict[GameObjType.CHARACTER].Find(gameObj => (teamID == ((Character)gameObj).TeamID) && playerID == ((Character)gameObj).PlayerID);
        }
        public GameObj? OneForInteract(XY Pos, GameObjType gameObjType)
        {
            return (GameObj?)GameObjDict[gameObjType].Find(gameObj => GameData.ApproachToInteract(gameObj.Position, Pos));
        }
        public GameObj? OneInTheSameCell(XY Pos, GameObjType gameObjType)
        {
            return (GameObj?)GameObjDict[gameObjType].Find(gameObj => (GameData.IsInTheSameCell(gameObj.Position, Pos)));
        }
        public GameObj? PartInTheSameCell(XY Pos, GameObjType gameObjType)
        {
            return (GameObj?)GameObjDict[gameObjType].Find(gameObj => (GameData.PartInTheSameCell(gameObj.Position, Pos)));
        }
        public GameObj? OneForInteractInACross(XY Pos, GameObjType gameObjType)
        {
            return (GameObj?)GameObjDict[gameObjType].Find(gameObj =>
                GameData.ApproachToInteractInACross(gameObj.Position, Pos));
        }
        public GameObj? OneInTheRange(XY Pos, int range, GameObjType gameObjType)
        {
            return (GameObj?)GameObjDict[gameObjType].Find(gameObj =>
                GameData.IsInTheRange(gameObj.Position, Pos, range));
        }
        public List<Character>? CharacterInTheRangeNotTeamID(XY Pos, int range, long teamID)
        {
            return GameObjDict[GameObjType.CHARACTER].Cast<Character>()?.FindAll(character =>
                (GameData.IsInTheRange(character.Position, Pos, range) && character.TeamID != teamID));
        }
        public List<Character>? AllCharacterInTheRange(XY Pos, int range)
        {
            return GameObjDict[GameObjType.CHARACTER].Cast<Character>()?.FindAll(character =>
                (GameData.IsInTheRange(character.Position, Pos, range)));
        }
        public List<Character>? CharacterOnTheSameLineNotTeamID(XY Pos, double theta, long teamID)
        {
            return GameObjDict[GameObjType.CHARACTER].Cast<Character>()?.FindAll(character =>
            (GameData.IsOnTheSameLine(Pos, character.Position, theta) && character.TeamID != teamID));
        }
        public List<Character>? CharacterInTheRangeInTeamID(XY Pos, int range, long teamID)
        {
            return GameObjDict[GameObjType.CHARACTER].Cast<Character>()?.FindAll(character =>
                (GameData.IsInTheRange(character.Position, Pos, range) && character.TeamID == teamID));
        }
        public List<Character>? CharacterInTheList(List<CellXY> PosList)
        {
            return GameObjDict[GameObjType.CHARACTER].Cast<Character>()?.FindAll(character =>
                PosList.Contains(GameData.PosGridToCellXY(character.Position)));
        }
        public List<Character>? CharacterInTeamID(long teamID)
        {
            return GameObjDict[GameObjType.CHARACTER].Cast<Character>()?.FindAll(character =>
                 character.TeamID == teamID);
        }
        public bool InAttackSize(Character character, GameObj gameObj)
        {
            XY pos1 = character.Position;
            XY pos2 = gameObj.Position;
            XY del = pos1 - pos2;
            if (del * del > character.AttackSize * character.AttackSize)
                return false;
            return true;
        }
        public bool Remove(GameObj gameObj)
        {
            GameObj? ans = (GameObj?)GameObjDict[gameObj.Type].RemoveOne(obj => gameObj.ID == obj.ID);
            if (ans != null)
            {
                ans.TryToRemove();
                return true;
            }
            return false;
        }
        public bool RemoveJustFromMap(GameObj gameObj)
        {
            if (GameObjDict[gameObj.Type].Remove(gameObj))
            {
                gameObj.TryToRemove();
                return true;
            }
            return false;
        }
        public void Add(IGameObj gameObj)
        {
            GameObjDict[gameObj.Type].Add(gameObj);
        }
        public Map(MapStruct mapResource, ComputeCenterType Atype = ComputeCenterType.NULL)
        {
            gameObjDict = [];
            foreach (GameObjType idx in Enum.GetValues(typeof(GameObjType)))
            {
                if (idx != GameObjType.NULL)
                    gameObjDict.TryAdd(idx, new LockedClassList<IGameObj>());
            }
            height = mapResource.height;
            width = mapResource.width;
            protoGameMap = mapResource.map;
            int count = 0;
            for (int i = 0; i < height; ++i)
            {
                for (int j = 0; j < width; ++j)
                {
                    switch (mapResource.map[i, j])
                    {
                        case PlaceType.BARRIER:
                            Add(new Barriers(GameData.GetCellCenterPos(i, j)));
                            break;
                        case PlaceType.BUSH:
                            Add(new Bush(GameData.GetCellCenterPos(i, j)));
                            break;
                        case PlaceType.COMPUTE_CENTER:
                            if (count == 0)
                            {
                                Add(new ComputeCenter(GameData.GetCellCenterPos(i, j), ComputeCenterType.OLCF));
                                count = 1;
                            }
                            else if (count == 1)
                            {
                                Add(new ComputeCenter(GameData.GetCellCenterPos(i, j), ComputeCenterType.ALCF));
                                count = 2;
                            }
                            else if (count == 2)
                            {
                                Add(new ComputeCenter(GameData.GetCellCenterPos(i, j), ComputeCenterType.LLNL_HPC));
                                count = 3;
                            }
                            else
                            {
                                Add(new ComputeCenter(GameData.GetCellCenterPos(i, j), ComputeCenterType.NSCC_WUXI));
                                count = 0;
                            }
                            break;
                        case PlaceType.RESOURCE:
                            Add(new Resource(GameData.GetCellCenterPos(i, j)));
                            break;
                        case PlaceType.SPACE:
                            Add(new Space(GameData.GetCellCenterPos(i, j)));
                            break;
                        case PlaceType.FACTORY:
                            if (i < 25)
                                Add(new Factory(GameData.GetCellCenterPos(i, j)));
                            else
                                Add(new Factory(GameData.GetCellCenterPos(i, j)));
                            break;
                    }
                }
            }
            Factories = GameObjDict[GameObjType.FACTORY].Cast<Factory>()?.ToNewList()!;
        }
    }
}