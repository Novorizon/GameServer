namespace DataBase
{
    public class ModelData : TableData
    {
        public int id;
        public string name;
        public string description { get; set; }
        public string path;

        private bool Updated;

        public ModelData()
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

        public ModelData Clone()
        {
            ModelData newClone = new ModelData();
            //deep copy
            newClone.id = id;
            newClone.name = name;
            return newClone;
        }
    }
}