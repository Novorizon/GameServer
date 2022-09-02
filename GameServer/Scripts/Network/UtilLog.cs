using UnityEngine;
using System.Collections;
using System;
using System.IO;

namespace Game
{
    public class UtilIO
    {
        //public static string GetText(string filePath)
        //{
        //    if (filePath.Contains("://"))
        //    {
        //        using (UnityEngine.Networking.UnityWebRequest www = UnityEngine.Networking.UnityWebRequest.Get(filePath))
        //        {
        //            www.SendWebRequest();
        //            yield return www.downloadHandler.text;
        //        }
        //    }
        //    else
        //        yield return System.IO.File.ReadAllText(filePath);
        //}

        public static string GetPlatformLoadDataDir()
        {
#if UNITY_EDITOR_WIN
            //always got below excpetion on MAC, dont know why
            //FileNotFoundException: Could not find file "/work/EventSystemProto/Assets/Data/result.json".;
            return Application.streamingAssetsPath;// result.json";
            //battleReportPath = "/work/EventSystemProto/Assets/Data/result.json";
            //battleReportPath = Application.streamingAssetsPath + "/Assets/Data/result.json";
#elif UNITY_EDITOR_OSX
             return "file://" + Application.streamingAssetsPath;
#elif UNITY_ANDROID
            /*
            Application.dataPath = "/data/app/com.Tap4Fun.N2-eHxRzCow_pBNtsjNxyA2JA==/base.apk/".
            Application.streamingAssetsPath = "/jar:file:/data/app/com.Tap4Fun.N2-lw6rdRp5bT_PXFnJoGDvVA==/base.apk!/assets"
            Application.persistentDataPath 
            */
            return  Application.streamingAssetsPath;
            
#elif UNITY_IPHONE
            return  Application.streamingAssetsPath;
#else
            return Application.streamingAssetsPath;
#endif

        }

        public static string GetPlatformSaveDataDir()
        {
#if UNITY_EDITOR_WIN
            //always got below excpetion on MAC, dont know why
            //FileNotFoundException: Could not find file "/work/EventSystemProto/Assets/Data/result.json".;
            return Application.streamingAssetsPath + "/../Data";// result.json";
            //battleReportPath = "/work/EventSystemProto/Assets/Data/result.json";
            //battleReportPath = Application.streamingAssetsPath + "/Assets/Data/result.json";
#elif UNITY_EDITOR_OSX
            return Application.streamingAssetsPath;
#elif UNITY_ANDROID
            return  Application.persistentDataPath;
#elif UNITY_IPHONE
            return  Application.persistentDataPath;
#else
            return Application.persistentDataPath;
#endif

        }

        public static bool PlatformSaveFile(string fileName, string content)
        {
            //GetPlatformSaveDataDir
            string _path = Path.Combine(UtilIO.GetPlatformSaveDataDir(), fileName);
            FileStream _fs = File.Open(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            if (_fs == null)
                return false;
            StreamWriter _sw = new StreamWriter(_fs);
            _fs.SetLength(0);
            _sw.Write(content);
            _sw.Close();
            return true;
        }
}

    //test stub
    public class DBLoader
    {
        public static string  GetHeroName(int key)
        {
            return key.ToString();
        }
        
        public static string GetSkillName(int key)
        {
            return key.ToString();
        }
        public static string GetEffectName(int key)
        {
            return key.ToString();
        }
        public static int GetDBIndexFromKeyValue(string key, string value)
        {
            return 0;
        }

    }

    public class TimeUtil
    {
        //transfer second to HH:mm:ss
        public static string FloatSecondToFormTime(float seconds)
        {
            int h = Mathf.FloorToInt(seconds / 3600f);
            int m = Mathf.FloorToInt(seconds / 60f - h * 60f);
            int s = Mathf.FloorToInt(seconds - m * 60f - h * 3600f);
            string _timeString = h.ToString("00") + ":" + m.ToString("00") + ":" + s.ToString("00");
            return _timeString;
        }


        /// <summary>
        /// 把毫秒级时间转化为对应格式本地时间
        /// </summary>
        /// <param name="ms">时间</param>
        /// <param name="form">年yyyy月MM日dd小时HH分钟mm秒ss</param>
        /// <returns></returns>
        public static string LongMsToFormTime(long ms, string form)
        {
            long ticks = new DateTime(1970, 1, 1).Ticks;
            DateTime dateTime = new DateTime(ms * 10000 + ticks, DateTimeKind.Utc);

            return dateTime.ToLocalTime().ToString(form);
        }


        /// <summary>
        /// 毫秒级时间转换为对应格式（倒计时类）
        /// </summary>
        /// <param name="ms">时间</param>
        /// <param name="form">年yyyy月MM日dd小时HH分钟mm秒ss</param>
        /// <returns></returns>
        public static string LongTimeToFormTime(long ms,string form)
        {
            DateTime dateTime = new DateTime(ms * 10000, DateTimeKind.Utc);

            return dateTime.ToString(form);
        }

        public static long TickToMilliSec(long tick)
        {
            return tick / (10 * 1000);
        }
        public static long TickToSec(long tick)
        {
            return tick / (10);
        }
    }
}
