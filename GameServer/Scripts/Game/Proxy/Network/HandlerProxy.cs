using System;
using Google.Protobuf;
using PureMVC.Patterns.Proxy;
using Net;
using System.Collections.Generic;
using Server;

namespace Net
{

    public delegate void Handler(TCPClientState state, object data);
    public class HandlerProxy : Proxy
    {
        public new static string NAME = typeof(HandlerProxy).FullName;

        NetProxy netProxy = null;

        private Dictionary<Type, Handler> handlers;

        public HandlerProxy() : base(NAME) { }

        public override void OnRegister()
        {
            base.OnRegister();
            netProxy = Facade.RetrieveProxy(NetProxy.NAME) as NetProxy;
            handlers = new Dictionary<Type, Handler>();
        }

        public override void OnRemove()
        {
        }

        public void RegisterHandler(Type type, Handler handler)
        {
            //one type may have more than one handler
            if (handlers.ContainsKey(type))
            {
                handlers[type] += handler;
            }
            else
            {
                handlers.Add(type, handler);
            }
        }

        public void RemoveHandler(Type type, Handler handler)
        {
            if (handlers != null && handlers.ContainsKey(type))
            {
                handlers[type] -= handler;
            }
        }



        public void DispatchProto(TCPClientState state, uint protoId, byte[] buff)
        {
            if (!ProtoUtils.ContainProtoId(protoId))
                return;

            Type protoType = ProtoUtils.GetProtoTypeByProtoId(protoId);
            try
            {
                MessageParser messageParser = ProtoUtils.GetMessageParser(protoType.TypeHandle);
                object toc = messageParser.ParseFrom(buff);

                if (handlers.TryGetValue(protoType, out Handler handler))
                {
                    handler(state, toc);
                }
            }
            catch
            {
                Console.WriteLine("DispatchProto Error:" + protoType.ToString());
            }
        }


        void OnError(string msg)
        {
            Console.WriteLine(msg);
        }

        void Log(string str)
        {
            Console.WriteLine(str);

        }
    }
}