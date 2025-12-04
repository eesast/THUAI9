using Preparation.Utility.Value.SafeValue.LockedValue;
using Preparation.Utility;

namespace Preparation.Interface
{
    public interface IFactory
    {
        public InVariableRange<long> HP { get; }
        public InVariableRange<long> Robust { get; }
        public InVariableRange<long> Storage { get; }

    }
}
