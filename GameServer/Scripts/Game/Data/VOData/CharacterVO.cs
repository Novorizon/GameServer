using System;

namespace Game
{
    public class ClientVO : ICloneable
    {

        public int serverId = 0;
        public long playerId = 0;
        public long sessionId = 0;

        public bool charLogined = false;



        object ICloneable.Clone()
        {
            return this.Clone();
        }
        public ClientVO Clone()
        {
            return (ClientVO)this.MemberwiseClone();
        }
    }
}