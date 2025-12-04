using Preparation.Interface;
using Preparation.Utility;
using System;

namespace GameClass.GameObj.Occupations
{
    public static class OccupationFactory
    {
        public static IOccupation FindIOccupation(CharacterType charactertype) => charactertype switch
        {
            CharacterType.DRONE => new Drone(),
            CharacterType.AUTONOMOUS_CAR => new AutonomousCar(),
            CharacterType.ROBOT => new Robot(),
            _ => new NullOccupation(),
        };
    }
}
