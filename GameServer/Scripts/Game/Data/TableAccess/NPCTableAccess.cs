using System.Collections.Generic;
using Mono.Data.Sqlite;
using System;
using Database;

namespace DataBase
{
    [TableAccess]
    public class NPCTableAccess : TableAccess
    {
        Dictionary<int, NPCData> datas;
        public override Type DataType => typeof(NPCData);

        public Dictionary<int, NPCData> GetDatas() => datas;


        public NPCTableAccess()
        {
            Name = "NPC";
            Loaded = false;
            datas = new Dictionary<int, NPCData>();
        }


        public override bool Load(SQLiteHelper db)
        {
            if (db == null || string.IsNullOrEmpty(Name))
                return false;

            SqliteDataReader reader = db.ReadFullTable(Name);
            if (reader == null)
                return false;

            NPCData data = null;

            while (reader.Read())
            {
                data = new NPCData();

                data.id = GetInt32(reader, "id");
                data.name = GetString(reader, "name");
                data.description = GetString(reader, "description");
                data.type = (NPCType)GetInt32(reader, "type");

                data.modelId = GetInt32(reader, "modelId");
                data.model = GetString(reader, "model");
                data.avatar = GetString(reader, "avatar");
                datas.Add(data.id, data);
            }

            reader.Close();
            Loaded = true;

            return true;
        }

        public override TableData GetData(int index)
        {
            if (datas == null || !datas.ContainsKey(index))
                return null;

            return datas[index];
        }



    }

}