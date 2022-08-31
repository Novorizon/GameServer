
//using System;
//using System.Collections;
//using System.Collections.Generic;
//using System.Reflection;

//namespace MVC.Providers
//{
//    public class ReflectionProvider : IReflectionProvider
//    {
//        public delegate object SpawneDelegate(string typeName, params object[] args);
//        public delegate object RecycleDelegate(object inst, out string typeName);

//        private readonly Dictionary<string, Type> loadedTypes = new Dictionary<string, Type>();

//        public SpawneDelegate customSpawner;
//        public RecycleDelegate customRecycler;

//        public void LoadTypes(string assemblyString)
//        {
//            var asmb = Assembly.Load(assemblyString);
//            var types = asmb.GetTypes();
//            foreach (var type in types)
//            {
//                loadedTypes.Add(type.FullName, type);
//            }
//        }

//        public object Recycle(object inst, out string typeName)
//        {
//            typeName = inst.GetType().FullName;
//            if (!loadedTypes.TryGetValue(typeName, out var _))
//            {
//                return customRecycler?.Invoke(inst, out typeName);
//            }

//            return inst;
//        }

//        public object Spawn(string typeName, params object[] args)
//        {
//            if (loadedTypes.TryGetValue(typeName, out var classType))
//            {
//                return Activator.CreateInstance(classType, args);
//            }

//            return customSpawner?.Invoke(typeName, args);
//        }
//    }
//}
