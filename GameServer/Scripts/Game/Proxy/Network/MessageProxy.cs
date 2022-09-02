using System;
using Google.Protobuf;
using PureMVC.Patterns.Proxy;
using Net;
using System.IO;
using System.Net.Sockets;

namespace Server
{
    public enum MSG_FREQUENCY_TYPE
    {
        IMMEDIATE,
        TIME_CONTROLED,
        NONE,
    }

    public class MessageProxy : Proxy
    {
        public new static string NAME = typeof(MessageProxy).FullName;

        public DateTime m_LastSendTime = DateTime.MinValue;
        private MSG_FREQUENCY_TYPE m_frequency = MSG_FREQUENCY_TYPE.IMMEDIATE;
        private static int CONTROL_TIME = 30 * 1000; // millisecond

        HandlerProxy handlerProxy = null;

        public MessageProxy() : base(NAME) { }

        public override void OnRegister()
        {
            base.OnRegister();
            handlerProxy = Facade.RetrieveProxy(HandlerProxy.NAME) as HandlerProxy;
        }

        public override void OnRemove()
        {
        }

        /// 发送SOCKET消息
        public void SendMessage(TCPClientState state, IMessage obj)
        {
            if (!ProtoUtils.ContainProtoType(obj.GetType()))
            {
                //NetDebug.instance.LogError("不存协议类型");
                return;
            }

            ByteBuffer buff = new ByteBuffer();
            uint protoId = ProtoUtils.GetProtoIdByProtoType(obj.GetType());

            byte[] result;
            using (MemoryStream ms = new MemoryStream())
            {
                obj.WriteTo(ms);
                result = ms.ToArray();
            }

            Int32 lengh = (Int32)(result.Length + 4);
            //Debug.Log("lengh" + lengh + ",protoId" + protoId);
            buff.WriteInt((Int32)lengh);


            buff.WriteInt((Int32)protoId);

            buff.WriteBytes(result);
            SendMessage(state, buff);
        }


        /// 发送消息
        public void SendMessage(TCPClientState state, ByteBuffer buffer)
        {
            MemoryStream ms = null;
            BinaryWriter writer = null;
            try
            {
                using (ms = new MemoryStream())
                {
                    byte[] message = buffer.ToBytes();
                    ms.Position = 0;
                    writer = new BinaryWriter(ms);
                    writer.Write(message);
                    writer.Flush();

                    TcpClient client = state.TcpClient;
                    NetworkStream networkStream = state.NetworkStream;
                    if (client != null && client.Connected && networkStream != null)
                    {
                        byte[] payload = ms.ToArray();
                        networkStream.BeginWrite(payload, 0, payload.Length, OnWrite, state);
                    }
                    else
                    {
                        OnError("WriteMessage Error");
                    }
                }
            }
            catch (Exception ex)
            {
                OnError("WriteMessage Error " + ex.Message.ToString());
            }
            finally
            {
                if (writer != null) writer.Dispose();
                if (ms != null) ms.Dispose();
            }

            buffer.Close();
        }



        /// 向链接写入数据流
        void OnWrite(IAsyncResult ar)
        {
            try
            {
                TCPClientState state = (TCPClientState)ar.AsyncState;
                state.NetworkStream.EndWrite(ar);
            }
            catch (Exception ex)
            {
                OnError("OnWrite Error " + ex.Message.ToString());
            }
        }

        /// 接收到消息
        public void ReceiveMessage(TCPClientState state, int count)
        {
            Log(" ReceiveMessage");

            byte[] bytes = new byte[count];
            Buffer.BlockCopy(state.Buffer, 0, bytes, 0, count);

            MemoryStream memStream = new MemoryStream();
            memStream.Seek(0, SeekOrigin.End);
            memStream.Write(bytes, 0, count);
            memStream.Seek(0, SeekOrigin.Begin);            //Reset to beginning

            BinaryReader reader = new BinaryReader(memStream);

            while (RemainingBytes(memStream) > 4)
            {
                Int32 messageLen = reader.ReadInt32();
                if (RemainingBytes(memStream) >= messageLen)
                {
                    MemoryStream ms = new MemoryStream();
                    BinaryWriter writer = new BinaryWriter(ms);
                    writer.Write(reader.ReadBytes(messageLen));
                    ms.Seek(0, SeekOrigin.Begin);
                    OnReceivedMessage(state, ms);
                }
                else
                {

                    memStream.Position = memStream.Position - 4;
                    break;
                }
            }
            byte[] leftover = reader.ReadBytes((int)RemainingBytes(memStream));
            memStream.SetLength(0);
            memStream.Write(leftover, 0, leftover.Length);

        }


        /// 剩余的字节
        private long RemainingBytes(MemoryStream memStream)
        {
            return memStream.Length - memStream.Position;
        }

        /// 接收到消息
        void OnReceivedMessage(TCPClientState state, MemoryStream ms)
        {
            try
            {
                BinaryReader r = new BinaryReader(ms);

                byte[] message = r.ReadBytes((int)(ms.Length - ms.Position));
                ByteBuffer buffer = new ByteBuffer(message);

                int mainId = buffer.ReadInt();
                int pbDataLen = message.Length - 4;
                byte[] pbData = buffer.ReadBytes(pbDataLen);

                //Type protoType = ProtoDic.GetProtoTypeByProtoId((uint)mainId);
                handlerProxy.DispatchProto(state, (uint)mainId, pbData);
            }
            catch (Exception ex)
            {
                OnError("OnReceivedMessage(): " + ex.Message.ToString());
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