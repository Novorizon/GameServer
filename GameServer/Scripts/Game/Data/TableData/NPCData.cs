using System;

namespace DataBase
{
    public  enum NPCType
    {
        None,

    }

    [Serializable]
    public class NPCData : TableData
    {
        public int id;
        public string name;
        public int modelId;
        public string model;                                // 
        public string avatar;
        public string description { get; set; }
        public int level;
        public NPCType type;

        private bool Updated;

        public NPCData()
        {
            Updated = false;
        }


        public NPCData Clone()
        {
            NPCData newClone = new NPCData();

            newClone.id = id;
            newClone.name = name;
            newClone.level = level;
            newClone.type = type;
            return newClone;
        }
    }
}