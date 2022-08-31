using System.Collections.Generic;
using Mono.Data.Sqlite;
using System;
using Database;
using System.Numerics;

namespace DataBase
{
    [TableAccess]
    public class DefaultTableAccess : TableAccess
    {
        Dictionary<int, TableData> datas;
        public override Type DataType => typeof(DefaultData);

        public Dictionary<int, TableData> GetDatas() => datas;


        public DefaultTableAccess()
        {
            Name = "DefaultInfo";
            Loaded = false;
            datas = new Dictionary<int, TableData>();
        }


        public override bool Load(SQLiteHelper db)
        {
            if (db == null || string.IsNullOrEmpty(Name))
                return false;

            SqliteDataReader reader = db.ReadFullTable(Name);
            if (reader == null)
                return false;

            DefaultData data = null;

            while (reader.Read())
            {
                data = new DefaultData();

                data.id = GetInt32(reader, "id");
                data.name = GetString(reader, "name");
                data.modelId = GetInt32(reader, "modelId");
                data.speed = GetFloat(reader, "speed");
                string positions = GetString(reader, "position");
                string[] position = positions.Split(',');
                if (position.Length == 3)
                {
                    float.TryParse(position[0], out float x);
                    float.TryParse(position[1], out float y);
                    float.TryParse(position[2], out float z);
                    data.position = new Vector3(x, y, z);
                }

                string forwards = GetString(reader, "forward");
                string[] forward = forwards.Split(',');
                if (forward.Length == 3)
                {
                    float.TryParse(forward[0], out float x);
                    float.TryParse(forward[1], out float y);
                    float.TryParse(forward[2], out float z);
                    data.forward = new Vector3(x, y, z);
                }
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