using DataBase;
using PureMVC.Patterns.Proxy;
using System.Collections.Generic;

namespace Game
{
    public class QuestProxy : Proxy
    {

        public new static string NAME = typeof(QuestProxy).FullName;

        Dictionary<int, QuestVO> datas;

        public QuestProxy() : base(NAME) { }

        public override void OnRegister()
        {
            datas = new Dictionary<int, QuestVO>();
        }

        public override void OnRemove()
        {
        }


        public Dictionary<int, QuestVO> Datas => datas;


        public QuestVO GetData(int id)
        {
            datas.TryGetValue(id, out QuestVO data);
            return data;
        }


        public void SetData(QuestVO data)
        {
            datas[data.id] = data;
        }

        public void SetData(DefaultData defaultData)
        {
            TableProxy tableProxy = Facade.RetrieveProxy(TableProxy.NAME) as TableProxy;
            if (tableProxy == null)
                return;
            for (int i = 0; i < defaultData.quests.Count; i++)
            {
                QuestVO vo = new QuestVO();
                int id = defaultData.quests[i];
                vo.id = id;
                vo.quest = tableProxy.GetData<QuestData>(id);

                vo.state = (QuestState)defaultData.states[i];
                vo.stage = defaultData.stages[i];

                datas[vo.id] = vo;
            }
        }
    }
}
