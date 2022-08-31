namespace DataBase
{
    public enum Gender
    {
        None,
        Male,
        Female,
    }
    public enum Role
    {
        None,
        Warrior,
        Wizard,
    }
    public enum ItemType
    {
        None,
        Weapon,
        Armor,
        Potion,
        BluePrint,
        Pet,
    }
    public class ItemUnit
    {
        public int id;
        public int count;
    }
        public class ItemData : TableData
    {
        public int id;
        public string name;
        public string description;

        public ItemType type;

        public Gender gender;
        public Role role;
        public int level;


        private bool Updated;

        public ItemData()
        {
            Updated = false;
        }

        public void UpdateHeroToLevel()
        {
            if (!Updated) 
            {
                Updated = true;
            }
        }

        public ItemData Clone()
        {
            ItemData newClone = new ItemData();
            //deep copy
            newClone.id = id;
            newClone.name = name;
            newClone.level = level;
            newClone.type = type;
            return newClone;
        }
    }
}