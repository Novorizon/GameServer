using System.Collections.Generic;
using System;

namespace DataBase
{


    [Serializable]
    public class RewardData : TableData
    {
        public int id;
        public int count;

        private bool Updated;

        public RewardData()
        {
            Updated = false;
        }


        public RewardData Clone()
        {
            RewardData newClone = new RewardData();

            newClone.id = id;
            return newClone;
        }
    }

    [Serializable]
    public class RewardBundleData : TableData
    {
        public int id;
        public string name;
        public string description;

        public string icon;
        public List<RewardData> rewards;

        private bool Updated;

        public RewardBundleData()
        {
            Updated = false;
        }


        public RewardBundleData Clone()
        {
            RewardBundleData newClone = new RewardBundleData();

            newClone.id = id;
            return newClone;
        }
    }
}