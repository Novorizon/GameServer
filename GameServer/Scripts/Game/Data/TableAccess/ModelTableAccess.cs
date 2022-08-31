using System.Collections.Generic;
using Mono.Data.Sqlite;
using System;
using Database;

namespace DataBase
{
    [TableAccess]
    public class ModelTableAccess : TableAccess
    {
        Dictionary<int, ModelData> datas;
        public override Type DataType => typeof(ModelData);

        public Dictionary<int, ModelData> GetDatas() => datas;
  

        public ModelTableAccess()
        {
            Name = "Model";
            Loaded = false;
            datas = new Dictionary<int, ModelData>();
        }


        public override bool Load(SQLiteHelper db)
        {
            if (db == null || string.IsNullOrEmpty(Name))
                return false;

            SqliteDataReader reader = db.ReadFullTable(Name);
            if (reader == null)
                return false;

            ModelData data = null;

            while (reader.Read())
            {
                data = new ModelData();

                data.id = GetInt32(reader, "id");
                data.name = GetString(reader, "name");
                data.description = GetString(reader, "description");
                data.path = GetString(reader, "path");

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