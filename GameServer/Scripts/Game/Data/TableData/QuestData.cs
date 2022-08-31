using System.Collections.Generic;
using System;

namespace DataBase
{

    public enum QuestLayer
    {
        Mission=1,//主线 
        Quest=1<<1,//支线
        Alliance = 1 <<2,
        Team= 1 <<3,

    }
    [Serializable]
    public class QuestData : TableData
    {
        public int id;
        public string name;
        public string description;
        public QuestLayer layer;

        public string icon;

        //接受条件
        public int level;

        //完成条件
        public List<ItemUnit> items;

        public int previous;//
        public int next;//

        public RewardBundleData reward;//奖励

        private bool Updated;

        public QuestData()
        {
            Updated = false;
        }


        public QuestData Clone()
        {
            QuestData newClone = new QuestData();

            newClone.id = id;
            newClone.name = name;
            newClone.level = level;
            return newClone;
        }
    }
}