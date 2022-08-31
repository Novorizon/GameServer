using System.Collections.Generic;
using System.Numerics;

namespace DataBase
{
    public class DefaultData : TableData
    {
        public int id;                                        // 
        public string name;                                     // 
        public string modelPath;                                     // 
        public int modelId;                                       // 

        public int gender;                                       // 等级
        public int level;                                // 

        public int attack;                                      // 
        public int defence;                                      // 

        public float speed;                                      // 
        public int health;                                       // 
        public int age;                                      // 

        public int strength;                         // 
        public int agility;                                  // 
        public int intelligence;                           // 


        public Vector3 position;                                       // 等级
        public Vector3 forward;                                // 

        public List<int> quests;                                       // 
        public List<int> states;                                       // 
        public List<int> stages;                                       // 
        private bool Updated;

        public DefaultData()
        {
            Updated = false;
        }

        public void UpdateHeroToLevel()
        {
            if (!Updated) //this must not be called more than once, otherwise something is wrong
            {
                Updated = true;
            }
        }

        public DefaultData Clone()
        {
            DefaultData newClone = new DefaultData();
            //deep copy
            newClone.id = id;
            newClone.name = name;
            newClone.level = level;
            return newClone;
        }
    }
}