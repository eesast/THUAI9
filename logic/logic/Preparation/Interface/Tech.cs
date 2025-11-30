namespace Preperation.Interface
{
    public interface Tech
    {
        public int Cost { get; }
        public int Level { get; }
    }
    public interface HPTech : Tech
    {
        public double HPIncrease { get; }
    }
    public interface AttackTech : Tech
    {
        public double AttackIncrease { get; }
    }
    public interface SpeedTech : Tech
    {
        public double SpeedIncrease { get; }
    }
    public interface EfficencyTech : Tech
    {
        public double EfficencyIncrease { get; }
    }
    public interface CapacityTech : Tech 
    {
        public double CapacityIncrease { get; }
    }
}
