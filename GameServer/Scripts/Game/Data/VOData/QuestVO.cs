using DataBase;

namespace Game
{
    public class QuestVO
    {
        public object Key => UID;
        public long UID;                                        // 

        public int id;
        public QuestData quest;
        public QuestState state;
        public int stage;
        public QuestVO()
        {
           
        }


        public QuestVO Clone()
        {
            QuestVO newClone = new QuestVO();
            return newClone;
        }
    }
}