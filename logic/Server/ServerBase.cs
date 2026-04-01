using Protobuf;

namespace Server
{
    abstract class ServerBase : AvailableService.AvailableServiceBase
    {
        public abstract void WaitForEnd();
        public abstract int[] GetScore();
        public abstract int[] GetMaterial();
        public abstract int[] GetComputePower();
    }
}