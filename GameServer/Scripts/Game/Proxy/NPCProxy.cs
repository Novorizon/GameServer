using DataBase;
using PureMVC.Patterns.Proxy;
using System.Collections.Generic;

namespace Game
{
    public class NpcProxy : Proxy
    {

        public new static string NAME = typeof(NpcProxy).FullName;

        Dictionary<int, NPCData> datas;

        public NpcProxy() : base(NAME) { }

        public override void OnRegister()
        {
            datas = new Dictionary<int, NPCData>();
        }

        public override void OnRemove()
        {
        }





        public NPCData GetData(int id)
        {
            datas.TryGetValue(id, out NPCData data);
            return data;
        }

        public Dictionary<int, NPCData> GetDatas() => datas;

        public void SetData(NPCData data) => datas[data.id] = data;

    }
}
