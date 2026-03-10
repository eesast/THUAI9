using System;
using Microsoft.Extensions.Logging;
using Preparation.Utility.Logging;

namespace GameClass.GameObj
{

    public static class LoggingFunctional
    {
        public static string CharacterLogInfo(Character character)
            => LogUtility.GetObjectInfo(typeof(Character), $"{character.TeamID} {character.PlayerID}");
        public static string CharacterLogInfo(long teamId, long characterId)
            => LogUtility.GetObjectInfo(typeof(Character), $"{teamId} {characterId}");
        public static string AutoLogInfo(object obj)
        {
            Type tp = obj.GetType();
            if (tp == typeof(Character))
                return CharacterLogInfo((Character)obj);
            else
                return LogUtility.GetObjectInfo(obj);
        }
    }

}

